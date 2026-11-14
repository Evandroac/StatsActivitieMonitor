using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Diagnostics.Tracing.Session;
using StatsMonitor.Settings;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Services;

/// <summary>
/// Counts frames per second of the foreground full-screen (game) window by listening to the
/// DXGI/D3D9 Present events via ETW (needs administrator rights). Publishes the value to the gadget while a game is in front.
/// </summary>
public sealed class FpsMonitor : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42;
    private const int D3D9PresentStart = 1;

    private readonly SettingsService _settings;
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ConcurrentDictionary<int, int> _frames = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _ownPid = Environment.ProcessId;
    private TraceEventSession? _session;
    private bool _traceFailed;
    private int _avgPid, _idleTicks;
    private double _avgFrames, _avgSeconds;

    public FpsMonitor(SettingsService settings, MainViewModel vm)
    {
        _settings = settings;
        _vm = vm;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    private void Tick()
    {
        if (!_settings.Current.ShowFpsGadget || _vm.EcoMode)
        {
            StopTrace();
            _avgPid = 0; _avgFrames = 0; _avgSeconds = 0; _vm.FpsAvgText = "--"; _vm.FpsText = "--";
            _vm.FpsActive = false;
            if (_vm.FpsHistory.Any(v => v != 0)) for (int i = 0; i < _vm.FpsHistory.Count; i++) _vm.PushFps(0);
            return;
        }

        StartTrace();
        double seconds = _clock.Elapsed.TotalSeconds;
        _clock.Restart();

        var counts = new Dictionary<int, int>();
        foreach (var pid in _frames.Keys.ToList())
            if (_frames.TryRemove(pid, out int n)) counts[pid] = n;

        int fgPid = ForegroundGamePid();
        if (fgPid > 0 && counts.TryGetValue(fgPid, out int frames) && frames >= 5 && seconds > 0)
        {
            if (fgPid != _avgPid) { _avgPid = fgPid; _avgFrames = 0; _avgSeconds = 0; }
            _idleTicks = 0;
            _avgFrames += frames; _avgSeconds += seconds;
            _vm.FpsText = (frames / seconds).ToString("0");
            _vm.PushFps(frames / seconds);
            _vm.FpsAvgText = (_avgFrames / _avgSeconds).ToString("0");
            _vm.FpsActive = true;
        }
        else
        {
            if (++_idleTicks >= 10) { _avgPid = 0; _avgFrames = 0; _avgSeconds = 0; _vm.FpsAvgText = "--"; }
            if (_idleTicks >= 2) _vm.FpsText = "--";
            _vm.PushFps(0);
            _vm.FpsActive = false;
        }
    }

    private void StartTrace()
    {
        if (_session != null || _traceFailed) return;
        try
        {
            _session = new TraceEventSession("StatsMonitorFpsSession") { StopOnDispose = true };
            _session.EnableProvider(DxgiProvider);
            _session.EnableProvider(D3D9Provider);
            var source = _session.Source;
            source.AllEvents += ev =>
            {
                int id = (int)ev.ID;
                if ((ev.ProviderGuid == DxgiProvider && id == DxgiPresentStart)
                    || (ev.ProviderGuid == D3D9Provider && id == D3D9PresentStart))
                    _frames.AddOrUpdate(ev.ProcessID, 1, (_, n) => n + 1);
            };
            var session = _session;
            new Thread(() => { try { session.Source.Process(); } catch { } }) { IsBackground = true, Name = "FpsTrace" }.Start();
        }
        catch
        {
            // Not elevated or ETW unavailable: FPS just isn't shown.
            _traceFailed = true;
            StopTrace();
        }
    }

    private void StopTrace()
    {
        var s = _session;
        _session = null;
        try { s?.Dispose(); } catch { }
        _frames.Clear();
    }

    // The foreground process, if its window covers the whole monitor (fullscreen / borderless game).
    private int ForegroundGamePid()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == _ownPid) return 0;
        if (!GetWindowRect(hwnd, out var r)) return 0;

        var mon = MonitorFromWindow(hwnd, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return 0;

        bool covers = r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
                      && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        return covers ? (int)pid : 0;
    }

    public void Dispose()
    {
        _timer.Stop();
        StopTrace();
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO mi);
}
