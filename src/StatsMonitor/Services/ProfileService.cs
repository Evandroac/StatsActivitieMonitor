using System.Text.Json;
using StatsMonitor.Settings;

namespace StatsMonitor.Services;

/// <summary>Named setting profiles ("Jogo", "Trabalho", "Silencioso" and user-defined ones).</summary>
public static class ProfileService
{
    public const string Game = "Jogo", Work = "Trabalho", Quiet = "Silencioso";

    /// <summary>Settings captured by a profile; theme, colors, language and window geometry are deliberately excluded.</summary>
    public static readonly string[] ProfileKeys =
    [
        nameof(AppSettings.UpdateIntervalMs), nameof(AppSettings.CpuIntervalMs), nameof(AppSettings.GpuIntervalMs),
        nameof(AppSettings.RamIntervalMs), nameof(AppSettings.OtherIntervalMs), nameof(AppSettings.ShowTemperatures),
        nameof(AppSettings.ShowGadget), nameof(AppSettings.AlwaysShowGadget), nameof(AppSettings.GadgetMode), nameof(AppSettings.GadgetDetailed), nameof(AppSettings.GadgetNoCharts),
        nameof(AppSettings.ShowCpuMain), nameof(AppSettings.ShowGpuMain), nameof(AppSettings.ShowRamMain),
        nameof(AppSettings.ShowVramMain), nameof(AppSettings.ShowNetworkMain), nameof(AppSettings.ShowSensorsMain), nameof(AppSettings.ShowBatteryMain),
        nameof(AppSettings.ShowCpuGadget), nameof(AppSettings.ShowGpuGadget), nameof(AppSettings.ShowRamGadget),
        nameof(AppSettings.ShowVramGadget), nameof(AppSettings.ShowNetworkGadget), nameof(AppSettings.ShowFpsGadget),
        nameof(AppSettings.NotifyAlerts), nameof(AppSettings.AlertSound),
    ];

    /// <summary>Creates the built-in profiles when the list is empty.</summary>
    public static void EnsureDefaults(AppSettings s)
    {
        if (s.Profiles.Count > 0) return;
        s.Profiles.Add(Make(Game, new()
        {
            [nameof(AppSettings.UpdateIntervalMs)] = 1000,
            [nameof(AppSettings.CpuIntervalMs)] = 0, [nameof(AppSettings.GpuIntervalMs)] = 0, [nameof(AppSettings.RamIntervalMs)] = 0, [nameof(AppSettings.OtherIntervalMs)] = 0,
            [nameof(AppSettings.ShowGadget)] = true,
            [nameof(AppSettings.AlwaysShowGadget)] = true,
            [nameof(AppSettings.GadgetMode)] = GadgetDisplayMode.FloatingBar,
            [nameof(AppSettings.ShowCpuGadget)] = true,
            [nameof(AppSettings.ShowGpuGadget)] = true,
            [nameof(AppSettings.ShowRamGadget)] = true,
            [nameof(AppSettings.ShowVramGadget)] = false,
            [nameof(AppSettings.ShowNetworkGadget)] = true,
            [nameof(AppSettings.ShowFpsGadget)] = true,
            [nameof(AppSettings.ShowTemperatures)] = true,
            [nameof(AppSettings.NotifyAlerts)] = true,
            [nameof(AppSettings.AlertSound)] = true,
        }));
        s.Profiles.Add(Make(Work, new()
        {
            [nameof(AppSettings.UpdateIntervalMs)] = 2000,
            [nameof(AppSettings.CpuIntervalMs)] = 0, [nameof(AppSettings.GpuIntervalMs)] = 0, [nameof(AppSettings.RamIntervalMs)] = 0, [nameof(AppSettings.OtherIntervalMs)] = 0,
            [nameof(AppSettings.ShowGadget)] = true,
            [nameof(AppSettings.AlwaysShowGadget)] = false,
            [nameof(AppSettings.GadgetMode)] = GadgetDisplayMode.BottomRight,
            [nameof(AppSettings.ShowCpuGadget)] = true,
            [nameof(AppSettings.ShowGpuGadget)] = false,
            [nameof(AppSettings.ShowRamGadget)] = true,
            [nameof(AppSettings.ShowVramGadget)] = true,
            [nameof(AppSettings.ShowNetworkGadget)] = false,
            [nameof(AppSettings.ShowFpsGadget)] = false,
            [nameof(AppSettings.ShowTemperatures)] = true,
            [nameof(AppSettings.NotifyAlerts)] = true,
            [nameof(AppSettings.AlertSound)] = true,
        }));
        s.Profiles.Add(Make(Quiet, new()
        {
            [nameof(AppSettings.UpdateIntervalMs)] = 5000,
            [nameof(AppSettings.CpuIntervalMs)] = 0, [nameof(AppSettings.GpuIntervalMs)] = 0, [nameof(AppSettings.RamIntervalMs)] = 0, [nameof(AppSettings.OtherIntervalMs)] = 0,
            [nameof(AppSettings.ShowGadget)] = false,
            [nameof(AppSettings.AlwaysShowGadget)] = false,
            [nameof(AppSettings.GadgetMode)] = GadgetDisplayMode.None,
            [nameof(AppSettings.ShowFpsGadget)] = false,
            [nameof(AppSettings.NotifyAlerts)] = false,
            [nameof(AppSettings.AlertSound)] = false,
        }));
    }

    private static SettingsProfile Make(string name, Dictionary<string, object> values) => new()
    {
        Name = name,
        Values = values.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value, kv.Value.GetType())),
    };

    public static SettingsProfile? Find(AppSettings s, string? name) =>
        s.Profiles.FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Stores the current values of the profile-aware settings under <paramref name="name"/> (replacing a profile with that name).</summary>
    public static SettingsProfile Capture(AppSettings s, string name)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var key in ProfileKeys)
        {
            var prop = typeof(AppSettings).GetProperty(key)!;
            values[key] = JsonSerializer.SerializeToElement(prop.GetValue(s), prop.PropertyType);
        }
        var profile = new SettingsProfile { Name = name.Trim(), Values = values };
        int i = s.Profiles.FindIndex(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) s.Profiles[i] = profile; else s.Profiles.Add(profile);
        s.ActiveProfile = profile.Name;
        return profile;
    }

    /// <summary>Applies a profile's values onto the settings. Unknown or invalid entries are ignored.</summary>
    public static bool Apply(AppSettings s, string? name)
    {
        if (Find(s, name) is not { } profile) return false;
        foreach (var (key, element) in profile.Values)
        {
            if (!ProfileKeys.Contains(key)) continue;
            var prop = typeof(AppSettings).GetProperty(key);
            if (prop is null) continue;
            try { prop.SetValue(s, element.Deserialize(prop.PropertyType)); }
            catch (JsonException) { }
        }
        s.ActiveProfile = profile.Name;
        return true;
    }

    public static bool Delete(AppSettings s, string? name)
    {
        if (Find(s, name) is not { } p) return false;
        s.Profiles.Remove(p);
        if (string.Equals(s.ActiveProfile, p.Name, StringComparison.OrdinalIgnoreCase)) s.ActiveProfile = null;
        return true;
    }
}
