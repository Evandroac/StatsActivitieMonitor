using StatsMonitor.Models;

namespace StatsMonitor.Services;

/// <summary>Abstraction over the hardware library so it can be replaced in the future.</summary>
/// <summary>Minimum time between hardware reads per component; the loop tick is the smallest of them.</summary>
public sealed record ComponentIntervals(TimeSpan Cpu, TimeSpan Gpu, TimeSpan Memory, TimeSpan Other)
{
    public TimeSpan For(HardwareScope g) => g switch { HardwareScope.Cpu => Cpu, HardwareScope.Gpu => Gpu, HardwareScope.Memory => Memory, _ => Other };
    public static ComponentIntervals Uniform(TimeSpan t) => new(t, t, t, t);
}

[Flags]
public enum HardwareScope { None = 0, Cpu = 1, Gpu = 2, Memory = 4, Other = 8, All = 15 }

public interface IHardwareMonitorService : IDisposable
{
    event EventHandler<HardwareSnapshot>? SnapshotUpdated;
    event EventHandler<Exception>? ErrorOccurred;

    TimeSpan Interval { get; set; }

    /// <summary>While true no hardware is read.</summary>
    bool Paused { get; set; }
    /// <summary>Hardware groups that are read; everything else is skipped to save resources.</summary>
    HardwareScope Scope { get; set; }
    ComponentIntervals? Components { get; set; }

    /// <summary>Refresh disk and motherboard sensors only every few seconds.</summary>
    bool SlowSensors { get; set; }

    void Start();
    void Stop();
}
