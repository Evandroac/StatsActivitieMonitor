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
        var s = service.Current;
        IntervalSeconds = s.UpdateIntervalMs / 1000.0;
        RunAtStartup = s.RunAtStartup;
        StartMinimized = s.StartMinimized;
        ShowTemperatures = s.ShowTemperatures;
        ShowGadget = s.ShowGadget;
        AlwaysShowGadget = s.AlwaysShowGadget;
        DarkTheme = s.DarkTheme;
        CpuWarnTemp = s.CpuWarnTemp; CpuCritTemp = s.CpuCritTemp;
        GpuWarnTemp = s.GpuWarnTemp; GpuCritTemp = s.GpuCritTemp;
        NormalColor = s.NormalColor; WarningColor = s.WarningColor; CriticalColor = s.CriticalColor;
        SaveCommand = new RelayCommand(Save);
    }

    public double IntervalSeconds { get; set; }
    public bool RunAtStartup { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowTemperatures { get; set; }
    public bool ShowGadget { get; set; }
    public bool AlwaysShowGadget { get; set; }
    public bool DarkTheme { get; set; }
    public double CpuWarnTemp { get; set; }
    public double CpuCritTemp { get; set; }
    public double GpuWarnTemp { get; set; }
    public double GpuCritTemp { get; set; }
    public string NormalColor { get; set; }
    public string WarningColor { get; set; }
    public string CriticalColor { get; set; }

    public ICommand SaveCommand { get; }

    public event Action? Saved;

    private void Save()
    {
        var s = _service.Current;
        s.UpdateIntervalMs = (int)Math.Clamp(IntervalSeconds * 1000, 250, 60000);
        s.RunAtStartup = RunAtStartup;
        s.StartMinimized = StartMinimized;
        s.ShowTemperatures = ShowTemperatures;
        s.ShowGadget = ShowGadget;
        s.AlwaysShowGadget = AlwaysShowGadget;
        s.DarkTheme = DarkTheme;
        s.CpuWarnTemp = CpuWarnTemp; s.CpuCritTemp = Math.Max(CpuCritTemp, CpuWarnTemp);
        s.GpuWarnTemp = GpuWarnTemp; s.GpuCritTemp = Math.Max(GpuCritTemp, GpuWarnTemp);
        s.NormalColor = NormalColor; s.WarningColor = WarningColor; s.CriticalColor = CriticalColor;

        _service.Save();
        StartupService.SetEnabled(s.RunAtStartup);
        ThemeHelper.Apply(s.DarkTheme);
        _onApplied();
        Saved?.Invoke();
    }
}
