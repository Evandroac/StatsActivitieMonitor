using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace StatsMonitor.Helpers;

/// <summary>Lowers the process footprint while the app has nothing to show: background priority and a trimmed working set.</summary>
public static class PowerSaver
{
    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    private static bool _eco;

    public static void Apply(bool eco)
    {
        if (_eco == eco) return;
        _eco = eco;
        try
        {
            using var p = Process.GetCurrentProcess();
            p.PriorityClass = eco ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
            if (eco)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                SetProcessWorkingSetSize(p.Handle, -1, -1);
            }
        }
        catch { /* best effort */ }
    }
}