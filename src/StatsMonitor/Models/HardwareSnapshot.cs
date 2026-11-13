namespace StatsMonitor.Models;

/// <summary>One reading of all monitored hardware. Null values mean the sensor is unavailable (shown as "N/D").</summary>
public sealed class HardwareSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public TimeSpan Uptime { get; init; }

    public string CpuName { get; init; } = "CPU";
    public double? CpuLoad { get; init; }
    public double? CpuClockMhz { get; init; }
    public double? CpuTemp { get; init; }

    public double? RamTotalGb { get; init; }
    public double? RamUsedGb { get; init; }
    public double? RamAvailableGb { get; init; }
    public double? RamLoad { get; init; }

    public string? GpuName { get; init; }
    public double? GpuLoad { get; init; }
    public double? GpuClockMhz { get; init; }
    public double? GpuTemp { get; init; }
    public double? GpuMemClockMhz { get; init; }
    public double? GpuPowerW { get; init; }
    public double? GpuFanRpm { get; init; }

    public double? VramTotalGb { get; init; }
    public double? VramUsedGb { get; init; }
    public double? VramAvailableGb { get; init; }
    public double? VramLoad { get; init; }
    public double? VramTemp { get; init; }

    /// <summary>True when the process runs elevated (needed by some temperature sensors).</summary>
    public bool IsElevated { get; init; }
}
