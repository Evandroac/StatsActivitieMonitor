using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace StatsMonitor.Diagnostics;

public static class ReportExporter
{
    private static string L(string pt, string en) => StatsMonitor.Helpers.Loc.Lang == "en" ? en : pt;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string ToJson(DiagnosticSession s) => JsonSerializer.Serialize(s, Json);

    private static string Csv(string v) => "\"" + (v ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

    public static string ToCsv(DiagnosticSession s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Component,Severity,Problem,DateTime,Evidence,ProbableCause,Recommendation,TestResult");
        foreach (var f in s.Findings)
            sb.AppendLine(string.Join(",", Csv(Comp.Title(f.Component, true)), Csv(f.Level.ToString()), Csv(f.Problem), Csv(f.Time.ToString("s", CultureInfo.InvariantCulture)), Csv(f.Evidence), Csv(f.Cause), Csv(f.Recommendation), Csv(f.TestResult)));
        return sb.ToString();
    }

    private static string Color(Severity s) => s switch { Severity.Normal => "#2ecc71", Severity.Info => "#3498db", Severity.Warning => "#f1c40f", Severity.Critical => "#e74c3c", _ => "#95a5a6" };

    public static string ToHtml(DiagnosticSession s)
    {
        string E(string? v) => WebUtility.HtmlEncode(v ?? "");
        bool en = StatsMonitor.Helpers.Loc.Lang == "en";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Stats Monitor</title><style>body{font-family:Segoe UI,Arial;margin:24px;background:#111;color:#eee}h1,h2{color:#fff}table{border-collapse:collapse;width:100%;margin:8px 0 24px}td,th{border:1px solid #333;padding:6px 8px;text-align:left;vertical-align:top}th{background:#222}.b{padding:2px 8px;border-radius:10px;color:#000;font-weight:600}</style></head><body>");
        sb.Append($"<h1>{E(L("Relatório de diagnóstico do computador", "Computer diagnostic report"))}</h1>");
        sb.Append($"<p>{E(s.Computer)} · {E(s.Model)} · {s.Started:g} · {E(L("Sessão", "Session"))} {E(s.Id)} · {(s.Full ? E(L("completo", "full")) : E(L("parcial", "partial")))} · {(s.Elevated ? "admin" : E(L("sem administrador", "not elevated")))}</p>");
        sb.Append($"<p>{E(L("Indicador geral (heurístico, não é métrica certificada)", "Overall indicator (heuristic, not a certified metric)"))}: <b>{(s.Score is { } sc ? sc + "/100" : E(L("indisponível", "unavailable")))}</b></p>");
        sb.Append($"<h2>{E(L("Saúde por componente", "Component health"))}</h2><table><tr><th>{E(L("Componente", "Component"))}</th><th>{E(L("Estado", "State"))}</th></tr>");
        foreach (var c in Comp.All) { var h = s.Health.GetValueOrDefault(c, Severity.Indeterminate); sb.Append($"<tr><td>{E(Comp.Title(c, en))}</td><td><span class=\"b\" style=\"background:{Color(h)}\">{E(SeverityInfo.Label(h, en))}</span></td></tr>"); }
        sb.Append("</table>");
        sb.Append($"<h2>{E(L("Achados", "Findings"))}</h2><table><tr><th>{E(L("Componente", "Component"))}</th><th>{E(L("Gravidade", "Severity"))}</th><th>{E(L("Problema", "Problem"))}</th><th>{E(L("Evidência", "Evidence"))}</th><th>{E(L("Causa provável", "Probable cause"))}</th><th>{E(L("Recomendação", "Recommendation"))}</th><th>{E(L("Data/hora", "Date/time"))}</th></tr>");
        foreach (var f in s.Findings)
            sb.Append($"<tr><td>{E(Comp.Title(f.Component, en))}</td><td><span class=\"b\" style=\"background:{Color(f.Level)}\">{E(SeverityInfo.Label(f.Level, en))}</span></td><td>{E(f.Problem)}</td><td>{E(f.Evidence)}</td><td>{E(f.Cause)}</td><td>{E(f.Recommendation)}</td><td>{f.Time:G}</td></tr>");
        sb.Append("</table>");
        if (s.Tests.Count > 0)
        {
            sb.Append($"<h2>{E(L("Testes de desempenho", "Performance tests"))}</h2><table><tr><th>{E(L("Teste", "Test"))}</th><th>{E(L("Resultado", "Result"))}</th><th>{E(L("Resumo", "Summary"))}</th></tr>");
            foreach (var t in s.Tests) sb.Append($"<tr><td>{E(t.Name)}</td><td>{E(SeverityInfo.Label(t.Result, en))}</td><td>{E(t.Summary)}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append($"<h2>{E(L("Inventário", "Inventory"))}</h2>");
        foreach (var (k, facts) in s.Inventory.Sections)
        {
            sb.Append($"<h3>{E(Comp.Title(k, en))}</h3><table>");
            foreach (var f in facts) sb.Append($"<tr><td>{E(f.Label)}</td><td>{E(f.Value)}</td></tr>");
            sb.Append("</table>");
        }
        if (s.Inventory.Errors.Count > 0)
        {
            sb.Append($"<h2>{E(L("Falhas de coleta", "Collection failures"))}</h2><ul>");
            foreach (var e in s.Inventory.Errors) sb.Append($"<li>{E(e)}</li>");
            sb.Append("</ul>");
        }
        sb.Append($"<p><i>{E(L("Somente leitura: nenhuma correção foi aplicada automaticamente. Dados marcados como indisponíveis não foram considerados saudáveis.", "Read-only: no fix was applied automatically. Data marked unavailable was not considered healthy."))}</i></p></body></html>");
        return sb.ToString();
    }

    public static IEnumerable<string> ToTextLines(DiagnosticSession s)
    {
        bool en = StatsMonitor.Helpers.Loc.Lang == "en";
        yield return L("RELATÓRIO DE DIAGNÓSTICO DO COMPUTADOR", "COMPUTER DIAGNOSTIC REPORT");
        yield return $"{s.Computer} - {s.Model}";
        yield return $"{s.Started:g} | {L("Sessão", "Session")} {s.Id} | {(s.Full ? L("completo", "full") : L("parcial", "partial"))}";
        yield return $"{L("Indicador geral (heurístico)", "Overall indicator (heuristic)")}: {(s.Score is { } sc ? sc + "/100" : L("indisponível", "unavailable"))}";
        yield return "";
        yield return L("SAÚDE POR COMPONENTE", "COMPONENT HEALTH");
        foreach (var c in Comp.All) yield return $"  {Comp.Title(c, en)}: {SeverityInfo.Label(s.Health.GetValueOrDefault(c, Severity.Indeterminate), en)}";
        yield return "";
        yield return L("ACHADOS", "FINDINGS");
        foreach (var f in s.Findings.Where(x => x.Level != Severity.Normal))
        {
            yield return $"[{SeverityInfo.Label(f.Level, en)}] {Comp.Title(f.Component, en)}: {f.Problem}";
            if (f.Evidence.Length > 0) yield return $"    {L("Evidência", "Evidence")}: {f.Evidence}";
            if (f.Cause.Length > 0) yield return $"    {L("Causa provável", "Probable cause")}: {f.Cause}";
            if (f.Recommendation.Length > 0) yield return $"    {L("Recomendação", "Recommendation")}: {f.Recommendation}";
        }
        yield return "";
        yield return L("TESTES", "TESTS");
        foreach (var t in s.Tests) yield return $"  {t.Name}: {SeverityInfo.Label(t.Result, en)} - {t.Summary}";
        yield return "";
        yield return L("INVENTÁRIO", "INVENTORY");
        foreach (var (k, facts) in s.Inventory.Sections)
        {
            yield return $"  {Comp.Title(k, en)}";
            foreach (var f in facts) yield return $"    {f.Label}: {f.Value}";
        }
    }

    /// <summary>Minimal text-only PDF (Helvetica, WinAnsi). No images or fonts are embedded.</summary>
    public static byte[] ToPdf(DiagnosticSession s)
    {
        var lines = new List<string>();
        foreach (var l in ToTextLines(s))
        {
            var t = l.Replace("\t", "  ");
            while (t.Length > 95) { int cut = t.LastIndexOf(' ', 95); if (cut < 40) cut = 95; lines.Add(t[..cut]); t = "      " + t[cut..].TrimStart(); }
            lines.Add(t);
        }
        const int perPage = 56;
        var pages = new List<List<string>>();
        for (int i = 0; i < lines.Count; i += perPage) pages.Add(lines.Skip(i).Take(perPage).ToList());
        if (pages.Count == 0) pages.Add([]);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
        var objs = new List<byte[]>();
        int n = pages.Count;
        // 1 catalog, 2 pages, 3 font, then (page, content) pairs
        string kids = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{4 + i * 2} 0 R"));
        objs.Add(enc.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objs.Add(enc.GetBytes($"<< /Type /Pages /Kids [{kids}] /Count {n} >>"));
        objs.Add(enc.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        for (int i = 0; i < n; i++)
        {
            var cs = new StringBuilder("BT /F1 9 Tf 12 TL 40 800 Td\n");
            foreach (var l in pages[i]) cs.Append('(').Append(l.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj T*\n");
            cs.Append($"ET\nBT /F1 8 Tf 280 20 Td (- {i + 1} / {n} -) Tj ET");
            var body = enc.GetBytes(cs.ToString());
            objs.Add(enc.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + i * 2} 0 R >>"));
            var head = enc.GetBytes($"<< /Length {body.Length} >>\nstream\n");
            var tail = enc.GetBytes("\nendstream");
            objs.Add(head.Concat(body).Concat(tail).ToArray());
        }
        using var ms = new MemoryStream();
        void W(byte[] b) => ms.Write(b);
        W(enc.GetBytes("%PDF-1.4\n"));
        var offsets = new List<long>();
        for (int i = 0; i < objs.Count; i++)
        {
            offsets.Add(ms.Position);
            W(enc.GetBytes($"{i + 1} 0 obj\n")); W(objs[i]); W(enc.GetBytes("\nendobj\n"));
        }
        long xref = ms.Position;
        W(enc.GetBytes($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n"));
        foreach (var o in offsets) W(enc.GetBytes($"{o:D10} 00000 n \n"));
        W(enc.GetBytes($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"));
        return ms.ToArray();
    }

    public static void Export(DiagnosticSession s, string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".json": File.WriteAllText(path, ToJson(s), new UTF8Encoding(false)); break;
            case ".csv": File.WriteAllText(path, ToCsv(s), new UTF8Encoding(true)); break;
            case ".html": File.WriteAllText(path, ToHtml(s), new UTF8Encoding(false)); break;
            case ".pdf": File.WriteAllBytes(path, ToPdf(s)); break;
            default: throw new NotSupportedException(Path.GetExtension(path));
        }
    }
}
