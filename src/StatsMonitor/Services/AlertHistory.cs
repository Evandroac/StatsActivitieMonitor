using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace StatsMonitor.Services;

public sealed record AlertHistoryEntry(DateTime Time, string Title, string Message)
{
    public string Display => $"{Time:dd/MM HH:mm:ss}  ·  {Title} — {Message}";
}

/// <summary>Keeps the most recent alerts (newest first) and persists them across sessions.</summary>
public static class AlertHistory
{
    public const int Capacity = 100;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "alerts.json");

    public static ObservableCollection<AlertHistoryEntry> Entries { get; } = Load();

    public static void Add(AlertEvent e) => Add(new AlertHistoryEntry(DateTime.Now, e.Title, e.Message));

    public static void Add(AlertHistoryEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > Capacity) Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

    public static void Clear() { Entries.Clear(); Save(); }

    private static ObservableCollection<AlertHistoryEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return new(JsonSerializer.Deserialize<List<AlertHistoryEntry>>(File.ReadAllText(FilePath)) ?? []);
        }
        catch { /* corrupt history is discarded */ }
        return [];
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Entries));
        }
        catch { /* history is best effort */ }
    }
}
