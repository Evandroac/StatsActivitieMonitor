using System.IO;
using System.Text.Json;

namespace StatsMonitor.Settings;

public sealed class AppSettings
{
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool RunAtStartup { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowTemperatures { get; set; } = true;
    public bool ShowGadget { get; set; } = true;
    public bool AlwaysShowGadget { get; set; }
    public bool DarkTheme { get; set; } = true;

    public double CpuWarnTemp { get; set; } = 70;
    public double CpuCritTemp { get; set; } = 85;
    public double GpuWarnTemp { get; set; } = 75;
    public double GpuCritTemp { get; set; } = 88;

    public string NormalColor { get; set; } = "#3DDC84";
    public string WarningColor { get; set; } = "#FFC107";
    public string CriticalColor { get; set; } = "#FF4D4F";

    public double? GadgetLeft { get; set; }
    public double? GadgetTop { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, Options));
        }
        catch
        {
            // Settings are non-critical; ignore IO failures.
        }
    }
}
