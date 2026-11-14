using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace StatsMonitor.Services;

public sealed record PingResult(int Seq, string Address, long? Ms, IPStatus Status, int Ttl);

/// <summary>Accumulates ping round-trip times: loss, min/avg/max, standard deviation and jitter (mean delta between consecutive replies).</summary>
public sealed class PingStats
{
    private readonly List<double> _ms = [];
    public int Sent { get; private set; }
    public int Received => _ms.Count;
    public double Loss => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
    public double Min => _ms.Count == 0 ? 0 : _ms.Min();
    public double Max => _ms.Count == 0 ? 0 : _ms.Max();
    public double Avg => _ms.Count == 0 ? 0 : _ms.Average();
    public double StdDev => _ms.Count < 2 ? 0 : Math.Sqrt(_ms.Sum(v => (v - Avg) * (v - Avg)) / _ms.Count);

    public double Jitter
    {
        get
        {
            if (_ms.Count < 2) return 0;
            double sum = 0;
            for (int i = 1; i < _ms.Count; i++) sum += Math.Abs(_ms[i] - _ms[i - 1]);
            return sum / (_ms.Count - 1);
        }
    }

    public void Add(double? ms)
    {
        Sent++;
        if (ms.HasValue) _ms.Add(ms.Value);
    }
}

public sealed record Hop(int Ttl, string? Address, string? Host, double? MinMs, double? AvgMs, double? MaxMs, int Lost, int Probes, bool Reached);

public sealed record DnsAnswer(string Type, string Name, string Data, uint Ttl);

public sealed record DnsResult(string Server, bool Ok, string Rcode, double Ms, bool Truncated, List<DnsAnswer> Answers, string? Error);

public sealed record PortResult(int Port, bool Open, double Ms, string? Error);

public sealed record HttpResult(bool Ok, int Status, string Reason, string FinalUrl, string Version, string? Server, double DnsMs, double TcpMs, double TotalMs,
    DateTime? CertExpiry, string? CertSubject, string? CertIssue, string? Error, string? Address);

public sealed record AdapterInfo(string Name, string Description, string Type, bool Up, string Mac, long SpeedMbps, int Mtu, bool Dhcp,
    List<string> Ipv4, List<string> Ipv6, List<string> Gateways, List<string> Dns);

public sealed record PublicIpInfo(string Ip, string Country, string Colo, string Http, string Tls, bool Warp);

public enum DiagState { Running, Ok, Warn, Fail, Skip }

public sealed record DiagStep(string Name, string Detail, DiagState State, double? Ms);

public static class NetworkToolsService
{
    private static readonly Dictionary<int, string> PortNames = new()
    {
        [20] = "FTP-data", [21] = "FTP", [22] = "SSH", [23] = "Telnet", [25] = "SMTP", [53] = "DNS", [67] = "DHCP", [80] = "HTTP", [110] = "POP3", [123] = "NTP",
        [135] = "RPC", [139] = "NetBIOS", [143] = "IMAP", [389] = "LDAP", [443] = "HTTPS", [445] = "SMB", [465] = "SMTPS", [587] = "SMTP (submission)",
        [636] = "LDAPS", [993] = "IMAPS", [995] = "POP3S", [1433] = "SQL Server", [1521] = "Oracle", [3306] = "MySQL", [3389] = "RDP", [5432] = "PostgreSQL",
        [5900] = "VNC", [6379] = "Redis", [8080] = "HTTP-alt", [8443] = "HTTPS-alt", [27017] = "MongoDB",
    };

    public const string CommonPorts = "21,22,23,25,53,80,110,143,443,445,993,995,1433,3306,3389,5432,8080,8443";

    public static string PortName(int port) => PortNames.TryGetValue(port, out var n) ? n : "";

    /// <summary>Parses "80,443,8000-8010" into distinct ports (1-65535), capped at 1024 entries. Invalid items are ignored.</summary>
    public static List<int> ParsePorts(string text)
    {
        var set = new SortedSet<int>();
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', 2);
            if (!int.TryParse(range[0], out int a) || a is < 1 or > 65535) continue;
            int b = a;
            if (range.Length == 2 && (!int.TryParse(range[1], out b) || b < a || b > 65535)) continue;
            for (int p = a; p <= b && set.Count < 1024; p++) set.Add(p);
        }
        return [.. set];
    }

    public static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct)
    {
        host = host.Trim();
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try
        {
            var all = await Dns.GetHostAddressesAsync(host, ct);
            return all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? all.FirstOrDefault();
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public static async Task PingAsync(IPAddress addr, int count, int timeoutMs, int size, int intervalMs, bool dontFragment, Action<PingResult> report, CancellationToken ct)
    {
        using var ping = new Ping();
        var buffer = new byte[Math.Clamp(size, 0, 65500)];
        var options = new PingOptions(128, dontFragment);
        for (int seq = 1; count == 0 || seq <= count; seq++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            PingResult r;
            try
            {
                var reply = await ping.SendPingAsync(addr, timeoutMs, buffer, options);
                r = new(seq, reply.Address?.ToString() ?? addr.ToString(), reply.Status == IPStatus.Success ? Math.Max(reply.RoundtripTime, 0) : null, reply.Status, reply.Options?.Ttl ?? 0);
            }
            catch (PingException) { r = new(seq, addr.ToString(), null, IPStatus.Unknown, 0); }
            report(r);
            if (count != 0 && seq >= count) break;
            int wait = intervalMs - (int)sw.ElapsedMilliseconds;
            if (wait > 0) await Task.Delay(wait, ct);
        }
    }

    public static async Task TraceAsync(IPAddress addr, int maxHops, int timeoutMs, int probes, bool resolveNames, Action<Hop> report, CancellationToken ct)
    {
        using var ping = new Ping();
        var buffer = new byte[32];
        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            var times = new List<double>();
            string? from = null;
            bool reached = false;
            for (int i = 0; i < probes; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var sw = Stopwatch.StartNew();
                    var reply = await ping.SendPingAsync(addr, timeoutMs, buffer, new PingOptions(ttl, true));
                    sw.Stop();
                    if (reply.Status is IPStatus.Success or IPStatus.TtlExpired)
                    {
                        times.Add(Math.Max(sw.Elapsed.TotalMilliseconds, 0));
                        from ??= reply.Address?.ToString();
                        if (reply.Status == IPStatus.Success) reached = true;
                    }
                }
                catch (PingException) { }
            }
            string? host = null;
            if (resolveNames && from != null)
            {
                try
                {
                    var t = Dns.GetHostEntryAsync(from);
                    if (await Task.WhenAny(t, Task.Delay(1500, ct)) == t) host = t.Result.HostName;
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            report(new Hop(ttl, from, host, times.Count > 0 ? times.Min() : null, times.Count > 0 ? times.Average() : null, times.Count > 0 ? times.Max() : null,
                probes - times.Count, probes, reached));
            if (reached) break;
        }
    }

    private static readonly Dictionary<string, ushort> DnsTypes = new(StringComparer.OrdinalIgnoreCase)
    { ["A"] = 1, ["NS"] = 2, ["CNAME"] = 5, ["SOA"] = 6, ["PTR"] = 12, ["MX"] = 15, ["TXT"] = 16, ["AAAA"] = 28, ["SRV"] = 33, ["CAA"] = 257 };

    public static string[] DnsTypeNames => ["A", "AAAA", "CNAME", "MX", "NS", "TXT", "PTR", "SOA", "SRV", "CAA"];

    public static string ReverseName(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length == 4) return string.Join(".", b.Reverse()) + ".in-addr.arpa";
        return string.Join(".", b.Reverse().SelectMany(x => new[] { (x & 0xF).ToString("x"), (x >> 4).ToString("x") })) + ".ip6.arpa";
    }

    public static byte[] BuildDnsQuery(string name, ushort qtype, ushort id)
    {
        var ms = new MemoryStream();
        void W16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        W16(id); W16(0x0100); W16(1); W16(0); W16(0); W16(0);
        var ascii = new IdnMapping().GetAscii(name.Trim().TrimEnd('.'));
        foreach (var label in ascii.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.WriteByte((byte)Math.Min(bytes.Length, 63));
            ms.Write(bytes, 0, Math.Min(bytes.Length, 63));
        }
        ms.WriteByte(0);
        W16(qtype); W16(1);
        return ms.ToArray();
    }

    private static string ReadName(byte[] b, ref int pos)
    {
        var labels = new List<string>();
        int p = pos, end = -1;
        for (int guard = 0; guard < 128 && p < b.Length; guard++)
        {
            int len = b[p];
            if (len == 0) { p++; break; }
            if ((len & 0xC0) == 0xC0)
            {
                if (end < 0) end = p + 2;
                p = ((len & 0x3F) << 8) | b[p + 1];
                continue;
            }
            labels.Add(Encoding.ASCII.GetString(b, p + 1, len));
            p += len + 1;
        }
        pos = end >= 0 ? end : p;
        return labels.Count == 0 ? "." : string.Join(".", labels);
    }

    public static (ushort Id, string Rcode, bool Truncated, List<DnsAnswer> Answers) ParseDnsResponse(byte[] b)
    {
        int U16(int p) => (b[p] << 8) | b[p + 1];
        ushort id = (ushort)U16(0);
        int flags = U16(2), rc = flags & 0xF;
        string[] rcodes = ["NOERROR", "FORMERR", "SERVFAIL", "NXDOMAIN", "NOTIMP", "REFUSED"];
        int qd = U16(4), an = U16(6), pos = 12;
        for (int i = 0; i < qd; i++) { ReadName(b, ref pos); pos += 4; }
        var answers = new List<DnsAnswer>();
        for (int i = 0; i < an && pos + 10 <= b.Length; i++)
        {
            string name = ReadName(b, ref pos);
            int type = U16(pos);
            uint ttl = ((uint)U16(pos + 4) << 16) | (uint)U16(pos + 6);
            int rdlen = U16(pos + 8);
            int rs = pos + 10;
            pos = rs + rdlen;
            if (pos > b.Length) break;
            string data;
            switch (type)
            {
                case 1 when rdlen == 4: data = new IPAddress(b[rs..(rs + 4)]).ToString(); break;
                case 28 when rdlen == 16: data = new IPAddress(b[rs..(rs + 16)]).ToString(); break;
                case 2 or 5 or 12: { int q = rs; data = ReadName(b, ref q); break; }
                case 15: { int q = rs + 2; data = $"{U16(rs)} {ReadName(b, ref q)}"; break; }
                case 16:
                {
                    var sb = new StringBuilder();
                    for (int q = rs; q < rs + rdlen;) { int l = b[q++]; sb.Append(Encoding.UTF8.GetString(b, q, Math.Min(l, rs + rdlen - q))); q += l; }
                    data = sb.ToString();
                    break;
                }
                case 6: { int q = rs; string mn = ReadName(b, ref q); string rn = ReadName(b, ref q); data = $"{mn} {rn} serial {((uint)U16(q) << 16) | (uint)U16(q + 2)}"; break; }
                case 33: { int q = rs + 6; data = $"prio {U16(rs)} peso {U16(rs + 2)} porta {U16(rs + 4)} {ReadName(b, ref q)}"; break; }
                default: data = $"({rdlen} bytes)"; break;
            }
            string tn = DnsTypes.FirstOrDefault(kv => kv.Value == type).Key ?? $"TYPE{type}";
            answers.Add(new DnsAnswer(tn, name, data, ttl));
        }
        return (id, rc < rcodes.Length ? rcodes[rc] : $"RCODE{rc}", (flags & 0x200) != 0, answers);
    }

    public static async Task<DnsResult> DnsQueryAsync(IPAddress server, string name, string type, int timeoutMs, CancellationToken ct)
    {
        if (!DnsTypes.TryGetValue(type, out var qtype)) qtype = 1;
        if (qtype == 12 && IPAddress.TryParse(name.Trim(), out var ip)) name = ReverseName(ip);
        var sw = Stopwatch.StartNew();
        try
        {
            using var udp = new UdpClient(server.AddressFamily);
            udp.Connect(server, 53);
            ushort id = (ushort)Random.Shared.Next(65536);
            await udp.SendAsync(BuildDnsQuery(name, qtype, id), ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            while (true)
            {
                var r = await udp.ReceiveAsync(cts.Token);
                var (rid, rcode, tc, answers) = ParseDnsResponse(r.Buffer);
                if (rid != id) continue;
                return new DnsResult(server.ToString(), true, rcode, sw.Elapsed.TotalMilliseconds, tc, answers, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new DnsResult(server.ToString(), false, "TIMEOUT", sw.Elapsed.TotalMilliseconds, false, [], "timeout"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new DnsResult(server.ToString(), false, "ERRO", sw.Elapsed.TotalMilliseconds, false, [], ex.Message); }
    }

    public static async Task<PortResult> CheckPortAsync(IPAddress addr, int port, int timeoutMs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient(addr.AddressFamily);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await client.ConnectAsync(addr, port, cts.Token);
            return new PortResult(port, true, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new PortResult(port, false, sw.Elapsed.TotalMilliseconds, "timeout (filtrada?)"); }
        catch (OperationCanceledException) { throw; }
        catch (SocketException ex) { return new PortResult(port, false, sw.Elapsed.TotalMilliseconds, ex.SocketErrorCode == SocketError.ConnectionRefused ? "recusada (fechada)" : ex.SocketErrorCode.ToString()); }
        catch (Exception ex) { return new PortResult(port, false, sw.Elapsed.TotalMilliseconds, ex.Message); }
    }

    public static async Task<HttpResult> HttpCheckAsync(string url, CancellationToken ct)
    {
        url = url.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return new HttpResult(false, 0, "", url, "", null, 0, 0, 0, null, null, null, "URL inválida", null);
        double dnsMs = 0, tcpMs = 0;
        string? address = null;
        try
        {
            var sw = Stopwatch.StartNew();
            var ip = await ResolveAsync(uri.Host, ct);
            dnsMs = sw.Elapsed.TotalMilliseconds;
            if (ip == null) return new HttpResult(false, 0, "", url, "", null, dnsMs, 0, 0, null, null, null, "Falha ao resolver o nome (DNS)", null);
            address = ip.ToString();
            sw.Restart();
            using (var tcp = new TcpClient(ip.AddressFamily))
            {
                using var c = CancellationTokenSource.CreateLinkedTokenSource(ct);
                c.CancelAfter(8000);
                await tcp.ConnectAsync(ip, uri.Port, c.Token);
            }
            tcpMs = sw.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new HttpResult(false, 0, "", url, "", null, dnsMs, tcpMs, 0, null, null, null, ex.Message, address); }

        X509Certificate2? cert = null;
        var tlsErrors = SslPolicyErrors.None;
        var handler = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10, ConnectTimeout = TimeSpan.FromSeconds(8) };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, c, _, e) =>
        {
            if (c != null) cert = new X509Certificate2(c);
            tlsErrors = e;
            return true;
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StatsMonitor-NetTools/1.0");
        var total = Stopwatch.StartNew();
        try
        {
            using var resp = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            total.Stop();
            return new HttpResult((int)resp.StatusCode is >= 200 and < 400, (int)resp.StatusCode, resp.ReasonPhrase ?? "", resp.RequestMessage?.RequestUri?.ToString() ?? url,
                "HTTP/" + resp.Version, resp.Headers.Server.ToString() is { Length: > 0 } s ? s : null, dnsMs, tcpMs, total.Elapsed.TotalMilliseconds,
                cert?.NotAfter, cert?.GetNameInfo(X509NameType.SimpleName, false), tlsErrors == SslPolicyErrors.None ? null : tlsErrors.ToString(), null, address);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new HttpResult(false, 0, "", url, "", null, dnsMs, tcpMs, total.Elapsed.TotalMilliseconds, cert?.NotAfter, cert?.GetNameInfo(X509NameType.SimpleName, false),
                tlsErrors == SslPolicyErrors.None ? null : tlsErrors.ToString(), ex.InnerException?.Message ?? ex.Message, address);
        }
    }

    public static List<AdapterInfo> GetAdapters(bool includeDown = false)
    {
        var list = new List<AdapterInfo>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            bool up = n.OperationalStatus == OperationalStatus.Up;
            if (!up && !includeDown) continue;
            try
            {
                var p = n.GetIPProperties();
                int mtu = 0; bool dhcp = false;
                try { var v4 = p.GetIPv4Properties(); mtu = v4.Mtu; dhcp = v4.IsDhcpEnabled; } catch { }
                var mac = string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(x => x.ToString("X2")));
                list.Add(new AdapterInfo(n.Name, n.Description, n.NetworkInterfaceType.ToString(), up, mac, n.Speed > 0 ? n.Speed / 1_000_000 : 0, mtu, dhcp,
                    [.. p.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => $"{a.Address}/{a.PrefixLength}")],
                    [.. p.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal).Select(a => a.Address.ToString())],
                    [.. p.GatewayAddresses.Select(g => g.Address.ToString()).Where(g => g != "0.0.0.0" && g != "::")],
                    [.. p.DnsAddresses.Select(d => d.ToString())]));
            }
            catch { }
        }
        return list;
    }

    public static async Task<PublicIpInfo?> GetPublicIpAsync(CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var text = await client.GetStringAsync("https://1.1.1.1/cdn-cgi/trace", ct);
            var d = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('=', 2)).Where(a => a.Length == 2).ToDictionary(a => a[0].Trim(), a => a[1].Trim());
            string G(string k) => d.TryGetValue(k, out var v) ? v : "";
            return new PublicIpInfo(G("ip"), G("loc"), G("colo"), G("http"), G("tls"), G("warp") is "on" or "plus");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public static async Task FlushDnsAsync()
    {
        using var p = Process.Start(new ProcessStartInfo("ipconfig.exe", "/flushdns") { CreateNoWindow = true, UseShellExecute = false });
        if (p != null) await p.WaitForExitAsync();
    }

    /// <summary>Measures throughput with parallel streams against Cloudflare's speed endpoints. Reports Mbps about every 250 ms and returns the final average.</summary>
    public static async Task<double> SpeedAsync(bool upload, int seconds, int streams, Action<double> report, CancellationToken ct)
    {
        long bytes = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sw = Stopwatch.StartNew();
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8), MaxConnectionsPerServer = streams };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("StatsMonitor-NetTools/1.0");

        async Task Worker()
        {
            try
            {
                var buffer = new byte[64 * 1024];
                while (!cts.IsCancellationRequested)
                {
                    if (!upload)
                    {
                        using var resp = await client.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        resp.EnsureSuccessStatusCode();
                        await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
                        int n;
                        while ((n = await s.ReadAsync(buffer, cts.Token)) > 0) Interlocked.Add(ref bytes, n);
                    }
                    else
                    {
                        using var content = new StreamContent(new CountingZeroStream(8_000_000, n => Interlocked.Add(ref bytes, n)));
                        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                        using var resp = await client.PostAsync("https://speed.cloudflare.com/__up", content, cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        var workers = Enumerable.Range(0, streams).Select(_ => Task.Run(Worker)).ToArray();
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        long last = 0; var lastT = TimeSpan.Zero;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(250, cts.Token);
                var now = sw.Elapsed; long cur = Interlocked.Read(ref bytes);
                report((cur - last) * 8 / 1e6 / Math.Max((now - lastT).TotalSeconds, 0.001));
                last = cur; lastT = now;
            }
        }
        catch (OperationCanceledException) { }
        try { await Task.WhenAll(workers); } catch { }
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Read(ref bytes) == 0) throw new IOException("Sem resposta do servidor de teste");
        return Interlocked.Read(ref bytes) * 8 / 1e6 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
    }

    private sealed class CountingZeroStream(long length, Action<int> counted) : Stream
    {
        private long _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, length - _pos);
            Array.Clear(buffer, offset, n);
            _pos += n;
            counted(n);
            return n;
        }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>Step-by-step connectivity check, from the local adapter out to the internet. Reports each step as it starts and again when it finishes.</summary>
    public static async Task RunDiagnosticsAsync(bool english, Action<DiagStep> report, CancellationToken ct)
    {
        string T(string pt, string en) => english ? en : pt;
        async Task Step(string name, Func<Task<DiagStep>> work)
        {
            report(new DiagStep(name, "…", DiagState.Running, null));
            ct.ThrowIfCancellationRequested();
            try { report(await work()); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { report(new DiagStep(name, ex.Message, DiagState.Fail, null)); }
        }

        async Task<(PingStats stats, string? err)> Probe(IPAddress ip, int n)
        {
            var st = new PingStats();
            string? err = null;
            await PingAsync(ip, n, 1500, 32, 200, false, r => { st.Add(r.Ms); if (r.Status != IPStatus.Success) err = r.Status.ToString(); }, ct);
            return (st, err);
        }

        DiagStep PingStep(string name, string target, PingStats st, double warnMs)
        {
            if (st.Received == 0) return new(name, T($"{target}: sem resposta (100% de perda)", $"{target}: no reply (100% loss)"), DiagState.Fail, null);
            var state = st.Loss > 0 || st.Avg > warnMs ? DiagState.Warn : DiagState.Ok;
            return new(name, T($"{target}: média {st.Avg:0} ms, perda {st.Loss:0}%, jitter {st.Jitter:0.0} ms", $"{target}: avg {st.Avg:0} ms, loss {st.Loss:0}%, jitter {st.Jitter:0.0} ms"), state, st.Avg);
        }

        var nAdapter = T("Adaptador de rede", "Network adapter");
        var nGw = T("Gateway (roteador)", "Gateway (router)");
        var nIp = T("Internet por IP (ICMP)", "Internet by IP (ICMP)");
        var nDns = T("Resolução de nomes (DNS)", "Name resolution (DNS)");
        var nCaptive = T("Acesso web / portal cativo", "Web access / captive portal");
        var nPub = T("IP público", "Public IP");

        AdapterInfo? active = null;
        await Step(nAdapter, () =>
        {
            active = GetAdapters().Where(a => a.Gateways.Count > 0 && a.Ipv4.Count > 0).OrderByDescending(a => a.SpeedMbps).FirstOrDefault();
            var any = active ?? GetAdapters().FirstOrDefault();
            if (any == null) return Task.FromResult(new DiagStep(nAdapter, T("Nenhum adaptador conectado", "No connected adapter"), DiagState.Fail, null));
            if (active == null) return Task.FromResult(new DiagStep(nAdapter, T($"{any.Name}: conectado, mas sem gateway/IP válido", $"{any.Name}: connected but no valid gateway/IP"), DiagState.Warn, null));
            return Task.FromResult(new DiagStep(nAdapter, $"{active.Name} ({active.Type}) · {active.Ipv4[0]} · {(active.SpeedMbps > 0 ? active.SpeedMbps + " Mbps" : "?")}", DiagState.Ok, null));
        });

        if (active == null)
        {
            foreach (var n in new[] { nGw, nIp, nDns, nCaptive, nPub }) report(new DiagStep(n, T("Ignorado: sem adaptador utilizável", "Skipped: no usable adapter"), DiagState.Skip, null));
            return;
        }

        bool gwOk = false;
        await Step(nGw, async () =>
        {
            var gw = IPAddress.Parse(active.Gateways[0]);
            var (st, _) = await Probe(gw, 4);
            gwOk = st.Received > 0;
            return PingStep(nGw, gw.ToString(), st, 30);
        });

        bool netOk = false;
        await Step(nIp, async () =>
        {
            var (a, _) = await Probe(IPAddress.Parse("1.1.1.1"), 4);
            var (b, _) = await Probe(IPAddress.Parse("8.8.8.8"), 4);
            netOk = a.Received > 0 || b.Received > 0;
            var best = a.Received > 0 && (b.Received == 0 || a.Avg <= b.Avg) ? ("1.1.1.1", a) : ("8.8.8.8", b);
            var step = PingStep(nIp, best.Item1, best.Item2, 100);
            if (a.Received > 0 && b.Received == 0 || a.Received == 0 && b.Received > 0)
                step = step with { State = DiagState.Warn, Detail = step.Detail + T(" (outro alvo não respondeu)", " (other target did not answer)") };
            return step;
        });

        await Step(nDns, async () =>
        {
            var servers = active.Dns.Select(IPAddress.Parse).Where(d => d.AddressFamily == AddressFamily.InterNetwork).ToList();
            var results = new List<string>();
            double? first = null;
            bool allFail = true;
            foreach (var s in servers.Take(2))
            {
                var r = await DnsQueryAsync(s, "www.microsoft.com", "A", 2000, ct);
                bool ok = r.Ok && r.Rcode == "NOERROR" && r.Answers.Count > 0;
                allFail &= !ok;
                if (ok) first ??= r.Ms;
                results.Add($"{s} {(ok ? r.Ms.ToString("0") + " ms" : r.Rcode)}");
            }
            var sw = Stopwatch.StartNew();
            var sys = await ResolveAsync("www.cloudflare.com", ct);
            double sysMs = sw.Elapsed.TotalMilliseconds;
            if (sys == null && allFail)
                return new DiagStep(nDns, T("Falha ao resolver nomes", "Failed to resolve names") + (results.Count > 0 ? " · " + string.Join(", ", results) : ""), DiagState.Fail, null);
            var state = allFail || sys == null || (first ?? sysMs) > 300 ? DiagState.Warn : DiagState.Ok;
            return new DiagStep(nDns, T($"Sistema: {sysMs:0} ms", $"System: {sysMs:0} ms") + (results.Count > 0 ? " · " + string.Join(", ", results) : ""), state, first ?? sysMs);
        });

        await Step(nCaptive, async () =>
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var sw = Stopwatch.StartNew();
            string body = await client.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt", ct);
            double ms = sw.Elapsed.TotalMilliseconds;
            return body.Trim() == "Microsoft Connect Test"
                ? new DiagStep(nCaptive, T($"Acesso HTTP direto à internet ({ms:0} ms)", $"Direct HTTP access to the internet ({ms:0} ms)"), DiagState.Ok, ms)
                : new DiagStep(nCaptive, T("Resposta inesperada: possível portal cativo (Wi-Fi público) ou proxy", "Unexpected response: possible captive portal (public Wi-Fi) or proxy"), DiagState.Warn, ms);
        });

        await Step(nPub, async () =>
        {
            var info = await GetPublicIpAsync(ct);
            return info == null
                ? new DiagStep(nPub, T("Não foi possível obter (sem internet ou bloqueado)", "Could not be obtained (no internet or blocked)"), netOk ? DiagState.Warn : DiagState.Fail, null)
                : new DiagStep(nPub, $"{info.Ip} · {info.Country} · {info.Colo}" + (info.Warp ? " · WARP" : ""), DiagState.Ok, null);
        });
        _ = gwOk;
    }
}
