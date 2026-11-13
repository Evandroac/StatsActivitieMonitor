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
        Cpu = new ComponentViewModel("CPU", t => Level(t, S.CpuWarnTemp, S.CpuCritTemp).brush, t => Level(t, S.CpuWarnTemp, S.CpuCritTemp).level);
        Gpu = new ComponentViewModel("GPU", t => Level(t, S.GpuWarnTemp, S.GpuCritTemp).brush, t => Level(t, S.GpuWarnTemp, S.GpuCritTemp).level);
        Ram = new ComponentViewModel("RAM", t => Level(t, 55, 70).brush) { HasTemperature = false };

        InitCpuDetails();
        ApplySettings();
        _monitor.SnapshotUpdated += OnSnapshot;
        _monitor.ErrorOccurred += OnError;
        Loc.LanguageChanged += () => { if (_last is { } l) Apply(l); if (_lastNet is { } n) Network.Update(n); };
        Units.Changed += () => { if (_last is { } l) Apply(l); };
    }

    private DetailItem? _gpuMemClock, _gpuVram, _gpuPower, _gpuFan;
    private DetailItem? _procItem, _threadItem, _handleItem;

    private void InitCpuDetails()
    {
        var info = CpuInfoService.Read();
        _gpuMemClock = new DetailItem("Frequência da memória", ComponentViewModel.NotAvailable);
        _gpuVram = new DetailItem("Uso da VRAM", ComponentViewModel.NotAvailable);
        _gpuPower = new DetailItem("Consumo energético", ComponentViewModel.NotAvailable);
        _gpuFan = new DetailItem("Ventoinhas", ComponentViewModel.NotAvailable);
        foreach (var d in new[] { _gpuVram, _gpuMemClock, _gpuPower, _gpuFan }) Gpu.Details.Add(d);
        _procItem = new DetailItem("Processos", "--");
        _threadItem = new DetailItem("Threads", "--");
        _handleItem = new DetailItem("Identificadores", "--");
        foreach (var d in new[]
        {
            new DetailItem("Velocidade base", info.BaseSpeed),
            new DetailItem("Sockets", info.Sockets.ToString()),
            new DetailItem("Núcleos", info.Cores.ToString()),
            new DetailItem("Processadores lógicos", info.LogicalProcessors.ToString()),
            new DetailItem("Virtualização", info.Virtualization),
            new DetailItem("Cache", info.Cache),
            _procItem, _threadItem, _handleItem,
        }) Cpu.Details.Add(d);
    }

    private void UpdateCounts()
    {
        if (CpuInfoService.ReadCounts() is not { } c) return;
        _procItem!.Value = c.Processes.ToString("N0");
        _threadItem!.Value = c.Threads.ToString("N0");
        _handleItem!.Value = c.Handles.ToString("N0");
    }

    private NetworkSnapshot? _lastNet;
    private AppSettings S => _settingsService.Current;

    public ComponentViewModel Cpu { get; }
    public ComponentViewModel Gpu { get; }
    public ComponentViewModel Ram { get; }
    public DiskViewModel Disk { get; } = new();
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
        _lastNet = s;
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
        foreach (var c in new[] { Cpu, Gpu, Ram }) c.RefreshBrush();
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
            SensorNote = Loc.T("Erro de monitoramento: {0}", ex.Message);
        });
    }

    private HardwareSnapshot? _last;

    private void Apply(HardwareSnapshot s)
    {
        _last = s;
        UpdateCounts();
        string NA = ComponentViewModel.NotAvailable;
        _gpuMemClock!.Value = s.GpuMemClockMhz is { } mc ? (mc >= 1000 ? $"{mc / 1000:0.00} GHz" : $"{mc:0} MHz") : NA;
        _gpuVram!.Value = s.VramUsedGb.HasValue && s.VramTotalGb.HasValue
            ? $"{s.VramUsedGb:0.0} / {s.VramTotalGb:0.0} GB" + (s.VramLoad.HasValue ? $" ({s.VramLoad:0}%)" : "") : NA;
        _gpuPower!.Value = s.GpuPowerW is { } w ? $"{w:0.#} W" : NA;
        _gpuFan!.Value = s.GpuFanRpm is { } f ? $"{f:0} RPM" : NA;
        Cpu.Update(s.CpuName, s.CpuLoad, s.CpuTemp, s.CpuClockMhz);
        Gpu.Update(s.GpuName, s.GpuLoad, s.GpuTemp, s.GpuClockMhz);
        Ram.Update(Loc.T("Memória RAM"), s.RamLoad, null, null, s.RamTotalGb, s.RamUsedGb, s.RamAvailableGb);
        Disk.Update(s);

        UptimeText = s.Uptime.Days > 0
            ? $"{s.Uptime.Days}d {s.Uptime.Hours:00}h {s.Uptime.Minutes:00}m {s.Uptime.Seconds:00}s"
            : $"{s.Uptime.Hours:00}h {s.Uptime.Minutes:00}m {s.Uptime.Seconds:00}s";
        LastUpdateText = s.Timestamp.ToString("dd/MM/yyyy HH:mm:ss");

        int worst = Math.Max(Level(s.CpuTemp, S.CpuWarnTemp, S.CpuCritTemp).level,
                             Level(s.GpuTemp, S.GpuWarnTemp, S.GpuCritTemp).level);
        (StatusText, StatusBrush) = worst switch
        {
            2 => (Loc.T("Temperatura crítica"), _crit),
            1 => (Loc.T("Temperatura elevada"), _warn),
            0 => (Loc.T("Funcionamento normal"), _normal),
            _ => (Loc.T("Sem sensores de temperatura"), Brushes.Gray),
        };

        var notes = new List<string>();
        if (!s.CpuTemp.HasValue && !s.IsElevated)
            notes.Add(Loc.T("Temperatura da CPU indisponível: execute como administrador para acessar este sensor."));
        if (s.GpuName is null) notes.Add(Loc.T("Nenhuma GPU compatível detectada."));
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
