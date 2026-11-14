using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StatsMonitor.Controls;
using StatsMonitor.Helpers;
using StatsMonitor.Services;

namespace StatsMonitor.Views;

/// <summary>Network toolbox for advanced users: diagnostics, ping, traceroute, DNS, port check, HTTP check, speed test and adapter details.</summary>
public sealed class NetworkToolsView : UserControl
{
    public static readonly string[] Keywords =
    [
        "Testes de conexão", "Diagnóstico de rede", "Ping", "Traceroute", "Rastrear rota", "Consulta DNS", "Verificar portas", "Porta TCP",
        "Teste HTTP", "Certificado SSL", "Teste de velocidade", "Download", "Upload", "Latência", "Jitter", "Perda de pacotes",
        "Adaptadores de rede", "IP público", "Gateway", "Limpar cache DNS", "Portal cativo",
    ];

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;

    private sealed class Runner { public CancellationTokenSource? Cts; }

    private readonly List<Runner> _runners = [];
    private readonly Dictionary<int, UIElement> _tabs = [];
    private readonly List<Button> _tabButtons = [];
    private readonly Border _body = new();
    private int _tab;

    private static readonly Brush Good = Frozen(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly Brush Warn = Frozen(Color.FromRgb(0xF1, 0xC4, 0x0F));
    private static readonly Brush Bad = Frozen(Color.FromRgb(0xE7, 0x4C, 0x3C));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public NetworkToolsView()
    {
        Build();
        Loc.LanguageChanged += OnLanguage;
        Unloaded += (_, _) => { Loc.LanguageChanged -= OnLanguage; CancelAll(); };
    }

    private void OnLanguage() { CancelAll(); Build(); }

    private void CancelAll()
    {
        foreach (var r in _runners) r.Cts?.Cancel();
        _runners.Clear();
    }

    private void Build()
    {
        _tabs.Clear();
        _tabButtons.Clear();
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock { Text = "🌐  " + L("Testes de conexão", "Connection tests"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        head.Children.Add(Caption(L("Ferramentas de rede para diagnosticar problemas: execute, compare e copie os resultados.",
                                    "Network tools to troubleshoot problems: run, compare and copy the results.")));
        root.Children.Add(head);

        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        string[] names =
        [
            "🩺  " + L("Diagnóstico", "Diagnostics"), "📡  Ping", "🧭  Traceroute", "🔎  DNS", "🔌  " + L("Portas", "Ports"),
            "🔗  HTTP/TLS", "🚀  " + L("Velocidade", "Speed"), "🖧  " + L("Adaptadores", "Adapters"),
        ];
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = names[i], Style = (Style)FindResourceOrNull("PillButton")!, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => Select(idx);
            _tabButtons.Add(b);
            tabs.Children.Add(b);
        }
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;
        Select(_tab);
    }

    private object? FindResourceOrNull(string key) => TryFindResource(key) ?? Application.Current.TryFindResource(key);

    private void Select(int i)
    {
        _tab = i;
        if (!_tabs.TryGetValue(i, out var content))
        {
            content = i switch { 0 => DiagnosticsTab(), 1 => PingTab(), 2 => TraceTab(), 3 => DnsTab(), 4 => PortsTab(), 5 => HttpTab(), 6 => SpeedTab(), _ => AdaptersTab() };
            _tabs[i] = content;
        }
        _body.Child = content;
        for (int k = 0; k < _tabButtons.Count; k++)
        {
            _tabButtons[k].FontWeight = k == i ? FontWeights.Bold : FontWeights.Normal;
            _tabButtons[k].SetResourceReference(BackgroundProperty, k == i ? "TrackBrush" : "InputBg");
        }
    }

    // ---------- helpers ----------

    private static TextBlock Caption(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 0) };

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 2) };

    private static TextBox Field(double width, string text) => new() { Width = width, Text = text, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 4, 6, 4) };

    private static StackPanel Labeled(string label, UIElement el, double right = 10)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, right, 8) };
        p.Children.Add(Label(label));
        p.Children.Add(el);
        return p;
    }

    private static ComboBox Combo(double width, string[] items, int selected)
    {
        var c = new ComboBox { Width = width, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var i in items) c.Items.Add(i);
        c.SelectedIndex = selected;
        return c;
    }

    private Button PillBtn(string text) => new() { Content = text, Style = (Style)FindResourceOrNull("PillButton")!, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 7, 14, 7) };

    private TextBox Console(double minHeight = 300)
    {
        var tb = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontSize = 12,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = minHeight, MaxHeight = 520,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8),
        };
        return tb;
    }

    private static void Append(TextBox tb, string line)
    {
        tb.AppendText(line + "\n");
        if (tb.Text.Length > 200_000) tb.Text = tb.Text[^100_000..];
        tb.ScrollToEnd();
    }

    private Border Card(UIElement child)
    {
        var b = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left, Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "CardBg");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        return b;
    }

    private Button CopyButton(Func<string> text)
    {
        var b = PillBtn("📋  " + L("Copiar resultado", "Copy result"));
        b.Click += async (_, _) =>
        {
            try { Clipboard.SetText(text()); } catch { return; }
            var old = b.Content;
            b.Content = "✔  " + L("Copiado", "Copied");
            await Task.Delay(1500);
            b.Content = old;
        };
        return b;
    }

    private Button ClearButton(TextBox log)
    {
        var b = PillBtn("🧹  " + L("Limpar", "Clear"));
        b.Click += (_, _) => log.Clear();
        return b;
    }

    /// <summary>Turns a button into a Run/Stop toggle that runs <paramref name="work"/> with a cancellation token.</summary>
    private void Wire(Button btn, string run, Func<CancellationToken, Task> work, TextBox? log)
    {
        var r = new Runner();
        _runners.Add(r);
        btn.Content = "▶  " + run;
        btn.Click += async (_, _) =>
        {
            if (r.Cts != null) { r.Cts.Cancel(); return; }
            r.Cts = new CancellationTokenSource();
            btn.Content = "⏹  " + L("Parar", "Stop");
            try { await work(r.Cts.Token); }
            catch (OperationCanceledException) { if (log != null) Append(log, "— " + L("interrompido", "stopped")); }
            catch (Exception ex) { if (log != null) Append(log, "✖ " + ex.Message); }
            finally { r.Cts.Dispose(); r.Cts = null; btn.Content = "▶  " + run; }
        };
    }

    private static StackPanel Page(string description)
    {
        var p = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.85, Margin = new Thickness(0, 0, 0, 10) });
        return p;
    }

    private static string Gateway() => NetworkToolsService.GetAdapters().SelectMany(a => a.Gateways).FirstOrDefault(g => IPAddress.TryParse(g, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? "";

    private static string Ms(double v) => v.ToString(v < 10 ? "0.0" : "0");

    // ---------- Diagnostics ----------

    private UIElement DiagnosticsTab()
    {
        var page = Page(L("Verifica, em sequência, do adaptador local até a internet: gateway, ICMP, DNS, acesso web (portal cativo) e IP público. O resultado aponta onde está o problema.",
                          "Checks, in order, from the local adapter to the internet: gateway, ICMP, DNS, web access (captive portal) and public IP. The result points to where the problem is."));
        var run = PillBtn("");
        var rows = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var verdict = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var steps = new List<DiagStep>();
        var order = new List<string>();
        var rowUi = new Dictionary<string, (TextBlock icon, TextBlock detail, TextBlock ms)>();

        string Icon(DiagState s) => s switch { DiagState.Running => "⏳", DiagState.Ok => "✅", DiagState.Warn => "⚠️", DiagState.Fail => "❌", _ => "⏭" };

        void Show(DiagStep s)
        {
            if (!rowUi.TryGetValue(s.Name, out var ui))
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                var icon = new TextBlock { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16 };
                var name = new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
                var ms = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cascadia Mono,Consolas") };
                Grid.SetColumn(name, 1); Grid.SetColumn(detail, 2); Grid.SetColumn(ms, 3);
                g.Children.Add(icon); g.Children.Add(name); g.Children.Add(detail); g.Children.Add(ms);
                rows.Children.Add(g);
                ui = (icon, detail, ms);
                rowUi[s.Name] = ui;
                order.Add(s.Name);
            }
            ui.icon.Text = Icon(s.State);
            ui.detail.Text = s.Detail;
            ui.ms.Text = s.Ms.HasValue ? Ms(s.Ms.Value) + " ms" : "";
            int i = steps.FindIndex(x => x.Name == s.Name);
            if (i >= 0) steps[i] = s; else steps.Add(s);
        }

        string Verdict()
        {
            DiagState S(int i) => i < steps.Count ? steps[i].State : DiagState.Skip;
            if (S(0) == DiagState.Fail) return "❌ " + L("Sem conexão local: verifique cabo, Wi-Fi ou o adaptador.", "No local connection: check the cable, Wi-Fi or the adapter.");
            if (S(1) == DiagState.Fail) return "❌ " + L("O roteador não responde: problema entre este PC e o gateway (cabo, Wi-Fi, firewall, IP).", "The router does not answer: problem between this PC and the gateway (cable, Wi-Fi, firewall, IP).");
            if (S(2) == DiagState.Fail) return "❌ " + L("O roteador responde, mas não há acesso à internet: verifique modem/provedor.", "The router answers but there is no internet access: check the modem/ISP.");
            if (S(3) == DiagState.Fail) return "❌ " + L("A internet funciona por IP, mas o DNS falha: tente outro servidor DNS (1.1.1.1 ou 8.8.8.8).", "Internet works by IP but DNS fails: try another DNS server (1.1.1.1 or 8.8.8.8).");
            if (S(4) == DiagState.Warn) return "⚠️ " + L("Possível portal cativo ou proxy interceptando o tráfego web.", "Possible captive portal or proxy intercepting web traffic.");
            if (steps.Any(s => s.State == DiagState.Warn)) return "⚠️ " + L("Conectado, mas com pontos de atenção (latência, perda ou DNS lento).", "Connected, but with warnings (latency, loss or slow DNS).");
            return "✅ " + L("Tudo funcionando normalmente.", "Everything is working normally.");
        }

        Wire(run, L("Executar diagnóstico", "Run diagnostics"), async ct =>
        {
            rows.Children.Clear(); rowUi.Clear(); steps.Clear(); order.Clear(); verdict.Text = "";
            await NetworkToolsService.RunDiagnosticsAsync(Loc.Lang == "en", Show, ct);
            verdict.Text = Verdict();
        }, null);

        var copy = CopyButton(() => string.Join("\n", steps.Select(s => $"[{s.State}] {s.Name}: {s.Detail}")) + "\n" + verdict.Text);
        var bar = new WrapPanel();
        bar.Children.Add(run); bar.Children.Add(copy);
        page.Children.Add(bar);
        page.Children.Add(Card(new StackPanel { Children = { rows, verdict } }));
        return page;
    }

    // ---------- Ping ----------

    private UIElement PingTab()
    {
        var page = Page(L("Envia pacotes ICMP e mostra latência, perda, jitter e desvio padrão em tempo real. Use contagem infinita para monitorar a estabilidade da conexão.",
                          "Sends ICMP packets and shows latency, loss, jitter and standard deviation in real time. Use infinite count to monitor connection stability."));
        var host = Field(220, "1.1.1.1");
        var count = Combo(90, ["∞", "10", "50", "100", "500"], 0);
        var size = Field(60, "32");
        var interval = Field(70, "1000");
        var timeout = Field(70, "2000");
        var df = new CheckBox { Content = L("Não fragmentar (DF)", "Don't fragment (DF)"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 14, 10, 0) };
        var run = PillBtn("");
        var log = Console(220);
        var stats = new TextBlock { FontFamily = new FontFamily("Cascadia Mono,Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) };
        var values = new ObservableCollection<double>();
        var chart = new Sparkline { Height = 90, Unit = "ms", Maximum = 50, Values = values, Margin = new Thickness(0, 0, 0, 8) };
        chart.SetResourceReference(Sparkline.StrokeProperty, "Accent");

        var presets = new WrapPanel();
        foreach (var (name, addr) in new[] { ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8"), ("Quad9", "9.9.9.9"), ("Gateway", "") })
        {
            var b = PillBtn(name);
            b.Padding = new Thickness(10, 4, 10, 4);
            b.Click += (_, _) => { var t = addr.Length > 0 ? addr : Gateway(); if (t.Length > 0) host.Text = t; };
            presets.Children.Add(b);
        }

        var inputs = new WrapPanel();
        inputs.Children.Add(Labeled(L("Destino (host ou IP)", "Target (host or IP)"), host));
        inputs.Children.Add(Labeled(L("Quantidade", "Count"), count));
        inputs.Children.Add(Labeled(L("Tamanho (bytes)", "Size (bytes)"), size));
        inputs.Children.Add(Labeled(L("Intervalo (ms)", "Interval (ms)"), interval));
        inputs.Children.Add(Labeled(L("Timeout (ms)", "Timeout (ms)"), timeout));
        inputs.Children.Add(df);

        Wire(run, L("Iniciar ping", "Start ping"), async ct =>
        {
            log.Clear(); values.Clear();
            var ip = await NetworkToolsService.ResolveAsync(host.Text, ct);
            if (ip == null) { Append(log, "✖ " + L("Não foi possível resolver o destino.", "Could not resolve the target.")); return; }
            int.TryParse(size.Text, out int sz); int.TryParse(interval.Text, out int iv); int.TryParse(timeout.Text, out int to);
            int n = count.SelectedIndex switch { 1 => 10, 2 => 50, 3 => 100, 4 => 500, _ => 0 };
            iv = Math.Clamp(iv, 50, 60000); to = Math.Clamp(to, 100, 30000); sz = Math.Clamp(sz, 0, 65500);
            Append(log, L($"Ping em {host.Text.Trim()} [{ip}] com {sz} bytes:", $"Pinging {host.Text.Trim()} [{ip}] with {sz} bytes:"));
            var st = new PingStats();
            try
            {
                await NetworkToolsService.PingAsync(ip, n, to, sz, iv, df.IsChecked == true, r =>
                {
                    st.Add(r.Ms);
                    Append(log, r.Ms.HasValue
                        ? $"#{r.Seq,-4} {r.Address,-15} {L("tempo", "time")}={r.Ms}ms TTL={r.Ttl}"
                        : $"#{r.Seq,-4} {(r.Status == IPStatus.TimedOut ? L("Tempo esgotado", "Request timed out") : r.Status.ToString())}");
                    values.Add(r.Ms ?? Math.Max(st.Max, to));
                    while (values.Count > 120) values.RemoveAt(0);
                    chart.Maximum = Math.Max(10, Math.Ceiling(values.Max() * 1.2 / 10) * 10);
                    stats.Text = L($"Enviados {st.Sent} · Recebidos {st.Received} · Perda {st.Loss:0.#}%  |  Mín {Ms(st.Min)} · Méd {Ms(st.Avg)} · Máx {Ms(st.Max)} ms  |  Jitter {st.Jitter:0.0} ms · σ {st.StdDev:0.0}",
                                   $"Sent {st.Sent} · Received {st.Received} · Loss {st.Loss:0.#}%  |  Min {Ms(st.Min)} · Avg {Ms(st.Avg)} · Max {Ms(st.Max)} ms  |  Jitter {st.Jitter:0.0} ms · σ {st.StdDev:0.0}");
                    stats.Foreground = st.Loss > 5 ? Bad : st.Loss > 0 || st.Avg > 100 ? Warn : Good;
                }, ct);
            }
            finally
            {
                if (st.Sent > 0) Append(log, $"— {stats.Text}");
            }
        }, log);

        var bar = new WrapPanel();
        bar.Children.Add(run); bar.Children.Add(CopyButton(() => log.Text)); bar.Children.Add(ClearButton(log));
        page.Children.Add(Card(new StackPanel { Children = { inputs, presets, bar, stats, chart, log } }));
        return page;
    }

    // ---------- Traceroute ----------

    private UIElement TraceTab()
    {
        var page = Page(L("Mostra cada salto (roteador) entre este PC e o destino, com a latência de cada um. Ajuda a achar onde a rota fica lenta ou perde pacotes. Saltos que não respondem (*) são comuns: muitos roteadores bloqueiam ICMP.",
                          "Shows each hop (router) between this PC and the target with its latency. Helps find where the path gets slow or drops packets. Non-answering hops (*) are common: many routers block ICMP."));
        var host = Field(240, "www.google.com");
        var hops = Field(60, "30");
        var timeout = Field(70, "2000");
        var names = new CheckBox { Content = L("Resolver nomes (DNS reverso)", "Resolve names (reverse DNS)"), IsChecked = true, Margin = new Thickness(0, 14, 10, 0) };
        var run = PillBtn("");
        var log = Console();
        var inputs = new WrapPanel();
        inputs.Children.Add(Labeled(L("Destino (host ou IP)", "Target (host or IP)"), host));
        inputs.Children.Add(Labeled(L("Máx. de saltos", "Max hops"), hops));
        inputs.Children.Add(Labeled(L("Timeout (ms)", "Timeout (ms)"), timeout));
        inputs.Children.Add(names);

        Wire(run, L("Rastrear rota", "Trace route"), async ct =>
        {
            log.Clear();
            var ip = await NetworkToolsService.ResolveAsync(host.Text, ct);
            if (ip == null) { Append(log, "✖ " + L("Não foi possível resolver o destino.", "Could not resolve the target.")); return; }
            int.TryParse(hops.Text, out int mh); int.TryParse(timeout.Text, out int to);
            Append(log, L($"Rota para {host.Text.Trim()} [{ip}] (máx. {Math.Clamp(mh, 1, 64)} saltos):", $"Route to {host.Text.Trim()} [{ip}] (max {Math.Clamp(mh, 1, 64)} hops):"));
            Append(log, $"{"#",-3} {L("Mín", "Min"),7} {L("Méd", "Avg"),7} {L("Máx", "Max"),7}  {L("Perda", "Loss"),5}  {L("Endereço", "Address")}");
            await NetworkToolsService.TraceAsync(ip, Math.Clamp(mh, 1, 64), Math.Clamp(to, 200, 10000), 3, names.IsChecked == true, h =>
            {
                if (h.Address == null) Append(log, $"{h.Ttl,-3} {"*",7} {"*",7} {"*",7}  {"100%",5}  {L("Tempo esgotado", "Request timed out")}");
                else Append(log, $"{h.Ttl,-3} {Ms(h.MinMs!.Value),7} {Ms(h.AvgMs!.Value),7} {Ms(h.MaxMs!.Value),7}  {(h.Lost * 100 / h.Probes) + "%",5}  {h.Address}{(h.Host != null ? "  (" + h.Host + ")" : "")}");
                if (h.Reached) Append(log, "✔ " + L("Destino alcançado.", "Destination reached."));
            }, ct);
        }, log);

        var bar = new WrapPanel();
        bar.Children.Add(run); bar.Children.Add(CopyButton(() => log.Text)); bar.Children.Add(ClearButton(log));
        page.Children.Add(Card(new StackPanel { Children = { inputs, bar, log } }));
        return page;
    }

    // ---------- DNS ----------

    private static readonly Dictionary<string, string> KnownDns = new()
    {
        ["1.1.1.1"] = "Cloudflare", ["1.0.0.1"] = "Cloudflare", ["8.8.8.8"] = "Google", ["8.8.4.4"] = "Google", ["9.9.9.9"] = "Quad9", ["208.67.222.222"] = "OpenDNS",
    };

    private UIElement DnsTab()
    {
        var page = Page(L("Consulta registros DNS diretamente em cada servidor e compara tempo de resposta e resultados. \"sistema\" usa os servidores DNS configurados nos adaptadores. Útil para detectar DNS lento, bloqueado ou com respostas diferentes.",
                          "Queries DNS records directly on each server and compares response time and results. \"system\" uses the DNS servers configured on your adapters. Useful to spot slow, blocked or inconsistent DNS."));
        var name = Field(240, "www.google.com");
        var type = Combo(90, NetworkToolsService.DnsTypeNames, 0);
        var servers = Field(380, "sistema, 1.1.1.1, 8.8.8.8, 9.9.9.9");
        var timeout = Field(70, "2000");
        var run = PillBtn("");
        var flush = PillBtn("🧽  " + L("Limpar cache DNS", "Flush DNS cache"));
        var log = Console();
        var inputs = new WrapPanel();
        inputs.Children.Add(Labeled(L("Nome ou IP (PTR)", "Name or IP (PTR)"), name));
        inputs.Children.Add(Labeled(L("Tipo", "Type"), type));
        inputs.Children.Add(Labeled(L("Servidores (separados por vírgula)", "Servers (comma separated)"), servers));
        inputs.Children.Add(Labeled(L("Timeout (ms)", "Timeout (ms)"), timeout));

        flush.Click += async (_, _) =>
        {
            try { await NetworkToolsService.FlushDnsAsync(); Append(log, "✔ " + L("Cache DNS do Windows limpo.", "Windows DNS cache flushed.")); }
            catch (Exception ex) { Append(log, "✖ " + ex.Message); }
        };

        Wire(run, L("Consultar", "Query"), async ct =>
        {
            log.Clear();
            int.TryParse(timeout.Text, out int to); to = Math.Clamp(to, 200, 15000);
            var list = new List<IPAddress>();
            foreach (var tok in servers.Text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (tok.Equals("sistema", StringComparison.OrdinalIgnoreCase) || tok.Equals("system", StringComparison.OrdinalIgnoreCase))
                    list.AddRange(NetworkToolsService.GetAdapters().SelectMany(a => a.Dns).Select(IPAddress.Parse).Where(d => !d.IsIPv6LinkLocal && !d.IsIPv6SiteLocal));
                else if (IPAddress.TryParse(tok, out var ip)) list.Add(ip);
                else Append(log, "⚠ " + L($"Servidor ignorado (IP inválido): {tok}", $"Ignored server (invalid IP): {tok}"));
            }
            list = [.. list.DistinctBy(x => x.ToString())];
            if (list.Count == 0) { Append(log, "✖ " + L("Nenhum servidor DNS válido.", "No valid DNS server.")); return; }
            Append(log, L($"Consultando {type.SelectedItem} de {name.Text.Trim()} em {list.Count} servidor(es)...", $"Querying {type.SelectedItem} for {name.Text.Trim()} on {list.Count} server(s)..."));
            var results = await Task.WhenAll(list.Select(s => NetworkToolsService.DnsQueryAsync(s, name.Text, (string)type.SelectedItem, to, ct)));
            foreach (var r in results)
            {
                string label = KnownDns.TryGetValue(r.Server, out var n) ? $" ({n})" : "";
                Append(log, "");
                Append(log, $"▸ {r.Server}{label}   {r.Rcode}   {Ms(r.Ms)} ms{(r.Truncated ? "   [TC]" : "")}");
                if (!r.Ok) Append(log, "    ✖ " + (r.Error ?? ""));
                else if (r.Answers.Count == 0) Append(log, "    " + L("(sem registros)", "(no records)"));
                foreach (var a in r.Answers) Append(log, $"    {a.Type,-6} {a.Name,-32} {a.Data}   TTL {a.Ttl}");
            }
            var ok = results.Where(r => r.Ok && r.Rcode == "NOERROR").OrderBy(r => r.Ms).ToList();
            Append(log, "");
            if (ok.Count > 0) Append(log, "🏁 " + L($"Mais rápido: {ok[0].Server} ({Ms(ok[0].Ms)} ms)", $"Fastest: {ok[0].Server} ({Ms(ok[0].Ms)} ms)"));
            var distinct = ok.Select(r => string.Join(",", r.Answers.Select(a => a.Data).OrderBy(x => x))).Distinct().Count();
            if (distinct > 1) Append(log, "⚠ " + L("Os servidores retornaram respostas diferentes (normal em CDNs; suspeito se for um site interno).", "Servers returned different answers (normal for CDNs; suspicious for an internal site)."));
        }, log);

        var bar = new WrapPanel();
        bar.Children.Add(run); bar.Children.Add(flush); bar.Children.Add(CopyButton(() => log.Text)); bar.Children.Add(ClearButton(log));
        page.Children.Add(Card(new StackPanel { Children = { inputs, bar, log } }));
        return page;
    }

    // ---------- Ports ----------

    private UIElement PortsTab()
    {
        var page = Page(L("Testa se portas TCP estão abertas em um host (conexão completa). Aceita lista e intervalos: 80,443,8000-8010. \"Recusada\" indica porta fechada; \"timeout\" geralmente indica firewall filtrando.",
                          "Tests whether TCP ports are open on a host (full connect). Accepts lists and ranges: 80,443,8000-8010. \"Refused\" means closed; \"timeout\" usually means a firewall is filtering."));
        var host = Field(240, "localhost");
        var ports = Field(320, "80,443");
        var timeout = Field(70, "1500");
        var run = PillBtn("");
        var common = PillBtn(L("Portas comuns", "Common ports"));
        common.Click += (_, _) => ports.Text = NetworkToolsService.CommonPorts;
        var log = Console();
        var inputs = new WrapPanel();
        inputs.Children.Add(Labeled(L("Host ou IP", "Host or IP"), host));
        inputs.Children.Add(Labeled(L("Portas", "Ports"), ports));
        inputs.Children.Add(Labeled(L("Timeout (ms)", "Timeout (ms)"), timeout));

        Wire(run, L("Verificar", "Check"), async ct =>
        {
            log.Clear();
            var list = NetworkToolsService.ParsePorts(ports.Text);
            if (list.Count == 0) { Append(log, "✖ " + L("Informe ao menos uma porta válida (1-65535).", "Enter at least one valid port (1-65535).")); return; }
            var ip = await NetworkToolsService.ResolveAsync(host.Text, ct);
            if (ip == null) { Append(log, "✖ " + L("Não foi possível resolver o host.", "Could not resolve the host.")); return; }
            int.TryParse(timeout.Text, out int to); to = Math.Clamp(to, 100, 15000);
            Append(log, L($"Verificando {list.Count} porta(s) em {host.Text.Trim()} [{ip}]...", $"Checking {list.Count} port(s) on {host.Text.Trim()} [{ip}]..."));
            using var gate = new SemaphoreSlim(48);
            var tasks = list.Select(async p =>
            {
                await gate.WaitAsync(ct);
                try { return await NetworkToolsService.CheckPortAsync(ip, p, to, ct); }
                finally { gate.Release(); }
            }).ToList();
            var results = await Task.WhenAll(tasks);
            foreach (var r in results.OrderBy(r => r.Port))
            {
                if (!r.Open && list.Count > 64) continue;
                string svc = NetworkToolsService.PortName(r.Port);
                Append(log, r.Open ? $"✅ {r.Port,-6} {L("ABERTA", "OPEN"),-8} {Ms(r.Ms),6} ms  {svc}" : $"⛔ {r.Port,-6} {L("fechada", "closed"),-8} {"",6}     {svc}  {r.Error}");
            }
            Append(log, "— " + L($"{results.Count(r => r.Open)} aberta(s) de {results.Length}", $"{results.Count(r => r.Open)} open of {results.Length}"));
        }, log);

        var bar = new WrapPanel();
        bar.Children.Add(run); bar.Children.Add(common); bar.Children.Add(CopyButton(() => log.Text)); bar.Children.Add(ClearButton(log));
        page.Children.Add(Card(new StackPanel { Children = { inputs, bar, log } }));
        return page;
    }

    // ---------- HTTP ----------

    private UIElement HttpTab()
    {
        var page = Page(L("Faz uma requisição GET e mostra o tempo de DNS, conexão TCP e resposta total, o código HTTP, versão do protocolo, servidor e a validade do certificado TLS.",
                          "Makes a GET request and shows DNS, TCP connect and total response time, HTTP status, protocol version, server and TLS certificate validity."));
        var url = Field(420, "https://www.google.com");
        var run = PillBtn("");
        var log = Console(240);
        Wire(run, L("Testar", "Test"), async ct =>
        {
            log.Clear();
            Append(log, L($"Requisitando {url.Text.Trim()}...", $"Requesting {url.Text.Trim()}..."));
            var r = await NetworkToolsService.HttpCheckAsync(url.Text, ct);
            Append(log, "");
            Append(log, $"{(r.Ok ? "✅" : "❌")} {(r.Status > 0 ? $"HTTP {r.Status} {r.Reason}" : L("Sem resposta", "No response"))}");
            if (r.Error != null) Append(log, $"   {L("Erro", "Error")}: {r.Error}");
            if (r.Address != null) Append(log, $"   {L("Endereço", "Address")}: {r.Address}");
            Append(log, $"   DNS: {Ms(r.DnsMs)} ms · TCP: {Ms(r.TcpMs)} ms · {L("Total (com nova conexão e TLS)", "Total (with new connection and TLS)")}: {Ms(r.TotalMs)} ms");
            if (r.Version.Length > 0) Append(log, $"   {L("Protocolo", "Protocol")}: {r.Version}   {L("Servidor", "Server")}: {r.Server ?? "—"}");
            if (r.FinalUrl.Length > 0 && r.Status > 0) Append(log, $"   URL {L("final", "final")}: {r.FinalUrl}");
            if (r.CertExpiry.HasValue)
            {
                int days = (int)(r.CertExpiry.Value - DateTime.Now).TotalDays;
                Append(log, $"   {(days < 0 || r.CertIssue != null ? "⚠" : "🔒")} {L("Certificado", "Certificate")}: {r.CertSubject} · {L("expira em", "expires")} {r.CertExpiry:dd/MM/yyyy} ({(days < 0 ? L("EXPIRADO", "EXPIRED") : L($"{days} dias", $"{days} days"))})");
                if (r.CertIssue != null) Append(log, $"   ⚠ {L("Problemas de validação", "Validation issues")}: {r.CertIssue}");
            }
        }, log);
        var bar = new WrapPanel();
        bar.Children.Add(run); bar.Children.Add(CopyButton(() => log.Text)); bar.Children.Add(ClearButton(log));
        page.Children.Add(Card(new StackPanel { Children = { Labeled("URL", url), bar, log } }));
        return page;
    }

    // ---------- Speed ----------

    private UIElement SpeedTab()
    {
        var page = Page(L("Mede latência, jitter, download e upload usando os servidores da Cloudflare (speed.cloudflare.com). O teste CONSOME dados: em conexões rápidas pode usar centenas de MB. Feche outros downloads para um resultado fiel.",
                          "Measures latency, jitter, download and upload using Cloudflare servers (speed.cloudflare.com). The test CONSUMES data: on fast links it can use hundreds of MB. Close other downloads for an accurate result."));
        var duration = Combo(90, ["5 s", "8 s", "12 s"], 1);
        var run = PillBtn("");
        var status = new TextBlock { Margin = new Thickness(0, 4, 0, 6), Opacity = 0.85 };
        var tiles = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        TextBlock Tile(string title, out TextBlock value)
        {
            value = new TextBlock { Text = "—", FontSize = 26, FontWeight = FontWeights.SemiBold };
            var t = new TextBlock { Text = title, Opacity = 0.7, FontSize = 12 };
            var panel = new StackPanel { Margin = new Thickness(0, 0, 36, 6), Children = { t, value } };
            tiles.Children.Add(panel);
            return t;
        }
        _ = Tile("⬇ Download (Mbps)", out var dl);
        _ = Tile("⬆ Upload (Mbps)", out var ul);
        _ = Tile(L("⏱ Latência (ms)", "⏱ Latency (ms)"), out var lat);
        _ = Tile("〰 Jitter (ms)", out var jit);
        var values = new ObservableCollection<double>();
        var chart = new Sparkline { Height = 110, Unit = "Mbps", Maximum = 100, Values = values, Margin = new Thickness(0, 0, 0, 8) };
        chart.SetResourceReference(Sparkline.StrokeProperty, "Accent");
        var history = Console(90);

        void Live(double mbps)
        {
            values.Add(mbps);
            while (values.Count > 100) values.RemoveAt(0);
            chart.Maximum = Math.Max(10, Math.Ceiling(values.Max() * 1.15 / 10) * 10);
        }

        Wire(run, L("Iniciar teste", "Start test"), async ct =>
        {
            int secs = duration.SelectedIndex switch { 0 => 5, 2 => 12, _ => 8 };
            dl.Text = ul.Text = lat.Text = jit.Text = "—";
            values.Clear();
            try
            {
                status.Text = L("Medindo latência…", "Measuring latency…");
                var st = new PingStats();
                await NetworkToolsService.PingAsync(IPAddress.Parse("1.1.1.1"), 10, 1500, 32, 100, false, r => st.Add(r.Ms), ct);
                lat.Text = st.Received > 0 ? Ms(st.Avg) : "—";
                jit.Text = st.Received > 1 ? st.Jitter.ToString("0.0") : "—";

                status.Text = L("Medindo download…", "Measuring download…");
                double down = await NetworkToolsService.SpeedAsync(false, secs, 4, Live, ct);
                dl.Text = down.ToString("0.0");
                values.Clear();

                status.Text = L("Medindo upload…", "Measuring upload…");
                double up = await NetworkToolsService.SpeedAsync(true, secs, 3, Live, ct);
                ul.Text = up.ToString("0.0");

                status.Text = "✔ " + L("Concluído.", "Done.");
                Append(history, $"{DateTime.Now:dd/MM HH:mm}  ⬇ {down:0.0} Mbps   ⬆ {up:0.0} Mbps   ⏱ {lat.Text} ms   〰 {jit.Text} ms");
            }
            catch (OperationCanceledException) { status.Text = L("Interrompido.", "Stopped."); throw; }
            catch (Exception ex)
            {
                status.Text = "✖ " + L("Falha no teste (sem internet ou servidor bloqueado): ", "Test failed (no internet or server blocked): ") + ex.Message;
            }
        }, null);

        var bar = new WrapPanel();
        bar.Children.Add(run);
        bar.Children.Add(Labeled(L("Duração por etapa", "Duration per stage"), duration));
        bar.Children.Add(CopyButton(() => history.Text));
        page.Children.Add(Card(new StackPanel { Children = { bar, tiles, status, chart, Label(L("Histórico desta sessão", "This session's history")), history } }));
        return page;
    }

    // ---------- Adapters ----------

    private UIElement AdaptersTab()
    {
        var page = Page(L("Detalhes dos adaptadores ativos: IP, máscara, gateway, DNS, MAC, velocidade do link e MTU, além do IP público e da região de saída.",
                          "Details of active adapters: IP, mask, gateway, DNS, MAC, link speed and MTU, plus the public IP and exit region."));
        var cards = new StackPanel();
        var pub = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        var showDown = new CheckBox { Content = L("Incluir adaptadores desconectados", "Include disconnected adapters"), Margin = new Thickness(0, 6, 10, 0) };
        var refresh = PillBtn("");
        var flush = PillBtn("🧽  " + L("Limpar cache DNS", "Flush DNS cache"));
        var report = new StringBuilder();

        UIElement Kv(string k, string v)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var key = new TextBlock { Text = k, Opacity = 0.7 };
            var val = new TextBlock { Text = v.Length == 0 ? "—" : v, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas") };
            Grid.SetColumn(val, 1);
            g.Children.Add(key); g.Children.Add(val);
            return g;
        }

        Wire(refresh, L("Atualizar", "Refresh"), async ct =>
        {
            cards.Children.Clear(); report.Clear();
            pub.Text = "🌍 " + L("Obtendo IP público…", "Getting public IP…");
            foreach (var a in NetworkToolsService.GetAdapters(showDown.IsChecked == true))
            {
                var st = new StackPanel();
                st.Children.Add(new TextBlock { Text = $"{(a.Up ? "🟢" : "⚪")}  {a.Name}", FontSize = 14, FontWeight = FontWeights.SemiBold });
                st.Children.Add(new TextBlock { Text = a.Description, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
                var rows = new (string, string)[]
                {
                    (L("Tipo", "Type"), a.Type), (L("IPv4 / máscara", "IPv4 / mask"), string.Join(", ", a.Ipv4)), ("IPv6", string.Join(", ", a.Ipv6)),
                    ("Gateway", string.Join(", ", a.Gateways)), ("DNS", string.Join(", ", a.Dns)), ("DHCP", a.Dhcp ? L("Sim", "Yes") : L("Não", "No")),
                    ("MAC", a.Mac), (L("Velocidade do link", "Link speed"), a.SpeedMbps > 0 ? a.SpeedMbps + " Mbps" : ""), ("MTU", a.Mtu > 0 ? a.Mtu.ToString() : ""),
                };
                report.AppendLine(a.Name);
                foreach (var (k, v) in rows) { st.Children.Add(Kv(k, v)); report.AppendLine($"  {k}: {v}"); }
                var card = Card(st);
                card.Margin = new Thickness(0, 0, 0, 8);
                cards.Children.Add(card);
            }
            if (cards.Children.Count == 0) cards.Children.Add(new TextBlock { Text = L("Nenhum adaptador ativo.", "No active adapter.") });
            var info = await NetworkToolsService.GetPublicIpAsync(ct);
            pub.Text = info == null
                ? "🌍 " + L("IP público indisponível (sem internet ou bloqueado).", "Public IP unavailable (no internet or blocked).")
                : $"🌍 {L("IP público", "Public IP")}: {info.Ip}   ·   {L("País", "Country")}: {info.Country}   ·   {L("Servidor Cloudflare", "Cloudflare server")}: {info.Colo}   ·   {info.Http} / {info.Tls}{(info.Warp ? "   ·   WARP" : "")}";
            report.Insert(0, pub.Text + "\n\n");
        }, null);

        flush.Click += async (_, _) =>
        {
            try { await NetworkToolsService.FlushDnsAsync(); pub.Text = "✔ " + L("Cache DNS do Windows limpo.", "Windows DNS cache flushed."); }
            catch (Exception ex) { pub.Text = "✖ " + ex.Message; }
        };

        var bar = new WrapPanel();
        bar.Children.Add(refresh); bar.Children.Add(flush); bar.Children.Add(CopyButton(() => report.ToString())); bar.Children.Add(showDown);
        page.Children.Add(bar);
        page.Children.Add(pub);
        page.Children.Add(cards);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        return page;
    }
}
