using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace StatsMonitor.Services;

/// <summary>Per-process network bytes via the ETW kernel provider (needs administrator). Runs only while the page is open.</summary>
public sealed class ProcessNetMonitor
{
    private readonly object _lock = new();
    private Dictionary<int, long> _bytes = [];
    private TraceEventSession? _session;
    private volatile bool _stop;
    public volatile bool Failed;

    public void Start()
    {
        if (_session != null) return;
        _stop = false; Failed = false;
        new Thread(Run) { IsBackground = true, Name = "ProcNetEtw" }.Start();
    }

    public void Stop()
    {
        _stop = true;
        try { _session?.Stop(); } catch { }
    }

    private void Add(int pid, int size)
    {
        lock (_lock) { _bytes[pid] = _bytes.GetValueOrDefault(pid) + size; }
    }

    public Dictionary<int, long> Take()
    {
        lock (_lock) { var r = _bytes; _bytes = []; return r; }
    }

    private void Run()
    {
        try
        {
            using var s = new TraceEventSession(KernelTraceEventParser.KernelSessionName) { StopOnDispose = true };
            _session = s;
            s.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
            var k = s.Source.Kernel;
            k.TcpIpSend += d => Add(d.ProcessID, d.size);
            k.TcpIpRecv += d => Add(d.ProcessID, d.size);
            k.UdpIpSend += d => Add(d.ProcessID, d.size);
            k.UdpIpRecv += d => Add(d.ProcessID, d.size);
            k.TcpIpSendIPV6 += d => Add(d.ProcessID, d.size);
            k.TcpIpRecvIPV6 += d => Add(d.ProcessID, d.size);
            k.UdpIpSendIPV6 += d => Add(d.ProcessID, d.size);
            k.UdpIpRecvIPV6 += d => Add(d.ProcessID, d.size);
            if (_stop) return;
            s.Source.Process();
        }
        catch { Failed = !_stop; }
        finally { _session = null; }
    }
}

/// <summary>Samples every process with plain Win32 calls (no Process objects) and computes CPU %, memory and I/O rates.</summary>
public sealed class ProcessSampler
{
    private readonly Dictionary<int, (long created, long cpu, long io)> _prev = [];
    private readonly Dictionary<string, (string desc, ImageSource? icon)> _meta = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _sw = new();
    private DateTime _linkAt = DateTime.MinValue;
    private ImageSource? _defaultIcon;

    public ProcessNetMonitor Net { get; } = new();
    public double LinkBytesPerSec { get; private set; } = 12.5e6;
    public long TotalRam { get; } = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    public void Reset() { _prev.Clear(); _sw.Reset(); }

    public List<ProcSample> Sample()
    {
        double dt = _sw.IsRunning ? _sw.Elapsed.TotalSeconds : 0;
        _sw.Restart();
        RefreshLink();

        var withWindow = new HashSet<int>();
        var hung = new HashSet<int>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero && GetWindowTextLength(h) > 0)
            {
                GetWindowThreadProcessId(h, out uint p);
                withWindow.Add((int)p);
                if (IsHungAppWindow(h)) hung.Add((int)p);
            }
            return true;
        }, IntPtr.Zero);

        var net = Net.Take();
        var list = new List<ProcSample>(300);
        var alive = new HashSet<int>();
        int cores = Environment.ProcessorCount;

        var snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == new IntPtr(-1)) return list;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
            {
                int pid = (int)e.th32ProcessID;
                if (pid == 0) continue;
                alive.Add(pid);
                long created = 0, cpu = 0, ws = 0, io = 0; string path = "";
                var h = OpenProcess(0x1000, false, (uint)pid);
                if (h != IntPtr.Zero)
                {
                    if (GetProcessTimes(h, out long c, out _, out long k, out long u)) { created = c; cpu = k + u; }
                    var pmc = new PMC { cb = (uint)Marshal.SizeOf<PMC>() };
                    if (K32GetProcessMemoryInfo(h, ref pmc, pmc.cb)) ws = (long)pmc.WorkingSetSize;
                    if (GetProcessIoCounters(h, out var ioc)) io = (long)(ioc.ReadTransferCount + ioc.WriteTransferCount);
                    var sb = new StringBuilder(1024); int n = sb.Capacity;
                    if (QueryFullProcessImageNameW(h, 0, sb, ref n)) path = sb.ToString();
                    CloseHandle(h);
                }

                double cpuPct = 0, diskBps = 0;
                if (dt > 0.05 && _prev.TryGetValue(pid, out var p) && p.created == created && created != 0)
                {
                    cpuPct = Math.Clamp((cpu - p.cpu) / (dt * 1e7 * cores) * 100, 0, 100);
                    diskBps = Math.Max(0, (io - p.io) / dt);
                }
                _prev[pid] = (created, cpu, io);
                double netBps = dt > 0.05 && net.TryGetValue(pid, out var nb) ? nb / dt : 0;

                var (desc, icon) = Meta(path);
                var name = pid == 4 ? "System" : e.szExeFile;
                list.Add(new ProcSample(pid, (int)e.th32ParentProcessID, created, name, path, desc, icon,
                    withWindow.Contains(pid), hung.Contains(pid), (int)e.cntThreads, cpuPct, ws, diskBps, netBps));
            }
        }
        finally { CloseHandle(snap); }

        foreach (var gone in _prev.Keys.Where(k => !alive.Contains(k)).ToList()) _prev.Remove(gone);
        return list;
    }

    private (string desc, ImageSource? icon) Meta(string path)
    {
        if (path.Length == 0) return ("", DefaultIcon());
        if (_meta.TryGetValue(path, out var m)) return m;
        string desc = ""; ImageSource? icon = null;
        try { desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; } catch { }
        try
        {
            using var ic = Icon.ExtractAssociatedIcon(path);
            if (ic != null)
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                icon = bs;
            }
        }
        catch { }
        return _meta[path] = (desc, icon ?? DefaultIcon());
    }

    private ImageSource DefaultIcon()
    {
        if (_defaultIcon != null) return _defaultIcon;
        var bs = Imaging.CreateBitmapSourceFromHIcon(SystemIcons.Application.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bs.Freeze();
        return _defaultIcon = bs;
    }

    private void RefreshLink()
    {
        if ((DateTime.UtcNow - _linkAt).TotalSeconds < 30) return;
        _linkAt = DateTime.UtcNow;
        try
        {
            long best = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Select(n => n.Speed).DefaultIfEmpty(0).Max();
            if (best > 1_000_000) LinkBytesPerSec = best / 8.0;
        }
        catch { }
    }

    public static string? GetCommandLine(int pid)
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={pid}");
            foreach (System.Management.ManagementObject o in s.Get()) return o["CommandLine"] as string;
        }
        catch { }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PMC
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr s, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr s, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr h, out long c, out long e, out long k, out long u);
    [DllImport("kernel32.dll")] private static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS c);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll")] private static extern bool K32GetProcessMemoryInfo(IntPtr h, ref PMC c, uint cb);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr h);
}
