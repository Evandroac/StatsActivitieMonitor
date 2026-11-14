using System.Globalization;
using System.Runtime.InteropServices;

namespace StatsMonitor.Services;

/// <summary>Extra RAM facts for the main card: commit charge and installed module summary.</summary>
public static class MemoryInfoService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>Commit charge (used, limit) in GB, or null if unavailable.</summary>
    public static (double Used, double Limit)? ReadCommit()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m)) return null;
        const double gb = 1073741824.0;
        return ((m.TotalPageFile - m.AvailPageFile) / gb, m.TotalPageFile / gb);
    }

    /// <summary>Returns (modules, "DDR4 · 3200 MHz") from WMI; blocking, call off the UI thread.</summary>
    public static (string Modules, string TypeSpeed) ReadModules()
    {
        try
        {
            var mods = SystemInfoService.AllOf("Win32_PhysicalMemory", "Capacity", "Speed", "ConfiguredClockSpeed", "SMBIOSMemoryType").ToList();
            if (mods.Count == 0) return ("--", "--");
            var parts = mods.Select(x => Convert.ToDouble(x["Capacity"] ?? 0, CultureInfo.InvariantCulture) / 1073741824.0)
                            .Select(g => $"{g:0} GB");
            var first = mods[0];
            string type = SystemInfoService.S(first, "SMBIOSMemoryType", "") switch
            {
                "20" => "DDR", "21" => "DDR2", "24" => "DDR3", "26" => "DDR4", "34" => "DDR5", "35" => "LPDDR5", "30" => "LPDDR4", _ => "",
            };
            string speed = SystemInfoService.S(first, "ConfiguredClockSpeed", "");
            if (speed is "" or "0") speed = SystemInfoService.S(first, "Speed", "");
            string ts = string.Join(" · ", new[] { type, speed is "" or "0" ? "" : speed + " MHz" }.Where(s => s.Length > 0));
            return ($"{mods.Count}× ({string.Join(" + ", parts)})", ts.Length > 0 ? ts : "--");
        }
        catch { return ("--", "--"); }
    }
}
