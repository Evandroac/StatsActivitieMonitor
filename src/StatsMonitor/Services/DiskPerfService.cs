using System.Management;

namespace StatsMonitor.Services;

public readonly record struct DiskPerf(double ReadBps, double WriteBps, double Activity);

/// <summary>Windows performance counters per physical disk, used when the hardware library exposes no throughput or activity sensors.</summary>
public static class DiskPerfService
{
    private static Dictionary<int, string> _models = new();
    private static DateTime _modelsAt = DateTime.MinValue;
    public static volatile int CacheSeconds = 2;
    private static Dictionary<string, DiskPerf> _perf = new();
    private static DateTime _perfAt = DateTime.MinValue;

    private static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static DiskPerf? Find(string diskName)
    {
        try
        {
            if (DateTime.Now - _modelsAt > TimeSpan.FromSeconds(60) || _models.Count == 0)
            {
                _modelsAt = DateTime.Now;
                var models = new Dictionary<int, string>();
                using var s = Wmi.Searcher("SELECT Index, Model FROM Win32_DiskDrive");
                foreach (ManagementBaseObject d in s.Get())
                    if (d["Model"] is string m) models[Convert.ToInt32(d["Index"])] = m.Trim();
                _models = models;
            }

            int? index = _models.Where(kv => diskName.Contains(kv.Value, StringComparison.OrdinalIgnoreCase)
                                             || kv.Value.Contains(diskName, StringComparison.OrdinalIgnoreCase))
                                .Select(kv => (int?)kv.Key).FirstOrDefault();
            // Names from the hardware library and WMI may differ in spacing/punctuation; fall back to a normalized match, then to the only disk.
            index ??= _models.Where(kv => Norm(kv.Value).Length > 0 && Norm(diskName).Contains(Norm(kv.Value)) || Norm(kv.Value).Contains(Norm(diskName)))
                             .Select(kv => (int?)kv.Key).FirstOrDefault();
            if (index is null && _models.Count == 1) index = _models.Keys.First();

            if (DateTime.Now - _perfAt > TimeSpan.FromSeconds(CacheSeconds))
            {
                _perfAt = DateTime.Now;
                var perf = new Dictionary<string, DiskPerf>();
                using var q = Wmi.Searcher(
                    "SELECT Name, DiskReadBytesPersec, DiskWriteBytesPersec, PercentDiskTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");
                foreach (ManagementBaseObject o in q.Get())
                {
                    string name = o["Name"]?.ToString() ?? "";
                    perf[name == "_Total" ? name : name.Split(' ')[0]] = new DiskPerf(
                        Convert.ToDouble(o["DiskReadBytesPersec"]),
                        Convert.ToDouble(o["DiskWriteBytesPersec"]),
                        Math.Min(100, Convert.ToDouble(o["PercentDiskTime"])));
                }
                _perf = perf;
            }
            if (index is not null && _perf.TryGetValue(index.ToString()!, out var p)) return p;
            return _models.Count > 0 && _perf.TryGetValue("_Total", out var total) && _models.Count == 1 ? total : null;
        }
        catch { /* counters unavailable */ }
        return null;
    }
}
