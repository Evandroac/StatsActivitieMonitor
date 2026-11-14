using System.Diagnostics;

namespace StatsMonitor.Services;

/// <summary>Checks the Windows Update Agent for pending (not installed) updates.</summary>
public static class WindowsUpdateChecker
{
    /// <summary>Number of pending updates, or null when the check is unavailable.</summary>
    public static Task<int?> CountPendingAsync() => Task.Run<int?>(() =>
    {
        try
        {
            var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (type is null) return null;
            dynamic session = Activator.CreateInstance(type)!;
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
            return (int)result.Updates.Count;
        }
        catch { return null; }
    });

    public static void OpenWindowsUpdate()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }); }
        catch { }
    }
}