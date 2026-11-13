using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StatsMonitor.Services;

public sealed record NetworkSnapshot(
    bool Connected,
    string? AdapterName,
    string? ConnectionType,
    string? LocalIp,
    string? Gateway,
    double? LinkSpeedMbps,
    double DownloadBps,
    double UploadBps,
    long TotalReceived,
    long TotalSent,
    long? LatencyMs,
    bool InternetReachable);

/// <summary>
/// Lightweight network monitor: reads per-interface byte counters (no packet capture) every second,
/// re-selects the active adapter every few seconds and pings a public DNS server at a low rate.
/// </summary>
public sealed class NetworkMonitorService : IDisposable
{
    private const int AdapterRefreshTicks = 5;
    private const int PingTicks = 5;
    private static readonly IPAddress PingTarget = IPAddress.Parse("1.1.1.1");

    private readonly Timer _timer;
    private readonly Ping _ping = new();
    private int _tick, _busy, _pingBusy;
    private NetworkInterface? _adapter;
    private string? _adapterId;
    private long _lastRx, _lastTx, _baseRx, _baseTx;
    private DateTime _lastSample;
    private long? _latency;
    private bool _reachable;

    public event EventHandler<NetworkSnapshot>? SnapshotUpdated;

    public NetworkMonitorService() => _timer = new Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);

    public void Start() => _timer.Change(0, 1000);

    private void Sample()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (_adapter == null || _tick % AdapterRefreshTicks == 0) SelectAdapter();
            _tick++;

            if (_adapter == null)
            {
                SnapshotUpdated?.Invoke(this, new NetworkSnapshot(false, null, null, null, null, null, 0, 0, 0, 0, null, false));
                return;
            }

            var stats = _adapter.GetIPStatistics();
            long rx = stats.BytesReceived, tx = stats.BytesSent;
            var now = DateTime.UtcNow;
            double down = 0, up = 0;
            if (_lastSample != default)
            {
                double secs = (now - _lastSample).TotalSeconds;
                if (secs > 0)
                {
                    down = Math.Max(0, rx - _lastRx) / secs;
                    up = Math.Max(0, tx - _lastTx) / secs;
                }
            }
            _lastRx = rx; _lastTx = tx; _lastSample = now;

            if (_tick % PingTicks == 1) _ = PingAsync();

            var props = _adapter.GetIPProperties();
            var ip = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            var gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            double? speed = _adapter.Speed > 0 ? _adapter.Speed / 1_000_000.0 : null;

            SnapshotUpdated?.Invoke(this, new NetworkSnapshot(true, _adapter.Name, TypeName(_adapter), ip?.ToString(), gw?.ToString(),
                speed, down, up, rx - _baseRx, tx - _baseTx, _latency, _reachable));
        }
        catch
        {
            _adapter = null;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void SelectAdapter()
    {
        var best = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (n, props: n.GetIPProperties()))
            .Where(x => x.props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                          && !g.Address.Equals(IPAddress.Any)))
            .OrderByDescending(x => x.n.Speed)
            .Select(x => x.n)
            .FirstOrDefault();

        if (best?.Id != _adapterId)
        {
            _adapterId = best?.Id;
            _lastSample = default;
            _latency = null;
            _reachable = false;
            if (best != null)
            {
                var s = best.GetIPStatistics();
                _baseRx = s.BytesReceived;
                _baseTx = s.BytesSent;
            }
        }
        _adapter = best;
    }

    private async Task PingAsync()
    {
        if (Interlocked.Exchange(ref _pingBusy, 1) == 1) return;
        try
        {
            var reply = await _ping.SendPingAsync(PingTarget, 1500).ConfigureAwait(false);
            _reachable = reply.Status == IPStatus.Success;
            _latency = _reachable ? reply.RoundtripTime : null;
        }
        catch
        {
            _reachable = false;
            _latency = null;
        }
        finally
        {
            Volatile.Write(ref _pingBusy, 0);
        }
    }

    private static string TypeName(NetworkInterface n) => n.NetworkInterfaceType switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => "Ethernet",
        _ => n.NetworkInterfaceType.ToString(),
    };

    public void Dispose()
    {
        _timer.Dispose();
        _ping.Dispose();
    }
}
