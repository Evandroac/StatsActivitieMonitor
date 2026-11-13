using System.IO;
using System.Text.Json;

namespace StatsMonitor.Settings;

public enum GadgetDisplayMode { Floating, Taskbar, None, FloatingBar, DockedTop }

public sealed class AppearancePreset
{
    public string Name { get; set; } = "";
    public bool DarkTheme { get; set; } = true;
    public Dictionary<string, string> ColorOverrides { get; set; } = new();
    public int GadgetOpacity { get; set; } = 80;
    public string NormalColor { get; set; } = "#3DDC84";
    public string WarningColor { get; set; } = "#FFC107";
    public string CriticalColor { get; set; } = "#FF4D4F";
}

public sealed class AppSettings
{
    public List<AppearancePreset> Presets { get; set; } = new();

    public GadgetDisplayMode GadgetMode { get; set; } = GadgetDisplayMode.Floating;
    public int UpdateIntervalMs { get; set; } = 1000;
    public bool RunAtStartup { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowTemperatures { get; set; } = true;
    public bool ShowGadget { get; set; } = true;
    public bool AlwaysShowGadget { get; set; }
    
    public bool DarkTheme { get; set; } = true;

    // Custom colors keyed by theme resource name (WindowBg, CardBg, ...); missing/empty means theme default.
    public Dictionary<string, string> ColorOverrides { get; set; } = new();
    // Gadget background opacity in percent; null keeps the theme default.
    public int? GadgetOpacity { get; set; }

    public bool ShowCpuMain { get; set; } = true;
    public bool ShowGpuMain { get; set; } = true;
    public bool ShowRamMain { get; set; } = true;
    public bool ShowVramMain { get; set; } = true;
    public bool ShowNetworkMain { get; set; } = true;

    public bool ShowCpuGadget { get; set; } = true;
    public bool ShowGpuGadget { get; set; } = true;
    public bool ShowRamGadget { get; set; } = true;
    public bool ShowVramGadget { get; set; } = true;
    public bool ShowNetworkGadget { get; set; }
    public bool ShowFpsGadget { get; set; }

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

    public string Snapshot() => JsonSerializer.Serialize(Current, Options);

    // Copies the snapshot back into the live instance so existing references stay valid.
    public void Restore(string snapshot)
    {
        var old = JsonSerializer.Deserialize<AppSettings>(snapshot);
        if (old is null) return;
        foreach (var p in typeof(AppSettings).GetProperties())
            p.SetValue(Current, p.GetValue(old));
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
