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
    public bool LangPt { get => _language == "pt"; set => SetLanguage(value, "pt"); }
    public bool LangEn { get => _language == "en"; set => SetLanguage(value, "en"); }

    // The language switches instantly; persistence follows the usual debounce.
    private void SetLanguage(bool value, string lang)
    {
        if (!value || _language == lang) return;
        _language = lang;
        Loc.SetLanguage(lang);
        AutoSave();
    }

    private bool _fahrenheit;
    public bool UnitC { get => !_fahrenheit; set => SetUnit(value, false); }
    public bool UnitF { get => _fahrenheit; set => SetUnit(value, true); }

    private void SetUnit(bool value, bool fahrenheit)
    {
        if (!value || _fahrenheit == fahrenheit) return;
        _fahrenheit = fahrenheit;
        Units.SetFahrenheit(fahrenheit);
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

        _onApplied();
    }

    private void Persist()
    {
        Apply();
        _service.Save();
        StartupService.SetEnabled(_service.Current.RunAtStartup);
    }
}
