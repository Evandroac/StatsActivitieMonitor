using StatsMonitor.Models;

namespace StatsMonitor.Diagnostics;

/// <summary>Orchestrates inventory → tests → analysis → persistence, reporting progress. Read-only: nothing on the machine is changed.</summary>
public static class DiagnosticRunner
{
    private static string L(string pt, string en) => StatsMonitor.Helpers.Loc.Lang == "en" ? en : pt;

    public static DiagnosticSession NewSession(bool full, HardwareSnapshot? live) => new()
    {
        Full = full,
        Elevated = live?.IsElevated ?? false,
        Computer = Environment.MachineName,
        AppVersion = typeof(DiagnosticRunner).Assembly.GetName().Version?.ToString(3) ?? "",
    };

    public static async Task<DiagnosticSession> RunFullAsync(DiagnosticStore store, Func<HardwareSnapshot?> live, Thresholds th, Action<int, string> progress, CancellationToken ct)
    {
        var s = NewSession(true, live());
        store.Audit($"full diagnostic started {s.Id}");
        var log = new Progress<string>(m => progress(-1, m));
        try
        {
            progress(2, L("Coletando inventário (somente leitura)…", "Collecting inventory (read-only)…"));
            s.Inventory = await Task.Run(() => InventoryCollector.CollectAll(live(), (n, p) => progress(2 + p * 40 / 100, L($"Inventário: {n}", $"Inventory: {n}")), ct), ct);
            s.Model = s.Inventory.S("hardware.model") ?? "";

            progress(45, L("Teste de CPU…", "CPU test…")); s.Tests.Add(await PerformanceTests.CpuAsync(live, log, ct));
            progress(62, L("Teste de memória…", "Memory test…")); s.Tests.Add(await PerformanceTests.MemoryAsync(log, ct));
            progress(72, L("Teste de disco…", "Disk test…")); s.Tests.Add(await PerformanceTests.DiskAsync(null, log, ct));
            progress(84, L("Teste de rede…", "Network test…")); s.Tests.Add(await PerformanceTests.NetworkAsync(log, ct));
            progress(92, L("Avaliando GPU…", "Assessing GPU…")); s.Tests.Add(PerformanceTests.Gpu(live()));

            progress(95, L("Analisando resultados…", "Analysing results…"));
            DiagnosticEngine.Analyze(s, live(), th);
            s.Finished = DateTime.Now;
            store.Save(s);
            store.Audit($"full diagnostic finished {s.Id} score={s.Score}");
            progress(100, L("Diagnóstico concluído.", "Diagnostic finished."));
        }
        catch (OperationCanceledException)
        {
            store.Audit($"full diagnostic cancelled {s.Id}");
            progress(-1, L("Diagnóstico cancelado pelo usuário. Nada foi salvo.", "Diagnostic cancelled by the user. Nothing was saved."));
            throw;
        }
        return s;
    }
}
