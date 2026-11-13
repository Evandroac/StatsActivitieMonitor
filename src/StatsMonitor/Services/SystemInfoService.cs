using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace StatsMonitor.Services;

public sealed record InfoGroup(string Title, List<InfoLine> Lines);
public sealed record InfoLine(string Label, string Value);

/// <summary>Collects static information about the computer through WMI.</summary>
public static class SystemInfoService
{
    private const string NA = "N/D";

    private static Task<List<InfoGroup>>? _cache;

    public static Task<List<InfoGroup>> GetAsync() => _cache ??= Task.Run(Read);

    public static List<InfoGroup> Read()
    {
        var groups = new List<InfoGroup>();

        var cs = First("Win32_ComputerSystem", "Name", "UserName", "Domain", "Manufacturer", "Model", "TotalPhysicalMemory");
        groups.Add(new("Computador",
        [
            new("Nome do computador", S(cs, "Name", Environment.MachineName)),
            new("Usuário", S(cs, "UserName", Environment.UserName)),
            new("Domínio / grupo de trabalho", S(cs, "Domain")),
            new("Fabricante", S(cs, "Manufacturer")),
            new("Modelo", S(cs, "Model")),
        ]));

        var os = First("Win32_OperatingSystem", "Caption", "Version", "BuildNumber", "OSArchitecture", "InstallDate", "LastBootUpTime");
        groups.Add(new("Sistema operacional",
        [
            new("Sistema", S(os, "Caption")),
            new("Versão", S(os, "Version")),
            new("Build", S(os, "BuildNumber")),
            new("Arquitetura", S(os, "OSArchitecture")),
            new("Instalado em", Date(os, "InstallDate")),
            new("Último boot", Date(os, "LastBootUpTime")),
            new("Runtime", RuntimeInformation.FrameworkDescription),
        ]));

        var bb = First("Win32_BaseBoard", "Manufacturer", "Product", "SerialNumber");
        var bios = First("Win32_BIOS", "Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate");
        groups.Add(new("Placa-mãe e BIOS",
        [
            new("Placa-mãe", $"{S(bb, "Manufacturer")} {S(bb, "Product")}".Trim()),
            new("BIOS", $"{S(bios, "Manufacturer")} {S(bios, "SMBIOSBIOSVersion")}".Trim()),
            new("Data da BIOS", Date(bios, "ReleaseDate")),
        ]));

        var cpu = First("Win32_Processor", "Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed");
        groups.Add(new("Processador",
        [
            new("Modelo", S(cpu, "Name")),
            new("Núcleos / threads", $"{S(cpu, "NumberOfCores")} / {S(cpu, "NumberOfLogicalProcessors")}"),
            new("Velocidade máxima", double.TryParse(S(cpu, "MaxClockSpeed"), out var mhz) ? $"{mhz / 1000:0.00} GHz" : NA),
        ]));

        var mem = new List<InfoLine>();
        double total = 0;
        foreach (var m in All("Win32_PhysicalMemory", "Capacity", "Speed", "Manufacturer", "PartNumber", "BankLabel"))
        {
            double gb = Convert.ToDouble(m["Capacity"] ?? 0, CultureInfo.InvariantCulture) / 1073741824.0;
            total += gb;
            mem.Add(new(S(m, "BankLabel", "Módulo"), $"{gb:0} GB · {S(m, "Speed")} MHz · {S(m, "Manufacturer")} {S(m, "PartNumber")}".Trim()));
        }
        mem.Insert(0, new("Total instalado", total > 0 ? $"{total:0} GB" : NA));
        groups.Add(new("Memória RAM", mem));

        var gpu = new List<InfoLine>();
        foreach (var g in All("Win32_VideoController", "Name", "DriverVersion", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate"))
        {
            gpu.Add(new(S(g, "Name"), $"Driver {S(g, "DriverVersion")} · {S(g, "CurrentHorizontalResolution")}x{S(g, "CurrentVerticalResolution")} @ {S(g, "CurrentRefreshRate")} Hz"));
        }
        groups.Add(new("Vídeo", gpu.Count > 0 ? gpu : [new("GPU", NA)]));

        var disks = new List<InfoLine>();
        foreach (var d in All("Win32_DiskDrive", "Model", "Size", "InterfaceType"))
        {
            double size = Convert.ToDouble(d["Size"] ?? 0, CultureInfo.InvariantCulture) / 1e9;
            disks.Add(new(S(d, "Model"), $"{size:0} GB · {S(d, "InterfaceType")}"));
        }
        groups.Add(new("Armazenamento", disks.Count > 0 ? disks : [new("Disco", NA)]));

        return groups;
    }

    private static IEnumerable<ManagementBaseObject> All(string cls, params string[] props)
    {
        var list = new List<ManagementBaseObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}");
            foreach (var o in searcher.Get()) list.Add(o);
        }
        catch { /* class unavailable */ }
        return list;
    }

    private static ManagementBaseObject? First(string cls, params string[] props) => All(cls, props).FirstOrDefault();

    private static string S(ManagementBaseObject? o, string prop, string fallback = NA)
    {
        try
        {
            string? v = o?[prop]?.ToString()?.Trim();
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
        catch { return fallback; }
    }

    private static string Date(ManagementBaseObject? o, string prop)
    {
        string v = S(o, prop, "");
        try { return v.Length > 0 ? ManagementDateTimeConverter.ToDateTime(v).ToString("dd/MM/yyyy HH:mm") : NA; }
        catch { return NA; }
    }
}
