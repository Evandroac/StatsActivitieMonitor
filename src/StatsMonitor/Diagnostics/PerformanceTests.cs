using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using StatsMonitor.Models;
using StatsMonitor.Services;

namespace StatsMonitor.Diagnostics;

/// <summary>Non-destructive, cancellable measurements. The disk test only touches one temporary file that is deleted on close.</summary>
public static class PerformanceTests
{
    private static string L(string pt, string en) => StatsMonitor.Helpers.Loc.Lang == "en" ? en : pt;

    /// <summary>Coefficient of variation (stddev / mean) in percent; 0 for fewer than 2 samples.</summary>
    public static double Variation(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        double mean = v.Average();
        if (mean <= 0) return 0;
        double var = v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1);
        return Math.Sqrt(var) / mean * 100;
    }

    public static async Task<TestResult> CpuAsync(Func<HardwareSnapshot?> live, IProgress<string>? log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var rates = new List<double>();
        var temps = new List<double>();
        var clocks = new List<double>();
        int threads = Environment.ProcessorCount;
        for (int round = 1; round <= 3; round++)
        {
            ct.ThrowIfCancellationRequested();
            log?.Report(L($"CPU: rodada {round}/3 em {threads} threads…", $"CPU: round {round}/3 on {threads} threads…"));
            long ops = await Task.Run(() =>
            {
                long total = 0;
                var end = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 2;
                Parallel.For(0, threads, new ParallelOptions { CancellationToken = ct }, _ =>
                {
                    long local = 0; double x = 1.0001;
                    while (Stopwatch.GetTimestamp() < end) { for (int i = 0; i < 20000; i++) x = x * 1.0000001 + Math.Sqrt(i); local += 20000; }
                    GC.KeepAlive(x);
                    Interlocked.Add(ref total, local);
                });
                return total;
            }, ct);
            rates.Add(ops / 1.5 / 1e6);
            var s = live();
            if (s?.CpuTemp is > 0) temps.Add(s.CpuTemp.Value);
            if (s?.CpuClockMhz is > 0) clocks.Add(s.CpuClockMhz.Value);
        }
        double cv = Variation(rates), avg = rates.Average();
        string extra = temps.Count > 0 ? $" · {L("temp. máx.", "max temp")} {temps.Max():0} °C" : $" · {L("temperatura indisponível", "temperature unavailable")}";
        var sev = cv > 15 ? Severity.Warning : Severity.Normal;
        if (temps.Count > 0 && temps.Max() >= 95) sev = Severity.Critical;
        return new TestResult
        {
            Name = L("Estresse de CPU (3 × 1,5 s)", "CPU stress (3 × 1.5 s)"), Component = Comp.Cpu, Result = sev, Value = avg, Unit = "Mops/s", DurationMs = sw.Elapsed.TotalMilliseconds,
            Summary = $"{avg:0.0} Mops/s · {L("variação", "variation")} {cv:0.#}%{extra}" + (clocks.Count > 0 ? $" · {clocks.Average():0} MHz" : ""),
        };
    }

    private const long Mix = unchecked((long)0x9E3779B97F4A7C15UL);

    public static async Task<TestResult> MemoryAsync(IProgress<string>? log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        const int mb = 256;
        log?.Report(L($"RAM: gravando e verificando {mb} MB…", $"RAM: writing and verifying {mb} MB…"));
        var (errors, mbps) = await Task.Run(() =>
        {
            var buf = new long[mb * 1024 * 1024 / 8];
            var t = Stopwatch.StartNew();
            for (int i = 0; i < buf.Length; i++) { if ((i & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested(); buf[i] = (long)i * Mix; }
            int bad = 0;
            for (int i = 0; i < buf.Length; i++) if (buf[i] != (long)i * Mix) bad++;
            for (int i = 0; i < buf.Length; i++) buf[i] = ~((long)i * Mix);
            for (int i = 0; i < buf.Length; i++) if (buf[i] != ~((long)i * Mix)) bad++;
            t.Stop();
            return (bad, mb * 4.0 / Math.Max(t.Elapsed.TotalSeconds, 0.001));
        }, ct);
        return new TestResult
        {
            Name = L("Integridade e banda de RAM", "RAM integrity and bandwidth"), Component = Comp.Memory, Value = mbps, Unit = "MB/s", DurationMs = sw.Elapsed.TotalMilliseconds,
            Result = errors > 0 ? Severity.Critical : Severity.Normal,
            Summary = errors > 0 ? L($"{errors} divergências em {mb} MB testados. Repita e use MemTest86 para confirmar.", $"{errors} mismatches in {mb} MB tested. Repeat and use MemTest86 to confirm.")
                : L($"0 erros em {mb} MB · ~{mbps:0} MB/s. Não substitui o MemTest86 (que testa toda a RAM).", $"0 errors in {mb} MB · ~{mbps:0} MB/s. Does not replace MemTest86 (which tests all RAM)."),
        };
    }

    public static async Task<TestResult> DiskAsync(string? dir, IProgress<string>? log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string root = dir ?? Path.GetTempPath();
        const int mb = 128;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            if (drive.AvailableFreeSpace < 1L << 30)
                return new TestResult { Name = L("Leitura/gravação de disco", "Disk read/write"), Component = Comp.Storage, Result = Severity.Indeterminate, Summary = L("Ignorado: menos de 1 GB livre.", "Skipped: less than 1 GB free.") };
        }
        catch (Exception ex) { return new TestResult { Name = L("Leitura/gravação de disco", "Disk read/write"), Component = Comp.Storage, Result = Severity.Indeterminate, Summary = ex.Message }; }

        log?.Report(L($"Disco: arquivo temporário de {mb} MB em {root}…", $"Disk: {mb} MB temp file in {root}…"));
        try
        {
            var r = await Task.Run(() =>
            {
                string path = Path.Combine(root, $"statsmonitor-diag-{Guid.NewGuid():N}.tmp");
                var data = new byte[1 << 20];
                new Random(42).NextBytes(data);
                byte[] expected = SHA256.HashData(data);
                using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 20, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                var w = Stopwatch.StartNew();
                for (int i = 0; i < mb; i++) { ct.ThrowIfCancellationRequested(); fs.Write(data, 0, data.Length); }
                fs.Flush(true); w.Stop();
                fs.Position = 0;
                var rd = Stopwatch.StartNew();
                int bad = 0; var buf = new byte[1 << 20];
                for (int i = 0; i < mb; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    int got = 0; while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                    if (got != buf.Length || !SHA256.HashData(buf).AsSpan().SequenceEqual(expected)) bad++;
                }
                rd.Stop();
                return (mb / Math.Max(w.Elapsed.TotalSeconds, 0.001), mb / Math.Max(rd.Elapsed.TotalSeconds, 0.001), bad);
            }, ct);
            var sev = r.bad > 0 ? Severity.Critical : (r.Item1 < 20 ? Severity.Warning : Severity.Normal);
            return new TestResult
            {
                Name = L("Leitura/gravação sequencial", "Sequential read/write"), Component = Comp.Storage, Result = sev, Value = r.Item1, Unit = "MB/s", DurationMs = sw.Elapsed.TotalMilliseconds,
                Summary = $"{L("Gravação", "Write")} {r.Item1:0} MB/s · {L("Leitura", "Read")} {r.Item2:0} MB/s (cache do SO pode inflar a leitura) · {L("blocos com divergência", "mismatching blocks")}: {r.bad}",
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new TestResult { Name = L("Leitura/gravação sequencial", "Sequential read/write"), Component = Comp.Storage, Result = Severity.Indeterminate, Summary = ex.Message, DurationMs = sw.Elapsed.TotalMilliseconds };
        }
    }

    public static async Task<TestResult> NetworkAsync(IProgress<string>? log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        log?.Report(L("Rede: ping 1.1.1.1 × 10 e resolução de DNS…", "Network: ping 1.1.1.1 × 10 and DNS lookup…"));
        try
        {
            var st = new PingStats();
            await NetworkToolsService.PingAsync(System.Net.IPAddress.Parse("1.1.1.1"), 10, 1500, 32, 200, false, r => st.Add(r.Ms), ct);
            bool dnsOk = false;
            try { dnsOk = await NetworkToolsService.ResolveAsync("www.microsoft.com", ct) != null; } catch (OperationCanceledException) { throw; } catch { }
            var sev = st.Received == 0 ? Severity.Warning : (st.Loss >= 10 || !dnsOk) ? Severity.Warning : Severity.Normal;
            if (st.Received == 0 && !dnsOk) sev = Severity.Warning;
            return new TestResult
            {
                Name = L("Latência, perda e DNS", "Latency, loss and DNS"), Component = Comp.Network, Result = sev, Value = st.Avg, Unit = "ms", DurationMs = sw.Elapsed.TotalMilliseconds,
                Summary = st.Received == 0 ? L("Sem resposta ao ping (a rede pode bloquear ICMP ou estar desconectada).", "No ping reply (network may block ICMP or be offline).")
                    : $"{L("média", "avg")} {st.Avg:0} ms · {L("jitter", "jitter")} {st.Jitter:0.#} ms · {L("perda", "loss")} {st.Loss:0}% · DNS {(dnsOk ? "OK" : L("falhou", "failed"))}",
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new TestResult { Name = L("Latência, perda e DNS", "Latency, loss and DNS"), Component = Comp.Network, Result = Severity.Indeterminate, Summary = ex.Message }; }
    }

    public static TestResult Gpu(HardwareSnapshot? live)
    {
        int tier = System.Windows.Media.RenderCapability.Tier >> 16;
        var parts = new List<string> { $"{L("Nível de renderização WPF", "WPF render tier")}: {tier} (0 = {L("software", "software")}, 2 = {L("aceleração completa", "full acceleration")})" };
        if (live?.GpuTemp is > 0) parts.Add($"{live.GpuTemp:0} °C");
        if (live?.GpuLoad is { } l) parts.Add($"{L("uso", "usage")} {l:0}%");
        return new TestResult
        {
            Name = L("Aceleração gráfica e sensores", "Graphics acceleration and sensors"), Component = Comp.Gpu,
            Result = tier == 0 ? Severity.Warning : Severity.Normal, Value = tier, Unit = "tier",
            Summary = string.Join(" · ", parts) + L(" · Limitação: não há teste de estresse 3D sintético.", " · Limitation: no synthetic 3D stress test."),
        };
    }
}
