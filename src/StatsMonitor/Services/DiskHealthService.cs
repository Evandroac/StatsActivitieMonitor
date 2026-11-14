using System.Management;

namespace StatsMonitor.Services;

/// <summary>Reads the Windows storage health status (backed by SMART) per physical disk.</summary>
public static class DiskHealthService
{
    public static Dictionary<string, string> Read()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = Wmi.Searcher("SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
            foreach (ManagementObject d in searcher.Get())
            {
                string? name = (d["FriendlyName"] as string)?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                result[name] = Convert.ToInt32(d["HealthStatus"]) switch
                {
                    0 => "Saudável",
                    1 => "Atenção",
                    2 => "Falha iminente",
                    _ => "N/D",
                };
            }
        }
        catch { /* WMI unavailable: health stays N/D */ }
        return result;
    }
}
