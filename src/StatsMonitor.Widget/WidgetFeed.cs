using System.Text.Json;

namespace StatsMonitor.Widget;

/// <summary>Reads the summary the Stats Monitor app publishes in %LocalAppData%\StatsMonitor\widget.json.</summary>
public static class WidgetFeed
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StatsMonitor", "widget.json");

    // Older than this means the main app is closed or paused.
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    public static string BuildData()
    {
        var d = new Dictionary<string, string>
        {
            ["cpu"] = "--", ["cpuTemp"] = "", ["ram"] = "--", ["ramUsed"] = "", ["gpu"] = "--", ["gpuTemp"] = "", ["disk"] = "--",
            ["status"] = "Abra o Stats Monitor",
        };
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Exists && DateTime.UtcNow - info.LastWriteTimeUtc < MaxAge)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (d.ContainsKey(p.Name)) d[p.Name] = p.Value.GetString() ?? "";
            }
        }
        catch (Exception) { }
        return JsonSerializer.Serialize(d);
    }
}
