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
        _fahrenheit = service.Current.TempUnit == "F";
        _loading = false;
        RefreshProfiles();
        _timer.Tick += (_, _) => Flush();
    }

    private void Load(AppSettings s)
    {
        IntervalSeconds = s.UpdateIntervalMs / 1000.0;
        RunAtStartup = s.RunAtStartup;
        StartMinimized = s.StartMinimized;
        ShowTemperatures = s.ShowTemperatures;
        ShowGadget = s.ShowGadget;
        AlwaysShowGadget = s.AlwaysShowGadget;
        _gadgetMode = s.GadgetMode;
        ShowCpuMain = s.ShowCpuMain; ShowGpuMain = s.ShowGpuMain; ShowRamMain = s.ShowRamMain;
        ShowVramMain = s.ShowVramMain; ShowNetworkMain = s.ShowNetworkMain;
        ShowCpuGadget = s.ShowCpuGadget; ShowGpuGadget = s.ShowGpuGadget; ShowRamGadget = s.ShowRamGadget;
        ShowVramGadget = s.ShowVramGadget; ShowNetworkGadget = s.ShowNetworkGadget; ShowFpsGadget = s.ShowFpsGadget;
        CpuWarnTemp = s.CpuWarnTemp; CpuCritTemp = s.CpuCritTemp;
        GpuWarnTemp = s.GpuWarnTemp; GpuCritTemp = s.GpuCritTemp;
        FollowSystemTheme = s.FollowSystemTheme; NotifyAlerts = s.NotifyAlerts; AlertSound = s.AlertSound;
        AlertLoadPercent = s.AlertLoadPercent; AlertSustainSeconds = s.AlertSustainSeconds; AlertCooldownMinutes = s.AlertCooldownMinutes; HotkeyEnabled = s.HotkeyEnabled; GadgetHotkey = s.GadgetHotkey;
        ShowSensorsMain = s.ShowSensorsMain; PauseWhenHidden = s.PauseWhenHidden; SlowSensors = s.SlowSensors;
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

    private double _intervalSeconds; public double IntervalSeconds { get => _intervalSeconds; set { if (_intervalSeconds != value) { _intervalSeconds = value; AutoSave(); } } }
    private bool _runAtStartup; public bool RunAtStartup { get => _runAtStartup; set { if (_runAtStartup != value) { _runAtStartup = value; AutoSave(); } } }
    private bool _startMinimized; public bool StartMinimized { get => _startMinimized; set { if (_startMinimized != value) { _startMinimized = value; AutoSave(); } } }
    private bool _showTemperatures; public bool ShowTemperatures { get => _showTemperatures; set { if (_showTemperatures != value) { _showTemperatures = value; AutoSave(); } } }
    private bool _showGadget; public bool ShowGadget { get => _showGadget; set { if (_showGadget != value) { _showGadget = value; AutoSave(); } } }
    private bool _alwaysShowGadget; public bool AlwaysShowGadget { get => _alwaysShowGadget; set { if (_alwaysShowGadget != value) { _alwaysShowGadget = value; AutoSave(); } } }
    private GadgetDisplayMode _gadgetMode;
    public bool GadgetFloating { get => _gadgetMode == GadgetDisplayMode.Floating; set => SetMode(value, GadgetDisplayMode.Floating); }
    public bool GadgetOnTaskbar { get => _gadgetMode == GadgetDisplayMode.Taskbar; set => SetMode(value, GadgetDisplayMode.Taskbar); }
    public bool GadgetFloatingBar { get => _gadgetMode == GadgetDisplayMode.FloatingBar; set => SetMode(value, GadgetDisplayMode.FloatingBar); }
    public bool GadgetDockedTop { get => _gadgetMode == GadgetDisplayMode.DockedTop; set => SetMode(value, GadgetDisplayMode.DockedTop); }
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
    private bool _notifyAlerts; public bool NotifyAlerts { get => _notifyAlerts; set { if (_notifyAlerts != value) { _notifyAlerts = value; AutoSave(); } } }
    private bool _alertSound; public bool AlertSound { get => _alertSound; set { if (_alertSound != value) { _alertSound = value; AutoSave(); } } }
    private double _alertSustainSeconds; public double AlertSustainSeconds { get => _alertSustainSeconds; set { if (_alertSustainSeconds != value) { _alertSustainSeconds = value; AutoSave(); } } }
    private double _alertCooldownMinutes; public double AlertCooldownMinutes { get => _alertCooldownMinutes; set { if (_alertCooldownMinutes != value) { _alertCooldownMinutes = value; AutoSave(); } } }
    private double _alertLoadPercent; public double AlertLoadPercent { get => _alertLoadPercent; set { if (_alertLoadPercent != value) { _alertLoadPercent = value; AutoSave(); } } }
    private bool _hotkeyEnabled; public bool HotkeyEnabled { get => _hotkeyEnabled; set { if (_hotkeyEnabled != value) { _hotkeyEnabled = value; AutoSave(); } } }
    private string _gadgetHotkey = "Ctrl+Alt+G"; public string GadgetHotkey { get => _gadgetHotkey; set { if (_gadgetHotkey != value && value is not null) { _gadgetHotkey = value; AutoSave(); } } }
    private bool _pauseWhenHidden; public bool PauseWhenHidden { get => _pauseWhenHidden; set { if (_pauseWhenHidden != value) { _pauseWhenHidden = value; AutoSave(); } } }
    private bool _slowSensors; public bool SlowSensors { get => _slowSensors; set { if (_slowSensors != value) { _slowSensors = value; AutoSave(); } } }
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
        s.RunAtStartup = RunAtStartup;
        s.StartMinimized = StartMinimized;
        s.ShowTemperatures = ShowTemperatures;
        s.ShowGadget = ShowGadget;
        s.AlwaysShowGadget = AlwaysShowGadget;
        s.GadgetMode = _gadgetMode;
        s.Language = _language;
        s.TempUnit = _fahrenheit ? "F" : "C";
        s.ShowCpuMain = ShowCpuMain; s.ShowGpuMain = ShowGpuMain; s.ShowRamMain = ShowRamMain;
        s.ShowVramMain = ShowVramMain; s.ShowNetworkMain = ShowNetworkMain;
        s.ShowCpuGadget = ShowCpuGadget; s.ShowGpuGadget = ShowGpuGadget; s.ShowRamGadget = ShowRamGadget;
        s.ShowVramGadget = ShowVramGadget; s.ShowNetworkGadget = ShowNetworkGadget; s.ShowFpsGadget = ShowFpsGadget;
        s.CpuWarnTemp = CpuWarnTemp; s.CpuCritTemp = Math.Max(CpuCritTemp, CpuWarnTemp);
        s.GpuWarnTemp = GpuWarnTemp; s.GpuCritTemp = Math.Max(GpuCritTemp, GpuWarnTemp);

        s.FollowSystemTheme = FollowSystemTheme; s.NotifyAlerts = NotifyAlerts; s.AlertSound = AlertSound;
        s.AlertLoadPercent = Math.Clamp(AlertLoadPercent, 50, 100); s.AlertSustainSeconds = (int)Math.Clamp(AlertSustainSeconds, 0, 600); s.AlertCooldownMinutes = (int)Math.Clamp(AlertCooldownMinutes, 0, 240); s.HotkeyEnabled = HotkeyEnabled;
        s.GadgetHotkey = HotkeyParser.TryParse(GadgetHotkey, out _, out _) ? GadgetHotkey : s.GadgetHotkey;
        s.ShowSensorsMain = ShowSensorsMain; s.PauseWhenHidden = PauseWhenHidden; s.SlowSensors = SlowSensors;
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
