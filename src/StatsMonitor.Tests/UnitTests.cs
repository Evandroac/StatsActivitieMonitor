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
        var cfg = new AppSettings { CpuWarnTemp = 70, CpuCritTemp = 85, AlertSustainSeconds = 0, AlertLevel = "both" };
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
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("es-MX", "es-MX")]
    [InlineData("es-ES", "es")]
    [InlineData("zh-TW", "zh-TW")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("de-DE", "de")]
    [InlineData("sv-SE", "en")]
    public void System_language_is_detected(string culture, string expected)
    {
        Assert.Equal(expected, SettingsService.DetectLanguage(new CultureInfo(culture)));
    }
}

public class TranslationCoverageTests
{
    [Fact]
    public void Every_language_covers_all_keys_and_keeps_placeholders()
    {
        var langs = new Dictionary<string, Dictionary<string, string>>
        {
            ["es"] = LocData.Es, ["fr"] = LocData.Fr, ["de"] = LocData.De, ["it"] = LocData.It, ["ru"] = LocData.Ru,
            ["zh-CN"] = LocData.ZhCn, ["zh-TW"] = LocData.ZhTw, ["ja"] = LocData.Ja, ["ko"] = LocData.Ko, ["ar"] = LocData.Ar, ["hi"] = LocData.Hi,
        };
        var problems = new List<string>();
        foreach (var (code, d) in langs)
            foreach (var (key, en) in LocData.En)
            {
                if (!d.TryGetValue(key, out var v)) { problems.Add($"{code}: missing '{key}'"); continue; }
                var ph = System.Text.RegularExpressions.Regex.Matches(en, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(x => x);
                var ph2 = System.Text.RegularExpressions.Regex.Matches(v, @"\{\d+[^}]*\}").Select(m => m.Value).OrderBy(x => x);
                if (!ph.SequenceEqual(ph2)) problems.Add($"{code}: placeholders differ for '{key}'");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)) + $"\n({problems.Count} total)");
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

public class NetworkToolsTests
{
    [Fact]
    public void ParsePorts_HandlesListsRangesAndInvalid()
    {
        Assert.Equal([22, 80, 443, 8000, 8001, 8002], NetworkToolsService.ParsePorts("443, 80;22 8000-8002 abc 0 70000 9-5"));
    }

    [Fact]
    public void PingStats_ComputesLossAndJitter()
    {
        var s = new PingStats();
        s.Add(10); s.Add(20); s.Add(null); s.Add(10);
        Assert.Equal(4, s.Sent);
        Assert.Equal(3, s.Received);
        Assert.Equal(25, s.Loss);
        Assert.Equal(10, s.Min);
        Assert.Equal(20, s.Max);
        Assert.Equal(10, s.Jitter);
    }

    [Fact]
    public void Dns_QueryRoundTripsAndParsesAnswers()
    {
        var q = NetworkToolsService.BuildDnsQuery("example.com", 1, 0x1234);
        Assert.Equal(0x12, q[0]);
        Assert.Equal(7, q[12]);
        // Response: header + question + one A answer using a compression pointer to the name.
        var resp = new List<byte> { 0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 };
        resp.AddRange(q.Skip(12));
        resp.AddRange([0xC0, 0x0C, 0, 1, 0, 1, 0, 0, 1, 0x2C, 0, 4, 93, 184, 216, 34]);
        var (id, rcode, tc, answers) = NetworkToolsService.ParseDnsResponse([.. resp]);
        Assert.Equal(0x1234, id);
        Assert.Equal("NOERROR", rcode);
        Assert.False(tc);
        var a = Assert.Single(answers);
        Assert.Equal("example.com", a.Name);
        Assert.Equal("93.184.216.34", a.Data);
        Assert.Equal(300u, a.Ttl);
    }

    [Fact]
    public void ReverseName_BuildsArpaNames()
    {
        Assert.Equal("4.3.2.1.in-addr.arpa", NetworkToolsService.ReverseName(System.Net.IPAddress.Parse("1.2.3.4")));
    }
}

public class BatteryTests
{
    [Fact]
    public void Estimate_UsesDischargeRateAndChargeRate()
    {
        var (rem, _) = BatteryService.Estimate(30000, 50000, 0, 15000, BatteryState.Discharging);
        Assert.Equal(TimeSpan.FromHours(2), rem);
        var (_, full) = BatteryService.Estimate(30000, 50000, 20000, 0, BatteryState.Charging);
        Assert.Equal(TimeSpan.FromHours(1), full);
        Assert.Equal((null, null), BatteryService.Estimate(30000, 50000, 0, 100, BatteryState.Discharging));
    }

    [Theory]
    [InlineData(40000, 50000, 80.0, 0)]
    [InlineData(35000, 50000, 70.0, 1)]
    [InlineData(20000, 50000, 40.0, 2)]
    public void Health_ComparesFullAndDesignCapacity(int full, int design, double pct, int level)
    {
        var h = BatteryService.Health(full, design);
        Assert.Equal(pct, h);
        Assert.Equal(level, BatteryService.HealthLevel(h));
        Assert.Null(BatteryService.Health(null, design));
        Assert.Equal(-1, BatteryService.HealthLevel(null));
    }

    [Fact]
    public void ClassifyState_DistinguishesChargingFullAndDischarging()
    {
        Assert.Equal(BatteryState.Charging, BatteryService.ClassifyState(60, true, true, false));
        Assert.Equal(BatteryState.Full, BatteryService.ClassifyState(100, true, true, false));
        Assert.Equal(BatteryState.PluggedNotCharging, BatteryService.ClassifyState(80, true, false, false));
        Assert.Equal(BatteryState.Discharging, BatteryService.ClassifyState(50, false, false, true));
    }

    [Fact]
    public void BatteryAlerts_FireOncePerDischargeAndRearm()
    {
        var n = new BatteryAlertNotifier();
        var cfg = new AppSettings { AlertLevel = "both" };
        BatteryInfo B(int p, BatteryState s) => BatteryInfo.None with { Present = true, Percent = p, State = s, PluggedIn = s != BatteryState.Discharging };
        Assert.Empty(n.Evaluate(B(50, BatteryState.Discharging), cfg));
        Assert.Equal(AlertKind.BatteryLow, Assert.Single(n.Evaluate(B(19, BatteryState.Discharging), cfg)).Kind);
        Assert.Empty(n.Evaluate(B(18, BatteryState.Discharging), cfg));
        Assert.Equal(AlertKind.BatteryCritical, Assert.Single(n.Evaluate(B(9, BatteryState.Discharging), cfg)).Kind);
        Assert.Empty(n.Evaluate(B(8, BatteryState.Discharging), cfg));
        Assert.Empty(n.Evaluate(B(30, BatteryState.Charging), cfg));
        Assert.Single(n.Evaluate(B(15, BatteryState.Discharging), cfg));
    }
}
public class DiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sm-diag-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static StatsMonitor.Diagnostics.DiagnosticSession Sample(string id, int? score = 90)
    {
        var s = new StatsMonitor.Diagnostics.DiagnosticSession { Id = id, Computer = "PC", Full = true, Score = score };
        s.Findings.Add(new StatsMonitor.Diagnostics.Finding { Component = "cpu", Problem = "x,\"y\"<script>", Level = StatsMonitor.Diagnostics.Severity.Warning, Evidence = "e" });
        return s;
    }

    [Fact]
    public void ScoreAndRoll()
    {
        var D = typeof(StatsMonitor.Diagnostics.DiagnosticEngine);
        var f = new[] { new StatsMonitor.Diagnostics.Finding { Component = "cpu", Level = StatsMonitor.Diagnostics.Severity.Critical },
                        new StatsMonitor.Diagnostics.Finding { Component = "cpu", Level = StatsMonitor.Diagnostics.Severity.Warning } };
        Assert.Equal(65, StatsMonitor.Diagnostics.DiagnosticEngine.Score(f));
        Assert.Equal(100, StatsMonitor.Diagnostics.DiagnosticEngine.Score(Array.Empty<StatsMonitor.Diagnostics.Finding>()));
    }

    [Fact]
    public void MissingDataIsNeverNormal()
    {
        var s = new StatsMonitor.Diagnostics.DiagnosticSession();
        StatsMonitor.Diagnostics.DiagnosticEngine.Analyze(s, null, new StatsMonitor.Diagnostics.Thresholds());
        Assert.DoesNotContain(s.Health, h => h.Value == StatsMonitor.Diagnostics.Severity.Normal && !s.Inventory.Collected.Contains(h.Key));
    }

    [Fact]
    public void VariationOfConstantIsZero()
    {
        Assert.Equal(0, StatsMonitor.Diagnostics.PerformanceTests.Variation([5.0, 5.0, 5.0]), 6);
        Assert.True(StatsMonitor.Diagnostics.PerformanceTests.Variation([1.0, 9.0]) > 0);
    }

    [Fact]
    public void StoreRoundTripCompareAndLimit()
    {
        var st = new StatsMonitor.Diagnostics.DiagnosticStore(_dir);
        st.Save(Sample("20240101-000001", 80));
        st.Save(Sample("20240102-000001", 95));
        Assert.Equal("PC", st.Load("20240101-000001")!.Computer);
        Assert.Equal(2, st.List().Count);
        var c = StatsMonitor.Diagnostics.DiagnosticStore.Compare(st.Load("20240101-000001")!, st.Load("20240102-000001")!);
        Assert.Equal(15, c.ScoreDelta);
        st.Audit("teste");
        Assert.True(File.Exists(Path.Combine(_dir, "audit.log")));
    }

    [Fact]
    public void ReportsAreSafeAndValid()
    {
        var s = Sample("1");
        Assert.DoesNotContain("<script>", StatsMonitor.Diagnostics.ReportExporter.ToHtml(s));
        Assert.Contains("\"\"y\"\"", StatsMonitor.Diagnostics.ReportExporter.ToCsv(s));
        var pdf = StatsMonitor.Diagnostics.ReportExporter.ToPdf(s);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Contains("%%EOF", System.Text.Encoding.ASCII.GetString(pdf, pdf.Length - 10, 10));
        Assert.Contains("PC", StatsMonitor.Diagnostics.ReportExporter.ToJson(s));
    }

    [Fact]
    public void DownsampleCapsPoints()
    {
        var raw = Enumerable.Range(0, 1000).Select(i => new StatsMonitor.Diagnostics.LiveSample(DateTime.Now, i, null, null, null, null, null, null)).ToList();
        Assert.True(StatsMonitor.Diagnostics.LiveMonitor.Downsample(raw, 120).Count <= 120);
    }
}