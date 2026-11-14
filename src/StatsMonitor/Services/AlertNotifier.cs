using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Settings;

namespace StatsMonitor.Services;

public enum AlertKind { CpuTemp, GpuTemp, CpuLoad, GpuLoad, RamLoad, NoNetwork, NoInternet, NetworkRestored, HighLatency }

public sealed record AlertEvent(AlertKind Kind, double Value, double Limit)
{
    public bool IsInfo => Kind == AlertKind.NetworkRestored;

    public string Title => Kind switch
    {
        AlertKind.CpuTemp or AlertKind.GpuTemp => Loc.T("Temperatura crítica"),
        AlertKind.NoNetwork or AlertKind.NoInternet => Loc.T("Sem conexão"),
        AlertKind.NetworkRestored => Loc.T("Conexão restabelecida"),
        AlertKind.HighLatency => Loc.T("Rede instável"),
        _ => Loc.T("Uso muito alto"),
    };

    public string Message => Kind switch
    {
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