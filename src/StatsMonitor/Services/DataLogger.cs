using System.Globalization;
using System.IO;
using System.Text;
using StatsMonitor.Models;

namespace StatsMonitor.Services;

/// <summary>
/// Optional CSV recording of the readings (one file per day) for later analysis and validation.
/// Rows are buffered in memory and flushed in batches, so the disk cost is negligible.
/// </summary>
public static class DataLogger
{
    private const long MaxFileBytes = 25L * 1024 * 1024;
    private const int RetentionDays = 14;
    private const string Header = "timestamp,cpu_load_pct,cpu_temp_c,cpu_clock_mhz,cpu_power_w,ram_load_pct,ram_used_gb,gpu_load_pct,gpu_temp_c,gpu_power_w,vram_load_pct,disk_max_activity_pct,disk_max_temp_c";

    private static readonly object Gate = new();
    private static readonly StringBuilder Buffer = new();
    private static bool _enabled, _hooked;
    private static TimeSpan _every = TimeSpan.FromSeconds(5);
    private static DateTime _lastRow = DateTime.MinValue, _lastFlush = DateTime.Now;

    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "logs");

    public static void Configure(bool enabled, int intervalSeconds)
    {
        lock (Gate)
        {
            _every = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 1, 300));
            if (!_hooked && System.Windows.Application.Current is { } app) { app.Exit += (_, _) => Flush(); _hooked = true; }
            if (_enabled && !enabled) FlushLocked();
            if (!_enabled && enabled) Cleanup();
            _enabled = enabled;
        }
    }

    public static void Write(HardwareSnapshot s)
    {
        lock (Gate)
        {
            if (!_enabled || s.Timestamp - _lastRow < _every) return;
            _lastRow = s.Timestamp;
            var c = CultureInfo.InvariantCulture;
            string F(double? v, string f = "0.#") => v?.ToString(f, c) ?? "";
            Buffer.Append(s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", c)).Append(',')
                .Append(F(s.CpuLoad)).Append(',').Append(F(s.CpuTemp)).Append(',').Append(F(s.CpuClockMhz, "0")).Append(',').Append(F(s.CpuPowerW)).Append(',')
                .Append(F(s.RamLoad)).Append(',').Append(F(s.RamUsedGb, "0.00")).Append(',')
                .Append(F(s.GpuLoad)).Append(',').Append(F(s.GpuTemp)).Append(',').Append(F(s.GpuPowerW)).Append(',').Append(F(s.VramLoad)).Append(',')
                .Append(F(s.Disks.Select(d => d.Activity).Where(a => a.HasValue).DefaultIfEmpty(null).Max())).Append(',')
                .Append(F(s.Disks.Select(d => d.Temp).Where(a => a.HasValue).DefaultIfEmpty(null).Max())).Append('\n');
            if (Buffer.Length > 8192 || DateTime.Now - _lastFlush > TimeSpan.FromSeconds(30)) FlushLocked();
        }
    }

    public static void Flush() { lock (Gate) FlushLocked(); }

    private static void FlushLocked()
    {
        _lastFlush = DateTime.Now;
        if (Buffer.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Folder);
            var file = Path.Combine(Folder, $"stats-{DateTime.Now:yyyy-MM-dd}.csv");
            var info = new FileInfo(file);
            if (info.Exists && info.Length > MaxFileBytes) { Buffer.Clear(); return; }
            var text = (info.Exists ? "" : Header + "\n") + Buffer;
            File.AppendAllText(file, text.Replace("\n", "\r\n"), new UTF8Encoding(false));
        }
        catch (Exception) { /* logging must never disturb monitoring */ }
        Buffer.Clear();
    }

    private static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.GetFiles(Folder, "stats-*.csv"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-RetentionDays)) File.Delete(f);
        }
        catch (Exception) { }
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Folder) { UseShellExecute = true });
        }
        catch (Exception) { }
    }
}
