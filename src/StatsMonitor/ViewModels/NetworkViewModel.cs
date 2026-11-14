using StatsMonitor.Helpers;
using StatsMonitor.Services;

namespace StatsMonitor.ViewModels;

public sealed class NetworkViewModel : ObservableObject
{
    private const string NA = ComponentViewModel.NotAvailable;
    private string _status = Loc.T("Verificando…"), _adapter = NA, _ip = NA, _gateway = NA, _link = NA;
    private string _down = "0 B/s", _up = "0 B/s", _latency = NA, _totalRx = NA, _totalTx = NA;
    private bool _online;

    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Online { get => _online; private set => Set(ref _online, value); }
    public string Adapter { get => _adapter; private set => Set(ref _adapter, value); }
    public string Ip { get => _ip; private set => Set(ref _ip, value); }
    public string Gateway { get => _gateway; private set => Set(ref _gateway, value); }
    public string Link { get => _link; private set => Set(ref _link, value); }
    public string Download { get => _down; private set => Set(ref _down, value); }
    public string Upload { get => _up; private set => Set(ref _up, value); }
    public string Latency { get => _latency; private set => Set(ref _latency, value); }
    public string TotalReceived { get => _totalRx; private set => Set(ref _totalRx, value); }
    public string TotalSent { get => _totalTx; private set => Set(ref _totalTx, value); }

    public void Update(NetworkSnapshot s)
    {
        Online = s.Connected && s.InternetReachable;
        Status = !s.Connected ? Loc.T("Sem conexão") : s.InternetReachable ? Loc.T("Conectado") : Loc.T("Sem acesso à internet");
        Adapter = s.Connected ? $"{s.ConnectionType} · {s.AdapterName}" : NA;
        Ip = s.LocalIp ?? NA;
        Gateway = s.Gateway ?? NA;
        Link = s.LinkSpeedMbps is { } m ? (m >= 1000 ? $"{m / 1000:0.#} Gbps" : $"{m:0} Mbps") : NA;
        Download = Rate(s.DownloadBps);
        Upload = Rate(s.UploadBps);
        Latency = s.LatencyMs is { } l ? $"{l} ms" : NA;
        TotalReceived = Bytes(s.TotalReceived);
        TotalSent = Bytes(s.TotalSent);
    }

    private static string Rate(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        return bits >= 1e6 ? $"{bits / 1e6:0.0} Mbps" : bits >= 1e3 ? $"{bits / 1e3:0} Kbps" : $"{bits:0} bps";
    }

    private static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        _ => $"{b / 1024.0:0} KB",
    };
}
