using StatsMonitor.Helpers;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace StatsMonitor.Services;

public sealed record InfoGroup(string Title, List<InfoLine> Lines);
/// <summary>CopyText is the text the copy button puts on the clipboard; null hides the button.</summary>
public sealed class InfoLine : System.ComponentModel.INotifyPropertyChanged
{
    private string _value;
    private string? _copyText;

    public InfoLine(string label, string value, string? copyText = null) { Label = label; _value = value; _copyText = copyText; }

    public string Label { get; }

    public string Value { get => _value; set { if (_value != value) { _value = value; Raise(nameof(Value)); } } }

    public string? CopyText { get => _copyText; set { if (_copyText != value) { _copyText = value; Raise(nameof(CopyText)); } } }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}

/// <summary>Collects static information about the computer through WMI.</summary>
public static class SystemInfoService
{
    internal const string NA = "N/D";

    private static Task<List<InfoGroup>>? _cache;

    private static string? _cacheLang;

    public static void Invalidate() => _cache = null;

    public static Task<List<InfoGroup>> GetAsync()
    {
        if (_cacheLang != Loc.Lang) { _cache = null; _cacheLang = Loc.Lang; }
        return _cache ??= Task.Run(Read);
    }

    public static List<InfoGroup> Read()
    {
        var groups = new List<InfoGroup>();

        var cs = First("Win32_ComputerSystem", "Name", "UserName", "Domain", "Manufacturer", "Model", "TotalPhysicalMemory");
        groups.Add(new(Loc.T("Computador"),
        [
            new(Loc.T("Nome do computador"), S(cs, "Name", Environment.MachineName), Copy(S(cs, "Name", Environment.MachineName))),
            new(Loc.T("Usuário"), S(cs, "UserName", Environment.UserName), Copy(S(cs, "UserName", Environment.UserName))),
            new(Loc.T("Domínio / grupo de trabalho"), S(cs, "Domain")),
            new(Loc.T("Fabricante"), S(cs, "Manufacturer"), Copy(S(cs, "Manufacturer"))),
            new(Loc.T("Modelo"), S(cs, "Model"), Copy(S(cs, "Model"))),
        ]));

        var os = First("Win32_OperatingSystem", "Caption", "Version", "BuildNumber", "OSArchitecture", "InstallDate", "LastBootUpTime");
        groups.Add(new(Loc.T("Sistema operacional"),
        [
            new(Loc.T("Sistema"), S(os, "Caption"), Copy(S(os, "Caption"))),
            new(Loc.T("Versão"), S(os, "Version"), Copy(S(os, "Version"))),
            new(Loc.T("Build"), S(os, "BuildNumber"), Copy(S(os, "BuildNumber"))),
            new(Loc.T("Arquitetura"), S(os, "OSArchitecture")),
            new(Loc.T("Instalado em"), Date(os, "InstallDate")),
            new(Loc.T("Último boot"), Date(os, "LastBootUpTime")),
            new(Loc.T("Runtime"), RuntimeInformation.FrameworkDescription),
        ]));

        var bb = First("Win32_BaseBoard", "Manufacturer", "Product", "SerialNumber");
        var bios = First("Win32_BIOS", "Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate");
        groups.Add(new(Loc.T("Placa-mãe e BIOS"),
        [
            new(Loc.T("Placa-mãe"), $"{S(bb, "Manufacturer")} {S(bb, "Product")}".Trim(), Copy($"{S(bb, "Manufacturer")} {S(bb, "Product")}".Trim())),
            new(Loc.T("BIOS"), $"{S(bios, "Manufacturer")} {S(bios, "SMBIOSBIOSVersion")}".Trim(), Copy($"{S(bios, "Manufacturer")} {S(bios, "SMBIOSBIOSVersion")}".Trim())),
            new(Loc.T("Data da BIOS"), Date(bios, "ReleaseDate")),
        ]));

        var cpu = First("Win32_Processor", "Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed");
        groups.Add(new(Loc.T("Processador"),
        [
            new(Loc.T("Modelo"), S(cpu, "Name"), Copy(S(cpu, "Name"))),
            new(Loc.T("Núcleos / threads"), $"{S(cpu, "NumberOfCores")} / {S(cpu, "NumberOfLogicalProcessors")}"),
            new(Loc.T("Velocidade máxima"), double.TryParse(S(cpu, "MaxClockSpeed"), out var mhz) ? $"{mhz / 1000:0.00} GHz" : NA),
        ]));

        var mem = new List<InfoLine>();
        double total = 0;
        foreach (var m in All("Win32_PhysicalMemory", "Capacity", "Speed", "Manufacturer", "PartNumber", "BankLabel"))
        {
            double gb = Convert.ToDouble(m["Capacity"] ?? 0, CultureInfo.InvariantCulture) / 1073741824.0;
            total += gb;
            mem.Add(new(S(m, "BankLabel", Loc.T("Módulo")), $"{gb:0} GB · {S(m, "Speed")} MHz · {S(m, "Manufacturer")} {S(m, "PartNumber")}".Trim()));
        }
        mem.Insert(0, new(Loc.T("Total instalado"), total > 0 ? $"{total:0} GB" : NA));
        groups.Add(new(Loc.T("Memória RAM"), mem));

        var gpu = new List<InfoLine>();
        foreach (var g in All("Win32_VideoController", "Name", "DriverVersion", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate"))
        {
            gpu.Add(new(S(g, "Name"), $"Driver {S(g, "DriverVersion")} · {S(g, "CurrentHorizontalResolution")}x{S(g, "CurrentVerticalResolution")} @ {S(g, "CurrentRefreshRate")} Hz", Copy(S(g, "Name"))));
        }
        groups.Add(new(Loc.T("Vídeo"), gpu.Count > 0 ? gpu : [new(Loc.T("GPU"), NA)]));

        var disks = new List<InfoLine>();
        foreach (var d in All("Win32_DiskDrive", "Model", "Size", "InterfaceType"))
        {
            double size = Convert.ToDouble(d["Size"] ?? 0, CultureInfo.InvariantCulture) / 1e9;
            disks.Add(new(S(d, "Model"), $"{size:0} GB · {S(d, "InterfaceType")}", Copy(S(d, "Model"))));
        }
        groups.Add(new(Loc.T("Armazenamento"), disks.Count > 0 ? disks : [new(Loc.T("Disco"), NA)]));

        return groups;
    }

    internal static IEnumerable<ManagementBaseObject> AllOf(string cls, params string[] props) => All(cls, props);

    private static string? Copy(string v) => v is "" or NA ? null : v;

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

    internal static string S(ManagementBaseObject? o, string prop, string fallback = NA)
    {
        try
        {
            string? v = o?[prop]?.ToString()?.Trim();
            return string.IsNullOrEmpty(v) ? fallback : v;
        }
        catch { return fallback; }
    }

    internal static string Date(ManagementBaseObject? o, string prop)
    {
        string v = S(o, prop, "");
        try { return v.Length > 0 ? ManagementDateTimeConverter.ToDateTime(v).ToString("dd/MM/yyyy HH:mm") : NA; }
        catch { return NA; }
    }
}
