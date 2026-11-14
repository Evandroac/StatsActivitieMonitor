using System.IO;
using System.Text.Json;

namespace StatsMonitor.Diagnostics;

public sealed record SessionComparison(string OlderId, string NewerId, int? ScoreDelta,
    List<(string Component, Severity Before, Severity After)> HealthChanges, List<Finding> NewProblems, List<Finding> Resolved);

/// <summary>Sessions persisted as JSON files plus an append-only audit log. The directory is injectable for tests.</summary>
public sealed class DiagnosticStore
{
    public const int MaxSessions = 100;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();

    public string Dir { get; }

    public DiagnosticStore(string? dir = null)
    {
        Dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "diagnostics");
        Directory.CreateDirectory(Dir);
    }

    private string PathOf(string id) => Path.Combine(Dir, $"session-{id}.json");

    public void Save(DiagnosticSession s)
    {
        lock (_gate)
        {
            File.WriteAllText(PathOf(s.Id), JsonSerializer.Serialize(s, Json));
            foreach (var old in Directory.GetFiles(Dir, "session-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(MaxSessions))
                try { File.Delete(old); } catch { }
        }
    }

    public DiagnosticSession? Load(string id)
    {
        try { return JsonSerializer.Deserialize<DiagnosticSession>(File.ReadAllText(PathOf(id))); }
        catch { return null; }
    }

    /// <summary>Newest first. Unreadable files are skipped.</summary>
    public List<DiagnosticSession> List()
    {
        var list = new List<DiagnosticSession>();
        foreach (var f in Directory.GetFiles(Dir, "session-*.json"))
        {
            try { if (JsonSerializer.Deserialize<DiagnosticSession>(File.ReadAllText(f)) is { } s) list.Add(s); } catch { }
        }
        return list.OrderByDescending(s => s.Started).ToList();
    }

    public bool Delete(string id)
    {
        try { var p = PathOf(id); if (!File.Exists(p)) return false; File.Delete(p); Audit($"session deleted {id}"); return true; }
        catch { return false; }
    }

    public void Audit(string message)
    {
        try { lock (_gate) File.AppendAllText(Path.Combine(Dir, "audit.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{message}{Environment.NewLine}"); }
        catch { }
    }

    private static string Key(Finding f) => f.Component + "|" + f.Problem;

    /// <summary>Compares two sessions chronologically: a problem is "new" if it is Warning/Critical now and was not before, "resolved" if the reverse.</summary>
    public static SessionComparison Compare(DiagnosticSession a, DiagnosticSession b)
    {
        var (older, newer) = a.Started <= b.Started ? (a, b) : (b, a);
        static bool Bad(Finding f) => f.Level is Severity.Warning or Severity.Critical;
        var oldBad = older.Findings.Where(Bad).GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var newBad = newer.Findings.Where(Bad).GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var changes = new List<(string, Severity, Severity)>();
        foreach (var c in Comp.All)
        {
            var before = older.Health.GetValueOrDefault(c, Severity.Indeterminate);
            var after = newer.Health.GetValueOrDefault(c, Severity.Indeterminate);
            if (before != after) changes.Add((c, before, after));
        }
        return new SessionComparison(older.Id, newer.Id,
            older.Score is { } o && newer.Score is { } n ? n - o : null, changes,
            newBad.Where(kv => !oldBad.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList(),
            oldBad.Where(kv => !newBad.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList());
    }
}
