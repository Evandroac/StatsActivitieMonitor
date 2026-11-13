using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.Settings;

namespace StatsMonitor.Services;

public static class HealthCheck
{
    /// <summary>Evaluates a snapshot against the alert thresholds. Returns whether the PC looks healthy and a localized report.</summary>
    public static (bool Healthy, string Report) Evaluate(HardwareSnapshot s, AppSettings cfg)
    {
        var problems = new List<string>();
        var notes = new List<string>();

        void Temp(string label, double? t, double warn, double crit)
        {
            if (t is null or <= 0) return;
            if (t >= crit) problems.Add(Loc.T("{0} em temperatura crítica ({1})", label, Units.Temp(t.Value)));
            else if (t >= warn) problems.Add(Loc.T("{0} com temperatura elevada ({1})", label, Units.Temp(t.Value)));
        }
        Temp("CPU", s.CpuTemp, cfg.CpuWarnTemp, cfg.CpuCritTemp);
        Temp("GPU", s.GpuTemp, cfg.GpuWarnTemp, cfg.GpuCritTemp);

        if (s.RamLoad is >= 90) problems.Add(Loc.T("Memória RAM quase cheia ({0:0}%)", s.RamLoad));
        if (s.CpuLoad is >= 95) notes.Add(Loc.T("CPU com uso muito alto ({0:0}%)", s.CpuLoad));

        foreach (var d in s.Disks)
        {
            if (d.Health is "Atenção" or "Falha iminente")
                problems.Add(Loc.T("Disco {0}: saúde {1}", d.Name, Loc.T(d.Health)));
            if (d.LifePercent is < 10)
                problems.Add(Loc.T("Disco {0}: vida útil baixa ({1:0}%)", d.Name, d.LifePercent));
        }
        foreach (var v in s.Volumes)
            if (v.TotalGb > 0 && v.FreeGb / v.TotalGb < 0.1)
                problems.Add(Loc.T("Unidade {0} com pouco espaço livre ({1:0.#} GB)", v.Name, v.FreeGb));

        if (problems.Count == 0)
        {
            string ok = Loc.T("Seu computador está saudável. Nenhum problema encontrado.");
            if (notes.Count > 0) ok += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, notes.Select(n => "• " + n));
            return (true, ok);
        }
        problems.AddRange(notes);
        return (false, Loc.T("Foram encontrados pontos de atenção:") + Environment.NewLine + Environment.NewLine +
                       string.Join(Environment.NewLine, problems.Select(p => "• " + p)));
    }
}
