using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Settings;

namespace StatsMonitor.Services;

public enum AlertKind { CpuTemp, GpuTemp, CpuLoad, GpuLoad, RamLoad, NoNetwork, NoInternet, NetworkRestored, HighLatency, GreetingMorning, GreetingAfternoon, GreetingNight,
    CpuTempWarn, GpuTempWarn, CpuLoadWarn, GpuLoadWarn, RamLoadWarn, BatteryLow, BatteryCritical }

public sealed record AlertEvent(AlertKind Kind, double Value, double Limit)
{
    public bool IsGreeting => Kind is AlertKind.GreetingMorning or AlertKind.GreetingAfternoon or AlertKind.GreetingNight;
    public bool IsAttention => Kind is AlertKind.CpuTempWarn or AlertKind.GpuTempWarn or AlertKind.CpuLoadWarn or AlertKind.GpuLoadWarn or AlertKind.RamLoadWarn or AlertKind.BatteryLow;
    public bool IsCritical => Kind is AlertKind.CpuTemp or AlertKind.GpuTemp or AlertKind.CpuLoad or AlertKind.GpuLoad or AlertKind.RamLoad or AlertKind.BatteryCritical;
    public bool IsInfo => Kind == AlertKind.NetworkRestored || IsGreeting;

    public string Title => Kind switch
    {
        AlertKind.CpuTemp or AlertKind.GpuTemp => Loc.T("Temperatura crítica"),
        AlertKind.NoNetwork or AlertKind.NoInternet => Loc.T("Sem conexão"),
        AlertKind.NetworkRestored => Loc.T("Conexão restabelecida"),
        AlertKind.HighLatency => Loc.T("Rede instável"),
        AlertKind.BatteryLow => Loc.T("Bateria fraca"),
        AlertKind.BatteryCritical => Loc.T("Bateria crítica"),
        _ when IsAttention => Loc.T("Atenção"),
        AlertKind.GreetingMorning => Loc.T("Bom dia!"),
        AlertKind.GreetingAfternoon => Loc.T("Boa tarde!"),
        AlertKind.GreetingNight => Loc.T("Boa noite!"),
        _ => Loc.T("Uso muito alto"),
    };

    public string Message => Kind switch
    {
        AlertKind.CpuTempWarn => Loc.T("{0}: {1} (atenção acima de {2})", "CPU", Units.Temp(Value), Units.Temp(Limit)),
        AlertKind.GpuTempWarn => Loc.T("{0}: {1} (atenção acima de {2})", "GPU", Units.Temp(Value), Units.Temp(Limit)),
        AlertKind.CpuLoadWarn => Loc.T("{0}: {1:0}% de uso (atenção acima de {2:0}%)", "CPU", Value, Limit),
        AlertKind.GpuLoadWarn => Loc.T("{0}: {1:0}% de uso (atenção acima de {2:0}%)", "GPU", Value, Limit),
        AlertKind.RamLoadWarn => Loc.T("{0}: {1:0}% de uso (atenção acima de {2:0}%)", "RAM", Value, Limit),
        AlertKind.BatteryLow or AlertKind.BatteryCritical => Loc.T("Bateria em {0:0}% (limite {1:0}%). Conecte o carregador.", Value, Limit),
        AlertKind.GreetingMorning => Loc.T("Tenha um ótimo dia! O Stats Monitor está acompanhando seu PC."),
        AlertKind.GreetingAfternoon => Loc.T("Bom resto de dia! O Stats Monitor continua de olho no seu PC."),
        AlertKind.GreetingNight => Loc.T("Boa noite! O Stats Monitor segue monitorando seu PC."),
        AlertKind.NoNetwork => Loc.T("Nenhuma rede ativa. Verifique o cabo ou o Wi-Fi."),
        AlertKind.NoInternet => Loc.T("Conectado à rede, mas sem acesso à internet."),
        AlertKind.NetworkRestored => Loc.T("A conexão com a internet voltou."),
        AlertKind.HighLatency => Loc.T("Latência alta: {0:0} ms (limite {1:0} ms)", Value, Limit),
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
        bool crit = cfg.AlertLevel != "attention", warn = cfg.AlertLevel != "critical";
        if (crit)
        {
            Check(events, AlertKind.CpuTemp, s.CpuTemp, cfg.CpuCritTemp, sustain, cooldown);
            Check(events, AlertKind.GpuTemp, s.GpuTemp, cfg.GpuCritTemp, sustain, cooldown);
            Check(events, AlertKind.CpuLoad, s.CpuLoad, cfg.AlertLoadPercent, sustain, cooldown);
            Check(events, AlertKind.GpuLoad, s.GpuLoad, cfg.AlertLoadPercent, sustain, cooldown);
            Check(events, AlertKind.RamLoad, s.RamLoad, cfg.AlertLoadPercent, sustain, cooldown);
        }

        // Attention level: past the warning limit; critical takes precedence when both levels are enabled.
        if (warn)
        {
            double loadWarn = Math.Max(0, cfg.AlertLoadPercent - 10);
            double? B(double? v, double limit) => crit ? Below(v, limit) : v;
            Check(events, AlertKind.CpuTempWarn, B(s.CpuTemp, cfg.CpuCritTemp), cfg.CpuWarnTemp, sustain, cooldown);
            Check(events, AlertKind.GpuTempWarn, B(s.GpuTemp, cfg.GpuCritTemp), cfg.GpuWarnTemp, sustain, cooldown);
            Check(events, AlertKind.CpuLoadWarn, B(s.CpuLoad, cfg.AlertLoadPercent), loadWarn, sustain, cooldown);
            Check(events, AlertKind.GpuLoadWarn, B(s.GpuLoad, cfg.AlertLoadPercent), loadWarn, sustain, cooldown);
            Check(events, AlertKind.RamLoadWarn, B(s.RamLoad, cfg.AlertLoadPercent), loadWarn, sustain, cooldown);
        }
        return events;
    }

    private static double? Below(double? v, double crit) => v is { } x && x >= crit ? null : v;

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
/// <summary>
/// Smart network alerts: no active adapter, adapter without internet access, high latency and restoration.
/// A problem must persist for a few seconds before alerting (avoids flapping), each problem alerts once per
/// episode, and a "restored" notice is sent only after an outage that was actually reported.
/// </summary>
public sealed class NetworkAlertNotifier
{
    public const double LatencyLimitMs = 250;
    private static readonly TimeSpan Sustain = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LatencySustain = TimeSpan.FromSeconds(20);

    private readonly Func<DateTime> _now;
    private AlertKind? _outage;
    private DateTime? _since;
    private bool _outageReported, _latencyActive;
    private DateTime? _latencySince;

    public NetworkAlertNotifier(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public IReadOnlyList<AlertEvent> Evaluate(NetworkSnapshot n, AppSettings cfg)
    {
        var events = new List<AlertEvent>();
        if (!cfg.NotifyNetworkAlerts) { _outage = null; _since = null; _outageReported = false; _latencySince = null; _latencyActive = false; return events; }
        var now = _now();

        AlertKind? problem = !n.Connected ? AlertKind.NoNetwork : !n.InternetReachable ? AlertKind.NoInternet : null;
        if (problem is { } p)
        {
            if (_outage != p) { _outage = p; _since = now; }
            if (now - _since!.Value >= Sustain && !(_outageReported && _lastKind == p))
            {
                _outageReported = true; _lastKind = p;
                events.Add(new AlertEvent(p, 0, 0));
            }
            _latencySince = null; _latencyActive = false;
            return events;
        }

        _outage = null; _since = null;
        if (_outageReported)
        {
            _outageReported = false; _lastKind = null;
            events.Add(new AlertEvent(AlertKind.NetworkRestored, 0, 0));
        }

        if (n.LatencyMs is { } l && l >= LatencyLimitMs)
        {
            _latencySince ??= now;
            if (!_latencyActive && now - _latencySince.Value >= LatencySustain)
            {
                _latencyActive = true;
                events.Add(new AlertEvent(AlertKind.HighLatency, l, LatencyLimitMs));
            }
        }
        else if (n.LatencyMs is null || n.LatencyMs < LatencyLimitMs * 0.6) { _latencySince = null; _latencyActive = false; }
        return events;
    }

    private AlertKind? _lastKind;
}
/// <summary>Greets once per period of the day (morning 05-12, afternoon 12-18, night otherwise), including at startup.</summary>
public sealed class GreetingNotifier
{
    private readonly Func<DateTime> _now;
    private (DateOnly Day, AlertKind Kind)? _last;

    public GreetingNotifier(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public AlertEvent? Evaluate(AppSettings cfg)
    {
        if (!cfg.NotifyGreetings) return null;
        var now = _now();
        var kind = now.Hour is >= 5 and < 12 ? AlertKind.GreetingMorning
                 : now.Hour is >= 12 and < 18 ? AlertKind.GreetingAfternoon
                 : AlertKind.GreetingNight;
        // After midnight the night period belongs to the previous day, so it is not repeated.
        var day = DateOnly.FromDateTime(now.Hour < 5 ? now.AddDays(-1) : now);
        if (_last is { } l && l.Day == day && l.Kind == kind) return null;
        _last = (day, kind);
        return new AlertEvent(kind, 0, 0);
    }
}

/// <summary>Battery alerts while discharging: attention at 20% and critical at 10%, once per discharge episode.</summary>
public sealed class BatteryAlertNotifier
{
    public const int LowPercent = 20, CriticalPercent = 10, Rearm = 5;
    private bool _lowFired, _criticalFired;

    public IReadOnlyList<AlertEvent> Evaluate(BatteryInfo b, AppSettings cfg)
    {
        var events = new List<AlertEvent>();
        if (!b.Present || b.Percent is not { } p || b.State != BatteryState.Discharging)
        {
            if (b.Present && b.State != BatteryState.Discharging) { _lowFired = false; _criticalFired = false; }
            return events;
        }
        if (p > LowPercent + Rearm) { _lowFired = false; _criticalFired = false; }
        if (!cfg.NotifyAlerts) return events;

        bool crit = cfg.AlertLevel != "attention", warn = cfg.AlertLevel != "critical";
        if (p <= CriticalPercent && crit && !_criticalFired)
        {
            _criticalFired = _lowFired = true;
            events.Add(new AlertEvent(AlertKind.BatteryCritical, p, CriticalPercent));
        }
        else if (p <= LowPercent && !(p <= CriticalPercent && crit) && warn && !_lowFired)
        {
            _lowFired = true;
            events.Add(new AlertEvent(AlertKind.BatteryLow, p, LowPercent));
        }
        return events;
    }
}