using StatsMonitor.Models;

namespace StatsMonitor.Services;

/// <summary>Abstraction over the hardware library so it can be replaced in the future.</summary>
public interface IHardwareMonitorService : IDisposable
{
    event EventHandler<HardwareSnapshot>? SnapshotUpdated;
    event EventHandler<Exception>? ErrorOccurred;

    TimeSpan Interval { get; set; }

    /// <summary>While true no hardware is read.</summary>
    bool Paused { get; set; }

    /// <summary>Refresh disk and motherboard sensors only every few seconds.</summary>
    bool SlowSensors { get; set; }

    void Start();
    void Stop();
}
