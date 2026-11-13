using System.Windows;
using System.Windows.Media;
using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Services;
using StatsMonitor.Settings;

namespace StatsMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IHardwareMonitorService _monitor;
    private readonly SettingsService _settingsService;
    private readonly NetworkMonitorService _network = new();
    private Brush _normal = Brushes.Green, _warn = Brushes.Orange, _crit = Brushes.Red;

    private string _uptimeText = "--", _lastUpdateText = "--";
    private string _statusText = "Iniciando…", _sensorNote = "";
    private Brush _statusBrush = Brushes.Gray;
    private bool _showTemperatures = true;

    public MainViewModel(IHardwareMonitorService monitor, SettingsService settingsService)
    {
        _monitor = monitor;
        _settingsService = settingsService;
        Cpu = new ComponentViewModel("CPU", t => Level(t, S.CpuWarnTemp, S.CpuCritTemp).brush);
        Gpu = new ComponentViewModel("GPU", t => Level(t, S.GpuWarnTemp, S.GpuCritTemp).brush);
        Ram = new ComponentViewModel("RAM", t => Level(t, 55, 70).brush) { HasTemperature = false };
        Vram = new ComponentViewModel("VRAM", t => Level(t, S.GpuWarnTemp, S.GpuCritTemp).brush);

        ApplySettings();
        _monitor.SnapshotUpdated += OnSnapshot;
        _monitor.ErrorOccurred += OnError;
    }

    private AppSettings S => _settingsService.Current;

    public ComponentViewModel Cpu { get; }
    public ComponentViewModel Gpu { get; }
    public ComponentViewModel Ram { get; }
    public ComponentViewModel Vram { get; }
    public NetworkViewModel Network { get; } = new();
    public string UptimeText { get => _uptimeText; private set => Set(ref _uptimeText, value); }
    public string LastUpdateText { get => _lastUpdateText; private set => Set(ref _lastUpdateText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public Brush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string FpsText { get => _fpsText; set => Set(ref _fpsText, value); }
    private string _fpsText = "--";
    private bool _fpsActive;
    public bool FpsActive { get => _fpsActive; set => Set(ref _fpsActive, value); }
    public string SensorNote { get => _sensorNote; private set => Set(ref _sensorNote, value); }
    public bool ShowTemperatures { get => _showTemperatures; private set => Set(ref _showTemperatures, value); }

    public void Start()
    {
        _monitor.Start();
        _network.SnapshotUpdated += OnNetwork;
        _network.Start();
    }

    private void OnNetwork(object? sender, NetworkSnapshot s)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => Network.Update(s));
    }

    /// <summary>Re-reads settings (interval, colors, temperature visibility).</summary>
    public void ApplySettings()
    {
        (_normal, _warn, _crit) = ThemeHelper.AlertBrushes(S);
        _monitor.Interval = TimeSpan.FromMilliseconds(Math.Clamp(S.UpdateIntervalMs, 250, 60000));
        ShowTemperatures = S.ShowTemperatures;
        foreach (var c in new[] { Cpu, Gpu })
        {
            c.ShowTemperature = ShowTemperatures;
        }
        foreach (var c in new[] { Cpu, Gpu, Ram, Vram }) c.RefreshBrush();
    }

    private (int level, Brush brush) Level(double? temp, double warn, double crit)
    {
        if (!temp.HasValue) return (-1, Brushes.Gray);
        if (temp >= crit) return (2, _crit);
        if (temp >= warn) return (1, _warn);
        return (0, _normal);
    }

    private void OnSnapshot(object? sender, HardwareSnapshot s)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(s));
    }

    private void OnError(object? sender, Exception ex)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            SensorNote = $"Erro de monitoramento: {ex.Message}";
        });
    }

    private void Apply(HardwareSnapshot s)
    {
        Cpu.Update(s.CpuName, s.CpuLoad, s.CpuTemp, s.CpuClockMhz);
        Gpu.Update(s.GpuName, s.GpuLoad, s.GpuTemp, s.GpuClockMhz);
        Ram.Update("Memória RAM", s.RamLoad, null, null, s.RamTotalGb, s.RamUsedGb, s.RamAvailableGb);
        Vram.Update(s.GpuName is null ? null : "Memória VRAM", s.VramLoad, s.VramTemp, null,
            s.VramTotalGb, s.VramUsedGb, s.VramAvailableGb);

        UptimeText = s.Uptime.Days > 0
            ? $"{s.Uptime.Days}d {s.Uptime.Hours:00}h {s.Uptime.Minutes:00}m {s.Uptime.Seconds:00}s"
            : $"{s.Uptime.Hours:00}h {s.Uptime.Minutes:00}m {s.Uptime.Seconds:00}s";
        LastUpdateText = s.Timestamp.ToString("dd/MM/yyyy HH:mm:ss");

        int worst = Math.Max(Level(s.CpuTemp, S.CpuWarnTemp, S.CpuCritTemp).level,
                             Level(s.GpuTemp, S.GpuWarnTemp, S.GpuCritTemp).level);
        (StatusText, StatusBrush) = worst switch
        {
            2 => ("Temperatura crítica", _crit),
            1 => ("Temperatura elevada", _warn),
            0 => ("Funcionamento normal", _normal),
            _ => ("Sem sensores de temperatura", Brushes.Gray),
        };

        var notes = new List<string>();
        if (!s.CpuTemp.HasValue && !s.IsElevated)
            notes.Add("Temperatura da CPU indisponível: execute como administrador para acessar este sensor.");
        if (s.GpuName is null) notes.Add("Nenhuma GPU compatível detectada.");
        SensorNote = string.Join(" ", notes);
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshot;
        _monitor.ErrorOccurred -= OnError;
        _network.SnapshotUpdated -= OnNetwork;
        _network.Dispose();
    }
}
