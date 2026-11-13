using System.Management;

namespace StatsMonitor.Services;

public readonly record struct DiskPerf(double ReadBps, double WriteBps, double Activity);

/// <summary>Windows performance counters per physical disk, used when the hardware library exposes no throughput or activity sensors.</summary>
public static class DiskPerfService
{
    private static Dictionary<int, string> _models = new();
    private static DateTime _modelsAt = DateTime.MinValue;

    public static DiskPerf? Find(string diskName)
    {
        try
        {
            if (DateTime.Now - _modelsAt > TimeSpan.FromSeconds(60) || _models.Count == 0)
            {
                _modelsAt = DateTime.Now;
                var models = new Dictionary<int, string>();
                using var s = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
                foreach (ManagementBaseObject d in s.Get())
                    if (d["Model"] is string m) models[Convert.ToInt32(d["Index"])] = m.Trim();
                _models = models;
            }

            int? index = _models.Where(kv => diskName.Contains(kv.Value, StringComparison.OrdinalIgnoreCase)
                                             || kv.Value.Contains(diskName, StringComparison.OrdinalIgnoreCase))
                                .Select(kv => (int?)kv.Key).FirstOrDefault();
            if (index is null) return null;

            using var q = new ManagementObjectSearcher(
                "SELECT Name, DiskReadBytesPersec, DiskWriteBytesPersec, PercentDiskTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
            foreach (ManagementBaseObject o in q.Get())
            {
                string name = o["Name"]?.ToString() ?? "";
                if (name != index.ToString() && !name.StartsWith(index + " ")) continue;
                return new DiskPerf(
                    Convert.ToDouble(o["DiskReadBytesPersec"]),
                    Convert.ToDouble(o["DiskWriteBytesPersec"]),
                    Math.Min(100, Convert.ToDouble(o["PercentDiskTime"])));
            }
        }
        catch { /* counters unavailable */ }
        return null;
    }
}
