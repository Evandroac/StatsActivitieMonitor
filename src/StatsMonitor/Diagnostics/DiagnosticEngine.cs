using StatsMonitor.Models;

namespace StatsMonitor.Diagnostics;

public sealed record Thresholds(double CpuWarnTemp = 80, double CpuCritTemp = 90, double GpuWarnTemp = 80, double GpuCritTemp = 90,
    double RamWarnPct = 85, double RamCritPct = 95, double DiskFreeWarnPct = 15, double DiskFreeCritPct = 5,
    double BatteryWarnHealth = 70, double BatteryCritHealth = 50, double StorageTempWarn = 60, double StorageTempCrit = 70);

/// <summary>Rule-based analysis. Pure: the same inputs always give the same findings. A rule only fires on measured evidence; missing data becomes Indeterminate, never Normal.</summary>
public static class DiagnosticEngine
{
    private static string L(string pt, string en) => StatsMonitor.Helpers.Loc.Lang == "en" ? en : pt;

    public static void Analyze(DiagnosticSession s, HardwareSnapshot? live, Thresholds t)
    {
        var f = new List<Finding>();
        var inv = s.Inventory;
        var now = DateTime.Now;

        void Add(string comp, Severity lvl, string problem, string evidence, string cause = "", string rec = "", string test = "")
            => f.Add(new Finding { Component = comp, Level = lvl, Problem = problem, Evidence = evidence, Cause = cause, Recommendation = rec, TestResult = test, Time = now });

        string TestFor(string comp) => string.Join("; ", s.Tests.Where(x => x.Component == comp).Select(x => $"{x.Name}: {SeverityInfo.Label(x.Result, false)}"));

        // Components whose collection failed completely stay undetermined.
        foreach (var c in Comp.All)
        {
            if (!inv.Collected.Contains(c))
                Add(c, Severity.Indeterminate, L("Componente não analisado", "Component not analysed"), L("A coleta não foi executada.", "Collection did not run."), "", L("Execute o diagnóstico completo.", "Run the full diagnostic."));
        }

        // CPU
        if (inv.Collected.Contains(Comp.Cpu))
        {
            double? temp = live?.CpuTemp is > 0 ? live.CpuTemp : null;
            if (temp is null) Add(Comp.Cpu, Severity.Indeterminate, L("Temperatura da CPU indisponível", "CPU temperature unavailable"), L("Nenhum sensor retornou valor.", "No sensor returned a value."), L("Sensor não exposto ou aplicativo sem administrador.", "Sensor not exposed or app not elevated."), L("Execute como administrador para ler sensores.", "Run as administrator to read sensors."));
            else if (temp >= t.CpuCritTemp) Add(Comp.Cpu, Severity.Critical, L("Temperatura da CPU crítica", "CPU temperature critical"), $"{temp:0} °C (limite {t.CpuCritTemp:0})", L("Refrigeração insuficiente, pasta térmica ressecada ou carga contínua alta.", "Insufficient cooling, dried thermal paste or sustained load."), L("Verifique cooler, poeira e fluxo de ar.", "Check cooler, dust and airflow."), TestFor(Comp.Cpu));
            else if (temp >= t.CpuWarnTemp) Add(Comp.Cpu, Severity.Warning, L("Temperatura da CPU elevada", "CPU temperature high"), $"{temp:0} °C (limite {t.CpuWarnTemp:0})", L("Carga alta ou ventilação limitada.", "High load or limited ventilation."), L("Acompanhe a temperatura e limpe o equipamento se persistir.", "Monitor temperature and clean the device if it persists."), TestFor(Comp.Cpu));
            else Add(Comp.Cpu, Severity.Normal, L("Temperatura da CPU dentro do esperado", "CPU temperature normal"), $"{temp:0} °C");

            if (live != null && live.CpuLoad >= 90) Add(Comp.Cpu, Severity.Warning, L("Uso de CPU muito alto no momento", "CPU usage very high right now"), $"{live.CpuLoad:0}%", L("Processo consumindo CPU.", "A process is consuming CPU."), L("Veja a tela Processos.", "See the Processes page."));
            if (temp >= t.CpuWarnTemp && live?.CpuClockMhz is { } clk && inv.N("cpu.maxMhz") is { } max && max > 0 && clk < max * 0.6 && live.CpuLoad >= 70)
                Add(Comp.Cpu, Severity.Warning, L("Possível limitação térmica (throttling)", "Possible thermal throttling"), $"{temp:0} °C, {clk:0} MHz ({clk * 100 / max:0}% {L("do máximo", "of max")}), {L("carga", "load")} {live.CpuLoad:0}%", L("Temperatura alta com frequência baixa sob carga é compatível com throttling; não é confirmação.", "High temperature with low clock under load is consistent with throttling; not a confirmation."), L("Verifique refrigeração e plano de energia.", "Check cooling and power plan."));
        }

        // Memory
        if (inv.Collected.Contains(Comp.Memory))
        {
            double? pct = inv.N("memory.totalGb") is { } tot && tot > 0 && inv.N("memory.availGb") is { } av ? 100 * (tot - av) / tot : null;
            if (pct is null) Add(Comp.Memory, Severity.Indeterminate, L("Uso de RAM indisponível", "RAM usage unavailable"), L("WMI não retornou memória.", "WMI returned no memory data."));
            else if (pct >= t.RamCritPct) Add(Comp.Memory, Severity.Critical, L("Memória RAM quase esgotada", "RAM almost exhausted"), $"{pct:0}%", L("Excesso de aplicativos ou vazamento de memória.", "Too many apps or a memory leak."), L("Feche aplicativos pesados; considere mais RAM.", "Close heavy apps; consider more RAM."), TestFor(Comp.Memory));
            else if (pct >= t.RamWarnPct) Add(Comp.Memory, Severity.Warning, L("Uso de RAM elevado", "High RAM usage"), $"{pct:0}%", L("Muitos aplicativos abertos.", "Many apps open."), L("Verifique a tela Processos.", "See the Processes page."), TestFor(Comp.Memory));
            else Add(Comp.Memory, Severity.Normal, L("Uso de RAM adequado", "RAM usage fine"), $"{pct:0}%");
            if (inv.N("memory.commitPct") is { } cm && cm >= 90) Add(Comp.Memory, Severity.Warning, L("Memória comprometida alta", "High committed memory"), $"{cm:0}%", L("Arquivo de paginação pode estar sendo muito usado.", "The page file may be heavily used."), L("Aumente a RAM ou o arquivo de paginação.", "Add RAM or enlarge the page file."));
            if (inv.N("memory.speed") is { } sp && inv.N("memory.configSpeed") is { } cs && cs > 0 && sp - cs >= 200)
                Add(Comp.Memory, Severity.Info, L("RAM rodando abaixo da frequência nominal", "RAM below rated speed"), $"{cs:0} / {sp:0} MHz", L("Perfil XMP/EXPO desativado ou limite da plataforma.", "XMP/EXPO profile off or platform limit."), L("Opcional: habilitar XMP/EXPO na BIOS.", "Optional: enable XMP/EXPO in BIOS."));
            if (live != null && pct >= t.RamWarnPct && live.Disks.Any(d => d.Activity >= 80))
                Add(Comp.Memory, Severity.Warning, L("Possível uso intenso de paginação", "Possible heavy paging"), $"RAM {pct:0}% + {L("disco ativo", "disk busy")} ≥80%", L("RAM alta com disco saturado é compatível com troca de páginas; não é confirmação.", "High RAM with a saturated disk is consistent with paging; not a confirmation."), L("Reduza o uso de memória.", "Reduce memory usage."));
        }

        // GPU
        if (inv.Collected.Contains(Comp.Gpu))
        {
            if (inv.N("gpu.errorCode") is { } ec && ec != 0) Add(Comp.Gpu, Severity.Critical, L("Dispositivo de vídeo com erro", "Video device error"), $"{L("Código", "Code")} {ec:0}", L("Driver ausente, corrompido ou dispositivo desativado.", "Missing/corrupt driver or disabled device."), L("Reinstale o driver de vídeo.", "Reinstall the video driver."));
            else if (inv.N("gpu.count") is > 0) Add(Comp.Gpu, Severity.Normal, L("Dispositivo de vídeo reportado sem erro", "Video device reports no error"), inv.S("gpu.name") ?? "");
            double? gt = live?.GpuTemp is > 0 ? live.GpuTemp : null;
            if (gt is null) Add(Comp.Gpu, Severity.Indeterminate, L("Temperatura da GPU indisponível", "GPU temperature unavailable"), L("Sem sensor de GPU.", "No GPU sensor."), "", "", TestFor(Comp.Gpu));
            else if (gt >= t.GpuCritTemp) Add(Comp.Gpu, Severity.Critical, L("Temperatura da GPU crítica", "GPU temperature critical"), $"{gt:0} °C", L("Refrigeração insuficiente.", "Insufficient cooling."), L("Verifique ventoinhas e fluxo de ar.", "Check fans and airflow."), TestFor(Comp.Gpu));
            else if (gt >= t.GpuWarnTemp) Add(Comp.Gpu, Severity.Warning, L("Temperatura da GPU elevada", "GPU temperature high"), $"{gt:0} °C", "", L("Acompanhe a temperatura.", "Monitor temperature."), TestFor(Comp.Gpu));
            else Add(Comp.Gpu, Severity.Normal, L("Temperatura da GPU adequada", "GPU temperature fine"), $"{gt:0} °C");
            if (live?.VramTotalGb is > 0 && live.VramUsedGb is { } vu && vu / live.VramTotalGb >= 0.95) Add(Comp.Gpu, Severity.Warning, L("VRAM quase cheia", "VRAM nearly full"), $"{vu:0.0}/{live.VramTotalGb:0.0} GB");
            if (inv.N("gpu.driverAgeDays") is { } da && da > 730) Add(Comp.Gpu, Severity.Info, L("Driver de vídeo com mais de 2 anos", "Video driver older than 2 years"), $"{da / 365:0.#} {L("anos", "years")}", L("Heurística por data; não prova que exista versão melhor.", "Date-based heuristic; does not prove a newer version exists."), L("Consulte o site do fabricante.", "Check the vendor site."));
        }

        // Storage
        if (inv.Collected.Contains(Comp.Storage))
        {
            int n = (int)(inv.N("storage.disks") ?? 0);
            if (n == 0) Add(Comp.Storage, Severity.Indeterminate, L("Saúde dos discos indisponível", "Disk health unavailable"), L("MSFT_PhysicalDisk não retornou discos.", "MSFT_PhysicalDisk returned no disks."), "", L("Execute como administrador.", "Run as administrator."));
            for (int i = 0; i < n; i++)
            {
                string k = $"storage.d{i}.", name = inv.S(k + "name") ?? $"#{i}";
                int h = (int)(inv.N(k + "health") ?? -1);
                if (h == 2) Add(Comp.Storage, Severity.Critical, $"{name}: {L("disco reportado como não saudável", "disk reported unhealthy")}", L("HealthStatus = Unhealthy (Windows)", "HealthStatus = Unhealthy (Windows)"), L("O Windows detectou falha ou erros graves no disco.", "Windows detected failure or serious errors."), L("Faça backup imediatamente e verifique o disco.", "Back up immediately and check the disk."), TestFor(Comp.Storage));
                else if (h == 1) Add(Comp.Storage, Severity.Warning, $"{name}: {L("saúde em atenção", "health warning")}", "HealthStatus = Warning", "", L("Faça backup e acompanhe.", "Back up and monitor."));
                else if (h == 0) Add(Comp.Storage, Severity.Normal, $"{name}: {L("saúde reportada como saudável", "health reported healthy")}", "HealthStatus = Healthy");
                else Add(Comp.Storage, Severity.Indeterminate, $"{name}: {L("saúde indisponível", "health unavailable")}", L("O Windows não informou HealthStatus.", "Windows did not report HealthStatus."));
                if (inv.N(k + "readErr") is { } re && re > 0) Add(Comp.Storage, Severity.Warning, $"{name}: {L("erros de leitura não corrigidos", "uncorrected read errors")}", $"{re:0}", L("Pode indicar setores com problema; também pode ser cabo/controlador.", "May indicate bad sectors; could also be cable/controller."), L("Faça backup e execute teste do fabricante.", "Back up and run the vendor test."));
                if (inv.N(k + "wear") is { } w && w >= 90) Add(Comp.Storage, Severity.Critical, $"{name}: {L("desgaste muito alto", "very high wear")}", $"{w:0}%", L("SSD próximo do fim da vida útil informada.", "SSD near its reported endurance."), L("Planeje substituição e backup.", "Plan replacement and backup."));
                else if (inv.N(k + "wear") is { } w2 && w2 >= 70) Add(Comp.Storage, Severity.Warning, $"{name}: {L("desgaste elevado", "elevated wear")}", $"{w2:0}%");
                if (inv.N(k + "temp") is { } tp)
                {
                    if (tp >= t.StorageTempCrit) Add(Comp.Storage, Severity.Critical, $"{name}: {L("temperatura crítica", "critical temperature")}", $"{tp:0} °C", "", L("Melhore a ventilação.", "Improve ventilation."));
                    else if (tp >= t.StorageTempWarn) Add(Comp.Storage, Severity.Warning, $"{name}: {L("temperatura elevada", "high temperature")}", $"{tp:0} °C");
                }
            }
            int v = (int)(inv.N("storage.volumes") ?? 0);
            for (int i = 0; i < v; i++)
            {
                string k = $"storage.v{i}.", nm = inv.S(k + "name") ?? "?";
                double fp = inv.N(k + "freePct") ?? 100;
                if (fp <= t.DiskFreeCritPct) Add(Comp.Storage, Severity.Critical, $"{nm} {L("quase sem espaço livre", "almost out of free space")}", $"{fp:0.#}% ({inv.N(k + "freeGb"):0.0} GB)", "", L("Libere espaço.", "Free up space."));
                else if (fp <= t.DiskFreeWarnPct) Add(Comp.Storage, Severity.Warning, $"{nm} {L("com pouco espaço livre", "low on free space")}", $"{fp:0.#}% ({inv.N(k + "freeGb"):0.0} GB)", "", L("Libere espaço.", "Free up space."));
                else Add(Comp.Storage, Severity.Normal, $"{nm} {L("com espaço livre adequado", "has enough free space")}", $"{fp:0.#}%");
            }
        }

        // Battery
        if (inv.Collected.Contains(Comp.Battery))
        {
            if (inv.N("battery.present") is not 1) Add(Comp.Battery, Severity.Info, L("Sem bateria detectada", "No battery detected"), L("Provavelmente um desktop; não se aplica.", "Probably a desktop; not applicable."));
            else if (inv.N("battery.health") is not { } hh) Add(Comp.Battery, Severity.Indeterminate, L("Saúde da bateria indisponível", "Battery health unavailable"), L("Capacidade de projeto/atual não informada.", "Design/full capacity not reported."));
            else if (hh < t.BatteryCritHealth) Add(Comp.Battery, Severity.Critical, L("Bateria muito degradada", "Battery heavily degraded"), $"{hh:0.#}% {L("da capacidade original", "of original capacity")}", L("Desgaste químico natural.", "Natural chemical wear."), L("Considere substituir a bateria.", "Consider replacing the battery."));
            else if (hh < t.BatteryWarnHealth) Add(Comp.Battery, Severity.Warning, L("Bateria degradada", "Battery degraded"), $"{hh:0.#}%", "", L("Autonomia reduzida; avalie substituição.", "Reduced runtime; consider replacing."));
            else Add(Comp.Battery, Severity.Normal, L("Saúde da bateria adequada", "Battery health fine"), $"{hh:0.#}%");
        }

        // OS
        if (inv.Collected.Contains(Comp.Os))
        {
            if (inv.N("os.pendingReboot") is 1) Add(Comp.Os, Severity.Warning, L("Reinicialização pendente", "Restart pending"), L("Chave de reinicialização do Windows presente.", "Windows pending-restart key present."), L("Atualização aguardando reinício.", "Update awaiting restart."), L("Reinicie o computador.", "Restart the computer."));
            if (inv.N("os.uptimeDays") is { } up && up >= 30) Add(Comp.Os, Severity.Info, L("Computador ligado há muito tempo", "Long uptime"), $"{up:0} {L("dias", "days")}", "", L("Reiniciar aplica atualizações e libera recursos.", "Restarting applies updates and frees resources."));
            if (inv.N("os.hotfixAgeDays") is { } ha && ha > 90) Add(Comp.Os, Severity.Warning, L("Sem atualizações recentes registradas", "No recent updates recorded"), $"{ha:0} {L("dias desde a última", "days since the last")}", L("Windows Update pode estar pausado ou falhando.", "Windows Update may be paused or failing."), L("Verifique o Windows Update.", "Check Windows Update."));
            if (inv.N("os.autoStopped") is { } st && st > 0) Add(Comp.Os, Severity.Info, L("Serviços automáticos parados", "Automatic services stopped"), $"{st:0}", L("Alguns são normais (iniciam sob demanda).", "Some are normal (on-demand)."), "");
            if (inv.N("os.eventErrors24h") is { } ev)
            {
                if (ev >= 100) Add(Comp.Os, Severity.Warning, L("Muitos erros no log do sistema", "Many System log errors"), $"{ev:0} / 24 h", L("Pode indicar driver ou hardware instável.", "May indicate an unstable driver or hardware."), L("Veja o Visualizador de Eventos.", "See Event Viewer."));
                else if (ev >= 10) Add(Comp.Os, Severity.Info, L("Erros no log do sistema", "System log errors"), $"{ev:0} / 24 h");
                else Add(Comp.Os, Severity.Normal, L("Poucos erros no log do sistema", "Few System log errors"), $"{ev:0} / 24 h");
            }
            else Add(Comp.Os, Severity.Indeterminate, L("Log de eventos indisponível", "Event log unavailable"), L("Leitura falhou ou foi negada.", "Read failed or was denied."));
        }

        // Drivers
        if (inv.Collected.Contains(Comp.Drivers))
        {
            if (inv.N("drivers.errors") is not { } de) Add(Comp.Drivers, Severity.Indeterminate, L("Estado dos dispositivos indisponível", "Device state unavailable"), L("WMI não respondeu.", "WMI did not respond."));
            else if (de > 0) Add(Comp.Drivers, Severity.Warning, L("Dispositivos com erro no Gerenciador de Dispositivos", "Devices with errors in Device Manager"), $"{de:0}: {inv.S("drivers.badList")}", L("Driver ausente, desativado ou com falha.", "Missing, disabled or failing driver."), L("Atualize ou reinstale os drivers listados.", "Update or reinstall the listed drivers."));
            else Add(Comp.Drivers, Severity.Normal, L("Nenhum dispositivo com erro", "No devices with errors"), "0");
            if (inv.N("drivers.conflicts") is > 0) Add(Comp.Drivers, Severity.Warning, L("Conflito de recursos detectado", "Resource conflict detected"), inv.N("drivers.conflicts")!.Value.ToString("0"));
            if (inv.N("drivers.oldNetDays") is { } on && on > 1095) Add(Comp.Drivers, Severity.Info, L("Driver de rede muito antigo", "Very old network driver"), $"{on / 365:0.#} {L("anos", "years")}", L("Heurística por data; não confirma defeito.", "Date heuristic; no defect confirmed."), "");
        }

        // Network
        if (inv.Collected.Contains(Comp.Network))
        {
            if (inv.N("network.adapters") is not { } na) Add(Comp.Network, Severity.Indeterminate, L("Adaptadores indisponíveis", "Adapters unavailable"), "");
            else if (inv.N("network.up") is 0) Add(Comp.Network, Severity.Warning, L("Nenhum adaptador de rede ativo", "No active network adapter"), $"{na:0} {L("adaptadores", "adapters")}", "", L("Verifique cabo, Wi-Fi e driver.", "Check cable, Wi-Fi and driver."), TestFor(Comp.Network));
            else Add(Comp.Network, Severity.Normal, L("Adaptador de rede ativo", "Network adapter up"), $"{inv.N("network.up"):0}");
        }
        foreach (var r in s.Tests.Where(x => x.Component == Comp.Network && x.Result is Severity.Warning or Severity.Critical))
            Add(Comp.Network, r.Result, r.Name, r.Summary, "", L("Execute os testes da tela Testes de conexão.", "Run the Connection tests page."), r.Summary);

        // Security
        if (inv.Collected.Contains(Comp.Security))
        {
            Sec(inv.N("security.defender"), Comp.Security, "Microsoft Defender", L("Antivírus desativado", "Antivirus disabled"), Severity.Warning, L("Ative a proteção ou confirme outro antivírus.", "Enable protection or confirm another antivirus."), Add);
            if (inv.N("security.defender") is 1) Sec(inv.N("security.realtime"), Comp.Security, L("Proteção em tempo real", "Real-time protection"), L("Proteção em tempo real desativada", "Real-time protection off"), Severity.Warning, L("Ative a proteção em tempo real.", "Enable real-time protection."), Add);
            if (inv.N("security.sigAgeDays") is { } sa && sa > 7) Add(Comp.Security, Severity.Warning, L("Assinaturas do antivírus desatualizadas", "Antivirus signatures outdated"), $"{sa:0} {L("dias", "days")}", "", L("Atualize as definições.", "Update definitions."));
            if (inv.N("security.firewallKnown") is { } fk) { if (inv.N("security.firewallOff") is > 0) Add(Comp.Security, Severity.Warning, L("Firewall desativado em algum perfil", "Firewall off in a profile"), $"{inv.N("security.firewallOff"):0}/{fk:0}", "", L("Ative o firewall.", "Enable the firewall.")); else Add(Comp.Security, Severity.Normal, L("Firewall ativo", "Firewall on"), $"{fk:0}/{fk:0}"); }
            else Add(Comp.Security, Severity.Indeterminate, L("Firewall indeterminado", "Firewall undetermined"), L("Registro não legível.", "Registry unreadable."));
            Sec(inv.N("security.uac"), Comp.Security, "UAC", L("UAC desativado", "UAC disabled"), Severity.Warning, L("Ative o UAC.", "Enable UAC."), Add);
            if (inv.N("security.wuDisabled") is 1) Add(Comp.Security, Severity.Warning, L("Serviço Windows Update desabilitado", "Windows Update service disabled"), "wuauserv = Disabled", "", L("Reabilite o serviço.", "Re-enable the service."));
            if (inv.N("security.secureBoot") is { } sb) { if (sb == 0) Add(Comp.Security, Severity.Info, "Secure Boot", L("Desativado", "Off")); else Add(Comp.Security, Severity.Normal, "Secure Boot", L("Ativo", "On")); }
            else Add(Comp.Security, Severity.Indeterminate, "Secure Boot", L("Não foi possível ler (BIOS legada ou sem permissão).", "Could not read (legacy BIOS or no permission)."));
            if (inv.N("security.tpm") is { } tp) { if (tp == 0) Add(Comp.Security, Severity.Info, "TPM", L("Desativado", "Off")); }
        }

        // Test results as findings
        foreach (var r in s.Tests.Where(x => x.Component != Comp.Network))
        {
            if (r.Result is Severity.Warning or Severity.Critical)
                Add(r.Component, r.Result, L("Teste: ", "Test: ") + r.Name, r.Summary, L("Resultado medido no teste; pode variar com carga de fundo.", "Measured in the test; may vary with background load."), L("Repita sem outros aplicativos abertos para confirmar.", "Repeat with no other apps open to confirm."), r.Summary);
            else if (r.Result == Severity.Indeterminate) Add(r.Component, Severity.Indeterminate, L("Teste não concluído: ", "Test not completed: ") + r.Name, r.Summary);
            else Add(r.Component, Severity.Normal, L("Teste aprovado: ", "Test passed: ") + r.Name, r.Summary, test: r.Summary);
        }

        s.Findings = f.OrderByDescending(x => SeverityInfo.Rank(x.Level)).ThenBy(x => x.Component).ToList();
        s.Health = Comp.All.ToDictionary(c => c, c => Roll(c, f, inv));
        s.Score = Score(s.Findings);
    }

    private static void Sec(double? v, string comp, string label, string problem, Severity lvl, string rec, Action<string, Severity, string, string, string, string, string> add)
    {
        if (v is null) add(comp, Severity.Indeterminate, label, L("Não foi possível determinar.", "Could not determine."), "", "", "");
        else if (v == 0) add(comp, lvl, problem, label, "", rec, "");
        else add(comp, Severity.Normal, label, L("Ativo", "On"), "", "", "");
    }

    /// <summary>Component state: worst non-Info finding. Components never collected are Indeterminate; a component with only Info/Normal is Normal.</summary>
    public static Severity Roll(string comp, IEnumerable<Finding> all, Inventory inv)
    {
        if (!inv.Collected.Contains(comp)) return Severity.Indeterminate;
        var list = all.Where(x => x.Component == comp).ToList();
        if (list.Count == 0) return Severity.Indeterminate;
        return SeverityInfo.Worst(list.Select(x => x.Level == Severity.Info ? Severity.Normal : x.Level));
    }

    /// <summary>Heuristic 0-100 indicator: 100 − 25 per critical − 10 per warning − 3 per undetermined. Not a certified metric.</summary>
    public static int Score(IEnumerable<Finding> f)
    {
        int c = 0, w = 0, i = 0;
        foreach (var x in f) { if (x.Level == Severity.Critical) c++; else if (x.Level == Severity.Warning) w++; else if (x.Level == Severity.Indeterminate) i++; }
        return Math.Max(0, 100 - 25 * c - 10 * w - 3 * i);
    }
}
