using System.Globalization;
using System.IO;
using System.Text.Json;
using StatsMonitor.Helpers;
using StatsMonitor.Models;

namespace StatsMonitor.Services;

/// <summary>Publishes a tiny summary (every few seconds) for the Windows 11 widget provider to read.</summary>
public static class WidgetFeed
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatsMonitor", "widget.json");
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(5);
    private static DateTime _last = DateTime.MinValue;

    public static void Publish(HardwareSnapshot s)
    {
        var now = DateTime.UtcNow;
        if (now - _last < Every) return;
        _last = now;
        try
        {
            var data = new Dictionary<string, string>
            {
                ["cpu"] = Pct(s.CpuLoad), ["cpuTemp"] = s.CpuTemp is { } ct ? Units.Temp(ct) : "",
                ["ram"] = Pct(s.RamLoad), ["ramUsed"] = s.RamUsedGb is { } u && s.RamTotalGb is { } t ? string.Create(CultureInfo.CurrentCulture, $"{u:0.0}/{t:0.0} GB") : "",
                ["gpu"] = Pct(s.GpuLoad), ["gpuTemp"] = s.GpuTemp is { } gt ? Units.Temp(gt) : "",
                ["disk"] = Pct(s.Disks.Count > 0 ? s.Disks.Max(d => d.Activity) : null), ["status"] = "",
            };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception) { }
    }

    private static string Pct(double? v) => v is { } x ? string.Create(CultureInfo.CurrentCulture, $"{x:0}%") : "--";
}