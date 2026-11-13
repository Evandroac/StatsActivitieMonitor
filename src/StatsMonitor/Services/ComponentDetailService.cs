using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using StatsMonitor.Helpers;
using StatsMonitor.Models;

namespace StatsMonitor.Services;

public enum ComponentKind { Cpu, Gpu, Memory, Disk, Network, Sensors }

/// <summary>
/// Detailed per-component pages (hamburger menu): live values from the latest snapshots plus static hardware
/// details read once (WMI / Win32) and cached until the language changes or a rescan is requested.
/// </summary>
public static class ComponentDetailService
{
    private const string NA = SystemInfoService.NA;

    private static readonly object Gate = new();
    private static readonly Dictionary<ComponentKind, Task<List<InfoGroup>>> StaticCache = [];
    private static string? _cacheLang;

    public static void Invalidate() { lock (Gate) StaticCache.Clear(); }

    public static string Title(ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => T("Processador (CPU)", "Processor (CPU)"),
        ComponentKind.Gpu => T("Placa de vídeo (GPU)", "Graphics card (GPU)"),
        ComponentKind.Memory => T("Memória RAM", "RAM memory"),
        ComponentKind.Disk => T("Discos e armazenamento", "Disks and storage"),
        ComponentKind.Network => T("Rede", "Network"),
        _ => T("Sensores", "Sensors"),
    };

    public static async Task<List<InfoGroup>> GetAsync(ComponentKind kind, HardwareSnapshot? snap, NetworkSnapshot? net)
    {
        Task<List<InfoGroup>> task;
        lock (Gate)
        {
            if (_cacheLang != Loc.Lang) { StaticCache.Clear(); _cacheLang = Loc.Lang; }
            if (!StaticCache.TryGetValue(kind, out task!)) StaticCache[kind] = task = Wmi.RunAsync(() => ReadStatic(kind), Wmi.Unavailable());
        }
        var statics = await task;
        var live = kind is ComponentKind.Network or ComponentKind.Cpu or ComponentKind.Memory ? await Task.Run(() => Live(kind, snap, net)) : Live(kind, snap, net);
        return [.. live, .. statics];
    }

    // Portuguese is the source text; English is given inline.
    private static string T(string pt, string en) => Loc.Lang == "en" ? en : pt;

    private static List<InfoGroup> Live(ComponentKind kind, HardwareSnapshot? s, NetworkSnapshot? net) => kind switch
    {
        ComponentKind.Cpu => LiveCpu(s),
        ComponentKind.Gpu => LiveGpu(s),
        ComponentKind.Memory => LiveMemory(s),
        ComponentKind.Disk => LiveDisk(s),
        ComponentKind.Network => LiveNetwork(net),
        _ => LiveSensors(s),
    };

    private static List<InfoGroup> ReadStatic(ComponentKind kind)
    {
        try
        {
            return kind switch
            {
                ComponentKind.Cpu => StaticCpu(),
                ComponentKind.Gpu => StaticGpu(),
                ComponentKind.Memory => StaticMemory(),
                ComponentKind.Disk => StaticDisk(),
                _ => [],
            };
        }
        catch { return Wmi.Unavailable(); }
    }

    #region CPU

    private static List<InfoGroup> LiveCpu(HardwareSnapshot? s)
    {
        var counts = CpuInfoService.ReadCounts();
        return
        [
            new(T("Processador em tempo real", "Processor in real time"),
            [
                new(T("Nome", "Name"), s?.CpuName ?? NA, Copy(s?.CpuName)),
                new(T("Uso", "Usage"), Pct(s?.CpuLoad)),
                new(T("Frequência atual", "Current frequency"), Mhz(s?.CpuClockMhz)),
                new(T("Temperatura", "Temperature"), Temp(s?.CpuTemp)),
                new(T("Processos", "Processes"), counts is { } c ? c.Processes.ToString("N0") : NA),
                new("Threads", counts is { } c2 ? c2.Threads.ToString("N0") : NA),
                new(T("Identificadores", "Handles"), counts is { } c3 ? c3.Handles.ToString("N0") : NA),
                new(T("Tempo de atividade do sistema", "System uptime"), s is null ? NA : Uptime(s.Uptime)),
            ]),
            .. CpuExtras(s),
            new(T("Processos que mais usam CPU", "Top CPU processes"), Processes.TopCpu()),
        ];
    }

    private static List<InfoGroup> CpuExtras(HardwareSnapshot? s)
    {
        var result = new List<InfoGroup>();
        var extra = new List<InfoLine>();
        if (s?.CpuPowerW is { } w) extra.Add(new(T("Consumo do pacote", "Package power"), $"{w:0.0} W"));
        if (s?.CpuVoltage is { } v) extra.Add(new(T("Tensão do núcleo", "Core voltage"), $"{v:0.000} V"));
        if (s?.CpuHottestCoreTemp is { } t) extra.Add(new(T("Núcleo mais quente", "Hottest core"), Temp(t)));
        if (extra.Count > 0) result.Add(new(T("Energia e temperatura", "Power and temperature"), extra));
        if (s?.CpuCoreLoads is { Count: > 0 } loads)
        {
            var lines = loads.Select((l, i) => new InfoLine($"{T("Núcleo", "Core")} {i + 1}", Pct(l))).ToList();
            lines.Insert(0, new(T("Núcleo mais ocupado", "Busiest core"), $"{T("Núcleo", "Core")} {loads.ToList().IndexOf(loads.Max()) + 1} · {Pct(loads.Max())}"));
            result.Add(new(T("Uso por núcleo", "Per-core usage"), lines));
        }
        return result;
    }

    private static List<InfoGroup> StaticCpu()
    {
        var p = SystemInfoService.AllOf("Win32_Processor", "Name", "Manufacturer", "Description", "Architecture", "Family", "Revision",
            "Stepping", "ProcessorId", "SocketDesignation", "MaxClockSpeed", "ExtClock", "AddressWidth").FirstOrDefault();
        var info = CpuInfoService.Read();

        string arch = SystemInfoService.S(p, "Architecture", "") switch
        {
            "0" => "x86", "5" => "ARM", "9" => "x64", "12" => "ARM64", _ => NA,
        };
        string maxClock = double.TryParse(SystemInfoService.S(p, "MaxClockSpeed"), NumberStyles.Any, CultureInfo.InvariantCulture, out var mhz)
            ? $"{mhz / 1000:0.00} GHz" : NA;
        string ext = double.TryParse(SystemInfoService.S(p, "ExtClock"), NumberStyles.Any, CultureInfo.InvariantCulture, out var bus) && bus > 0
            ? $"{bus:0} MHz" : NA;
        string name = SystemInfoService.S(p, "Name");
        string id = SystemInfoService.S(p, "ProcessorId");

        return
        [
            new(T("Identificação", "Identification"),
            [
                new(T("Modelo", "Model"), name, Copy(name)),
                new(T("Fabricante", "Manufacturer"), SystemInfoService.S(p, "Manufacturer"), Copy(SystemInfoService.S(p, "Manufacturer"))),
                new(T("Descrição", "Description"), SystemInfoService.S(p, "Description")),
                new(T("Arquitetura", "Architecture"), arch),
                new(T("Largura de endereço", "Address width"), SystemInfoService.S(p, "AddressWidth") is var aw && aw != NA ? $"{aw} bits" : NA),
                new(T("Revisão / stepping", "Revision / stepping"), $"{SystemInfoService.S(p, "Revision")} / {SystemInfoService.S(p, "Stepping")}"),
                new(T("Soquete", "Socket"), SystemInfoService.S(p, "SocketDesignation"), Copy(SystemInfoService.S(p, "SocketDesignation"))),
                new(T("ID do processador", "Processor ID"), id, Copy(id)),
            ]),
            new(T("Núcleos e frequência", "Cores and frequency"),
            [
                new(T("Sockets", "Sockets"), info.Sockets.ToString()),
                new(T("Núcleos físicos", "Physical cores"), info.Cores.ToString()),
                new(T("Processadores lógicos", "Logical processors"), info.LogicalProcessors.ToString()),
                new(T("Velocidade base", "Base speed"), info.BaseSpeed),
                new(T("Velocidade máxima", "Maximum speed"), maxClock),
                new(T("Clock do barramento", "Bus clock"), ext),
                new(T("Virtualização", "Virtualization"), info.Virtualization),
            ]),
            new(T("Cache do processador", "Processor cache"), CacheLines(info.Cache)),
        ];
    }

    private static List<InfoLine> CacheLines(string cache)
    {
        if (cache == NA) return [new(T("Cache", "Cache"), NA)];
        return cache.Split(" · ", StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Split(": ", 2))
            .Where(p => p.Length == 2)
            .Select(p => new InfoLine(T("Cache ", "Cache ") + p[0], p[1]))
            .ToList();
    }

    #endregion

    #region GPU

    private static List<InfoGroup> LiveGpu(HardwareSnapshot? s) =>
    [
        new(T("GPU em tempo real", "GPU in real time"),
        [
            new(T("Nome", "Name"), s?.GpuName ?? NA, Copy(s?.GpuName)),
            new(T("Uso", "Usage"), Pct(s?.GpuLoad)),
            new(T("Frequência do núcleo", "Core clock"), Mhz(s?.GpuClockMhz)),
            new(T("Frequência da memória", "Memory clock"), Mhz(s?.GpuMemClockMhz)),
            new(T("Temperatura", "Temperature"), Temp(s?.GpuTemp)),
            new(T("Consumo energético", "Power draw"), s?.GpuPowerW is { } w ? $"{w:0.0} W" : NA),
            new(T("Ventoinha", "Fan"), s?.GpuFanRpm is { } f ? $"{f:0} RPM" : NA),
            new(T("Temperatura do hot spot", "Hot spot temperature"), Temp(s?.GpuHotSpotTemp)),
            new(T("Tensão", "Voltage"), s?.GpuVoltage is { } gv ? $"{gv:0.000} V" : NA),
            new(T("Controlador de memória", "Memory controller"), Pct(s?.GpuMemControllerLoad)),
            new(T("Motor de vídeo (codificação)", "Video engine (encode)"), Pct(s?.GpuVideoEngineLoad)),
        ]),
        new(T("Memória de vídeo (VRAM)", "Video memory (VRAM)"),
        [
            new(T("Total", "Total"), Gb(s?.VramTotalGb)),
            new(T("Em uso", "In use"), Gb(s?.VramUsedGb)),
            new(T("Disponível", "Available"), Gb(s?.VramAvailableGb)),
            new(T("Uso", "Usage"), Pct(s?.VramLoad)),
            new(T("Temperatura", "Temperature"), Temp(s?.VramTemp)),
        ]),
    ];

    private static List<InfoGroup> StaticGpu()
    {
        var list = SystemInfoService.AllOf("Win32_VideoController", "Name", "AdapterCompatibility", "VideoProcessor", "DriverVersion",
            "DriverDate", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate", "CurrentBitsPerPixel", "Status", "PNPDeviceID").ToList();
        var groups = new List<InfoGroup>();
        for (int i = 0; i < list.Count; i++)
        {
            var g = list[i];
            string name = SystemInfoService.S(g, "Name");
            string res = SystemInfoService.S(g, "CurrentHorizontalResolution") is var w && w != NA
                ? $"{w} x {SystemInfoService.S(g, "CurrentVerticalResolution")}" : NA;
            string hz = SystemInfoService.S(g, "CurrentRefreshRate") is var r && r != NA ? $"{r} Hz" : NA;
            string bpp = SystemInfoService.S(g, "CurrentBitsPerPixel") is var b && b != NA ? $"{b} bits" : NA;
            string title = T("Adaptador de vídeo", "Video adapter") + (list.Count > 1 ? $" {i + 1}" : "");
            groups.Add(new(title,
            [
                new(T("Modelo", "Model"), name, Copy(name)),
                new(T("Fabricante", "Manufacturer"), SystemInfoService.S(g, "AdapterCompatibility")),
                new(T("Processador de vídeo", "Video processor"), SystemInfoService.S(g, "VideoProcessor")),
                new(T("Versão do driver", "Driver version"), SystemInfoService.S(g, "DriverVersion"), Copy(SystemInfoService.S(g, "DriverVersion"))),
                new(T("Data do driver", "Driver date"), SystemInfoService.Date(g, "DriverDate")),
                new(T("Resolução atual", "Current resolution"), res),
                new(T("Taxa de atualização", "Refresh rate"), hz),
                new(T("Profundidade de cor", "Color depth"), bpp),
                new(T("Status", "Status"), SystemInfoService.S(g, "Status")),
                new(T("ID do dispositivo", "Device ID"), SystemInfoService.S(g, "PNPDeviceID"), Copy(SystemInfoService.S(g, "PNPDeviceID"))),
            ]));
        }
        return groups;
    }

    #endregion

    #region Memory

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private static List<InfoGroup> LiveMemory(HardwareSnapshot? s)
    {
        var groups = new List<InfoGroup>
        {
            new(T("Memória em tempo real", "Memory in real time"),
            [
                new(T("Total", "Total"), Gb(s?.RamTotalGb)),
                new(T("Em uso", "In use"), Gb(s?.RamUsedGb)),
                new(T("Disponível", "Available"), Gb(s?.RamAvailableGb)),
                new(T("Uso", "Usage"), Pct(s?.RamLoad)),
            ]),
        };

        groups.Add(new(T("Processos que mais usam memória", "Top memory processes"), Processes.TopMemory()));
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            double G(ulong v) => v / 1073741824.0;
            double commitUsed = G(m.TotalPageFile - m.AvailPageFile);
            groups.Add(new(T("Memória virtual e commit", "Virtual memory and commit"),
            [
                new(T("Limite de commit", "Commit limit"), $"{G(m.TotalPageFile):0.0} GB"),
                new(T("Commit em uso", "Commit in use"), $"{commitUsed:0.0} GB ({commitUsed / Math.Max(G(m.TotalPageFile), 0.01) * 100:0}%)"),
                new(T("Arquivo de paginação e RAM livres", "Free page file and RAM"), $"{G(m.AvailPageFile):0.0} GB"),
                new(T("Espaço de endereçamento virtual", "Virtual address space"), $"{G(m.TotalVirtual):0} GB"),
            ]));
        }
        return groups;
    }

    private static List<InfoGroup> StaticMemory()
    {
        var groups = new List<InfoGroup>();
        var array = SystemInfoService.AllOf("Win32_PhysicalMemoryArray", "MemoryDevices", "MaxCapacity").FirstOrDefault();
        var modules = SystemInfoService.AllOf("Win32_PhysicalMemory", "Capacity", "Speed", "ConfiguredClockSpeed", "Manufacturer", "PartNumber",
            "BankLabel", "DeviceLocator", "SMBIOSMemoryType", "FormFactor", "ConfiguredVoltage").ToList();

        double total = modules.Sum(x => Convert.ToDouble(x["Capacity"] ?? 0, CultureInfo.InvariantCulture)) / 1073741824.0;
        string maxCap = double.TryParse(SystemInfoService.S(array, "MaxCapacity"), NumberStyles.Any, CultureInfo.InvariantCulture, out var kb) && kb > 0
            ? $"{kb / 1048576.0:0} GB" : NA;
        string slots = SystemInfoService.S(array, "MemoryDevices");
        groups.Add(new(T("Configuração da memória", "Memory configuration"),
        [
            new(T("Total instalado", "Total installed"), total > 0 ? $"{total:0} GB" : NA),
            new(T("Módulos instalados", "Installed modules"), modules.Count.ToString()),
            new(T("Slots totais", "Total slots"), slots),
            new(T("Capacidade máxima suportada", "Maximum supported capacity"), maxCap),
        ]));

        for (int i = 0; i < modules.Count; i++)
        {
            var m = modules[i];
            double gb = Convert.ToDouble(m["Capacity"] ?? 0, CultureInfo.InvariantCulture) / 1073741824.0;
            string type = SystemInfoService.S(m, "SMBIOSMemoryType", "") switch
            {
                "20" => "DDR", "21" => "DDR2", "24" => "DDR3", "26" => "DDR4", "34" => "DDR5", "35" => "LPDDR5", "30" => "LPDDR4", _ => NA,
            };
            string form = SystemInfoService.S(m, "FormFactor", "") switch
            {
                "8" => "DIMM", "12" => "SO-DIMM", "13" => "Micro-DIMM", _ => NA,
            };
            string part = SystemInfoService.S(m, "PartNumber");
            string volt = double.TryParse(SystemInfoService.S(m, "ConfiguredVoltage"), NumberStyles.Any, CultureInfo.InvariantCulture, out var mv) && mv > 0
                ? $"{mv / 1000:0.00} V" : NA;
            groups.Add(new($"{T("Módulo de memória", "Memory module")} {i + 1}",
            [
                new(T("Local", "Location"), $"{SystemInfoService.S(m, "DeviceLocator")} ({SystemInfoService.S(m, "BankLabel")})"),
                new(T("Capacidade", "Capacity"), $"{gb:0} GB"),
                new(T("Tipo", "Type"), $"{type} · {form}"),
                new(T("Velocidade nominal", "Rated speed"), SystemInfoService.S(m, "Speed") is var sp && sp != NA ? $"{sp} MHz" : NA),
                new(T("Velocidade configurada", "Configured speed"), SystemInfoService.S(m, "ConfiguredClockSpeed") is var cs && cs != NA ? $"{cs} MHz" : NA),
                new(T("Tensão configurada", "Configured voltage"), volt),
                new(T("Fabricante", "Manufacturer"), SystemInfoService.S(m, "Manufacturer")),
                new(T("Número da peça", "Part number"), part, Copy(part)),
            ]));
        }
        return groups;
    }

    #endregion

    #region Disk

    private static List<InfoGroup> LiveDisk(HardwareSnapshot? s)
    {
        var groups = new List<InfoGroup>();
        foreach (var d in s?.Disks ?? [])
        {
            groups.Add(new($"{T("Disco", "Disk")}: {d.Name}",
            [
                new(T("Temperatura", "Temperature"), Temp(d.Temp)),
                new(T("Velocidade de leitura", "Read speed"), Rate(d.ReadBps)),
                new(T("Velocidade de gravação", "Write speed"), Rate(d.WriteBps)),
                new(T("Tempo de atividade", "Active time"), Pct(d.Activity)),
                new(T("Vida útil restante", "Remaining life"), Pct(d.LifePercent)),
                new(T("Saúde (S.M.A.R.T.)", "Health (S.M.A.R.T.)"), d.Health ?? NA),
                new(T("Total lido", "Total read"), d.TotalReadGb is { } tr ? $"{tr:0.0} GB" : NA),
                new(T("Total gravado", "Total written"), d.TotalWrittenGb is { } tw ? $"{tw:0.0} GB" : NA),
                new(T("Horas ligado", "Power-on hours"), d.PowerOnHours is { } ph ? $"{ph:N0} h" : NA),
            ]));
        }

        var vols = new List<InfoLine>();
        foreach (var v in s?.Volumes ?? [])
        {
            double used = v.TotalGb - v.FreeGb;
            string fs = FileSystem(v.Name);
            vols.Add(new(v.Name, $"{v.FreeGb:0.0} GB {T("livres de", "free of")} {v.TotalGb:0.0} GB · {used / Math.Max(v.TotalGb, 0.01) * 100:0}% {T("usado", "used")}{(fs.Length > 0 ? " · " + fs : "")}"));
        }
        if (vols.Count > 0) groups.Add(new(T("Volumes (unidades)", "Volumes (drives)"), vols));
        if (s?.Volumes is { Count: > 0 } all)
        {
            double tot = all.Sum(v => v.TotalGb), free = all.Sum(v => v.FreeGb);
            var fullest = all.OrderBy(v => v.FreeGb / Math.Max(v.TotalGb, 0.01)).First();
            groups.Insert(0, new(T("Resumo de armazenamento", "Storage summary"),
            [
                new(T("Capacidade total", "Total capacity"), $"{tot:0.0} GB"),
                new(T("Espaço livre total", "Total free space"), $"{free:0.0} GB ({free / Math.Max(tot, 0.01) * 100:0}%)"),
                new(T("Volume mais cheio", "Fullest volume"), $"{fullest.Name} · {(1 - fullest.FreeGb / Math.Max(fullest.TotalGb, 0.01)) * 100:0}% {T("usado", "used")}"),
            ]));
        }
        if (groups.Count == 0) groups.Add(new(T("Discos em tempo real", "Disks in real time"), [new(T("Disco", "Disk"), NA)]));
        return groups;
    }

    private static string FileSystem(string name)
    {
        try { return new DriveInfo(name.TrimEnd('\\').Substring(0, 1)).DriveFormat; }
        catch { return ""; }
    }

    private static List<InfoGroup> StaticDisk()
    {
        var kinds = new Dictionary<string, (string Media, string Bus, string Spindle)>();
        try
        {
            using var searcher = Wmi.Searcher("SELECT DeviceId, MediaType, BusType, SpindleSpeed FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
            foreach (var o in searcher.Get())
            {
                string media = o["MediaType"]?.ToString() switch { "3" => "HDD", "4" => "SSD", "5" => "SCM", _ => NA };
                string bus = o["BusType"]?.ToString() switch
                {
                    "17" => "NVMe", "11" => "SATA", "7" => "USB", "8" => "RAID", "10" => "SAS", "3" => "ATA", "1" => "SCSI", _ => NA,
                };
                string spin = o["SpindleSpeed"] is { } sp && Convert.ToUInt32(sp) is > 0 and < uint.MaxValue ? $"{sp} RPM" : NA;
                kinds[o["DeviceId"]?.ToString() ?? ""] = (media, bus, spin);
            }
        }
        catch { /* storage namespace unavailable */ }

        var groups = new List<InfoGroup>();
        var disks = SystemInfoService.AllOf("Win32_DiskDrive", "Model", "SerialNumber", "InterfaceType", "MediaType", "FirmwareRevision",
            "Size", "Partitions", "BytesPerSector", "Index", "Status").ToList();
        for (int i = 0; i < disks.Count; i++)
        {
            var d = disks[i];
            double size = Convert.ToDouble(d["Size"] ?? 0, CultureInfo.InvariantCulture) / 1e9;
            kinds.TryGetValue(SystemInfoService.S(d, "Index", ""), out var k);
            string model = SystemInfoService.S(d, "Model");
            string serial = SystemInfoService.S(d, "SerialNumber");
            groups.Add(new($"{T("Detalhes do disco", "Disk details")} {i + 1}",
            [
                new(T("Modelo", "Model"), model, Copy(model)),
                new(T("Tipo de mídia", "Media type"), k.Media ?? SystemInfoService.S(d, "MediaType")),
                new(T("Barramento", "Bus"), k.Bus ?? SystemInfoService.S(d, "InterfaceType")),
                new(T("Rotação (HDD)", "Spindle speed (HDD)"), k.Spindle ?? NA),
                new(T("Capacidade", "Capacity"), size > 0 ? $"{size:0.0} GB" : NA),
                new(T("Partições", "Partitions"), SystemInfoService.S(d, "Partitions")),
                new(T("Bytes por setor", "Bytes per sector"), SystemInfoService.S(d, "BytesPerSector")),
                new(T("Firmware", "Firmware"), SystemInfoService.S(d, "FirmwareRevision"), Copy(SystemInfoService.S(d, "FirmwareRevision"))),
                new(T("Número de série", "Serial number"), serial, Copy(serial)),
                new(T("Status", "Status"), SystemInfoService.S(d, "Status")),
            ]));
        }
        return groups;
    }

    #endregion

    #region Network

    private static List<InfoGroup> LiveNetwork(NetworkSnapshot? n)
    {
        var groups = new List<InfoGroup>
        {
            new(T("Rede em tempo real", "Network in real time"),
            [
                new(T("Status", "Status"), n is null ? NA : !n.Connected ? T("Sem conexão", "Disconnected") : n.InternetReachable ? T("Conectado", "Connected") : T("Sem acesso à internet", "No internet access")),
                new(T("Adaptador ativo", "Active adapter"), n?.Connected == true ? $"{n.ConnectionType} · {n.AdapterName}" : NA),
                new(T("IP local", "Local IP"), n?.LocalIp ?? NA, Copy(n?.LocalIp)),
                new(T("Gateway", "Gateway"), n?.Gateway ?? NA, Copy(n?.Gateway)),
                new(T("Velocidade do link", "Link speed"), n?.LinkSpeedMbps is { } l ? Link(l) : NA),
                new(T("Download", "Download"), n is null ? NA : Bits(n.DownloadBps)),
                new(T("Upload", "Upload"), n is null ? NA : Bits(n.UploadBps)),
                new(T("Latência", "Latency"), n?.LatencyMs is { } ms ? $"{ms} ms" : NA),
                new(T("Total recebido", "Total received"), n is null ? NA : Bytes(n.TotalReceived)),
                new(T("Total enviado", "Total sent"), n is null ? NA : Bytes(n.TotalSent)),
            ]),
        };

        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(i => i.OperationalStatus == OperationalStatus.Up)
                .ThenBy(i => i.Name);
            foreach (var nic in nics) groups.Add(Adapter(nic));
        }
        catch (NetworkInformationException) { }
        return groups;
    }

    private static InfoGroup Adapter(NetworkInterface nic)
    {
        IPInterfaceProperties? ip = null;
        try { ip = nic.GetIPProperties(); } catch (NetworkInformationException) { }
        var v4 = ip?.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        var v6 = ip?.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal);
        string mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
        string? dns = ip is null ? null : string.Join(", ", ip.DnsAddresses.Select(a => a.ToString()));
        string? gw = ip?.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
        bool dhcp = false;
        try { dhcp = ip?.GetIPv4Properties().IsDhcpEnabled ?? false; } catch (NetworkInformationException) { }
        long rx = 0, tx = 0;
        try { var st = nic.GetIPv4Statistics(); rx = st.BytesReceived; tx = st.BytesSent; } catch (NetworkInformationException) { }

        return new($"{T("Adaptador de rede", "Network adapter")}: {nic.Name}",
        [
            new(T("Descrição", "Description"), nic.Description),
            new(T("Tipo", "Type"), nic.NetworkInterfaceType.ToString()),
            new(T("Status", "Status"), nic.OperationalStatus == OperationalStatus.Up ? T("Ativo", "Up") : nic.OperationalStatus.ToString()),
            new(T("Endereço MAC", "MAC address"), mac.Length > 0 ? mac : NA, Copy(mac)),
            new(T("Velocidade", "Speed"), nic.Speed > 0 ? Link(nic.Speed / 1e6) : NA),
            new(T("IPv4", "IPv4"), v4?.Address.ToString() ?? NA, Copy(v4?.Address.ToString())),
            new(T("Máscara de sub-rede", "Subnet mask"), v4?.IPv4Mask?.ToString() ?? NA),
            new(T("IPv6", "IPv6"), v6?.Address.ToString() ?? NA, Copy(v6?.Address.ToString())),
            new(T("Gateway", "Gateway"), gw ?? NA, Copy(gw)),
            new(T("Servidores DNS", "DNS servers"), string.IsNullOrEmpty(dns) ? NA : dns, Copy(dns)),
            new(T("DHCP", "DHCP"), dhcp ? T("Habilitado", "Enabled") : T("Desabilitado", "Disabled")),
            new(T("Dados recebidos", "Data received"), Bytes(rx)),
            new(T("Dados enviados", "Data sent"), Bytes(tx)),
        ]);
    }

    #endregion

    #region Sensors

    private static TimeSpan _selfCpu;
    private static DateTime _selfAt;

    // Lets the user verify how little the monitor itself costs.
    private static InfoGroup SelfUsage()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        string cpu = NA;
        if (_selfAt != default && (now - _selfAt).TotalSeconds >= 0.5)
            cpu = $"{(p.TotalProcessorTime - _selfCpu).TotalSeconds / (now - _selfAt).TotalSeconds / Environment.ProcessorCount * 100:0.0}%";
        if (_selfAt == default || (now - _selfAt).TotalSeconds >= 0.5) { _selfCpu = p.TotalProcessorTime; _selfAt = now; }
        return new(T("Consumo do Stats Monitor", "Stats Monitor resource usage"),
        [
            new(T("Uso de CPU do app", "App CPU usage"), cpu),
            new(T("Memória do app", "App memory"), $"{p.WorkingSet64 / 1048576.0:0} MB"),
            new("Threads", p.Threads.Count.ToString()),
            new(T("Tempo de CPU acumulado", "Total CPU time"), $"{p.TotalProcessorTime.TotalSeconds:0.0} s"),
        ]);
    }

    private static List<InfoGroup> LiveSensors(HardwareSnapshot? s)
    {
        var groups = new List<InfoGroup> { SelfUsage() };
        foreach (var cat in Enum.GetValues<SensorCategory>())
        {
            var lines = (s?.Sensors ?? []).Where(x => x.Category == cat)
                .Select(x => new InfoLine(x.Name, $"{x.Value:0.##} {x.Unit}".Trim())).ToList();
            if (lines.Count == 0) continue;
            groups.Add(new(cat switch
            {
                SensorCategory.Fan => T("Ventoinhas", "Fans"),
                SensorCategory.Power => T("Energia", "Power"),
                SensorCategory.Voltage => T("Tensão", "Voltage"),
                _ => T("Bateria", "Battery"),
            } + " (" + T("sensor", "sensor") + ")", lines));
        }

        if (groups.Count == 0)
        {
            string note = s is { IsElevated: false }
                ? T("Nenhum sensor disponível. Execute como administrador para ler mais sensores.", "No sensors available. Run as administrator to read more sensors.")
                : T("Nenhum sensor disponível neste computador.", "No sensors available on this computer.");
            groups.Add(new(T("Sensores", "Sensors"), [new(note, "")]));
        }
        return groups;
    }

    #endregion

    private static class Processes
    {
        private static readonly Dictionary<int, TimeSpan> Last = [];
        private static DateTime _at;
        private static List<InfoLine> _cpu = [];

        public static List<InfoLine> TopMemory()
        {
            var list = new List<(string Name, long Bytes)>();
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                using (p) { try { list.Add((p.ProcessName, p.WorkingSet64)); } catch { } }
            }
            return list.GroupBy(x => x.Name).Select(g => (Name: g.Key, Bytes: g.Sum(x => x.Bytes)))
                .OrderByDescending(x => x.Bytes).Take(5)
                .Select(x => new InfoLine(x.Name, $"{x.Bytes / 1048576.0:N0} MB")).ToList();
        }

        public static List<InfoLine> TopCpu()
        {
            lock (Last)
            {
                var now = DateTime.UtcNow;
                var current = new Dictionary<int, (string Name, TimeSpan Cpu)>();
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    using (p) { try { current[p.Id] = (p.ProcessName, p.TotalProcessorTime); } catch { } }
                }
                double elapsed = (now - _at).TotalSeconds;
                if (_at != default && elapsed >= 0.5)
                {
                    _cpu = current.Where(c => Last.ContainsKey(c.Key))
                        .Select(c => (c.Value.Name, Pct: (c.Value.Cpu - Last[c.Key]).TotalSeconds / elapsed / Environment.ProcessorCount * 100))
                        .GroupBy(x => x.Name).Select(g => (Name: g.Key, Pct: g.Sum(x => x.Pct)))
                        .Where(x => x.Pct > 0.05).OrderByDescending(x => x.Pct).Take(5)
                        .Select(x => new InfoLine(x.Name, $"{x.Pct:0.0}%")).ToList();
                }
                else if (_at != default) return _cpu;
                Last.Clear();
                foreach (var c in current) Last[c.Key] = c.Value.Cpu;
                _at = now;
                return _cpu.Count > 0 ? _cpu : [new(T("Medindo…", "Measuring…"), "")];
            }
        }
    }

    #region Formatting

    private static string? Copy(string? v) => string.IsNullOrWhiteSpace(v) || v == NA ? null : v;

    private static string Pct(double? v) => v is { } x ? $"{x:0.0}%" : NA;

    private static string Mhz(double? v) => v is { } x ? (x >= 1000 ? $"{x / 1000:0.00} GHz" : $"{x:0} MHz") : NA;

    private static string Temp(double? v) => v is { } x && x > 0 ? Units.Temp(x) : NA;

    private static string Gb(double? v) => v is { } x ? $"{x:0.00} GB" : NA;

    private static string Rate(double? bytesPerSec) => bytesPerSec is not { } b ? NA
        : b >= 1 << 20 ? $"{b / (1 << 20):0.0} MB/s" : b >= 1024 ? $"{b / 1024:0} KB/s" : $"{b:0} B/s";

    private static string Bits(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        return bits >= 1e6 ? $"{bits / 1e6:0.0} Mbps" : bits >= 1e3 ? $"{bits / 1e3:0} Kbps" : $"{bits:0} bps";
    }

    private static string Link(double mbps) => mbps >= 1000 ? $"{mbps / 1000:0.#} Gbps" : $"{mbps:0} Mbps";

    private static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        _ => $"{b / 1024.0:0} KB",
    };

    private static string Uptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}min" : $"{t.Hours}h {t.Minutes}min";

    #endregion
}
