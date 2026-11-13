using System.Management;

namespace StatsMonitor.Services;

/// <summary>WMI queries with a time limit, so a slow or broken provider can never hang the app.</summary>
public static class Wmi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    public static ManagementObjectSearcher Searcher(string query, string? scope = null)
    {
        var options = new EnumerationOptions { Timeout = Timeout, ReturnImmediately = false, Rewindable = false };
        var ms = new ManagementScope(scope ?? @"root\cimv2", new ConnectionOptions { Timeout = Timeout });
        return new ManagementObjectSearcher(ms, new ObjectQuery(query), options);
    }

    public static List<InfoGroup> Unavailable() =>
        [new(Helpers.Loc.T("Indisponível"), [new(Helpers.Loc.T("Os dados não responderam a tempo ou não estão disponíveis neste computador."), "")])];

    /// <summary>Runs <paramref name="work"/> on a worker thread and returns <paramref name="fallback"/> if it fails or exceeds the limit.</summary>
    public static async Task<T> RunAsync<T>(Func<T> work, T fallback, TimeSpan? limit = null)
    {
        var task = Task.Run(work);
        try
        {
            if (await Task.WhenAny(task, Task.Delay(limit ?? TimeSpan.FromSeconds(15))) == task) return await task;
        }
        catch { /* provider failure */ }
        return fallback;
    }
}
