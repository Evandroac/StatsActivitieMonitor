using System.IO;
using System.Security.Principal;
using StatsMonitor.Helpers;
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
    private volatile bool _paused;
    private volatile int _scope = (int)HardwareScope.All;
    public HardwareScope Scope { get => (HardwareScope)_scope; set => _scope = (int)value; }
    private volatile ComponentIntervals? _components;
    public ComponentIntervals? Components { get => _components; set => _components = value; }
    private readonly Dictionary<HardwareScope, DateTime> _groupAt = new();
    private HardwareScope _due = HardwareScope.All;
    public bool Paused { get => _paused; set => _paused = value; }
    private volatile bool _slowSensors;
    public bool SlowSensors { get => _slowSensors; set { _slowSensors = value; DiskPerfService.CacheSeconds = value ? SlowSeconds : 2; } }
    private const int SlowSeconds = 10;
    private DateTime _slowAt = DateTime.MinValue;
    private bool _slowDue = true;

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
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = true, IsBatteryEnabled = true };
            _computer.Open();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }

        while (!token.IsCancellationRequested)
        {
            if (_paused)
            {
                try { await Task.Delay(500, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }
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
        _slowDue = !_slowSensors || DateTime.Now - _slowAt >= TimeSpan.FromSeconds(SlowSeconds);
        if (_slowDue) _slowAt = DateTime.Now;
        _due = DueGroups();
        if (_computer != null)
        {
            foreach (var hw in _computer.Hardware)
            {
                Collect(hw, all);
            }
        }

        var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // LibreHardwareMonitor exposes both physical ("Total Memory") and "Virtual Memory" (RAM + pagefile).
        var mems = all.Where(h => h.HardwareType == HardwareType.Memory).ToList();
        var mem = mems.FirstOrDefault(h => h.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                  ?? mems.FirstOrDefault(h => !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                  ?? mems.FirstOrDefault();
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
            CpuClockMhz = CpuClock(cpu),
            CpuTemp = CpuTemperature(cpu) ?? AcpiTemperature(),
            CpuPowerW = Value(cpu, SensorType.Power, "CPU Package") ?? Like(cpu!, SensorType.Power, "package"),
            CpuVoltage = cpu is null ? null : Like(cpu, SensorType.Voltage, "core"),
            CpuHottestCoreTemp = cpu?.Sensors
                .Where(x => x.SensorType == SensorType.Temperature && x.Name.Contains("Core #", StringComparison.OrdinalIgnoreCase) && Valid(x.SensorType, x.Value).HasValue)
                .Select(x => (double?)x.Value!.Value).Max(),
            CpuCoreLoads = CoreLoads(cpu),

            RamTotalGb = ramTotal,
            RamUsedGb = ramUsed,
            RamAvailableGb = ramAvail,
            RamLoad = ramLoad,

            GpuName = gpu?.Name.Trim(),
            GpuLoad = Value(gpu, SensorType.Load, "GPU Core") ?? Value(gpu, SensorType.Load, "D3D 3D"),
            GpuClockMhz = Value(gpu, SensorType.Clock, "GPU Core"),
            GpuMemClockMhz = Value(gpu, SensorType.Clock, "GPU Memory")
                             ?? Max(gpu, SensorType.Clock, n => n.Contains("Memory", StringComparison.OrdinalIgnoreCase)),
            GpuPowerW = Value(gpu, SensorType.Power, "GPU Package")
                        ?? Value(gpu, SensorType.Power, "GPU Power")
                        ?? Max(gpu, SensorType.Power, _ => true),
            GpuFanRpm = Max(gpu, SensorType.Fan, _ => true),
            GpuHotSpotTemp = Value(gpu, SensorType.Temperature, "GPU Hot Spot"),
            GpuVoltage = gpu is null ? null : Like(gpu, SensorType.Voltage, "gpu"),
            GpuMemControllerLoad = Value(gpu, SensorType.Load, "GPU Memory Controller"),
            GpuVideoEngineLoad = Value(gpu, SensorType.Load, "GPU Video Engine"),
            GpuTemp = Value(gpu, SensorType.Temperature, "GPU Core")
                      ?? Value(gpu, SensorType.Temperature, "GPU Hot Spot")
                      ?? FirstValue(gpu, SensorType.Temperature),

            VramTotalGb = vramTotal,
            VramUsedGb = vramUsed,
            VramAvailableGb = vramFree,
            VramLoad = vramLoad,
            Disks = Scope.HasFlag(HardwareScope.Other) ? ReadDisks(all) : [],
            Volumes = Scope.HasFlag(HardwareScope.Other) ? CachedVolumes() : [],
            Sensors = Scope.HasFlag(HardwareScope.Other) ? ReadSensors(all) : [],
            VramTemp = gpu?.Sensors
                .Where(s => s.SensorType == SensorType.                Temperature && Valid(s.SensorType, s.Value).HasValue                            && s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                .Select(s => (double?)s.Value!.Value).Max(),
        };
    }

    private static List<SensorReading> ReadSensors(List<IHardware> all)
    {
        var list = new List<SensorReading>();
        foreach (var h in all.Where(h => h.HardwareType is HardwareType.Cpu or HardwareType.Motherboard or HardwareType.SuperIO
                                         or HardwareType.EmbeddedController or HardwareType.Battery))
        {
            foreach (var s in h.Sensors)
            {
                if (Valid(s.SensorType, s.Value) is not { } v) continue;
                bool battery = h.HardwareType == HardwareType.Battery;
                (SensorCategory cat, string unit)? kind = s.SensorType switch
                {
                    SensorType.Fan => (SensorCategory.Fan, "RPM"),
                    SensorType.Power => (SensorCategory.Power, "W"),
                    SensorType.Voltage when !battery => (SensorCategory.Voltage, "V"),
                    SensorType.Level when battery && s.Name.Contains("Charge", StringComparison.OrdinalIgnoreCase) => (SensorCategory.Battery, "%"),
                    SensorType.Level when battery && s.Name.Contains("Degradation", StringComparison.OrdinalIgnoreCase) => (SensorCategory.Battery, "%"),
                    _ => null,
                };
                if (kind is not { } k) continue;
                if (k.cat == SensorCategory.Fan && v <= 0) continue;
                list.Add(new SensorReading(k.cat, battery ? $"{Loc.T("Bateria")} - {s.Name}" : s.Name, v, k.unit));
            }
        }
        return list;
    }

    private DateTime _healthAt = DateTime.MinValue;
    private Dictionary<string, string> _health = new();

    private List<DiskSnapshot> ReadDisks(List<IHardware> all)
    {
        if (DateTime.Now - _healthAt > TimeSpan.FromSeconds(60))
        {
            _healthAt = DateTime.Now;
            _health = DiskHealthService.Read();
        }

        var list = new List<DiskSnapshot>();
        foreach (var d in all.Where(h => h.HardwareType == HardwareType.Storage))
        {
            string name = d.Name.Trim();
            var life = Value(d, SensorType.Level, "Remaining Life") ?? Value(d, SensorType.Level, "Life")
                       ?? Value(d, SensorType.Level, "Available Spare");
            string? health = _health.FirstOrDefault(kv => name.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)
                                                          || kv.Key.Contains(name, StringComparison.OrdinalIgnoreCase)).Value;
            double? read = Value(d, SensorType.Throughput, "Read Rate") ?? Like(d, SensorType.Throughput, "read");
            double? write = Value(d, SensorType.Throughput, "Write Rate") ?? Like(d, SensorType.Throughput, "write");
            double? activity = Value(d, SensorType.Load, "Total Activity") ?? Like(d, SensorType.Load, "activity");
            if (read is null || write is null || activity is null)
            {
                var perf = DiskPerfService.Find(name);
                read ??= perf?.ReadBps; write ??= perf?.WriteBps; activity ??= perf?.Activity;
            }
            double? hours = d.Sensors.FirstOrDefault(x => x.Name.Contains("Power On Hours", StringComparison.OrdinalIgnoreCase) && x.Value.HasValue)?.Value;
            list.Add(new DiskSnapshot(name, FirstValue(d, SensorType.Temperature), read, write, activity, life, health,
                Like(d, SensorType.Data, "data read"), Like(d, SensorType.Data, "data written"), hours));
        }
        return list;
    }

    private static List<double> CoreLoads(IHardware? cpu)
    {
        if (cpu is null) return [];
        var rx = new System.Text.RegularExpressions.Regex(@"Core #(\d+)(?: Thread #\d+)?$");
        return cpu.Sensors
            .Where(x => x.SensorType == SensorType.Load && x.Value.HasValue && !x.Name.Contains("Thread", StringComparison.OrdinalIgnoreCase) && rx.IsMatch(x.Name))
            .OrderBy(x => int.Parse(rx.Match(x.Name).Groups[1].Value))
            .Select(x => (double)x.Value!.Value).ToList();
    }

    private static double? Like(IHardware hw, SensorType type, string part) =>
        Valid(type, hw.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(part, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)?.Value);

    private DateTime _volumesAt = DateTime.MinValue;
    private List<VolumeSnapshot> _volumes = [];

    private List<VolumeSnapshot> CachedVolumes()
    {
        if (DateTime.Now - _volumesAt > TimeSpan.FromSeconds(10))
        {
            _volumesAt = DateTime.Now;
            _volumes = ReadVolumes();
        }
        return _volumes;
    }

    private static List<VolumeSnapshot> ReadVolumes()
    {
        var list = new List<VolumeSnapshot>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new VolumeSnapshot(d.Name.TrimEnd('\\'), d.TotalSize / 1073741824.0, d.AvailableFreeSpace / 1073741824.0));
            }
            catch (IOException) { }
        }
        return list;
    }

    private HardwareScope DueGroups()
    {
        var iv = _components;
        if (iv == null) return HardwareScope.All;
        var now = DateTime.Now;
        var due = HardwareScope.None;
        foreach (var g in new[] { HardwareScope.Cpu, HardwareScope.Gpu, HardwareScope.Memory, HardwareScope.Other })
        {
            if (_groupAt.TryGetValue(g, out var last) && now - last < iv.For(g) - TimeSpan.FromMilliseconds(60)) continue;
            _groupAt[g] = now;
            due |= g;
        }
        return due;
    }

    private void Collect(IHardware hw, List<IHardware> list)
    {
        var group = hw.HardwareType switch
        {
            HardwareType.Cpu => HardwareScope.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareScope.Gpu,
            HardwareType.Memory => HardwareScope.Memory,
            _ => HardwareScope.Other,
        };
        if (!Scope.HasFlag(group)) return;
        bool slow = hw.HardwareType is HardwareType.Storage or HardwareType.Motherboard or HardwareType.SuperIO
            or HardwareType.EmbeddedController or HardwareType.Battery;
        if (_due.HasFlag(group) && (!slow || _slowDue)) hw.Update();
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

    private static double? Max(IHardware? hw, SensorType type, Func<string, bool> nameFilter)
    {
        var values = hw?.Sensors
            .Where(s => s.SensorType == type && s.Value.HasValue && nameFilter(s.Name))
            .Select(s => (double)s.Value!.Value).ToList();
        return values is { Count: > 0 } ? values.Max() : null;
    }

    private static double? Average(IHardware? hw, SensorType type, Func<string, bool> nameFilter)
    {
        var values = hw?.Sensors
            .Where(s => s.SensorType == type && s.Value.HasValue && nameFilter(s.Name))
            .Select(s => (double)s.Value!.Value).ToList();
        return values is { Count: > 0 } ? values.Average() : null;
    }

    // AMD reports "Core #1", Intel "CPU Core #1"; "(Effective)" and bus sensors are fallbacks.
    private static double? CpuClock(IHardware? cpu)
    {
        static bool IsCore(string n) => (n.StartsWith("Core #", StringComparison.OrdinalIgnoreCase)
                                         || n.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                                        && !n.Contains("Effective", StringComparison.OrdinalIgnoreCase);
        return Average(cpu, SensorType.Clock, n => IsCore(n) && n.Contains('#'))
               ?? Average(cpu, SensorType.Clock, n => n.Contains("Core", StringComparison.OrdinalIgnoreCase))
               ?? Average(cpu, SensorType.Clock, n => !n.Contains("Bus", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>True when the PawnIO driver (needed by LibreHardwareMonitor for Intel/AMD CPU temperature) is installed.</summary>
    public static bool PawnIoInstalled()
    {
        try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO"); return k is not null; }
        catch { return false; }
    }

    private static double? _acpiTemp;
    private static DateTime _acpiAt = DateTime.MinValue;

    // Driver-free fallback (ACPI thermal zones, in tenths of Kelvin); refreshed every 5 s because WMI is slow.
    private static double? AcpiTemperature()
    {
        if (DateTime.UtcNow - _acpiAt < TimeSpan.FromSeconds(5)) return _acpiTemp;
        _acpiAt = DateTime.UtcNow;
        try
        {
            double? max = null;
            using var q = Wmi.Searcher("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\WMI");
            foreach (System.Management.ManagementBaseObject o in q.Get())
            {
                var c = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                if (c > 0 && c <= 125 && (max is null || c > max)) max = c;
            }
            _acpiTemp = max;
        }
        catch { _acpiTemp = null; }
        return _acpiTemp;
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
