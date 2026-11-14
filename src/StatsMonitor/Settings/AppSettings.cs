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

/// <summary>A named subset of settings that can be switched at once (e.g. Game, Work, Quiet).</summary>
public sealed class SettingsProfile
{
    public string Name { get; set; } = "";
    public Dictionary<string, JsonElement> Values { get; set; } = new();
}

public sealed class AppSettings
{
    public List<AppearancePreset> Presets { get; set; } = new();

    public GadgetDisplayMode GadgetMode { get; set; } = GadgetDisplayMode.Floating;
    public int UpdateIntervalMs { get; set; } = 1000;
    /// <summary>Per-component update interval in ms; 0 follows the general interval.</summary>
    public int CpuIntervalMs { get; set; }
    public int GpuIntervalMs { get; set; }
    public int RamIntervalMs { get; set; }
    public int OtherIntervalMs { get; set; }
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

    public bool SidebarPinned { get; set; }
    public bool SimplifiedView { get; set; }
    public bool PauseWhenHidden { get; set; }
    public bool EcoWhenMinimized { get; set; } = true;
    public bool LogEnabled { get; set; }
    public int LogIntervalSeconds { get; set; } = 5;
    public bool SlowSensors { get; set; }
    public string Language { get; set; } = "pt";
 public double FontScalePercent { get; set; } = 110;
    public string TempUnit { get; set; } = "C";
    public bool FollowSystemTheme { get; set; }

    public bool NotifyAlerts { get; set; } = true;
    public bool AlertSound { get; set; } = true;
    public bool NotifyNetworkAlerts { get; set; } = true;
    public bool NotifyGreetings { get; set; } = true;
    /// <summary>Which hardware alert levels notify: "both", "attention" or "critical".</summary>
    public string AlertLevel { get; set; } = "both";
    public double AlertLoadPercent { get; set; } = 95;
    public int AlertSustainSeconds { get; set; } = 10;
    public int AlertCooldownMinutes { get; set; } = 2;

    public bool HotkeyEnabled { get; set; } = true;
    public string GadgetHotkey { get; set; } = "Ctrl+Alt+G";

    public bool ShowSensorsMain { get; set; } = true;
    public bool ShowBatteryMain { get; set; } = true;

    public string? ActiveProfile { get; set; }
    public List<SettingsProfile> Profiles { get; set; } = new();
    public bool WindowMaximized { get; set; } = true;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsService(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>True when no settings file existed at load time (first run).</summary>
    public bool IsFirstRun { get; private set; }

    public void Load()
    {
        IsFirstRun = !File.Exists(_path);
        try
        {
            if (!IsFirstRun)
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

    /// <summary>Writes the current settings to a user-chosen file.</summary>
    public void Export(string path) => File.WriteAllText(path, Snapshot());

    /// <summary>Replaces the current settings with the content of a file. Returns false (leaving settings untouched) when the file is invalid.</summary>
    public bool Import(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (JsonSerializer.Deserialize<AppSettings>(json) is null) return false;
            Restore(json);
            Save();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Picks the UI language from the OS culture ("pt" for Portuguese, otherwise "en").</summary>
    public static string DetectLanguage(System.Globalization.CultureInfo? culture = null) =>
        (culture ?? System.Globalization.CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en";
}