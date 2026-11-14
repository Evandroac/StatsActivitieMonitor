using System.IO;
using System.Text.Json;
using System.Windows;
using StatsMonitor.Helpers;

namespace StatsMonitor.Services;

/// <summary>One column of the process list: identity, sort key, width and visibility.</summary>
public sealed class ColumnDef : ObservableObject
{
    private double _width;

    public ColumnDef(string id, string pt, string en, ProcSort? sort, double width, bool star = false, bool required = false, bool right = false, bool visible = true)
    {
        Id = id; Pt = pt; En = en; Sort = sort; DefaultWidth = width; _width = width;
        Star = star; Required = required; Right = right; Visible = visible; DefaultVisible = visible;
    }

    public string Id { get; }
    public string Pt { get; }
    public string En { get; }
    public ProcSort? Sort { get; }
    public double DefaultWidth { get; }
    public bool Star { get; }
    public bool Required { get; }
    public bool Right { get; }
    public bool Visible { get; internal set; }
    public bool DefaultVisible { get; }
    public string Title => Loc.Lang == "en" ? En : Pt;

    public double Width
    {
        get => _width;
        set { if (Set(ref _width, value)) Raise(nameof(GridWidth)); }
    }

    public GridLength GridWidth => Star && _width <= 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(_width);
}

/// <summary>User-configurable columns (order, visibility, width) shared by the header and every row; persisted to disk.</summary>
public static class ProcessColumns
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "process-columns.json");

    public static readonly List<ColumnDef> All =
    [
        new("name", "Nome", "Name", ProcSort.Name, 0, star: true, required: true),
        new("status", "Status", "Status", ProcSort.Status, 140),
        new("cpu", "CPU", "CPU", ProcSort.Cpu, 84, right: true),
        new("mem", "Memória", "Memory", ProcSort.Memory, 84, right: true),
        new("disk", "Disco", "Disk", ProcSort.Disk, 84, right: true),
        new("net", "Rede", "Network", ProcSort.Network, 84, right: true),
        new("pid", "PID", "PID", ProcSort.Pid, 80, right: true, visible: false),
        new("parent", "PID pai", "Parent PID", ProcSort.Parent, 90, right: true, visible: false),
        new("threads", "Threads", "Threads", ProcSort.Threads, 84, right: true, visible: false),
        new("type", "Tipo", "Type", ProcSort.Type, 130, visible: false),
        new("exe", "Executável", "Executable", ProcSort.Exe, 150, visible: false),
        new("started", "Iniciado em", "Started", ProcSort.Started, 140, visible: false),
        new("path", "Caminho", "Path", ProcSort.Path, 280, visible: false),
    ];

    public static int Version { get; private set; }
    public static event Action? Changed;

    static ProcessColumns() => Load();

    public static List<ColumnDef> Visible => All.Where(c => c.Visible).ToList();

    public static void SetVisible(ColumnDef c, bool visible)
    {
        if (c.Required || c.Visible == visible) return;
        c.Visible = visible;
        Commit();
    }

    /// <summary>Moves a column so it takes the place of the visible column at <paramref name="visibleIndex"/>.</summary>
    public static void MoveTo(ColumnDef c, int visibleIndex)
    {
        var vis = Visible;
        if (visibleIndex < 0 || visibleIndex >= vis.Count) return;
        var target = vis[visibleIndex];
        if (target == c) return;
        bool right = vis.IndexOf(c) < visibleIndex;
        All.Remove(c);
        All.Insert(All.IndexOf(target) + (right ? 1 : 0), c);
        Commit();
    }

    public static void Reset()
    {
        var order = new[] { "name", "status", "cpu", "mem", "disk", "net", "pid", "parent", "threads", "type", "exe", "started", "path" };
        All.Sort((a, b) => Array.IndexOf(order, a.Id).CompareTo(Array.IndexOf(order, b.Id)));
        foreach (var c in All) { c.Visible = c.DefaultVisible; c.Width = c.DefaultWidth; }
        Commit();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var data = new Stored
            {
                Order = All.Select(c => c.Id).ToList(),
                Hidden = All.Where(c => !c.Visible).Select(c => c.Id).ToList(),
                Widths = All.ToDictionary(c => c.Id, c => c.Width),
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch { }
    }

    private static void Commit()
    {
        Version++;
        Save();
        Changed?.Invoke();
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
            if (data == null) return;
            var ordered = data.Order.Select(id => All.FirstOrDefault(c => c.Id == id)).Where(c => c != null).Cast<ColumnDef>().ToList();
            ordered.AddRange(All.Where(c => !ordered.Contains(c)));
            All.Clear();
            All.AddRange(ordered);
            foreach (var c in All)
            {
                if (!c.Required && data.Hidden.Contains(c.Id)) c.Visible = false;
                else if (!c.Required && data.Order.Contains(c.Id)) c.Visible = true;
                if (data.Widths.TryGetValue(c.Id, out var w)) c.Width = w <= 0 ? 0 : Math.Clamp(w, 50, 800);
            }
        }
        catch { }
    }

    private sealed class Stored
    {
        public List<string> Order { get; set; } = [];
        public List<string> Hidden { get; set; } = [];
        public Dictionary<string, double> Widths { get; set; } = [];
    }
}
