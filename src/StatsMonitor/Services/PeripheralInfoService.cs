using StatsMonitor.Helpers;
using System.Globalization;
using System.Management;

namespace StatsMonitor.Services;

/// <summary>Collects information about the connected peripherals through WMI, grouped by context.</summary>
public static class PeripheralInfoService
{
    private const string NA = "N/D";

    public static Task<List<InfoGroup>> GetAsync() => Task.Run(Read);

    public static List<InfoGroup> Read()
    {
        var groups = new List<InfoGroup>
        {
            Group("Monitores", Monitors()),
            Group("Teclados", Lines("Win32_Keyboard", ["Name", "Description", "Status"], o => (S(o, "Name"), S(o, "Status")))),
            Group("Mouses e apontadores", Lines("Win32_PointingDevice", ["Name", "Manufacturer", "NumberOfButtons"],
                o => (S(o, "Name"), $"{S(o, "Manufacturer")} · {S(o, "NumberOfButtons")} {Loc.T("botões")}"))),
            Group("Áudio", Lines("Win32_SoundDevice", ["Name", "Manufacturer", "Status"], o => (S(o, "Name"), $"{S(o, "Manufacturer")} · {S(o, "Status")}"))),
            Group("Câmeras", Pnp("PNPClass = 'Camera' OR PNPClass = 'Image'")),
            Group("Impressoras", Lines("Win32_Printer", ["Name", "DriverName", "PortName", "Default", "WorkOffline"], o =>
                (S(o, "Name"), $"{S(o, "DriverName")} · {S(o, "PortName")}" + (S(o, "Default") == "True" ? $" · {Loc.T("padrão")}" : "")
                    + (S(o, "WorkOffline") == "True" ? $" · {Loc.T("offline")}" : "")))),
            Group("Bluetooth", Pnp("PNPClass = 'Bluetooth'", exclude: ["enumerator", "rfcomm", "protocol", "service", "transport", "avrcp", "device information"])),
            Group("Armazenamento externo (USB)", Lines("Win32_DiskDrive", ["Model", "Size", "InterfaceType", "MediaType"], o =>
                (S(o, "Model"), $"{Convert.ToDouble(o["Size"] ?? 0, CultureInfo.InvariantCulture) / 1e9:0.0} GB · {S(o, "MediaType")}"), "InterfaceType = 'USB'")),
            Group("Outros dispositivos USB", Pnp("PNPClass = 'USB'", exclude: ["hub", "controller", "composite", "root", "generic usb"])),
        };
        return groups;
    }

    private static InfoGroup Group(string title, List<InfoLine> lines) =>
        new(Loc.T(title), lines.Count > 0 ? lines : [new(Loc.T("Nenhum dispositivo detectado"), "-")]);

    private static List<InfoLine> Monitors()
    {
        var list = new List<InfoLine>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT ManufacturerName, UserFriendlyName, ProductCodeID, YearOfManufacture FROM WmiMonitorID");
            foreach (ManagementBaseObject m in searcher.Get())
            {
                string name = Decode(m["UserFriendlyName"]);
                string maker = Decode(m["ManufacturerName"]);
                string code = Decode(m["ProductCodeID"]);
                list.Add(new(string.IsNullOrWhiteSpace(name) ? Loc.T("Monitor") : name,
                    $"{maker} {code} · {S(m, "YearOfManufacture")}".Trim()));
            }
        }
        catch { /* class unavailable */ }

        foreach (var g in SystemInfoService.AllOf("Win32_VideoController", "Name", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate"))
        {
            if (S(g, "CurrentHorizontalResolution") == NA) continue;
            list.Add(new(Loc.T("Resolução ({0})", S(g, "Name")), $"{S(g, "CurrentHorizontalResolution")}x{S(g, "CurrentVerticalResolution")} @ {S(g, "CurrentRefreshRate")} Hz"));
        }
        return list;
    }

    private static string Decode(object? value) =>
        value is ushort[] arr ? new string(arr.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim() : "";

    private static List<InfoLine> Pnp(string where, string[]? exclude = null) =>
        Lines("Win32_PnPEntity", ["Name", "Manufacturer", "Status", "PNPClass"], o => (S(o, "Name"), $"{S(o, "Manufacturer")} · {S(o, "Status")}"), where, exclude);

    private static List<InfoLine> Lines(string cls, string[] props, Func<ManagementBaseObject, (string, string)> map, string? where = null, string[]? exclude = null)
    {
        var list = new List<InfoLine>();
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {string.Join(",", props)} FROM {cls}" + (where is null ? "" : $" WHERE {where}"));
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var (label, value) = map(o);
                if (exclude is not null && exclude.Any(x => label.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                if (list.Any(l => l.Label == label && l.Value == value)) continue;
                list.Add(new(label, value));
            }
        }
        catch { /* class unavailable */ }
        return list;
    }

    private static string S(ManagementBaseObject? o, string prop)
    {
        try
        {
            string? v = o?[prop]?.ToString()?.Trim();
            return string.IsNullOrEmpty(v) ? NA : v;
        }
        catch { return NA; }
    }
}
