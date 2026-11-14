using System.Management;
using System.Runtime.InteropServices;

namespace StatsMonitor.Services;

public enum BatteryState { Unknown, Discharging, Charging, Full, PluggedNotCharging }

public sealed record BatteryInfo(
    bool Present, int? Percent, bool PluggedIn, BatteryState State,
    TimeSpan? Remaining, TimeSpan? ToFull,
    double? HealthPercent, int? FullMWh, int? DesignMWh, int? RemainingMWh,
    double? RateW, double? Volts, int? Cycles, string? Name, string? Manufacturer, string? Chemistry, bool BatterySaver)
{
    public static readonly BatteryInfo None = new(false, null, true, BatteryState.Unknown, null, null, null, null, null, null, null, null, null, null, null, null, false);
}

/// <summary>
/// Battery data for laptops: Win32 power status (level, AC line, flags) plus the WMI "root\WMI" battery classes
/// (capacities, charge/discharge rate, cycles). Everything degrades gracefully when a provider is missing.
/// </summary>
public static class BatteryService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private static readonly object Gate = new();
    private static BatteryInfo _latest = BatteryInfo.None;
    private static DateTime _latestAt = DateTime.MinValue;
    private static (DateTime At, int? Design, string? Name, string? Maker, string? Chem, int? Cycles)? _static;
    private static (DateTime At, int? Full)? _full;
    private static bool? _has;

    /// <summary>True when the computer has a battery (laptop, tablet or UPS seen as a battery).</summary>
    public static bool HasBattery
    {
        get
        {
            if (_has is { } h) return h;
            try { _has = GetSystemPowerStatus(out var ps) && ps.BatteryFlag is not (128 or 255); }
            catch { _has = false; }
            return _has.Value;
        }
    }

    /// <summary>Last read value without touching WMI.</summary>
    public static BatteryInfo Latest { get { lock (Gate) return _latest; } }

    /// <summary>Returns the cached reading when it is newer than <paramref name="maxAge"/>; otherwise reads again.</summary>
    public static BatteryInfo ReadCached(TimeSpan maxAge)
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _latestAt <= maxAge && _latest.Present) return _latest;
        }
        var info = Read();
        lock (Gate) { _latest = info; _latestAt = DateTime.UtcNow; }
        return info;
    }

    public static BatteryInfo Read()
    {
        if (!HasBattery) return BatteryInfo.None;
        try
        {
            if (!GetSystemPowerStatus(out var ps)) return BatteryInfo.None;
            bool plugged = ps.ACLineStatus == 1;
            bool flagCharging = (ps.BatteryFlag & 8) != 0;
            int? pct = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : null;
            TimeSpan? osRemaining = ps.BatteryLifeTime != uint.MaxValue ? TimeSpan.FromSeconds(ps.BatteryLifeTime) : null;

            var (rem, chargeRate, dischargeRate, volts, wmiCharging, wmiDischarging, wmiPlugged) = ReadStatus();
            var full = ReadFullCapacity();
            var st = ReadStatic();
            if (wmiPlugged is { } wp && ps.ACLineStatus == 255) plugged = wp;

            if (pct is null && rem is { } r && full is > 0) pct = (int)Math.Clamp(Math.Round(r * 100.0 / full.Value), 0, 100);
            bool charging = wmiCharging ?? flagCharging;
            bool discharging = wmiDischarging ?? (!plugged && !flagCharging);
            var state = ClassifyState(pct, plugged, charging, discharging);

            var (remaining, toFull) = Estimate(rem, full, chargeRate, dischargeRate, state);
            if (remaining is null && state == BatteryState.Discharging) remaining = osRemaining;

            double? rateW = state switch
            {
                BatteryState.Charging when chargeRate > 0 => chargeRate / 1000.0,
                BatteryState.Discharging when dischargeRate > 0 => dischargeRate / 1000.0,
                _ => null,
            };

            return new BatteryInfo(true, pct, plugged, state, remaining, toFull, Health(full, st.Design), full, st.Design, rem,
                rateW, volts, st.Cycles, st.Name, st.Maker, st.Chem, (ps.SystemStatusFlag & 1) != 0);
        }
        catch { return BatteryInfo.None; }
    }

    public static BatteryState ClassifyState(int? percent, bool plugged, bool charging, bool discharging)
    {
        if (charging && !(percent >= 100)) return BatteryState.Charging;
        if (plugged) return percent >= 100 || charging ? BatteryState.Full : BatteryState.PluggedNotCharging;
        return discharging || !plugged ? BatteryState.Discharging : BatteryState.Unknown;
    }

    /// <summary>Time left (discharging) or to a full charge (charging) from capacities in mWh and rates in mW.</summary>
    public static (TimeSpan? Remaining, TimeSpan? ToFull) Estimate(int? remainingMWh, int? fullMWh, int? chargeRateMW, int? dischargeRateMW, BatteryState state)
    {
        const double MinRate = 500, MaxHours = 100;
        static TimeSpan? Hours(double mwh, double rate) =>
            rate >= MinRate && mwh >= 0 && mwh / rate <= MaxHours ? TimeSpan.FromHours(mwh / rate) : null;

        if (state == BatteryState.Discharging && remainingMWh is { } r && dischargeRateMW is { } d) return (Hours(r, d), null);
        if (state == BatteryState.Charging && remainingMWh is { } r2 && fullMWh is { } f && chargeRateMW is { } c) return (null, Hours(Math.Max(0, f - r2), c));
        return (null, null);
    }

    public static double? Health(int? fullMWh, int? designMWh) =>
        fullMWh is > 0 && designMWh is > 0 ? Math.Min(100, Math.Round(fullMWh.Value * 100.0 / designMWh.Value, 1)) : null;

    /// <summary>0 = good (80%+), 1 = worn (60-80%), 2 = replace soon (&lt;60%), -1 = unknown.</summary>
    public static int HealthLevel(double? health) => health is not { } h ? -1 : h >= 80 ? 0 : h >= 60 ? 1 : 2;

    private static (int? Rem, int? Charge, int? Discharge, double? Volts, bool? Charging, bool? Discharging, bool? Plugged) ReadStatus()
    {
        int rem = 0, charge = 0, discharge = 0, volts = 0, n = 0;
        bool charging = false, discharging = false, plugged = false;
        try
        {
            using var s = Wmi.Searcher("SELECT RemainingCapacity, ChargeRate, DischargeRate, Voltage, Charging, Discharging, PowerOnline FROM BatteryStatus", @"root\WMI");
            foreach (ManagementBaseObject o in s.Get())
            {
                n++;
                rem += Convert.ToInt32(o["RemainingCapacity"] ?? 0);
                charge += Convert.ToInt32(o["ChargeRate"] ?? 0);
                discharge += Convert.ToInt32(o["DischargeRate"] ?? 0);
                volts = Math.Max(volts, Convert.ToInt32(o["Voltage"] ?? 0));
                charging |= o["Charging"] is true;
                discharging |= o["Discharging"] is true;
                plugged |= o["PowerOnline"] is true;
            }
        }
        catch { /* provider missing: fall back to the Win32 status */ }
        return n == 0 ? (null, null, null, null, null, null, null)
            : (rem > 0 ? rem : null, charge, discharge, volts > 0 ? volts / 1000.0 : null, charging, discharging, plugged);
    }

    private static int? ReadFullCapacity()
    {
        if (_full is { } f && DateTime.UtcNow - f.At < TimeSpan.FromMinutes(10)) return f.Full;
        int total = 0;
        try
        {
            using var s = Wmi.Searcher("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\WMI");
            foreach (ManagementBaseObject o in s.Get()) total += Convert.ToInt32(o["FullChargedCapacity"] ?? 0);
        }
        catch { }
        int? value = total > 0 ? total : null;
        _full = (DateTime.UtcNow, value);
        return value;
    }

    private static (int? Design, string? Name, string? Maker, string? Chem, int? Cycles) ReadStatic()
    {
        if (_static is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(10)) return (c.Design, c.Name, c.Maker, c.Chem, c.Cycles);
        int design = 0, cycles = 0;
        string? name = null, maker = null, chem = null;
        try
        {
            using var s = Wmi.Searcher("SELECT DesignedCapacity, DeviceName, ManufactureName FROM BatteryStaticData", @"root\WMI");
            foreach (ManagementBaseObject o in s.Get())
            {
                design += Convert.ToInt32(o["DesignedCapacity"] ?? 0);
                name ??= Clean(o["DeviceName"] as string);
                maker ??= Clean(o["ManufactureName"] as string);
            }
        }
        catch { }
        try
        {
            using var s = Wmi.Searcher("SELECT CycleCount FROM BatteryCycleCount", @"root\WMI");
            foreach (ManagementBaseObject o in s.Get()) cycles = Math.Max(cycles, Convert.ToInt32(o["CycleCount"] ?? 0));
        }
        catch { }
        try
        {
            using var s = Wmi.Searcher("SELECT Name, Chemistry, DesignCapacity FROM Win32_Battery");
            foreach (ManagementBaseObject o in s.Get())
            {
                name ??= Clean(o["Name"] as string);
                chem ??= ChemistryName(o["Chemistry"] is { } ch ? Convert.ToInt32(ch) : 0);
                if (design == 0 && o["DesignCapacity"] is { } dc) design = Convert.ToInt32(dc);
            }
        }
        catch { }
        var r = (design > 0 ? design : (int?)null, name, maker, chem, cycles > 0 ? cycles : (int?)null);
        _static = (DateTime.UtcNow, r.Item1, r.name, r.maker, r.chem, r.Item5);
        return r;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string? ChemistryName(int code) => code switch
    {
        3 => "Lead acid", 4 => "NiCd", 5 => "NiMH", 6 => "Li-ion", 7 => "Zinc air", 8 => "Li-polymer", _ => null,
    };
}
