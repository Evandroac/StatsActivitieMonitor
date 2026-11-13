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
        GadgetOnTaskbar = s.GadgetMode == GadgetDisplayMode.Taskbar;
        ShowCpuMain = s.ShowCpuMain; ShowGpuMain = s.ShowGpuMain; ShowRamMain = s.ShowRamMain;
        ShowVramMain = s.ShowVramMain; ShowNetworkMain = s.ShowNetworkMain;
        ShowCpuGadget = s.ShowCpuGadget; ShowGpuGadget = s.ShowGpuGadget; ShowRamGadget = s.ShowRamGadget;
        ShowVramGadget = s.ShowVramGadget; ShowNetworkGadget = s.ShowNetworkGadget;
        CpuWarnTemp = s.CpuWarnTemp; CpuCritTemp = s.CpuCritTemp;
        GpuWarnTemp = s.GpuWarnTemp; GpuCritTemp = s.GpuCritTemp;
        _snapshot = service.Snapshot();
        SaveCommand = new RelayCommand(Save);
        ApplyCommand = new RelayCommand(Apply);
    }

    private readonly string _snapshot;
    public ICommand ApplyCommand { get; }

    public double IntervalSeconds { get; set; }
    public bool RunAtStartup { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShowTemperatures { get; set; }
    public bool ShowGadget { get; set; }
    public bool AlwaysShowGadget { get; set; }
    public bool GadgetOnTaskbar { get; set; }
    public bool GadgetFloating { get => !GadgetOnTaskbar; set => GadgetOnTaskbar = !value; }
    public bool ShowCpuMain { get; set; }
    public bool ShowGpuMain { get; set; }
    public bool ShowRamMain { get; set; }
    public bool ShowVramMain { get; set; }
    public bool ShowNetworkMain { get; set; }
    public bool ShowCpuGadget { get; set; }
    public bool ShowGpuGadget { get; set; }
    public bool ShowRamGadget { get; set; }
    public bool ShowVramGadget { get; set; }
    public bool ShowNetworkGadget { get; set; }
    public double CpuWarnTemp { get; set; }
    public double CpuCritTemp { get; set; }
    public double GpuWarnTemp { get; set; }
    public double GpuCritTemp { get; set; }

    public ICommand SaveCommand { get; }

    public event Action? Saved;

    public bool IsSaved { get; private set; }

    // Previews the values without persisting them.
    private void Apply()
    {
        var s = _service.Current;
        s.UpdateIntervalMs = (int)Math.Clamp(IntervalSeconds * 1000, 250, 60000);
        s.RunAtStartup = RunAtStartup;
        s.StartMinimized = StartMinimized;
        s.ShowTemperatures = ShowTemperatures;
        s.ShowGadget = ShowGadget;
        s.AlwaysShowGadget = AlwaysShowGadget;
        s.GadgetMode = GadgetOnTaskbar ? GadgetDisplayMode.Taskbar : GadgetDisplayMode.Floating;
        s.ShowCpuMain = ShowCpuMain; s.ShowGpuMain = ShowGpuMain; s.ShowRamMain = ShowRamMain;
        s.ShowVramMain = ShowVramMain; s.ShowNetworkMain = ShowNetworkMain;
        s.ShowCpuGadget = ShowCpuGadget; s.ShowGpuGadget = ShowGpuGadget; s.ShowRamGadget = ShowRamGadget;
        s.ShowVramGadget = ShowVramGadget; s.ShowNetworkGadget = ShowNetworkGadget;
        s.CpuWarnTemp = CpuWarnTemp; s.CpuCritTemp = Math.Max(CpuCritTemp, CpuWarnTemp);
        s.GpuWarnTemp = GpuWarnTemp; s.GpuCritTemp = Math.Max(GpuCritTemp, GpuWarnTemp);

        _onApplied();
    }

    private void Save()
    {
        Apply();
        _service.Save();
        StartupService.SetEnabled(_service.Current.RunAtStartup);
        IsSaved = true;
        Saved?.Invoke();
    }

    public void Revert()
    {
        _service.Restore(_snapshot);
        _onApplied();
    }
}
