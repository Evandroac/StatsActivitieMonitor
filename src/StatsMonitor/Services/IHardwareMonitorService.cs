using StatsMonitor.Models;

namespace StatsMonitor.Services;

/// <summary>Abstraction over the hardware library so it can be replaced in the future.</summary>
public interface IHardwareMonitorService : IDisposable
{
    event EventHandler<HardwareSnapshot>? SnapshotUpdated;
    event EventHandler<Exception>? ErrorOccurred;

    TimeSpan Interval { get; set; }

    void Start();
    void Stop();
}
