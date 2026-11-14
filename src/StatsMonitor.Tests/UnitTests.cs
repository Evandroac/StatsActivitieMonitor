using System.Globalization;
using System.IO;
using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Services;
using StatsMonitor.Settings;
using Xunit;

namespace StatsMonitor.Tests;

[CollectionDefinition("global-state", DisableParallelization = true)]
public sealed class GlobalStateCollection;

[Collection("global-state")]
public class LocAndUnitsTests : IDisposable
{
    public LocAndUnitsTests() { Loc.SetLanguage("pt"); Units.SetFahrenheit(false); }
    public void Dispose() { Loc.SetLanguage("pt"); Units.SetFahrenheit(false); }

    [Fact]
    public void Portuguese_returns_the_key()
    {
        Assert.Equal("Sair", Loc.T("Sair"));
    }

    [Fact]
    public void English_translates_and_unknown_keys_fall_back()
    {
        Loc.SetLanguage("en");
        Assert.Equal("Exit", Loc.T("Sair"));
        Assert.Equal("chave inexistente", Loc.T("chave inexistente"));
    }

    [Fact]
    public void Formatting_arguments_are_applied()
    {
        Loc.SetLanguage("en");
        Assert.Equal("CPU: 99% usage (limit 95%)", Loc.T("CPU: {0:0}% de uso (limite {1:0}%)", 99.0, 95.0));
    }

    [Fact]
    public void LanguageChanged_is_raised_once_per_change()
    {
        int n = 0;
        void H() => n++;
        Loc.LanguageChanged += H;
        try { Loc.SetLanguage("en"); Loc.SetLanguage("en"); Loc.SetLanguage("pt"); }
        finally { Loc.LanguageChanged -= H; }
        Assert.Equal(2, n);
    }

    [Theory]
    [InlineData(false, 0, "0°C")]
    [InlineData(false, 52.4, "52°C")]
    [InlineData(true, 0, "32°F")]
    [InlineData(true, 100, "212°F")]
    public void Temperature_is_formatted_in_the_selected_unit(bool fahrenheit, double celsius, string expected)
    {
        Units.SetFahrenheit(fahrenheit);
        Assert.Equal(expected, Units.Temp(celsius));
    }
}

[Collection("global-state")]
public class GreetingTests
{
    [Fact]
    public void Greets_once_per_period_and_follows_the_time_of_day()
    {
        var now = new DateTime(2025, 1, 1, 8, 0, 0);
        var g = new GreetingNotifier(() => now);
        var cfg = new AppSettings();
        Assert.Equal(AlertKind.GreetingMorning, g.Evaluate(cfg)!.Kind);
        Assert.Null(g.Evaluate(cfg));
        now = now.AddHours(5);
        Assert.Equal(AlertKind.GreetingAfternoon, g.Evaluate(cfg)!.Kind);
        now = now.AddHours(6);
        Assert.Equal(AlertKind.GreetingNight, g.Evaluate(cfg)!.Kind);
        now = now.AddHours(4);
        Assert.Null(g.Evaluate(cfg));
        Assert.Null(new GreetingNotifier(() => now).Evaluate(new AppSettings { NotifyGreetings = false }));
    }
}

public class NetworkAlertTests
{
    private static NetworkSnapshot Net(bool connected, bool internet, long? lat = 20) =>
        new(connected, "Wi-Fi", null, null, null, null, 0, 0, 0, 0, lat, internet);

    [Fact]
    public void Outage_alerts_once_after_sustain_and_reports_restoration()
    {
        var now = new DateTime(2025, 1, 1, 12, 0, 0);
        var n = new NetworkAlertNotifier(() => now);
        var cfg = new AppSettings();
        Assert.Empty(n.Evaluate(Net(true, false), cfg));
        now = now.AddSeconds(11);
        Assert.Equal(AlertKind.NoInternet, Assert.Single(n.Evaluate(Net(true, false), cfg)).Kind);
        now = now.AddSeconds(30);
        Assert.Empty(n.Evaluate(Net(true, false), cfg));
        Assert.Equal(AlertKind.NetworkRestored, Assert.Single(n.Evaluate(Net(true, true), cfg)).Kind);
    }

    [Fact]
    public void Short_blip_and_disabled_setting_raise_nothing()
    {
        var now = new DateTime(2025, 1, 1, 12, 0, 0);
        var n = new NetworkAlertNotifier(() => now);
        n.Evaluate(Net(false, false), new AppSettings());
        now = now.AddSeconds(3);
        Assert.Empty(n.Evaluate(Net(true, true), new AppSettings()));
        now = now.AddMinutes(1);
        Assert.Empty(n.Evaluate(Net(false, false), new AppSettings { NotifyNetworkAlerts = false }));
    }
}

public class AlertNotifierTests : IDisposable
{
    public AlertNotifierTests() => Loc.SetLanguage("pt");
    public void Dispose() => Loc.SetLanguage("pt");

    private static HardwareSnapshot Snap(double? cpuTemp = null, double? cpuLoad = null) => new() { CpuTemp = cpuTemp, CpuLoad = cpuLoad };

    [Fact]
    public void Attention_level_fires_before_critical_and_yields_to_it()
    {
        var cfg = new AppSettings { CpuWarnTemp = 70, CpuCritTemp = 85, AlertSustainSeconds = 0 };
        var n = new AlertNotifier();
        Assert.Equal(AlertKind.CpuTempWarn, Assert.Single(n.Evaluate(Snap(cpuTemp: 75), cfg)).Kind);
        Assert.Equal(AlertKind.CpuTemp, Assert.Single(n.Evaluate(Snap(cpuTemp: 90), cfg)).Kind);
    }

    [Theory]
    [InlineData("attention", AlertKind.CpuTempWarn, 90)]
    [InlineData("critical", AlertKind.CpuTemp, 90)]
    public void Alert_level_setting_selects_which_levels_notify(string level, AlertKind expected, double temp)
    {
        var cfg = new AppSettings { CpuWarnTemp = 70, CpuCritTemp = 85, AlertSustainSeconds = 0, AlertLevel = level };
        Assert.Equal(expected, Assert.Single(new AlertNotifier().Evaluate(Snap(cpuTemp: temp), cfg)).Kind);
        Assert.Empty(new AlertNotifier().Evaluate(Snap(cpuTemp: 75), new AppSettings { CpuWarnTemp = 70, CpuCritTemp = 85, AlertSustainSeconds = 0, AlertLevel = "critical" }));
    }

    [Fact]
    public void Temperature_alert_fires_once_until_it_drops_below_the_limit()
    {
        var cfg = new AppSettings { CpuWarnTemp = 85, CpuCritTemp = 85, AlertSustainSeconds = 0 };
        var n = new AlertNotifier();
        Assert.Empty(n.Evaluate(Snap(cpuTemp: 80), cfg));
        Assert.Single(n.Evaluate(Snap(cpuTemp: 90), cfg));
        Assert.Empty(n.Evaluate(Snap(cpuTemp: 91), cfg));
    }

    [Fact]
    public void Alert_rearms_after_cooldown_and_cool_down()
    {
        var cfg = new AppSettings { CpuCritTemp = 85, AlertSustainSeconds = 0 };
        var now = new DateTime(2025, 1, 1, 12, 0, 0);
        var n = new AlertNotifier(() => now) { Cooldown = TimeSpan.FromMinutes(2) };
        Assert.Single(n.Evaluate(Snap(cpuTemp: 90), cfg));
        n.Evaluate(Snap(cpuTemp: 60), cfg);
        now = now.AddSeconds(30);
        Assert.Empty(n.Evaluate(Snap(cpuTemp: 90), cfg));
        n.Evaluate(Snap(cpuTemp: 60), cfg);
        now = now.AddMinutes(3);
        Assert.Single(n.Evaluate(Snap(cpuTemp: 90), cfg));
    }

    [Fact]
    public void Alert_needs_the_value_to_stay_above_the_limit_for_the_configured_seconds()
    {
        var cfg = new AppSettings { AlertLoadPercent = 95, AlertSustainSeconds = 10 };
        var now = new DateTime(2025, 1, 1, 12, 0, 0);
        var n = new AlertNotifier(() => now);
        Assert.Empty(n.Evaluate(Snap(cpuLoad: 99), cfg));
        now = now.AddSeconds(5);
        Assert.Empty(n.Evaluate(Snap(cpuLoad: 99), cfg));
        now = now.AddSeconds(6);
        var ev = Assert.Single(n.Evaluate(Snap(cpuLoad: 99), cfg));
        Assert.Equal(AlertKind.CpuLoad, ev.Kind);
    }

    [Fact]
    public void A_dip_below_the_limit_restarts_the_sustain_timer()
    {
        var cfg = new AppSettings { AlertLoadPercent = 95, AlertSustainSeconds = 10 };
        var now = new DateTime(2025, 1, 1, 12, 0, 0);
        var n = new AlertNotifier(() => now);
        n.Evaluate(Snap(cpuLoad: 99), cfg);
        now = now.AddSeconds(8);
        n.Evaluate(Snap(cpuLoad: 50), cfg);
        now = now.AddSeconds(8);
        Assert.Empty(n.Evaluate(Snap(cpuLoad: 99), cfg));
    }
    [Fact]
    public void Disabled_notifications_never_fire()
    {
        var cfg = new AppSettings { NotifyAlerts = false };
        Assert.Empty(new AlertNotifier().Evaluate(Snap(cpuTemp: 120), cfg));
    }
}

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("smtests").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void First_run_is_detected_and_values_round_trip()
    {
        var a = new SettingsService(P("a.json"));
        a.Load();
        Assert.True(a.IsFirstRun);
        a.Current.CpuCritTemp = 77;
        a.Current.TempUnit = "F";
        a.Save();

        var b = new SettingsService(P("a.json"));
        b.Load();
        Assert.False(b.IsFirstRun);
        Assert.Equal(77, b.Current.CpuCritTemp);
        Assert.Equal("F", b.Current.TempUnit);
    }

    [Fact]
    public void Export_then_import_restores_settings()
    {
        var a = new SettingsService(P("a.json"));
        a.Load();
        a.Current.GpuCritTemp = 70;
        a.Export(P("export.json"));

        var b = new SettingsService(P("b.json"));
        b.Load();
        Assert.True(b.Import(P("export.json")));
        Assert.Equal(70, b.Current.GpuCritTemp);
    }

    [Fact]
    public void Importing_garbage_fails_and_keeps_current_settings()
    {
        File.WriteAllText(P("bad.json"), "not json");
        var s = new SettingsService(P("s.json"));
        s.Load();
        s.Current.CpuCritTemp = 66;
        Assert.False(s.Import(P("bad.json")));
        Assert.Equal(66, s.Current.CpuCritTemp);
    }

    [Theory]
    [InlineData("pt-BR", "pt")]
    [InlineData("pt-PT", "pt")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "en")]
    public void System_language_is_detected(string culture, string expected)
    {
        Assert.Equal(expected, SettingsService.DetectLanguage(new CultureInfo(culture)));
    }
}

public class ProfileAndHotkeyTests
{
    [Fact]
    public void Default_profiles_are_created_once()
    {
        var s = new AppSettings();
        ProfileService.EnsureDefaults(s);
        ProfileService.EnsureDefaults(s);
        Assert.Equal(new[] { "Jogo", "Trabalho", "Silencioso" }, s.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void Applying_a_profile_changes_only_profile_settings()
    {
        var s = new AppSettings { DarkTheme = false, Language = "en" };
        ProfileService.EnsureDefaults(s);
        Assert.True(ProfileService.Apply(s, "silencioso"));
        Assert.False(s.ShowGadget);
        Assert.Equal(5000, s.UpdateIntervalMs);
        Assert.False(s.DarkTheme);
        Assert.Equal("en", s.Language);
        Assert.Equal("Silencioso", s.ActiveProfile);
    }

    [Fact]
    public void Captured_profile_restores_values()
    {
        var s = new AppSettings { UpdateIntervalMs = 3000 };
        ProfileService.Capture(s, "Meu");
        s.UpdateIntervalMs = 500;
        ProfileService.Apply(s, "Meu");
        Assert.Equal(3000, s.UpdateIntervalMs);
        Assert.True(ProfileService.Delete(s, "Meu"));
        Assert.False(ProfileService.Apply(s, "Meu"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+G", true)]
    [InlineData("ctrl + shift + F5", true)]
    [InlineData("G", false)]
    [InlineData("Ctrl+Alt", false)]
    [InlineData("Foo+G", false)]
    [InlineData("", false)]
    public void Hotkey_text_is_parsed(string text, bool valid)
    {
        Assert.Equal(valid, HotkeyParser.TryParse(text, out var mod, out var vk));
        if (valid) { Assert.NotEqual(0u, mod); Assert.NotEqual(0u, vk); }
    }
}

public class ProcessTreeTests
{
    private static ProcessRow R(int pid, int parent, string name, double cpu, long created) =>
        new(new ProcSample(pid, parent, created, name, "", "", null, false, false, 1, cpu, 1000, 0, 0));

    [Fact]
    public void Parents_aggregate_children_and_children_show_only_when_expanded()
    {
        var p = R(10, 1, "a.exe", 5, 100); var c1 = R(11, 10, "a.exe", 20, 200); var c2 = R(12, 10, "a.exe", 10, 300);
        var all = new[] { p, c1, c2 };
        var q = new ProcessQuery();
        var collapsed = ProcessTree.Arrange(all, q);
        Assert.Single(collapsed);
        Assert.Equal(35, collapsed[0].Cpu);
        Assert.Equal(2, collapsed[0].ChildCount);

        p.IsExpanded = true;
        var open = ProcessTree.Arrange(all, q);
        Assert.Equal([10, 11, 12], open.Select(r => r.Pid).ToArray());
        Assert.Equal(1, open[1].Depth);
    }

    [Fact]
    public void Search_flattens_and_sort_orders_by_value()
    {
        var all = new[] { R(1, 0, "x.exe", 1, 1), R(2, 1, "chrome.exe", 30, 2), R(3, 1, "chrome.exe", 60, 3) };
        var list = ProcessTree.Arrange(all, new ProcessQuery { Search = "chrome" });
        Assert.Equal([3, 2], list.Select(r => r.Pid).ToArray());
        Assert.All(list, r => Assert.Equal(0, r.Depth));
    }

    [Fact]
    public void Parent_cycles_do_not_hang_or_lose_rows()
    {
        var a = R(1, 2, "a", 0, 0); var b = R(2, 1, "b", 0, 0);
        Assert.NotEmpty(ProcessTree.Arrange(new[] { a, b }, new ProcessQuery()));
    }

    [Fact]
    public void Sampler_lists_the_current_process()
    {
        var s = new ProcessSampler();
        s.Sample();
        var me = s.Sample().FirstOrDefault(p => p.Pid == Environment.ProcessId);
        Assert.NotNull(me);
        Assert.True(me!.WorkingSet > 0);
        Assert.True(me.Path.Length > 0);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\a b\\app.exe\" --x \"y z\"", "C:\\Program Files\\a b\\app.exe", "--x \"y z\"")]
    [InlineData("C:\\tools\\app.exe -v", "C:\\tools\\app.exe", "-v")]
    [InlineData("app.exe", "C:\\tools\\app.exe", "")]
    public void Command_line_arguments_are_extracted(string cmd, string path, string expected) =>
        Assert.Equal(expected, ProcessTree.StripExecutable(cmd, path));
}