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
/// Decides when to raise critical alerts. An alert fires once when a value crosses its limit (load alerts must
/// stay above it for several samples), re-arms after the value drops back below the limit, and is rate-limited.
/// </summary>
public sealed class AlertNotifier
{
    public const int LoadSamplesRequired = 5;
    public const double Hysteresis = 3;

    private sealed class State { public bool Active; public int Streak; public DateTime LastFired = DateTime.MinValue; }

    private readonly Dictionary<AlertKind, State> _state = Enum.GetValues<AlertKind>().ToDictionary(k => k, _ => new State());
    private readonly Func<DateTime> _now;

    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(2);

    public AlertNotifier(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public IReadOnlyList<AlertEvent> Evaluate(HardwareSnapshot s, AppSettings cfg)
    {
        var events = new List<AlertEvent>();
        if (!cfg.NotifyAlerts)
        {
            foreach (var st in _state.Values) { st.Active = false; st.Streak = 0; }
            return events;
        }

        Check(events, AlertKind.CpuTemp, s.CpuTemp, cfg.CpuCritTemp, 1);
        Check(events, AlertKind.GpuTemp, s.GpuTemp, cfg.GpuCritTemp, 1);
        Check(events, AlertKind.CpuLoad, s.CpuLoad, cfg.AlertLoadPercent, LoadSamplesRequired);
        Check(events, AlertKind.GpuLoad, s.GpuLoad, cfg.AlertLoadPercent, LoadSamplesRequired);
        Check(events, AlertKind.RamLoad, s.RamLoad, cfg.AlertLoadPercent, LoadSamplesRequired);
        return events;
    }

    private void Check(List<AlertEvent> events, AlertKind kind, double? value, double limit, int samples)
    {
        var st = _state[kind];
        if (value is not { } v) { st.Streak = 0; return; }

        if (v >= limit)
        {
            st.Streak++;
            if (st.Active || st.Streak < samples) return;
            st.Active = true;
            var now = _now();
            if (now - st.LastFired < Cooldown) return;
            st.LastFired = now;
            events.Add(new AlertEvent(kind, v, limit));
        }
        else
        {
            st.Streak = 0;
            if (v < limit - Hysteresis) st.Active = false;
        }
    }
}
