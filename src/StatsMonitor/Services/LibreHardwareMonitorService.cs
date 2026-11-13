using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using StatsMonitor.Models;

namespace StatsMonitor.Services;

public sealed class LibreHardwareMonitorService : IHardwareMonitorService
{
    private readonly object _gate = new();
    private Computer? _computer;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;
    private readonly bool _elevated = IsElevated();

    public event EventHandler<HardwareSnapshot>? SnapshotUpdated;
    public event EventHandler<Exception>? ErrorOccurred;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _loop is { IsCompleted: false }) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token));
        }
    }

    public void Stop()
    {
        Task? loop;
        lock (_gate)
        {
            _cts?.Cancel();
            loop = _loop;
            _loop = null;
        }
        try { loop?.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        lock (_gate)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
            _computer.Open();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                SnapshotUpdated?.Invoke(this, ReadSnapshot());
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }

            try { await Task.Delay(Interval, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }

        try { _computer?.Close(); } catch { /* ignore on shutdown */ }
        _computer = null;
    }

    private HardwareSnapshot ReadSnapshot()
    {
        var all = new List<IHardware>();
        if (_computer != null)
        {
            foreach (var hw in _computer.Hardware)
            {
                Collect(hw, all);
            }
        }

        var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var mem = all.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
        // Prefer the discrete GPU (largest VRAM) over integrated graphics.
        var gpu = all.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                     .OrderByDescending(h => Value(h, SensorType.SmallData, "GPU Memory Total") ?? 0)
                     .FirstOrDefault();

        double? ramUsed = Value(mem, SensorType.Data, "Memory Used");
        double? ramAvail = Value(mem, SensorType.Data, "Memory Available");
        double? ramTotal = ramUsed + ramAvail;
        double? ramLoad = Value(mem, SensorType.Load, "Memory") ?? (ramTotal > 0 ? ramUsed / ramTotal * 100 : null);

        // SmallData sensors are reported in MB.
        double? vramTotal = Value(gpu, SensorType.SmallData, "GPU Memory Total") / 1024;
        double? vramUsed = (Value(gpu, SensorType.SmallData, "GPU Memory Used")
                            ?? Value(gpu, SensorType.SmallData, "D3D Dedicated Memory Used")) / 1024;
        double? vramFree = Value(gpu, SensorType.SmallData, "GPU Memory Free") / 1024;
        vramFree ??= vramTotal - vramUsed;
        double? vramLoad = Value(gpu, SensorType.Load, "GPU Memory")
                           ?? (vramTotal > 0 ? vramUsed / vramTotal * 100 : null);

        return new HardwareSnapshot
        {
            Timestamp = DateTime.Now,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            IsElevated = _elevated,

            CpuName = cpu?.Name.Trim() ?? "CPU",
            CpuLoad = Value(cpu, SensorType.Load, "CPU Total"),
            CpuClockMhz = Average(cpu, SensorType.Clock, n => n.StartsWith("CPU Core #", StringComparison.Ordinal)),
            CpuTemp = CpuTemperature(cpu),

            RamTotalGb = ramTotal,
            RamUsedGb = ramUsed,
            RamAvailableGb = ramAvail,
            RamLoad = ramLoad,
            RamTemp = MaxTemp(all.Where(h => h.HardwareType == HardwareType.Memory)),

            GpuName = gpu?.Name.Trim(),
            GpuLoad = Value(gpu, SensorType.Load, "GPU Core") ?? Value(gpu, SensorType.Load, "D3D 3D"),
            GpuClockMhz = Value(gpu, SensorType.Clock, "GPU Core"),
            GpuTemp = Value(gpu, SensorType.Temperature, "GPU Core")
                      ?? Value(gpu, SensorType.Temperature, "GPU Hot Spot")
                      ?? FirstValue(gpu, SensorType.Temperature),

            VramTotalGb = vramTotal,
            VramUsedGb = vramUsed,
            VramAvailableGb = vramFree,
            VramLoad = vramLoad,
            VramTemp = gpu?.Sensors
                .Where(s => s.SensorType == SensorType.                Temperature && Valid(s.SensorType, s.Value).HasValue                            && s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                .Select(s => (double?)s.Value!.Value).Max(),
        };
    }

    private static void Collect(IHardware hw, List<IHardware> list)
    {
        hw.Update();
        list.Add(hw);
        foreach (var sub in hw.SubHardware) Collect(sub, list);
    }

    private static double? Value(IHardware? hw, SensorType type, string name)
    {
        var v = hw?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;
        return Valid(type, v);
    }

    // A temperature of 0 (or absurd) means the sensor could not really be read.
    private static double? Valid(SensorType type, float? v)
    {
        if (!v.HasValue) return null;
        if (type == SensorType.Temperature && (v.Value <= 0 || v.Value > 125)) return null;
        return v.Value;
    }

    private static double? FirstValue(IHardware? hw, SensorType type)
    {
        var v = hw?.Sensors.FirstOrDefault(s =>         s.SensorType == type && Valid(type, s.Value).HasValue)?.Value;
                return Valid(type, v);
    }

    private static double? Average(IHardware? hw, SensorType type, Func<string, bool> nameFilter)
    {
        var values = hw?.Sensors
            .Where(s => s.SensorType == type && s.Value.HasValue && nameFilter(s.Name))
            .Select(s => (double)s.Value!.Value).ToList();
        return values is { Count: > 0 } ? values.Average() : null;
    }

    private static double? MaxTemp(IEnumerable<IHardware> hardware)
    {
        var values = hardware.SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Temperature && Valid(s.SensorType, s.Value).HasValue)
            .Select(s => (double)s.Value!.Value).ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    private static double? CpuTemperature(IHardware? cpu)
    {
        if (cpu == null) return null;
        string[] preferred = ["CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Core Average", "Core Max", "CPU Cores"];
        foreach (var name in preferred)
        {
            var v = Value(cpu, SensorType.Temperature, name);
            if (v.HasValue) return v;
        }
        return MaxTemp([cpu]);
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
