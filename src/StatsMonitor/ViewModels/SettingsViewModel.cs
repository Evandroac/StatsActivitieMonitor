using System.Windows.Input;
using StatsMonitor.Helpers;
using StatsMonitor.Services;
using StatsMonitor.Settings;

namespace StatsMonitor.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly Action _onApplied;

    public SettingsViewModel(SettingsService service, Action onApplied)
    {
        _service = service;
        _onApplied = onApplied;
        Load(service.Current);
        _language = service.Current.Language == "en" ? "en" : "pt";
 _fontScale = UiScale.Clamp(service.Current.FontScalePercent);
        _fahrenheit = service.Current.TempUnit == "F";
        _loading = false;
        RefreshProfiles();
        _timer.Tick += (_, _) => Flush();
    }

    private void Load(AppSettings s)
    {
        IntervalSeconds = s.UpdateIntervalMs / 1000.0;
        CpuIntervalSeconds = s.CpuIntervalMs / 1000.0;
        GpuIntervalSeconds = s.GpuIntervalMs / 1000.0;
        RamIntervalSeconds = s.RamIntervalMs / 1000.0;
        OtherIntervalSeconds = s.OtherIntervalMs / 1000.0;
        RunAtStartup = s.RunAtStartup;
        StartMinimized = s.StartMinimized;
        ShowTemperatures = s.ShowTemperatures;
        ShowGadget = s.ShowGadget;
        AlwaysShowGadget = s.AlwaysShowGadget;
        _gadgetDetailed = s.GadgetDetailed;
        _gadgetMode = s.GadgetMode == GadgetDisplayMode.Taskbar ? GadgetDisplayMode.BottomRight : s.GadgetMode;
        ShowCpuMain = s.ShowCpuMain; ShowGpuMain = s.ShowGpuMain; ShowRamMain = s.ShowRamMain;
        ShowVramMain = s.ShowVramMain; ShowNetworkMain = s.ShowNetworkMain;
        ShowCpuGadget = s.ShowCpuGadget; ShowGpuGadget = s.ShowGpuGadget; ShowRamGadget = s.ShowRamGadget;
        ShowVramGadget = s.ShowVramGadget; ShowNetworkGadget = s.ShowNetworkGadget; ShowFpsGadget = s.ShowFpsGadget;
        CpuWarnTemp = s.CpuWarnTemp; CpuCritTemp = s.CpuCritTemp;
        GpuWarnTemp = s.GpuWarnTemp; GpuCritTemp = s.GpuCritTemp;
        FollowSystemTheme = s.FollowSystemTheme; NotifyAlerts = s.NotifyAlerts; NotifyNetworkAlerts = s.NotifyNetworkAlerts; NotifyGreetings = s.NotifyGreetings; AlertLevel = s.AlertLevel is "attention" or "critical" ? s.AlertLevel : "both"; AlertSound = s.AlertSound;
        AlertLoadPercent = s.AlertLoadPercent; AlertSustainSeconds = s.AlertSustainSeconds; AlertCooldownMinutes = s.AlertCooldownMinutes; ShowSplash = s.ShowSplash; BatteryLowPercent = s.BatteryLowPercent; BatteryCriticalPercent = s.BatteryCriticalPercent; HotkeyEnabled = s.HotkeyEnabled; GadgetHotkey = s.GadgetHotkey;
        ShowSensorsMain = s.ShowSensorsMain; ShowBatteryMain = s.ShowBatteryMain; PauseWhenHidden = s.PauseWhenHidden; EcoWhenMinimized = s.EcoWhenMinimized; AnimationsEnabled = s.AnimationsEnabled; SlowSensors = s.SlowSensors; LogEnabled = s.LogEnabled; LogIntervalSeconds = s.LogIntervalSeconds;
    }

    /// <summary>Restores every option on this page to its default value and saves.</summary>
    public void ResetDefaults()
    {
        _timer.Stop();
        _loading = true;
        Load(new AppSettings());
        _loading = false;
        Raise(string.Empty);
        Persist();
    }

    private double _fontScale = 110;
 public double FontScalePercent { get => _fontScale; set { var v = UiScale.Clamp(value); if (_fontScale != v) { _fontScale = v; UiScale.Set(v); Raise(nameof(FontScalePercent)); Raise(nameof(FontScaleText)); AutoSave(); } } }
 public string FontScaleText => $"{_fontScale:0}%";
 private string _language = "pt";
    public string Language { get => _language; set => SetLanguage(value); }

    // The language switches instantly; persistence follows the usual debounce.
    private void SetLanguage(string? lang)
    {
        if (lang is not ("pt" or "en") || _language == lang) return;
        _language = lang;
        Loc.SetLanguage(lang);
        AutoSave();
    }

    private bool _fahrenheit;
    // Advisory messages for values that are allowed but may hurt how the app works. Empty means fine.
    public string IntervalWarning => IntervalSeconds < 1 ? Loc.T("⚠ Intervalos abaixo de 1 s aumentam o uso de CPU do app.")
        : IntervalSeconds > 30 ? Loc.T("⚠ Intervalos longos deixam valores e alertas desatualizados.") : "";
    public string CpuTempWarning => TempWarning(CpuWarnTemp, CpuCritTemp);
    public string GpuTempWarning => TempWarning(GpuWarnTemp, GpuCritTemp);
    public string AlertWarning => AlertLoadPercent < 70 ? Loc.T("⚠ Limite de uso muito baixo: os alertas serão frequentes.")
        : AlertSustainSeconds < 1 ? Loc.T("⚠ Sem tempo mínimo, picos curtos disparam alertas.")
        : AlertCooldownMinutes < 1 ? Loc.T("⚠ Sem intervalo mínimo, as notificações podem se repetir em sequência.")
        : AlertCooldownMinutes > 60 ? Loc.T("⚠ Intervalo muito longo pode esconder problemas recorrentes.") : "";

    private static string TempWarning(double warn, double crit) =>
        warn >= crit ? Loc.T("⚠ O limite de atenção deve ser menor que o crítico; senão o aviso de atenção nunca aparece.")
        : crit < 60 ? Loc.T("⚠ Limite crítico muito baixo: o alerta pode disparar em uso normal.")
        : crit > 100 ? Loc.T("⚠ Limite crítico muito alto: o aviso pode vir tarde demais para evitar danos.") : "";

    public double TempMin => Units.FromCelsius(0);
    public double TempMax => Units.FromCelsius(150);
    public bool UnitC { get => !_fahrenheit; set => SetUnit(value, false); }
    public bool UnitF { get => _fahrenheit; set => SetUnit(value, true); }

    private void SetUnit(bool value, bool fahrenheit)
    {
        if (!value || _fahrenheit == fahrenheit) return;
        _fahrenheit = fahrenheit;
        Units.SetFahrenheit(fahrenheit);
        Raise(nameof(TempMin)); Raise(nameof(TempMax));
        foreach (var n in new[] { nameof(CpuWarnTemp), nameof(CpuCritTemp), nameof(GpuWarnTemp), nameof(GpuCritTemp) }) Raise(n);
        AutoSave();
    }

    private bool _loading = true;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private void SetMode(bool value, GadgetDisplayMode mode)
    {
        if (!value || _gadgetMode == mode) return;
        _gadgetMode = mode;
        AutoSave();
    }

    // Every change is applied and persisted automatically (debounced).
    private void AutoSave()
    {
        if (_loading) return;
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Persists any pending change immediately.</summary>
    public void Flush()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        Persist();
    }

    private double _cpuIntervalSeconds; public double CpuIntervalSeconds { get => _cpuIntervalSeconds; set { if (_cpuIntervalSeconds != value) { _cpuIntervalSeconds = value; AutoSave(); } } }
    private double _gpuIntervalSeconds; public double GpuIntervalSeconds { get => _gpuIntervalSeconds; set { if (_gpuIntervalSeconds != value) { _gpuIntervalSeconds = value; AutoSave(); } } }
    private double _ramIntervalSeconds; public double RamIntervalSeconds { get => _ramIntervalSeconds; set { if (_ramIntervalSeconds != value) { _ramIntervalSeconds = value; AutoSave(); } } }
    private double _otherIntervalSeconds; public double OtherIntervalSeconds { get => _otherIntervalSeconds; set { if (_otherIntervalSeconds != value) { _otherIntervalSeconds = value; AutoSave(); } } }
    // 0 means "same as the general interval"; anything else is clamped to the supported range.
    private static int PerComponentMs(double seconds) => seconds < 0.25 ? 0 : (int)Math.Clamp(seconds * 1000, 250, 60000);
    private double _intervalSeconds; public double IntervalSeconds { get => _intervalSeconds; set { if (_intervalSeconds != value) { _intervalSeconds = value; AutoSave(); } } }
    private bool _runAtStartup; public bool RunAtStartup { get => _runAtStartup; set { if (_runAtStartup != value) { _runAtStartup = value; AutoSave(); } } }
    private bool _startMinimized; public bool StartMinimized { get => _startMinimized; set { if (_startMinimized != value) { _startMinimized = value; AutoSave(); } } }
    private bool _showTemperatures; public bool ShowTemperatures { get => _showTemperatures; set { if (_showTemperatures != value) { _showTemperatures = value; AutoSave(); } } }
    private bool _showGadget; public bool ShowGadget { get => _showGadget; set { if (_showGadget != value) { _showGadget = value; AutoSave(); } } }
    private bool _alwaysShowGadget; public bool AlwaysShowGadget { get => _alwaysShowGadget; set { if (_alwaysShowGadget != value) { _alwaysShowGadget = value; AutoSave(); } } }
    private GadgetDisplayMode _gadgetMode;
    public bool GadgetFloating { get => _gadgetMode == GadgetDisplayMode.Floating; set => SetMode(value, GadgetDisplayMode.Floating); }
    public bool GadgetFloatingBar { get => _gadgetMode == GadgetDisplayMode.FloatingBar; set => SetMode(value, GadgetDisplayMode.FloatingBar); }
    public bool GadgetDockedTop { get => _gadgetMode == GadgetDisplayMode.DockedTop; set => SetMode(value, GadgetDisplayMode.DockedTop); }
    public bool GadgetDockedBottom { get => _gadgetMode == GadgetDisplayMode.DockedBottom; set => SetMode(value, GadgetDisplayMode.DockedBottom); }
    public bool GadgetTopLeft { get => _gadgetMode == GadgetDisplayMode.TopLeft; set => SetMode(value, GadgetDisplayMode.TopLeft); }
    public bool GadgetTopRight { get => _gadgetMode == GadgetDisplayMode.TopRight; set => SetMode(value, GadgetDisplayMode.TopRight); }
    public bool GadgetBottomLeft { get => _gadgetMode == GadgetDisplayMode.BottomLeft; set => SetMode(value, GadgetDisplayMode.BottomLeft); }
    public bool GadgetBottomRight { get => _gadgetMode == GadgetDisplayMode.BottomRight; set => SetMode(value, GadgetDisplayMode.BottomRight); }
    private bool _gadgetDetailed;
    public string GadgetViewKey
    {
        get => _gadgetDetailed ? "Detailed" : "Simple";
        set { bool d = value == "Detailed"; if (_gadgetDetailed != d) { _gadgetDetailed = d; Raise(); AutoSave(); } }
    }
    public string GadgetModeKey
    {
        get => _gadgetMode.ToString();
        set { if (Enum.TryParse<GadgetDisplayMode>(value, out var m)) SetMode(true, m); }
    }
    public bool GadgetNone { get => _gadgetMode == GadgetDisplayMode.None; set => SetMode(value, GadgetDisplayMode.None); }
    private bool _showCpuMain; public bool ShowCpuMain { get => _showCpuMain; set { if (_showCpuMain != value) { _showCpuMain = value; AutoSave(); } } }
    private bool _showGpuMain; public bool ShowGpuMain { get => _showGpuMain; set { if (_showGpuMain != value) { _showGpuMain = value; AutoSave(); } } }
    private bool _showRamMain; public bool ShowRamMain { get => _showRamMain; set { if (_showRamMain != value) { _showRamMain = value; AutoSave(); } } }
    private bool _showVramMain; public bool ShowVramMain { get => _showVramMain; set { if (_showVramMain != value) { _showVramMain = value; AutoSave(); } } }
    private bool _showNetworkMain; public bool ShowNetworkMain { get => _showNetworkMain; set { if (_showNetworkMain != value) { _showNetworkMain = value; AutoSave(); } } }
    private bool _showCpuGadget; public bool ShowCpuGadget { get => _showCpuGadget; set { if (_showCpuGadget != value) { _showCpuGadget = value; AutoSave(); } } }
    private bool _showGpuGadget; public bool ShowGpuGadget { get => _showGpuGadget; set { if (_showGpuGadget != value) { _showGpuGadget = value; AutoSave(); } } }
    private bool _showRamGadget; public bool ShowRamGadget { get => _showRamGadget; set { if (_showRamGadget != value) { _showRamGadget = value; AutoSave(); } } }
    private bool _showVramGadget; public bool ShowVramGadget { get => _showVramGadget; set { if (_showVramGadget != value) { _showVramGadget = value; AutoSave(); } } }
    private bool _showNetworkGadget; public bool ShowNetworkGadget { get => _showNetworkGadget; set { if (_showNetworkGadget != value) { _showNetworkGadget = value; AutoSave(); } } }
    private bool _showFpsGadget; public bool ShowFpsGadget { get => _showFpsGadget; set { if (_showFpsGadget != value) { _showFpsGadget = value; AutoSave(); } } }
    private double _cpuWarnTemp; public double CpuWarnTemp { get => _cpuWarnTemp; set { if (_cpuWarnTemp != value) { _cpuWarnTemp = value; AutoSave(); } } }
    private double _cpuCritTemp; public double CpuCritTemp { get => _cpuCritTemp; set { if (_cpuCritTemp != value) { _cpuCritTemp = value; AutoSave(); } } }
    private double _gpuWarnTemp; public double GpuWarnTemp { get => _gpuWarnTemp; set { if (_gpuWarnTemp != value) { _gpuWarnTemp = value; AutoSave(); } } }
    private double _gpuCritTemp; public double GpuCritTemp { get => _gpuCritTemp; set { if (_gpuCritTemp != value) { _gpuCritTemp = value; AutoSave(); } } }

    private bool _followSystemTheme; public bool FollowSystemTheme { get => _followSystemTheme; set { if (_followSystemTheme != value) { _followSystemTheme = value; Raise(); AutoSave(); } } }
    private bool _notifyNetworkAlerts; public bool NotifyNetworkAlerts { get => _notifyNetworkAlerts; set { if (_notifyNetworkAlerts != value) { _notifyNetworkAlerts = value; AutoSave(); } } }
    private bool _notifyGreetings; public bool NotifyGreetings { get => _notifyGreetings; set { if (_notifyGreetings != value) { _notifyGreetings = value; AutoSave(); } } }
    private string _alertLevel = "both"; public string AlertLevel { get => _alertLevel; set { if (value != null && _alertLevel != value) { _alertLevel = value; AutoSave(); } } }
    private bool _notifyAlerts; public bool NotifyAlerts { get => _notifyAlerts; set { if (_notifyAlerts != value) { _notifyAlerts = value; AutoSave(); } } }
    private bool _alertSound; public bool AlertSound { get => _alertSound; set { if (_alertSound != value) { _alertSound = value; AutoSave(); } } }
    private double _alertSustainSeconds; public double AlertSustainSeconds { get => _alertSustainSeconds; set { if (_alertSustainSeconds != value) { _alertSustainSeconds = value; AutoSave(); } } }
    private bool _showSplash; public bool ShowSplash { get => _showSplash; set { if (_showSplash != value) { _showSplash = value; AutoSave(); } } }
    private double _batteryLowPercent; public double BatteryLowPercent { get => _batteryLowPercent; set { if (_batteryLowPercent != value) { _batteryLowPercent = value; AutoSave(); } } }
    private double _batteryCriticalPercent; public double BatteryCriticalPercent { get => _batteryCriticalPercent; set { if (_batteryCriticalPercent != value) { _batteryCriticalPercent = value; AutoSave(); } } }
    private double _alertCooldownMinutes; public double AlertCooldownMinutes { get => _alertCooldownMinutes; set { if (_alertCooldownMinutes != value) { _alertCooldownMinutes = value; AutoSave(); } } }
    private double _alertLoadPercent; public double AlertLoadPercent { get => _alertLoadPercent; set { if (_alertLoadPercent != value) { _alertLoadPercent = value; AutoSave(); } } }
    private bool _hotkeyEnabled; public bool HotkeyEnabled { get => _hotkeyEnabled; set { if (_hotkeyEnabled != value) { _hotkeyEnabled = value; AutoSave(); } } }
    private string _gadgetHotkey = "Ctrl+Alt+G"; public string GadgetHotkey { get => _gadgetHotkey; set { if (_gadgetHotkey != value && value is not null) { _gadgetHotkey = value; AutoSave(); } } }
    private bool _animationsEnabled; public bool AnimationsEnabled { get => _animationsEnabled; set { if (_animationsEnabled != value) { _animationsEnabled = value; AutoSave(); } } }
    private bool _ecoWhenMinimized; public bool EcoWhenMinimized { get => _ecoWhenMinimized; set { if (_ecoWhenMinimized != value) { _ecoWhenMinimized = value; AutoSave(); } } }
    private bool _pauseWhenHidden; public bool PauseWhenHidden { get => _pauseWhenHidden; set { if (_pauseWhenHidden != value) { _pauseWhenHidden = value; AutoSave(); } } }
    private bool _logEnabled; public bool LogEnabled { get => _logEnabled; set { if (_logEnabled != value) { _logEnabled = value; AutoSave(); } } }
    private double _logIntervalSeconds = 5; public double LogIntervalSeconds { get => _logIntervalSeconds; set { if (_logIntervalSeconds != value) { _logIntervalSeconds = value; AutoSave(); } } }
    public string LogFolder => DataLogger.Folder;
    private bool _slowSensors; public bool SlowSensors { get => _slowSensors; set { if (_slowSensors != value) { _slowSensors = value; AutoSave(); } } }
    private bool _showBatteryMain; public bool ShowBatteryMain { get => _showBatteryMain; set { if (_showBatteryMain != value) { _showBatteryMain = value; AutoSave(); } } }
    public bool HasBattery => Services.BatteryService.HasBattery;
    private bool _showSensorsMain; public bool ShowSensorsMain { get => _showSensorsMain; set { if (_showSensorsMain != value) { _showSensorsMain = value; AutoSave(); } } }

    public string[] HotkeyOptions { get; } = ["Ctrl+Alt+G", "Ctrl+Alt+S", "Ctrl+Shift+G", "Ctrl+Alt+M", "Win+Shift+G"];

    // Profiles
    public System.Collections.ObjectModel.ObservableCollection<string> ProfileNames { get; } = new();
    private string? _selectedProfile;
    public string? SelectedProfile { get => _selectedProfile; set { _selectedProfile = value; Raise(); } }
    private string _newProfileName = "";
    public string NewProfileName { get => _newProfileName; set { _newProfileName = value; Raise(); } }

    /// <summary>Raised when the user picks a profile to apply; the host applies it and reopens this page.</summary>
    public event Action<string>? ProfileRequested;

    public void RefreshProfiles()
    {
        ProfileService.EnsureDefaults(_service.Current);
        ProfileNames.Clear();
        foreach (var p in _service.Current.Profiles) ProfileNames.Add(p.Name);
        _selectedProfile = _service.Current.ActiveProfile is { } a && ProfileNames.Contains(a) ? a : ProfileNames.FirstOrDefault();
        Raise(nameof(SelectedProfile));
    }

    public void ApplySelectedProfile()
    {
        Flush();
        if (SelectedProfile is { Length: > 0 } n) ProfileRequested?.Invoke(n);
    }

    public void SaveProfile()
    {
        Flush();
        var name = string.IsNullOrWhiteSpace(NewProfileName) ? SelectedProfile : NewProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        ProfileService.Capture(_service.Current, name);
        _service.Save();
        NewProfileName = "";
        RefreshProfiles();
        SelectedProfile = name;
    }

    public void DeleteProfile()
    {
        if (SelectedProfile is not { } n) return;
        ProfileService.Delete(_service.Current, n);
        _service.Save();
        RefreshProfiles();
    }

    public void Export(string path)
    {
        Flush();
        _service.Export(path);
    }

    /// <summary>Imports settings, then re-applies language, unit and theme. Returns false for an invalid file.</summary>
    public bool Import(string path)
    {
        _timer.Stop();
        if (!_service.Import(path)) return false;
        var s = _service.Current;
        _loading = true;
        Load(s);
        _language = s.Language == "en" ? "en" : "pt";
 _fontScale = UiScale.Clamp(s.FontScalePercent); UiScale.Set(_fontScale);
        _fahrenheit = s.TempUnit == "F";
        _loading = false;
        Loc.SetLanguage(_language);
        Units.SetFahrenheit(_fahrenheit);
        ThemeHelper.Apply(s);
        StartupService.SetEnabled(s.RunAtStartup);
        RefreshProfiles();
        Raise(string.Empty);
        _onApplied();
        return true;
    }

    private void Apply()
    {
        var s = _service.Current;
        s.UpdateIntervalMs = (int)Math.Clamp(IntervalSeconds * 1000, 250, 60000);
        s.CpuIntervalMs = PerComponentMs(CpuIntervalSeconds);
        s.GpuIntervalMs = PerComponentMs(GpuIntervalSeconds);
        s.RamIntervalMs = PerComponentMs(RamIntervalSeconds);
        s.OtherIntervalMs = PerComponentMs(OtherIntervalSeconds);
        s.RunAtStartup = RunAtStartup;
        s.StartMinimized = StartMinimized;
        s.ShowTemperatures = ShowTemperatures;
        s.ShowGadget = ShowGadget;
        s.AlwaysShowGadget = AlwaysShowGadget;
        s.GadgetMode = _gadgetMode;
        s.GadgetDetailed = _gadgetDetailed;
        s.Language = _language;
 s.FontScalePercent = _fontScale;
        s.TempUnit = _fahrenheit ? "F" : "C";
        s.ShowCpuMain = ShowCpuMain; s.ShowGpuMain = ShowGpuMain; s.ShowRamMain = ShowRamMain;
        s.ShowVramMain = ShowVramMain; s.ShowNetworkMain = ShowNetworkMain;
        s.ShowCpuGadget = ShowCpuGadget; s.ShowGpuGadget = ShowGpuGadget; s.ShowRamGadget = ShowRamGadget;
        s.ShowVramGadget = ShowVramGadget; s.ShowNetworkGadget = ShowNetworkGadget; s.ShowFpsGadget = ShowFpsGadget;
        s.CpuWarnTemp = CpuWarnTemp; s.CpuCritTemp = Math.Max(CpuCritTemp, CpuWarnTemp);
        s.GpuWarnTemp = GpuWarnTemp; s.GpuCritTemp = Math.Max(GpuCritTemp, GpuWarnTemp);

        s.FollowSystemTheme = FollowSystemTheme; s.NotifyAlerts = NotifyAlerts; s.NotifyNetworkAlerts = NotifyNetworkAlerts; s.NotifyGreetings = NotifyGreetings; s.AlertLevel = AlertLevel; s.AlertSound = AlertSound;
        s.AlertLoadPercent = Math.Clamp(AlertLoadPercent, 50, 100); s.AlertSustainSeconds = (int)Math.Clamp(AlertSustainSeconds, 0, 600); s.AlertCooldownMinutes = (int)Math.Clamp(AlertCooldownMinutes, 0, 240); s.ShowSplash = ShowSplash; s.BatteryLowPercent = (int)Math.Clamp(BatteryLowPercent, 2, 90); s.BatteryCriticalPercent = (int)Math.Clamp(BatteryCriticalPercent, 1, Math.Max(1, s.BatteryLowPercent - 1)); s.HotkeyEnabled = HotkeyEnabled;
        s.GadgetHotkey = HotkeyParser.TryParse(GadgetHotkey, out _, out _) ? GadgetHotkey : s.GadgetHotkey;
        s.ShowSensorsMain = ShowSensorsMain; s.ShowBatteryMain = ShowBatteryMain; s.PauseWhenHidden = PauseWhenHidden; s.EcoWhenMinimized = EcoWhenMinimized; s.AnimationsEnabled = AnimationsEnabled; s.SlowSensors = SlowSensors; s.LogEnabled = LogEnabled; s.LogIntervalSeconds = (int)Math.Clamp(LogIntervalSeconds, 1, 300);
        ThemeHelper.Apply(s);

        _onApplied();
    }

    private void Persist()
    {
        Apply();
        _service.Save();
        StartupService.SetEnabled(_service.Current.RunAtStartup);
    }
}
