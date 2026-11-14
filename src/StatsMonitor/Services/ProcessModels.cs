using System.Windows;
using System.Windows.Media;
using StatsMonitor.Helpers;

namespace StatsMonitor.Services;

public enum ProcType { App, Background, Windows }
public enum ProcSort { Name, Cpu, Memory, Disk, Network, Pid, Status, Type, Threads, Parent, Exe, Path, Started }

public sealed record ProcSample(int Pid, int Parent, long Created, string Name, string Path, string Description,
    ImageSource? Icon, bool HasWindow, bool Hung, int Threads, double Cpu, long WorkingSet, double DiskBps, double NetBps);

/// <summary>Search, filters and sorting applied to the process list. Any search/filter flattens the tree.</summary>
public sealed class ProcessQuery
{
    public string Search { get; set; } = "";
    public ProcType? Type { get; set; }
    public bool? Hung { get; set; }
    public ProcSort Sort { get; set; } = ProcSort.Cpu;
    public bool Descending { get; set; } = true;
    public bool Flat => Search.Trim().Length > 0 || Type != null || Hung != null;
}

public sealed class ProcessRow : ObservableObject
{
    public static long TotalRam = 16L << 30;
    public static double DiskRefBps = 100e6;
    public static double LinkBytesPerSec = 12.5e6;
    public static bool NetAvailable = true;

    private static readonly Brush[] HeatBrushes = Enumerable.Range(0, 6).Select(i =>
    {
        var b = new SolidColorBrush(Color.FromArgb((byte)(i == 0 ? 0 : 28 + i * 20), 255, 160, 40));
        b.Freeze();
        return (Brush)b;
    }).ToArray();
    private static readonly Brush OkBrush = Frozen(Color.FromRgb(0x3D, 0xDC, 0x84));
    private static readonly Brush BadBrush = Frozen(Color.FromRgb(0xFF, 0x5C, 0x5C));
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static readonly double[] CpuScale = [1, 5, 15, 30, 50];
    private static readonly double[] MemScale = [0.5, 2, 5, 10, 20];

    private static Brush Heat(double pct, double[] scale)
    {
        int level = 0;
        while (level < scale.Length && pct >= scale[level]) level++;
        return HeatBrushes[level];
    }

    public int Pid { get; }
    public long Created { get; }
    public int ParentPid { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Path { get; private set; } = "";
    public ImageSource? Icon { get; private set; }
    public ProcType Type { get; private set; }
    public bool Hung { get; private set; }
    public int Threads { get; private set; }
    public double OwnCpu { get; private set; }
    public long OwnMem { get; private set; }
    public double OwnDisk { get; private set; }
    public double OwnNet { get; private set; }

    private double _cpu; private long _mem; private double _disk, _net;
    private int _depth, _children; private bool _expander, _expanded;

    public double Cpu => _cpu;
    public long MemBytes => _mem;
    public double DiskBps => _disk;
    public double NetBps => _net;
    public int Depth => _depth;
    public int ChildCount => _children;
    public bool ShowExpander => _expander;

    public const int HistoryLength = 60;
    public readonly List<float>[] Hist = [[], [], [], []];
    public Color Color { get; }
    public Brush ColorBrush { get; }

    public ProcessRow(ProcSample s)
    {
        Pid = s.Pid; Created = s.Created;
        Color = FromHue(s.Pid * 0.61803398875 % 1.0 * 360);
        var b = new SolidColorBrush(Color); b.Freeze(); ColorBrush = b;
        Update(s);
    }

    private static Color FromHue(double h)
    {
        const double sat = 0.7, val = 0.95;
        double c = val * sat, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = val - c;
        var (r, g, bl) = (int)(h / 60) switch { 0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((bl + m) * 255));
    }

    /// <summary>Appends the process' own CPU/memory/disk/network percentages (chart order) to its rolling history.</summary>
    public void Record()
    {
        float[] v = [(float)OwnCpu, (float)(TotalRam > 0 ? OwnMem * 100.0 / TotalRam : 0),
                     (float)Math.Min(100, OwnDisk / DiskRefBps * 100), (float)Math.Min(100, OwnNet / LinkBytesPerSec * 100)];
        for (int i = 0; i < 4; i++)
        {
            Hist[i].Add(v[i]);
            if (Hist[i].Count > HistoryLength) Hist[i].RemoveAt(0);
        }
    }

    public void Update(ProcSample s)
    {
        ParentPid = s.Parent; Name = s.Name; Path = s.Path; Icon = s.Icon; Threads = s.Threads;
        Description = s.Description;
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Type = s.HasWindow ? ProcType.App
             : s.Pid <= 4 || (Path.Length > 0 && Path.StartsWith(win, StringComparison.OrdinalIgnoreCase)) ? ProcType.Windows
             : ProcType.Background;
        if (Hung != s.Hung) { Hung = s.Hung; Raise(nameof(StatusText)); Raise(nameof(StatusBrush)); }
        OwnCpu = s.Cpu; OwnMem = s.WorkingSet; OwnDisk = s.DiskBps; OwnNet = s.NetBps;
        Raise(nameof(NameText)); Raise(nameof(Icon)); Raise(nameof(Tip));
        Raise(nameof(ThreadsText)); Raise(nameof(TypeText)); Raise(nameof(ParentText)); Raise(nameof(Path));
    }

    public bool IsExpanded
    {
        get => _expanded;
        set { if (Set(ref _expanded, value)) Raise(nameof(Glyph)); }
    }

    public void SetDisplay(double cpu, long mem, double disk, double net, int depth, int children, bool expander)
    {
        if (Math.Abs(_cpu - cpu) > 0.001) { _cpu = cpu; Raise(nameof(CpuText)); Raise(nameof(CpuBrush)); }
        if (_mem != mem) { _mem = mem; Raise(nameof(MemText)); Raise(nameof(MemBrush)); Raise(nameof(MemTip)); }
        if (Math.Abs(_disk - disk) > 1) { _disk = disk; Raise(nameof(DiskText)); Raise(nameof(DiskBrush)); Raise(nameof(DiskTip)); }
        if (Math.Abs(_net - net) > 1) { _net = net; Raise(nameof(NetText)); Raise(nameof(NetBrush)); Raise(nameof(NetTip)); }
        if (_depth != depth) { _depth = depth; Raise(nameof(Indent)); }
        if (_children != children) { _children = children; Raise(nameof(NameText)); }
        if (_expander != expander) { _expander = expander; Raise(nameof(ExpanderVisibility)); }
    }

    public void RefreshTexts()
    {
        foreach (var n in new[] { nameof(StatusText), nameof(NetText), nameof(MemTip), nameof(DiskTip), nameof(NetTip), nameof(Tip), nameof(TypeText), nameof(StartedText) }) Raise(n);
    }

    private static string Pct(double v) => v < 0.05 ? "0%" : v < 10 ? $"{v:0.0}%" : $"{v:0}%";
    private static string Rate(double bps) =>
        bps >= 1e6 ? $"{bps / 1e6:0.0} MB/s" : bps >= 1e3 ? $"{bps / 1e3:0} KB/s" : $"{bps:0} B/s";
    private static string En(string pt, string en) => Loc.Lang == "en" ? en : Loc.T(pt);

    public double MemPct => TotalRam > 0 ? _mem * 100.0 / TotalRam : 0;
    public double DiskPct => Math.Min(100, _disk / DiskRefBps * 100);
    public double NetPct => Math.Min(100, _net / LinkBytesPerSec * 100);

    public string NameText => (Description.Length > 0 ? Description : Name) + (_children > 0 ? $" ({_children})" : "");
    public string StatusText => Hung ? En("Não está respondendo", "Not responding") : En("Em execução", "Running");
    public Brush StatusBrush => Hung ? BadBrush : OkBrush;
    public string CpuText => Pct(_cpu);
    public string MemText => Pct(MemPct);
    public string DiskText => Pct(DiskPct);
    public string NetText => NetAvailable ? Pct(NetPct) : "—";
    public Brush CpuBrush => Heat(_cpu, CpuScale);
    public Brush MemBrush => Heat(MemPct, MemScale);
    public Brush DiskBrush => Heat(DiskPct, CpuScale);
    public Brush NetBrush => NetAvailable ? Heat(NetPct, CpuScale) : HeatBrushes[0];
    public string MemTip => $"{_mem / 1048576.0:#,0.#} MB";
    public string DiskTip => Rate(_disk);
    public string NetTip => NetAvailable ? Rate(_net) : En("Indisponível (requer administrador)", "Unavailable (requires administrator)");
    public string PidText => Pid.ToString();
    public string ParentText => ParentPid.ToString();
    public string ThreadsText => Threads.ToString();
    public string TypeText => Type switch { ProcType.App => En("Aplicativo", "App"), ProcType.Windows => En("Processo do Windows", "Windows process"), _ => En("Segundo plano", "Background") };
    public string StartedText => Created > 0 ? DateTime.FromFileTime(Created).ToString("g") : "—";
    public string Tip => $"{Name}  •  PID {Pid}" + (Path.Length > 0 ? "\n" + Path : "");
    public Thickness Indent => new(_depth * 18, 0, 0, 0);
    public string Glyph => _expanded ? "▾" : "▸";
    public Visibility ExpanderVisibility => _expander ? Visibility.Visible : Visibility.Hidden;
}

public static class ProcessTree
{
    /// <summary>Returns the visible, ordered rows: a tree (parents aggregate their subtree) or a flat list when searching/filtering.</summary>
    public static List<ProcessRow> Arrange(IReadOnlyCollection<ProcessRow> all, ProcessQuery q)
    {
        var cmp = Comparer(q);
        if (q.Flat)
        {
            var flat = all.Where(r => Matches(r, q)).ToList();
            foreach (var r in flat) r.SetDisplay(r.OwnCpu, r.OwnMem, r.OwnDisk, r.OwnNet, 0, 0, false);
            flat.Sort(cmp);
            return flat;
        }

        var byPid = new Dictionary<int, ProcessRow>();
        foreach (var r in all) byPid[r.Pid] = r;
        var kids = new Dictionary<int, List<ProcessRow>>();
        var roots = new List<ProcessRow>();
        foreach (var r in all)
        {
            if (r.ParentPid != r.Pid && byPid.TryGetValue(r.ParentPid, out var p)
                && (p.Created == 0 || r.Created == 0 || p.Created < r.Created))
            {
                if (!kids.TryGetValue(p.Pid, out var l)) kids[p.Pid] = l = [];
                l.Add(r);
            }
            else roots.Add(r);
        }

        var agg = new Dictionary<int, (double cpu, long mem, double disk, double net, int count)>();
        var seen = new HashSet<int>();
        (double cpu, long mem, double disk, double net, int count) Walk(ProcessRow r)
        {
            seen.Add(r.Pid);
            var t = (cpu: r.OwnCpu, mem: r.OwnMem, disk: r.OwnDisk, net: r.OwnNet, count: 0);
            if (kids.TryGetValue(r.Pid, out var l))
                foreach (var c in l)
                {
                    if (seen.Contains(c.Pid)) continue;
                    var a = Walk(c);
                    t.cpu += a.cpu; t.mem += a.mem; t.disk += a.disk; t.net += a.net; t.count += 1 + a.count;
                }
            agg[r.Pid] = t;
            return t;
        }
        foreach (var r in roots) Walk(r);
        // rows in a parent cycle are unreachable from any root; promote them
        foreach (var r in all.Where(x => !seen.Contains(x.Pid)).ToList()) { roots.Add(r); Walk(r); }

        var result = new List<ProcessRow>();
        var emitted = new HashSet<int>();
        void Emit(ProcessRow r, int depth)
        {
            if (!emitted.Add(r.Pid)) return;
            var a = agg[r.Pid];
            var has = kids.TryGetValue(r.Pid, out var l) && l.Count > 0;
            r.SetDisplay(a.cpu, a.mem, a.disk, a.net, depth, a.count, has);
            result.Add(r);
            if (has && r.IsExpanded)
            {
                var sorted = l!.ToList();
                sorted.Sort(cmp);
                foreach (var c in sorted) Emit(c, depth + 1);
            }
        }
        roots.Sort(cmp);
        foreach (var r in roots) Emit(r, 0);
        return result;
    }

    public static bool Matches(ProcessRow r, ProcessQuery q)
    {
        if (q.Type != null && r.Type != q.Type) return false;
        if (q.Hung != null && r.Hung != q.Hung) return false;
        var s = q.Search.Trim();
        if (s.Length == 0) return true;
        return r.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || r.Description.Contains(s, StringComparison.OrdinalIgnoreCase)
            || r.Path.Contains(s, StringComparison.OrdinalIgnoreCase)
            || r.Pid.ToString().Contains(s);
    }

    private static Comparison<ProcessRow> Comparer(ProcessQuery q) => (a, b) =>
    {
        int c = q.Sort switch
        {
            ProcSort.Cpu => a.Cpu.CompareTo(b.Cpu),
            ProcSort.Memory => a.MemBytes.CompareTo(b.MemBytes),
            ProcSort.Disk => a.DiskBps.CompareTo(b.DiskBps),
            ProcSort.Network => a.NetBps.CompareTo(b.NetBps),
            ProcSort.Pid => a.Pid.CompareTo(b.Pid),
            ProcSort.Parent => a.ParentPid.CompareTo(b.ParentPid),
            ProcSort.Threads => a.Threads.CompareTo(b.Threads),
            ProcSort.Started => a.Created.CompareTo(b.Created),
            ProcSort.Status => a.Hung.CompareTo(b.Hung),
            ProcSort.Type => a.Type.CompareTo(b.Type),
            ProcSort.Exe => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase),
            ProcSort.Path => string.Compare(a.Path, b.Path, StringComparison.CurrentCultureIgnoreCase),
            _ => string.Compare(a.NameText, b.NameText, StringComparison.CurrentCultureIgnoreCase),
        };
        if (q.Descending) c = -c;
        return c != 0 ? c : a.Pid.CompareTo(b.Pid);
    };

    /// <summary>Removes the executable (quoted or not) from a full command line, leaving only the arguments.</summary>
    public static string StripExecutable(string commandLine, string exePath)
    {
        var s = (commandLine ?? "").Trim();
        if (s.Length == 0) return "";
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end < 0 ? "" : s[(end + 1)..].Trim();
        }
        if (exePath.Length > 0 && s.StartsWith(exePath, StringComparison.OrdinalIgnoreCase))
            return s[exePath.Length..].Trim();
        int sp = s.IndexOf(' ');
        return sp < 0 ? "" : s[(sp + 1)..].Trim();
    }
}
