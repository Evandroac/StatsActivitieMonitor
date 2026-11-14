using System.IO;
using System.Text.Json;
using StatsMonitor.Models;
using StatsMonitor.Services;

namespace StatsMonitor.Diagnostics;

public sealed record LiveSample(DateTime Time, double? Cpu, double? CpuTemp, double? Ram, double? Gpu, double? GpuTemp, double? Disk, double? NetMbps);

public sealed class LiveAlertConfig
{
    public bool Enabled { get; set; } = true;
    public double Cpu { get; set; } = 95;
    public double CpuTemp { get; set; } = 90;
    public double Ram { get; set; } = 95;
    public double Gpu { get; set; } = 98;
    public double GpuTemp { get; set; } = 90;
    public double Disk { get; set; } = 98;
    /// <summary>Consecutive seconds above the limit before an alert is raised.</summary>
    public int Seconds { get; set; } = 10;
}

/// <summary>In-memory ring buffer fed from the app's existing snapshots (no extra sensor polling). Only active while the diagnostics page is open.</summary>
public sealed class LiveMonitor
{
    public const int Capacity = 3600;
    private readonly List<LiveSample> _buf = new();
    private readonly Dictionary<string, int> _streak = new();
    private readonly Dictionary<string, DateTime> _lastAlert = new();
    private readonly string _cfgPath;

    public LiveAlertConfig Config { get; private set; } = new();
    public event Action<string>? Alert;

    public LiveMonitor(string dir)
    {
        _cfgPath = Path.Combine(dir, "live-alerts.json");
        try { if (File.Exists(_cfgPath)) Config = JsonSerializer.Deserialize<LiveAlertConfig>(File.ReadAllText(_cfgPath)) ?? new(); } catch { }
    }

    public void SaveConfig() { try { File.WriteAllText(_cfgPath, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true })); } catch { } }

    public void Add(HardwareSnapshot s, NetworkSnapshot? net)
    {
        var disk = s.Disks.Select(d => d.Activity).Where(a => a != null).DefaultIfEmpty(null).Max();
        Add(new LiveSample(DateTime.Now, s.CpuLoad, s.CpuTemp is > 0 ? s.CpuTemp : null, s.RamLoad, s.GpuLoad, s.GpuTemp is > 0 ? s.GpuTemp : null, disk,
            net is null ? null : (net.DownloadBps + net.UploadBps) * 8 / 1e6));
    }

    public void Add(LiveSample x)
    {
        lock (_buf) { _buf.Add(x); if (_buf.Count > Capacity) _buf.RemoveRange(0, _buf.Count - Capacity); }
        if (!Config.Enabled) return;
        Check("CPU", x.Cpu, Config.Cpu, "%"); Check(L("Temp. CPU", "CPU temp"), x.CpuTemp, Config.CpuTemp, " °C");
        Check("RAM", x.Ram, Config.Ram, "%"); Check("GPU", x.Gpu, Config.Gpu, "%");
        Check(L("Temp. GPU", "GPU temp"), x.GpuTemp, Config.GpuTemp, " °C"); Check(L("Disco", "Disk"), x.Disk, Config.Disk, "%");
    }

    private static string L(string pt, string en) => StatsMonitor.Helpers.Loc.Lang == "en" ? en : pt;

    private void Check(string name, double? v, double limit, string unit)
    {
        if (v is null || v < limit) { _streak[name] = 0; return; }
        int n = _streak[name] = _streak.GetValueOrDefault(name) + 1;
        if (n < Config.Seconds) return;
        if (_lastAlert.TryGetValue(name, out var t) && (DateTime.Now - t).TotalMinutes < 2) return;
        _lastAlert[name] = DateTime.Now;
        Alert?.Invoke(L($"{name} em {v:0}{unit} há {n} s (limite {limit:0}{unit})", $"{name} at {v:0}{unit} for {n} s (limit {limit:0}{unit})"));
    }

    /// <summary>Samples of the last <paramref name="minutes"/> minutes, averaged into at most <paramref name="maxPoints"/> buckets.</summary>
    public List<LiveSample> Window(int minutes, int maxPoints = 120)
    {
        List<LiveSample> raw;
        var cutoff = DateTime.Now.AddMinutes(-minutes);
        lock (_buf) raw = _buf.Where(x => x.Time >= cutoff).ToList();
        return Downsample(raw, maxPoints);
    }

    public static List<LiveSample> Downsample(IReadOnlyList<LiveSample> raw, int maxPoints)
    {
        if (raw.Count <= maxPoints || maxPoints < 1) return raw.ToList();
        var res = new List<LiveSample>(maxPoints);
        double size = (double)raw.Count / maxPoints;
        for (int i = 0; i < maxPoints; i++)
        {
            var g = raw.Skip((int)(i * size)).Take(Math.Max(1, (int)((i + 1) * size) - (int)(i * size))).ToList();
            double? Avg(Func<LiveSample, double?> f) { var v = g.Select(f).Where(a => a != null).Select(a => a!.Value).ToList(); return v.Count == 0 ? null : v.Average(); }
            res.Add(new LiveSample(g[g.Count / 2].Time, Avg(a => a.Cpu), Avg(a => a.CpuTemp), Avg(a => a.Ram), Avg(a => a.Gpu), Avg(a => a.GpuTemp), Avg(a => a.Disk), Avg(a => a.NetMbps)));
        }
        return res;
    }
}
