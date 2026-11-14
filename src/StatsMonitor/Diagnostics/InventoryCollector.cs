using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Services;

namespace StatsMonitor.Diagnostics;

/// <summary>Read-only inventory of hardware and software. Every source is queried independently: a failure is recorded in <see cref="Inventory.Errors"/> and the value stays unavailable, never invented.</summary>
public static class InventoryCollector
{
    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;
    public static string NA => L("Indisponível", "Unavailable");

    public static Inventory CollectAll(HardwareSnapshot? live, Action<string, int>? progress = null, CancellationToken ct = default)
    {
        var inv = new Inventory();
        for (int i = 0; i < Comp.All.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(Comp.Title(Comp.All[i], Loc.Lang == "en"), i * 100 / Comp.All.Length);
            Collect(inv, Comp.All[i], live);
        }
        return inv;
    }

    /// <summary>Fast subset used by the overview so identification data appears before the slow collectors finish.</summary>
    public static Inventory CollectBasics(HardwareSnapshot? live)
    {
        var inv = new Inventory();
        foreach (var c in new[] { Comp.Hardware, Comp.Cpu, Comp.Memory, Comp.Gpu }) Collect(inv, c, live);
        try { OsBasics(inv, []); } catch (Exception ex) { inv.Errors.Add($"{Comp.Os}/Windows: {ex.Message}"); }
        return inv;
    }

    public static void Collect(Inventory inv, string comp, HardwareSnapshot? live)
    {
        var facts = new List<Fact>();
        inv.Sections[comp] = facts;
        void Step(string name, Action<List<Fact>> work)
        {
            try { work(facts); }
            catch (Exception ex) { inv.Errors.Add($"{comp}/{name}: {ex.Message}"); facts.Add(new Fact(name, NA)); }
        }
        switch (comp)
        {
            case Comp.Hardware: Step(L("Computador", "Computer"), f => Hardware(inv, f)); break;
            case Comp.Cpu: Step("CPU", f => Cpu(inv, f, live)); break;
            case Comp.Memory: Step("RAM", f => Memory(inv, f, live)); break;
            case Comp.Gpu: Step("GPU", f => Gpu(inv, f, live)); break;
            case Comp.Storage: Step(L("Armazenamento", "Storage"), f => Storage(inv, f, live)); break;
            case Comp.Battery: Step(L("Bateria", "Battery"), f => Battery(inv, f)); break;
            case Comp.Os:
                Step("Windows", f => OsBasics(inv, f));
                Step(L("Serviços e inicialização", "Services and startup"), f => OsServices(inv, f));
                Step(L("Aplicativos e atualizações", "Apps and updates"), f => OsApps(inv, f));
                Step(L("Eventos do sistema", "System events"), f => OsEvents(inv, f));
                break;
            case Comp.Drivers: Step("Drivers", f => Drivers(inv, f)); break;
            case Comp.Network: Step(L("Adaptadores", "Adapters"), f => NetworkAdapters(inv, f)); break;
            case Comp.Security:
                Step("Microsoft Defender", f => Defender(inv, f));
                Step(L("Firewall e UAC", "Firewall and UAC"), f => FirewallUac(inv, f));
                Step("Secure Boot / TPM / BitLocker", f => Platform(inv, f));
                break;
        }
        if (!inv.Collected.Contains(comp)) inv.Collected.Add(comp);
    }

    // ---------- helpers ----------

    private static List<Dictionary<string, object?>> Query(string query, string? scope, params string[] props)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var searcher = Wmi.Searcher(query, scope);
        foreach (System.Management.ManagementBaseObject o in searcher.Get())
        {
            var row = new Dictionary<string, object?>();
            foreach (var p in props) { try { row[p] = o[p]; } catch { row[p] = null; } }
            rows.Add(row);
            o.Dispose();
        }
        return rows;
    }

    private static string Str(object? o) => (o?.ToString() ?? "").Trim();
    private static double Dbl(object? o) => o is null ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture);
    private static string F(double v, string fmt = "0.#") => v.ToString(fmt, CultureInfo.CurrentCulture);

    private static DateTime? WmiDate(object? o)
    {
        var s = Str(o);
        if (s.Length < 14) return null;
        try { return System.Management.ManagementDateTimeConverter.ToDateTime(s); } catch { return null; }
    }

    private static object? Reg(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(name);
    }

    // ---------- hardware ----------

    private static void Hardware(Inventory inv, List<Fact> f)
    {
        var cs = Query("SELECT Manufacturer, Model, Name, SystemType FROM Win32_ComputerSystem", null, "Manufacturer", "Model", "Name", "SystemType").FirstOrDefault();
        if (cs != null)
        {
            inv.Str["hardware.maker"] = Str(cs["Manufacturer"]); inv.Str["hardware.model"] = Str(cs["Model"]); inv.Str["hardware.name"] = Str(cs["Name"]);
            f.Add(new Fact(L("Nome do computador", "Computer name"), Str(cs["Name"])));
            f.Add(new Fact(L("Fabricante", "Manufacturer"), Str(cs["Manufacturer"])));
            f.Add(new Fact(L("Modelo", "Model"), Str(cs["Model"])));
            f.Add(new Fact(L("Tipo de sistema", "System type"), Str(cs["SystemType"])));
        }
        var bios = Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", null, "Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate").FirstOrDefault();
        if (bios != null)
        {
            f.Add(new Fact("BIOS/UEFI", $"{Str(bios["Manufacturer"])} {Str(bios["SMBIOSBIOSVersion"])}"));
            if (WmiDate(bios["ReleaseDate"]) is { } d) { f.Add(new Fact(L("Data da BIOS", "BIOS date"), d.ToString("d"))); inv.Num["hardware.biosAgeDays"] = (DateTime.Now - d).TotalDays; }
        }
        foreach (var b in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard", null, "Manufacturer", "Product"))
            f.Add(new Fact(L("Placa-mãe", "Motherboard"), $"{Str(b["Manufacturer"])} {Str(b["Product"])}"));
    }

    // ---------- cpu ----------

    private static string Arch(int code) => code switch { 0 => "x86", 5 => "ARM", 9 => "x64", 12 => "ARM64", _ => L("Desconhecida", "Unknown") };

    private static void Cpu(Inventory inv, List<Fact> f, HardwareSnapshot? live)
    {
        var rows = Query("SELECT Name, Manufacturer, Architecture, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor",
            null, "Name", "Manufacturer", "Architecture", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "CurrentClockSpeed", "L2CacheSize", "L3CacheSize", "SocketDesignation");
        if (rows.Count == 0) throw new InvalidOperationException(L("nenhum processador retornado pelo WMI", "WMI returned no processor"));
        var p = rows[0];
        inv.Str["cpu.name"] = Str(p["Name"]);
        inv.Num["cpu.cores"] = rows.Sum(r => Dbl(r["NumberOfCores"]));
        inv.Num["cpu.threads"] = rows.Sum(r => Dbl(r["NumberOfLogicalProcessors"]));
        inv.Num["cpu.maxMhz"] = Dbl(p["MaxClockSpeed"]);
        f.Add(new Fact(L("Modelo", "Model"), Str(p["Name"])));
        f.Add(new Fact(L("Fabricante", "Manufacturer"), Str(p["Manufacturer"])));
        f.Add(new Fact(L("Arquitetura", "Architecture"), Arch((int)Dbl(p["Architecture"]))));
        f.Add(new Fact(L("Núcleos", "Cores"), F(inv.Num["cpu.cores"], "0")));
        f.Add(new Fact("Threads", F(inv.Num["cpu.threads"], "0")));
        f.Add(new Fact(L("Frequência máxima", "Max frequency"), $"{F(inv.Num["cpu.maxMhz"], "0")} MHz"));
        f.Add(new Fact(L("Frequência atual", "Current frequency"), live?.CpuClockMhz is { } c ? $"{c:0} MHz" : $"{F(Dbl(p["CurrentClockSpeed"]), "0")} MHz (WMI)"));
        f.Add(new Fact("Cache L2 / L3", $"{Dbl(p["L2CacheSize"]) / 1024:0.#} MB / {Dbl(p["L3CacheSize"]) / 1024:0.#} MB"));
        f.Add(new Fact("Socket", Str(p["SocketDesignation"])));
        f.Add(new Fact(L("Utilização", "Usage"), live?.CpuLoad is { } l ? $"{l:0}%" : NA));
        f.Add(new Fact(L("Temperatura", "Temperature"), live?.CpuTemp is > 0 ? Units.Temp(live.CpuTemp.Value) : L("Indisponível (sensor ou administrador necessário)", "Unavailable (sensor or administrator required)")));
        f.Add(new Fact(L("Limitação térmica", "Thermal throttling"), L("Não há indicador direto; o diagnóstico compara carga, frequência e temperatura", "No direct indicator; the diagnosis compares load, frequency and temperature")));
        if (CpuInfoService.ReadCounts() is { } c2)
        {
            inv.Num["os.processes"] = c2.Processes;
            f.Add(new Fact(L("Processos / Threads", "Processes / Threads"), $"{c2.Processes:N0} / {c2.Threads:N0}"));
        }
    }

    // ---------- memory ----------

    private static string MemType(int t) => t switch { 20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 0 => L("Desconhecido", "Unknown"), _ => $"SMBIOS {t}" };

    private static void Memory(Inventory inv, List<Fact> f, HardwareSnapshot? live)
    {
        var os = Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem", null,
            "TotalVisibleMemorySize", "FreePhysicalMemory", "TotalVirtualMemorySize", "FreeVirtualMemory").FirstOrDefault();
        if (os != null)
        {
            double total = Dbl(os["TotalVisibleMemorySize"]) / 1048576, free = Dbl(os["FreePhysicalMemory"]) / 1048576;
            double commitTotal = Dbl(os["TotalVirtualMemorySize"]) / 1048576, commitFree = Dbl(os["FreeVirtualMemory"]) / 1048576;
            inv.Num["memory.totalGb"] = total; inv.Num["memory.availGb"] = free;
            if (commitTotal > 0) inv.Num["memory.commitPct"] = 100 * (commitTotal - commitFree) / commitTotal;
            f.Add(new Fact(L("Capacidade instalada (visível)", "Installed capacity (visible)"), $"{total:0.0} GB"));
            f.Add(new Fact(L("Disponível", "Available"), $"{free:0.0} GB"));
            f.Add(new Fact(L("Utilização", "Usage"), $"{100 * (total - free) / Math.Max(total, 0.01):0}%"));
            f.Add(new Fact(L("Memória comprometida", "Committed memory"), $"{commitTotal - commitFree:0.0} / {commitTotal:0.0} GB ({inv.Num.GetValueOrDefault("memory.commitPct"):0}%)"));
        }
        var mods = Query("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer, DeviceLocator, PartNumber FROM Win32_PhysicalMemory", null,
            "Capacity", "Speed", "ConfiguredClockSpeed", "SMBIOSMemoryType", "Manufacturer", "DeviceLocator", "PartNumber");
        if (mods.Count > 0)
        {
            inv.Num["memory.modules"] = mods.Count;
            inv.Num["memory.speed"] = mods.Max(m => Dbl(m["Speed"]));
            inv.Num["memory.configSpeed"] = mods.Where(m => Dbl(m["ConfiguredClockSpeed"]) > 0).Select(m => Dbl(m["ConfiguredClockSpeed"])).DefaultIfEmpty(0).Min();
            f.Add(new Fact(L("Módulos", "Modules"), mods.Count.ToString()));
            f.Add(new Fact(L("Tipo", "Type"), MemType((int)Dbl(mods[0]["SMBIOSMemoryType"]))));
            f.Add(new Fact(L("Frequência", "Frequency"), $"{inv.Num["memory.speed"]:0} MHz" + (inv.Num["memory.configSpeed"] > 0 ? $" ({L("configurada", "configured")} {inv.Num["memory.configSpeed"]:0} MHz)" : "")));
            foreach (var m in mods)
                f.Add(new Fact(Str(m["DeviceLocator"]), $"{Dbl(m["Capacity"]) / 1073741824:0.#} GB · {Str(m["Manufacturer"])} {Str(m["PartNumber"])}"));
        }
        else f.Add(new Fact(L("Módulos", "Modules"), NA));
        var arr = Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray", null, "MemoryDevices").FirstOrDefault();
        if (arr != null && Dbl(arr["MemoryDevices"]) > 0)
        {
            inv.Num["memory.slots"] = Dbl(arr["MemoryDevices"]);
            f.Add(new Fact(L("Slots (usados / total)", "Slots (used / total)"), $"{mods.Count} / {Dbl(arr["MemoryDevices"]):0}"));
        }
    }

    // ---------- gpu ----------

    private static void Gpu(Inventory inv, List<Fact> f, HardwareSnapshot? live)
    {
        var gpus = Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, AdapterRAM, ConfigManagerErrorCode, Status FROM Win32_VideoController", null,
            "Name", "AdapterCompatibility", "DriverVersion", "DriverDate", "AdapterRAM", "ConfigManagerErrorCode", "Status");
        if (gpus.Count == 0) throw new InvalidOperationException(L("nenhuma placa de vídeo retornada pelo WMI", "WMI returned no video controller"));
        inv.Num["gpu.count"] = gpus.Count;
        inv.Num["gpu.errorCode"] = gpus.Max(g => Dbl(g["ConfigManagerErrorCode"]));
        for (int i = 0; i < gpus.Count; i++)
        {
            var g = gpus[i];
            string pre = gpus.Count > 1 ? $"[{i + 1}] " : "";
            if (i == 0) { inv.Str["gpu.name"] = Str(g["Name"]); inv.Str["gpu.driver"] = Str(g["DriverVersion"]); }
            f.Add(new Fact(pre + L("Modelo", "Model"), Str(g["Name"])));
            f.Add(new Fact(pre + L("Fabricante", "Manufacturer"), Str(g["AdapterCompatibility"])));
            f.Add(new Fact(pre + "Driver", Str(g["DriverVersion"])));
            if (WmiDate(g["DriverDate"]) is { } d)
            {
                f.Add(new Fact(pre + L("Data do driver", "Driver date"), d.ToString("d")));
                if (i == 0) inv.Num["gpu.driverAgeDays"] = (DateTime.Now - d).TotalDays;
            }
            f.Add(new Fact(pre + L("Status do dispositivo", "Device status"), $"{Str(g["Status"])} (código {Dbl(g["ConfigManagerErrorCode"]):0})"));
        }
        double? vram = live?.VramTotalGb ?? (Dbl(gpus[0]["AdapterRAM"]) > 0 ? Dbl(gpus[0]["AdapterRAM"]) / 1073741824 : null);
        f.Add(new Fact("VRAM", vram is { } v ? $"{v:0.0} GB" + (live?.VramTotalGb is null ? L(" (WMI limita a 4 GB)", " (WMI caps at 4 GB)") : "") : NA));
        f.Add(new Fact(L("Frequência", "Frequency"), live?.GpuClockMhz is { } c ? $"{c:0} MHz" : NA));
        f.Add(new Fact(L("Utilização", "Usage"), live?.GpuLoad is { } l ? $"{l:0}%" : NA));
        f.Add(new Fact(L("Temperatura", "Temperature"), live?.GpuTemp is > 0 ? Units.Temp(live.GpuTemp.Value) : NA));
        f.Add(new Fact(L("Consumo energético", "Power draw"), live?.GpuPowerW is { } w ? $"{w:0.#} W" : NA));
    }

    // ---------- storage ----------

    private static string MediaType(int t) => t switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => L("Desconhecido", "Unknown") };

    private static void Storage(Inventory inv, List<Fact> f, HardwareSnapshot? live)
    {
        var disks = Query("SELECT DeviceId, FriendlyName, Manufacturer, Model, MediaType, BusType, Size, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk",
            @"root\Microsoft\Windows\Storage", "DeviceId", "FriendlyName", "Manufacturer", "Model", "MediaType", "BusType", "Size", "HealthStatus", "OperationalStatus");
        var counters = new Dictionary<string, Dictionary<string, object?>>();
        try
        {
            foreach (var c in Query("SELECT DeviceId, Temperature, Wear, PowerOnHours, ReadErrorsTotal, ReadErrorsUncorrected, WriteErrorsTotal FROM MSFT_StorageReliabilityCounter",
                @"root\Microsoft\Windows\Storage", "DeviceId", "Temperature", "Wear", "PowerOnHours", "ReadErrorsTotal", "ReadErrorsUncorrected", "WriteErrorsTotal"))
                counters[Str(c["DeviceId"])] = c;
        }
        catch (Exception ex) { inv.Errors.Add($"storage/SMART: {ex.Message}"); }

        inv.Num["storage.disks"] = disks.Count;
        for (int i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            string name = Str(d["FriendlyName"]), id = Str(d["DeviceId"]), k = $"storage.d{i}.";
            double size = Dbl(d["Size"]) / 1073741824;
            inv.Str[k + "name"] = name; inv.Num[k + "sizeGb"] = size;
            inv.Num[k + "health"] = d["HealthStatus"] is null ? -1 : Dbl(d["HealthStatus"]);
            f.Add(new Fact(name, $"{MediaType((int)Dbl(d["MediaType"]))} · {size:0} GB · {L("Saúde", "Health")}: {HealthName((int)inv.Num[k + "health"])}"));
            f.Add(new Fact("   " + L("Fabricante / modelo", "Manufacturer / model"), $"{Str(d["Manufacturer"])} {Str(d["Model"])}".Trim()));
            string smart = "";
            if (counters.TryGetValue(id, out var c))
            {
                if (c["Temperature"] is not null && Dbl(c["Temperature"]) > 0) { inv.Num[k + "temp"] = Dbl(c["Temperature"]); smart += $"{Dbl(c["Temperature"]):0} °C · "; }
                if (c["Wear"] is not null) { inv.Num[k + "wear"] = Dbl(c["Wear"]); smart += $"{L("desgaste", "wear")} {Dbl(c["Wear"]):0}% · "; }
                if (c["PowerOnHours"] is not null) { inv.Num[k + "hours"] = Dbl(c["PowerOnHours"]); smart += $"{Dbl(c["PowerOnHours"]):N0} h · "; }
                if (c["ReadErrorsUncorrected"] is not null) { inv.Num[k + "readErr"] = Dbl(c["ReadErrorsUncorrected"]); smart += $"{L("erros de leitura não corrigidos", "uncorrected read errors")}: {Dbl(c["ReadErrorsUncorrected"]):0} · "; }
                if (c["WriteErrorsTotal"] is not null) { inv.Num[k + "writeErr"] = Dbl(c["WriteErrorsTotal"]); smart += $"{L("erros de escrita", "write errors")}: {Dbl(c["WriteErrorsTotal"]):0}"; }
            }
            var ls = live?.Disks.FirstOrDefault(x => name.Contains(x.Name, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (ls != null)
            {
                if (!inv.Num.ContainsKey(k + "temp") && ls.Temp is > 0) { inv.Num[k + "temp"] = ls.Temp.Value; smart += $"{ls.Temp:0} °C · "; }
                if (!inv.Num.ContainsKey(k + "wear") && ls.LifePercent is { } life) { inv.Num[k + "wear"] = 100 - life; smart += $"{L("vida útil restante", "life left")} {life:0}% · "; }
                if (!inv.Num.ContainsKey(k + "hours") && ls.PowerOnHours is { } h) { inv.Num[k + "hours"] = h; smart += $"{h:N0} h · "; }
            }
            f.Add(new Fact("   SMART", smart.Length > 0 ? smart.TrimEnd(' ', '·') : L("Indisponível (pode exigir administrador ou suporte do disco)", "Unavailable (may require administrator or drive support)")));
        }
        if (disks.Count == 0) f.Add(new Fact(L("Discos físicos", "Physical disks"), NA));

        int v = 0;
        foreach (var dr in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
        {
            string k = $"storage.v{v++}.";
            double total = dr.TotalSize / 1073741824.0, free = dr.AvailableFreeSpace / 1073741824.0;
            inv.Str[k + "name"] = dr.Name; inv.Num[k + "totalGb"] = total; inv.Num[k + "freeGb"] = free; inv.Num[k + "freePct"] = total > 0 ? 100 * free / total : 0;
            f.Add(new Fact(L("Unidade ", "Drive ") + dr.Name, $"{total - free:0.0} / {total:0.0} GB {L("usados", "used")} · {free:0.0} GB {L("livres", "free")} ({inv.Num[k + "freePct"]:0}%) · {dr.DriveFormat}"));
        }
        inv.Num["storage.volumes"] = v;
    }

    public static string HealthName(int h) => h switch { 0 => L("Saudável", "Healthy"), 1 => L("Atenção", "Warning"), 2 => L("Não saudável", "Unhealthy"), _ => L("Indisponível", "Unavailable") };

    // ---------- battery ----------

    private static void Battery(Inventory inv, List<Fact> f)
    {
        var b = BatteryService.ReadCached(TimeSpan.FromSeconds(2));
        inv.Num["battery.present"] = b.Present ? 1 : 0;
        if (!b.Present) { f.Add(new Fact(L("Bateria", "Battery"), L("Nenhuma bateria detectada (desktop ou bateria não exposta ao Windows)", "No battery detected (desktop or battery not exposed to Windows)"))); return; }
        if (b.Percent is { } p) inv.Num["battery.percent"] = p;
        if (b.HealthPercent is { } h) inv.Num["battery.health"] = h;
        if (b.Cycles is { } cy) inv.Num["battery.cycles"] = cy;
        f.Add(new Fact(L("Nível de carga", "Charge level"), b.Percent is { } pc ? $"{pc}%" : NA));
        f.Add(new Fact(L("Estado", "State"), ViewModels.BatteryViewModel.StateText(b.State)));
        f.Add(new Fact(L("Fonte de energia", "Power source"), b.PluggedIn ? L("Carregador (rede elétrica)", "Charger (mains)") : L("Bateria", "Battery")));
        f.Add(new Fact(L("Capacidade original (projeto)", "Design capacity"), b.DesignMWh is { } dm ? ViewModels.BatteryViewModel.Wh(dm) : NA));
        f.Add(new Fact(L("Capacidade atual (carga máxima)", "Current full capacity"), b.FullMWh is { } fm ? ViewModels.BatteryViewModel.Wh(fm) : NA));
        f.Add(new Fact(L("Saúde estimada (atual ÷ projeto)", "Estimated health (current ÷ design)"), b.HealthPercent is { } hp ? $"{hp:0.#}%" : NA));
        f.Add(new Fact(L("Ciclos de carga", "Charge cycles"), b.Cycles is { } c ? c.ToString() : NA));
        f.Add(new Fact(L("Tensão", "Voltage"), b.Volts is { } v ? $"{v:0.00} V" : NA));
        f.Add(new Fact(L("Autonomia estimada", "Estimated runtime"), b.Remaining is { } r ? ViewModels.BatteryViewModel.FormatDuration(r) : (b.PluggedIn ? L("Conectado à energia", "Plugged in") : NA)));
        f.Add(new Fact(L("Tempo para carga completa", "Time to full charge"), b.ToFull is { } t ? ViewModels.BatteryViewModel.FormatDuration(t) : NA));
    }

    // ---------- operating system ----------

    private static void OsBasics(Inventory inv, List<Fact> f)
    {
        var os = Query("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem", null,
            "Caption", "Version", "BuildNumber", "OSArchitecture", "InstallDate", "LastBootUpTime").FirstOrDefault()
            ?? throw new InvalidOperationException("Win32_OperatingSystem");
        string ubr = Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR")?.ToString() ?? "";
        string disp = Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")?.ToString() ?? "";
        inv.Str["os.caption"] = Str(os["Caption"]); inv.Str["os.build"] = Str(os["BuildNumber"]);
        f.Add(new Fact(L("Versão do Windows", "Windows version"), $"{Str(os["Caption"])} {disp}".Trim()));
        f.Add(new Fact("Build", $"{Str(os["Version"])}{(ubr.Length > 0 ? "." + ubr : "")}"));
        f.Add(new Fact(L("Arquitetura", "Architecture"), Str(os["OSArchitecture"])));
        if (WmiDate(os["InstallDate"]) is { } inst) f.Add(new Fact(L("Instalado em", "Installed on"), inst.ToString("d")));
        if (WmiDate(os["LastBootUpTime"]) is { } boot)
        {
            inv.Num["os.uptimeDays"] = (DateTime.Now - boot).TotalDays;
            f.Add(new Fact(L("Tempo de atividade", "Uptime"), $"{(DateTime.Now - boot).Days} d {(DateTime.Now - boot).Hours} h"));
        }
        bool pending = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null
                    || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null;
        inv.Num["os.pendingReboot"] = pending ? 1 : 0;
        f.Add(new Fact(L("Reinicialização pendente", "Pending restart"), pending ? L("Sim", "Yes") : L("Não", "No")));
    }

    private static void OsServices(Inventory inv, List<Fact> f)
    {
        var svc = Query("SELECT Name, DisplayName, State, StartMode FROM Win32_Service", null, "Name", "DisplayName", "State", "StartMode");
        int running = svc.Count(s => Str(s["State"]) == "Running");
        var stopped = svc.Where(s => Str(s["StartMode"]) == "Auto" && Str(s["State"]) != "Running").Select(s => Str(s["DisplayName"])).ToList();
        inv.Num["os.services"] = svc.Count; inv.Num["os.autoStopped"] = stopped.Count;
        f.Add(new Fact(L("Serviços (em execução / total)", "Services (running / total)"), $"{running} / {svc.Count}"));
        f.Add(new Fact(L("Automáticos parados", "Automatic but stopped"), stopped.Count == 0 ? "0" : $"{stopped.Count}: {string.Join(", ", stopped.Take(6))}{(stopped.Count > 6 ? "…" : "")}"));
        inv.Num["os.processes"] = Process.GetProcesses().Length;
        f.Add(new Fact(L("Processos em execução", "Running processes"), inv.Num["os.processes"].ToString("0")));
        var startup = Query("SELECT Name, Location FROM Win32_StartupCommand", null, "Name", "Location");
        inv.Num["os.startup"] = startup.Count;
        f.Add(new Fact(L("Programas de inicialização", "Startup programs"), startup.Count == 0 ? "0" : $"{startup.Count}: {string.Join(", ", startup.Select(s => Str(s["Name"])).Distinct().Take(8))}{(startup.Count > 8 ? "…" : "")}"));
    }

    private static int CountApps(RegistryKey root, string path)
    {
        using var k = root.OpenSubKey(path);
        if (k == null) return 0;
        int n = 0;
        foreach (var sub in k.GetSubKeyNames())
        {
            using var s = k.OpenSubKey(sub);
            if (s?.GetValue("DisplayName") is string dn && dn.Length > 0 && s.GetValue("SystemComponent") is not 1 && s.GetValue("ParentKeyName") is null) n++;
        }
        return n;
    }

    private static void OsApps(Inventory inv, List<Fact> f)
    {
        int apps = CountApps(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
                 + CountApps(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
                 + CountApps(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        inv.Num["os.apps"] = apps;
        f.Add(new Fact(L("Aplicativos instalados (registro)", "Installed apps (registry)"), apps.ToString()));
        var fixes = Query("SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering", null, "HotFixID", "InstalledOn");
        var dates = fixes.Select(x => DateTime.TryParse(Str(x["InstalledOn"]), CultureInfo.CurrentCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null).Where(d => d != null).Select(d => d!.Value).ToList();
        f.Add(new Fact(L("Atualizações instaladas (hotfixes)", "Installed updates (hotfixes)"), fixes.Count.ToString()));
        if (dates.Count > 0)
        {
            var last = dates.Max();
            inv.Num["os.hotfixAgeDays"] = (DateTime.Now - last).TotalDays;
            f.Add(new Fact(L("Última atualização registrada", "Latest recorded update"), last.ToString("d")));
        }
        else f.Add(new Fact(L("Última atualização registrada", "Latest recorded update"), NA));
    }

    private static void OsEvents(Inventory inv, List<Fact> f)
    {
        const string xpath = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 86400000]]]";
        var sources = new Dictionary<string, int>();
        int total = 0;
        using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
        for (EventRecord? e = reader.ReadEvent(); e != null && total < 5000; e = reader.ReadEvent())
        {
            using (e) { total++; var src = e.ProviderName ?? "?"; sources[src] = sources.GetValueOrDefault(src) + 1; }
        }
        inv.Num["os.eventErrors24h"] = total;
        f.Add(new Fact(L("Erros críticos/erros no log do sistema (24 h)", "Critical/error events in the System log (24 h)"), total.ToString()));
        if (total > 0) f.Add(new Fact(L("Principais origens", "Top sources"), string.Join(", ", sources.OrderByDescending(s => s.Value).Take(5).Select(s => $"{s.Key} ({s.Value})"))));
    }

    // ---------- drivers ----------

    private static void Drivers(Inventory inv, List<Fact> f)
    {
        var drv = Query("SELECT DeviceName, Manufacturer, DriverVersion, DriverDate, DeviceClass FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL", null,
            "DeviceName", "Manufacturer", "DriverVersion", "DriverDate", "DeviceClass");
        inv.Num["drivers.total"] = drv.Count;
        f.Add(new Fact(L("Drivers instalados", "Installed drivers"), drv.Count.ToString()));
        foreach (var g in drv.GroupBy(d => Str(d["Manufacturer"])).Where(g => g.Key.Length > 0).OrderByDescending(g => g.Count()).Take(5))
            f.Add(new Fact("   " + g.Key, L($"{g.Count()} drivers", $"{g.Count()} drivers")));
        foreach (var cls in new[] { "DISPLAY", "NET", "MEDIA", "BLUETOOTH", "HDC" })
        {
            var d = drv.Where(x => Str(x["DeviceClass"]).Equals(cls, StringComparison.OrdinalIgnoreCase)).Select(x => new { Name = Str(x["DeviceName"]), Ver = Str(x["DriverVersion"]), Date = WmiDate(x["DriverDate"]) })
                .Where(x => x.Date != null).OrderBy(x => x.Date).FirstOrDefault();
            if (d == null) continue;
            f.Add(new Fact($"{L("Driver mais antigo", "Oldest driver")} ({cls})", $"{d.Name} {d.Ver} · {d.Date:d}"));
            if (cls == "DISPLAY") inv.Num["drivers.oldDisplayDays"] = (DateTime.Now - d.Date!.Value).TotalDays;
            if (cls == "NET") inv.Num["drivers.oldNetDays"] = (DateTime.Now - d.Date!.Value).TotalDays;
        }
        var bad = Query("SELECT Name, ConfigManagerErrorCode, PNPClass FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0", null, "Name", "ConfigManagerErrorCode", "PNPClass");
        inv.Num["drivers.errors"] = bad.Count;
        inv.Num["drivers.unknown"] = bad.Count(b => Dbl(b["ConfigManagerErrorCode"]) is 28 or 1 || Str(b["Name"]).StartsWith("Unknown", StringComparison.OrdinalIgnoreCase));
        inv.Num["drivers.conflicts"] = bad.Count(b => Dbl(b["ConfigManagerErrorCode"]) is 12 or 13);
        inv.Str["drivers.badList"] = string.Join("; ", bad.Take(8).Select(b => $"{Str(b["Name"]).DefaultIfEmpty()} (código {Dbl(b["ConfigManagerErrorCode"]):0})").Select(x => x));
        f.Add(new Fact(L("Dispositivos com erro", "Devices with errors"), bad.Count == 0 ? "0" : $"{bad.Count}: {inv.Str["drivers.badList"]}"));
        f.Add(new Fact(L("Dispositivos desconhecidos", "Unknown devices"), inv.Num["drivers.unknown"].ToString("0")));
        f.Add(new Fact(L("Conflitos de recursos", "Resource conflicts"), inv.Num["drivers.conflicts"].ToString("0")));
    }

    // ---------- network ----------

    private static void NetworkAdapters(Inventory inv, List<Fact> f)
    {
        var list = NetworkToolsService.GetAdapters(true);
        var up = list.Where(a => a.Up).ToList();
        inv.Num["network.adapters"] = list.Count; inv.Num["network.up"] = up.Count;
        if (up.Count > 0) inv.Num["network.linkMbps"] = up.Max(a => a.SpeedMbps);
        f.Add(new Fact(L("Adaptadores (ativos / total)", "Adapters (up / total)"), $"{up.Count} / {list.Count}"));
        foreach (var a in list)
        {
            string kind = a.Type.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ? "Wi-Fi" : a.Type.Contains("Ethernet", StringComparison.OrdinalIgnoreCase) ? "Ethernet" : a.Type;
            f.Add(new Fact($"{a.Name} ({kind})", a.Up
                ? $"{L("ativo", "up")} · {(a.SpeedMbps > 0 ? a.SpeedMbps + " Mbps" : L("velocidade N/D", "speed N/A"))} · IP {string.Join(", ", a.Ipv4)} · Gateway {string.Join(", ", a.Gateways)} · DNS {string.Join(", ", a.Dns)}"
                : L("desconectado", "disconnected")));
        }
    }

    // ---------- security ----------

    private static void Defender(Inventory inv, List<Fact> f)
    {
        var d = Query("SELECT AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureAge, AntispywareEnabled FROM MSFT_MpComputerStatus",
            @"root\Microsoft\Windows\Defender", "AMServiceEnabled", "AntivirusEnabled", "RealTimeProtectionEnabled", "AntivirusSignatureAge", "AntispywareEnabled").FirstOrDefault();
        if (d == null) { f.Add(new Fact("Microsoft Defender", L("Indisponível (outro antivírus ativo ou provedor ausente)", "Unavailable (another antivirus active or provider missing)"))); return; }
        string YN(object? o) => o is bool b ? (b ? L("Ativo", "On") : L("Desativado", "Off")) : NA;
        if (d["AntivirusEnabled"] is bool av) inv.Num["security.defender"] = av ? 1 : 0;
        if (d["RealTimeProtectionEnabled"] is bool rt) inv.Num["security.realtime"] = rt ? 1 : 0;
        if (d["AntivirusSignatureAge"] is not null) inv.Num["security.sigAgeDays"] = Dbl(d["AntivirusSignatureAge"]);
        f.Add(new Fact(L("Antivírus", "Antivirus"), YN(d["AntivirusEnabled"])));
        f.Add(new Fact(L("Proteção em tempo real", "Real-time protection"), YN(d["RealTimeProtectionEnabled"])));
        f.Add(new Fact(L("Idade das assinaturas", "Signature age"), d["AntivirusSignatureAge"] is null ? NA : $"{Dbl(d["AntivirusSignatureAge"]):0} {L("dias", "days")}"));
    }

    private static void FirewallUac(Inventory inv, List<Fact> f)
    {
        int off = 0, known = 0;
        foreach (var (key, label) in new[] { ("DomainProfile", L("Domínio", "Domain")), ("StandardProfile", L("Privado", "Private")), ("PublicProfile", L("Público", "Public")) })
        {
            var v = Reg(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{key}", "EnableFirewall");
            if (v is int i) { known++; if (i == 0) off++; f.Add(new Fact($"Firewall ({label})", i != 0 ? L("Ativo", "On") : L("Desativado", "Off"))); }
            else f.Add(new Fact($"Firewall ({label})", NA));
        }
        if (known > 0) { inv.Num["security.firewallKnown"] = known; inv.Num["security.firewallOff"] = off; }
        var uac = Reg(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
        if (uac is int u) inv.Num["security.uac"] = u != 0 ? 1 : 0;
        f.Add(new Fact(L("Controle de Conta de Usuário (UAC)", "User Account Control (UAC)"), uac is int u2 ? (u2 != 0 ? L("Ativo", "On") : L("Desativado", "Off")) : NA));
        var wu = Query("SELECT State, StartMode FROM Win32_Service WHERE Name='wuauserv'", null, "State", "StartMode").FirstOrDefault();
        if (wu != null)
        {
            inv.Num["security.wuDisabled"] = Str(wu["StartMode"]) == "Disabled" ? 1 : 0;
            f.Add(new Fact("Windows Update", $"{L("serviço", "service")}: {Str(wu["State"])} ({Str(wu["StartMode"])})"));
        }
        else f.Add(new Fact("Windows Update", NA));
    }

    private static void Platform(Inventory inv, List<Fact> f)
    {
        var sb = Reg(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        if (sb is int s) inv.Num["security.secureBoot"] = s;
        f.Add(new Fact("Secure Boot", sb is int s2 ? (s2 == 1 ? L("Ativo", "On") : L("Desativado", "Off")) : L("Indisponível (BIOS legada ou sem permissão)", "Unavailable (legacy BIOS or no permission)")));
        try
        {
            var tpm = Query("SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm", "IsEnabled_InitialValue", "IsActivated_InitialValue", "SpecVersion").FirstOrDefault();
            if (tpm == null) f.Add(new Fact("TPM", L("Não encontrado", "Not found")));
            else
            {
                inv.Num["security.tpm"] = tpm["IsEnabled_InitialValue"] is true ? 1 : 0;
                f.Add(new Fact("TPM", $"{(tpm["IsEnabled_InitialValue"] is true ? L("Ativo", "On") : L("Desativado", "Off"))} · {L("versão", "version")} {Str(tpm["SpecVersion"]).Split(',')[0]}"));
            }
        }
        catch (Exception ex) { inv.Errors.Add($"security/TPM: {ex.Message}"); f.Add(new Fact("TPM", L("Indisponível (exige administrador)", "Unavailable (requires administrator)"))); }
        try
        {
            var vols = Query("SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume", @"root\CIMV2\Security\MicrosoftVolumeEncryption", "DriveLetter", "ProtectionStatus");
            foreach (var v in vols) f.Add(new Fact($"BitLocker {Str(v["DriveLetter"])}", Dbl(v["ProtectionStatus"]) == 1 ? L("Protegido", "Protected") : Dbl(v["ProtectionStatus"]) == 0 ? L("Desativado", "Off") : NA));
            if (vols.Count > 0) inv.Num["security.bitlocker"] = vols.Any(v => Dbl(v["ProtectionStatus"]) == 1) ? 1 : 0;
        }
        catch (Exception ex) { inv.Errors.Add($"security/BitLocker: {ex.Message}"); f.Add(new Fact("BitLocker", L("Indisponível (exige administrador)", "Unavailable (requires administrator)"))); }
    }
}
