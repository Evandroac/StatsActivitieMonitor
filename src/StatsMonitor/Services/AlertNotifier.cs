using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Settings;

namespace StatsMonitor.Services;

public enum AlertKind { CpuTemp, GpuTemp, CpuLoad, GpuLoad, RamLoad }

public sealed record AlertEvent(AlertKind Kind, double Value, double Limit)
{
    public string Title => Loc.T(Kind is AlertKind.CpuTemp or AlertKind.GpuTemp ? "Temperatura crítica" : "Uso muito alto");

    public string Message => Kind switch
    {
        AlertKind.CpuTemp => Loc.T("CPU: {0} (limite {1})", Units.Temp(Value), Units.Temp(Limit)),
        AlertKind.GpuTemp => Loc.T("GPU: {0} (limite {1})", Units.Temp(Value), Units.Temp(Limit)),
        AlertKind.CpuLoad => Loc.T("CPU: {0:0}% de uso (limite {1:0}%)", Value, Limit),
        AlertKind.GpuLoad => Loc.T("GPU: {0:0}% de uso (limite {1:0}%)", Value, Limit),
        _ => Loc.T("Memória RAM: {0:0}% de uso (limite {1:0}%)", Value, Limit),
    };
}

/// <summary>
/// Decides when to raise critical alerts. A value must stay at or above its limit for the configured number of
/// seconds, an alert fires once per episode (re-armed after the value drops below the limit minus a hysteresis),
/// and alerts of the same kind are rate-limited by a minimum interval.
/// </summary>
public sealed class AlertNotifier
{
    public const double Hysteresis = 3;

    private sealed class State { public bool Active; public DateTime? Since; public DateTime LastFired = DateTime.MinValue; }

    private readonly Dictionary<AlertKind, State> _state = Enum.GetValues<AlertKind>().ToDictionary(k => k, _ => new State());
    private readonly Func<DateTime> _now;

    /// <summary>Overrides the minimum interval between alerts of the same kind (defaults to the setting).</summary>
    public TimeSpan? Cooldown { get; init; }

    public AlertNotifier(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public IReadOnlyList<AlertEvent> Evaluate(HardwareSnapshot s, AppSettings cfg)
    {
        var events = new List<AlertEvent>();
        if (!cfg.NotifyAlerts)
        {
            foreach (var st in _state.Values) { st.Active = false; st.Since = null; }
            return events;
        }

        var sustain = TimeSpan.FromSeconds(Math.Max(0, cfg.AlertSustainSeconds));
        var cooldown = Cooldown ?? TimeSpan.FromMinutes(Math.Max(0, cfg.AlertCooldownMinutes));
        Check(events, AlertKind.CpuTemp, s.CpuTemp, cfg.CpuCritTemp, sustain, cooldown);
        Check(events, AlertKind.GpuTemp, s.GpuTemp, cfg.GpuCritTemp, sustain, cooldown);
        Check(events, AlertKind.CpuLoad, s.CpuLoad, cfg.AlertLoadPercent, sustain, cooldown);
        Check(events, AlertKind.GpuLoad, s.GpuLoad, cfg.AlertLoadPercent, sustain, cooldown);
        Check(events, AlertKind.RamLoad, s.RamLoad, cfg.AlertLoadPercent, sustain, cooldown);
        return events;
    }

    private void Check(List<AlertEvent> events, AlertKind kind, double? value, double limit, TimeSpan sustain, TimeSpan cooldown)
    {
        var st = _state[kind];
        var now = _now();
        if (value is not { } v) { st.Since = null; return; }

        if (v >= limit)
        {
            st.Since ??= now;
            if (st.Active || now - st.Since.Value < sustain) return;
            st.Active = true;
            if (now - st.LastFired < cooldown) return;
            st.LastFired = now;
            events.Add(new AlertEvent(kind, v, limit));
        }
        else
        {
            st.Since = null;
            if (v < limit - Hysteresis) st.Active = false;
        }
    }
}