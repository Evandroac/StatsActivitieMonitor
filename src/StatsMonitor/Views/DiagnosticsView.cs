using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StatsMonitor.Controls;
using StatsMonitor.Diagnostics;
using StatsMonitor.Helpers;
using StatsMonitor.Models;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

/// <summary>Computer diagnostic module: read-only inventory, performance tests, rule-based analysis, history and reports.</summary>
public sealed class DiagnosticsView : UserControl
{
    public static readonly string[] Keywords =
    [
        "Diagnóstico do computador", "Diagnóstico completo", "Diagnóstico inteligente", "Saúde do sistema", "Hardware", "Processador", "Memória RAM", "Placa de vídeo", "Armazenamento", "SMART", "Bateria e energia", "Sistema operacional", "Drivers e dispositivos", "Rede e conectividade", "Segurança", "Antivírus", "Firewall", "Secure Boot", "TPM", "BitLocker", "Monitoramento em tempo real", "Testes de desempenho", "Teste de CPU", "Teste de memória", "Teste de disco", "SFC", "Histórico de diagnósticos", "Comparar diagnósticos", "Relatórios", "Exportar PDF", "Exportar HTML", "Exportar CSV", "Exportar JSON", "Alertas de monitoramento", "Achados e recomendações", "Visão geral", "Indicador geral", "Principais pontos de atenção", "INICIAR DIAGNÓSTICO COMPLETO", "CANCELAR DIAGNÓSTICO", "Diagnosticar este componente", "Estado do componente", "Dado indisponível", "Computador", "Nome do computador", "Fabricante", "Modelo", "Tipo de sistema", "Data da BIOS", "BIOS", "Placa-mãe", "Arquitetura", "Núcleos", "Frequência máxima", "Frequência atual", "Utilização", "Temperatura", "Limitação térmica", "Processos / Threads", "Capacidade instalada", "Memória comprometida", "Módulos de memória", "Slots de memória", "Frequência da memória", "VRAM", "Consumo energético", "Driver da placa de vídeo", "Data do driver", "Status do dispositivo", "Discos físicos", "Saúde do disco", "Desgaste do SSD", "Erros de leitura", "Erros de escrita", "Espaço livre", "Unidades de disco", "HDD", "SSD", "Nível de carga", "Fonte de energia", "Capacidade original", "Capacidade atual", "Saúde da bateria", "Ciclos de carga", "Tensão", "Autonomia estimada", "Tempo para carga completa", "Versão do Windows", "Instalado em", "Tempo de atividade", "Reinicialização pendente", "Serviços", "Serviços parados", "Programas de inicialização", "Aplicativos instalados", "Atualizações instaladas", "Hotfixes", "Eventos do sistema", "Log de eventos", "Erros no log do sistema", "Drivers instalados", "Driver mais antigo", "Dispositivos com erro", "Dispositivos desconhecidos", "Conflitos de recursos", "Adaptadores de rede", "Gateway", "DNS", "IP", "Latência da rede", "Perda de pacotes", "Microsoft Defender", "Proteção em tempo real", "Assinaturas do antivírus", "Controle de Conta de Usuário (UAC)", "UAC", "Windows Update", "Gráficos de monitoramento", "Alertas configuráveis", "Ativar alertas", "Duração mínima (s)", "Alertas recentes", "Temp. CPU", "Temp. GPU", "Rede Mbps", "Testar CPU", "Testar RAM", "Testar disco", "Testar rede", "Avaliar GPU", "Verificar arquivos do Windows (SFC /verifyonly)", "Todos", "Gravidade", "Normal", "Informativo", "Atenção", "Crítico", "Indeterminado", "Evidência", "Causa provável", "Recomendação", "Abrir", "Excluir", "Comparar", "Novos problemas", "Problemas resolvidos", "Variação do indicador", "Exportar", "Abrir pasta dos diagnósticos", "Log de auditoria", "Histórico", "Indisponível",
    ];

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;
    private static bool En => Loc.Lang == "en";

    private static readonly Lazy<DiagnosticStore> LazyStore = new(() => new DiagnosticStore());
    private static readonly Lazy<LiveMonitor> LazyLive = new(() => new LiveMonitor(LazyStore.Value.Dir));
    private static DiagnosticStore Store => LazyStore.Value;
    private static LiveMonitor Live => LazyLive.Value;
    private static readonly Thresholds Limits = new();

    private static readonly Brush Good = Frozen(0x2E, 0xCC, 0x71), Info = Frozen(0x34, 0x98, 0xDB), Warn = Frozen(0xF1, 0xC4, 0x0F), Bad = Frozen(0xE7, 0x4C, 0x3C), Unknown = Frozen(0x95, 0xA5, 0xA6);
    private static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    private static Brush BrushOf(Severity s) => s switch { Severity.Normal => Good, Severity.Info => Info, Severity.Warning => Warn, Severity.Critical => Bad, _ => Unknown };

    private readonly MainViewModel _vm;
    private readonly StackPanel _body = new();
    private readonly List<Button> _nav = [];
    private readonly ObservableCollection<string> _log = [];
    private readonly List<string> _alerts = [];
    private readonly Dictionary<string, TextBlock> _liveText = [];
    private readonly Dictionary<string, Sparkline> _charts = [];
    private DiagnosticSession _cur;
    private CancellationTokenSource? _cts;
    private int _sec;
    private int _minutes = 5;
    private double _progress;
    private string _progressText = "";
    private Severity? _filter;
    private string? _cmpA, _cmpB;
    private ProgressBar? _bar;
    private TextBlock? _barText;
    private TextBox? _logBox;
    private bool _loading;

    public DiagnosticsView(MainViewModel vm)
    {
        _vm = vm;
        _cur = DiagnosticRunner.NewSession(false, vm.LastSnapshot);
        Build();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.SnapshotApplied += OnSnapshot;
        Live.Alert += OnAlert;
        Loc.LanguageChanged += OnLanguage;
        _ = RefreshInventoryAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.SnapshotApplied -= OnSnapshot;
        Live.Alert -= OnAlert;
        Loc.LanguageChanged -= OnLanguage;
        _cts?.Cancel();
    }

    private void OnLanguage() { Build(); }

    private void OnAlert(string msg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _alerts.Insert(0, $"{DateTime.Now:HH:mm:ss}  {msg}");
            if (_alerts.Count > 50) _alerts.RemoveAt(_alerts.Count - 1);
            Store.Audit("live alert: " + msg);
            if (_sec == 11) Show(11);
        });
    }

    private void OnSnapshot(HardwareSnapshot s)
    {
        Live.Add(s, _vm.LastNetwork);
        if (_sec == 0) UpdateLiveTiles(s);
        else if (_sec == 11) RefreshCharts();
    }

    // ---------- inventory ----------

    private async Task RefreshInventoryAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var snap = _vm.LastSnapshot;
            var ses = DiagnosticRunner.NewSession(false, snap);
            ses.Inventory = await Task.Run(() => InventoryCollector.CollectAll(snap, null));
            ses.Model = ses.Inventory.S("hardware.model") ?? "";
            DiagnosticEngine.Analyze(ses, snap, Limits);
            if (!_running) { _cur = ses; Show(_sec); }
        }
        catch (Exception ex) { _log.Add("✖ " + ex.Message); }
        finally { _loading = false; }
    }

    private bool _running => _cts != null;

    // ---------- layout ----------

    private static readonly (string Icon, string Pt, string En)[] Sections =
    [
        ("🏠", "Visão geral", "Overview"), ("🖥", "Hardware", "Hardware"), ("🧠", "Processador", "Processor"), ("🧮", "Memória RAM", "RAM"),
        ("🎮", "Placa de vídeo", "Graphics card"), ("💾", "Armazenamento", "Storage"), ("🔋", "Bateria e energia", "Battery and power"),
        ("🪟", "Sistema operacional", "Operating system"), ("🔌", "Drivers e dispositivos", "Drivers and devices"), ("🌐", "Rede e conectividade", "Network and connectivity"),
        ("🛡", "Segurança", "Security"), ("📈", "Monitoramento em tempo real", "Real-time monitoring"), ("⏱", "Testes de desempenho", "Performance tests"),
        ("🩺", "Diagnóstico inteligente", "Smart diagnosis"), ("🕘", "Histórico", "History"), ("📄", "Relatórios", "Reports"),
    ];

    private object? Res(string key) => TryFindResource(key) ?? Application.Current.TryFindResource(key);

    private void Build()
    {
        _nav.Clear(); _liveText.Clear(); _charts.Clear();
        var root = new Grid { Margin = new Thickness(16) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0), Width = 210 };
        left.Children.Add(new TextBlock { Text = "🩺  " + L("Diagnóstico", "Diagnostics"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        left.Children.Add(new TextBlock { Text = L("Escolha uma seção para analisar hardware, software, testes, histórico e relatórios do computador.", "Pick a section to review the computer's hardware, software, tests, history and reports."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) });
        for (int i = 0; i < Sections.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = $"{Sections[i].Icon}  {L(Sections[i].Pt, Sections[i].En)}", Style = (Style?)Res("PillButton"), Margin = new Thickness(0, 0, 0, 4), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6) };
            b.Click += (_, _) => Show(idx);
            _nav.Add(b); left.Children.Add(b);
        }
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = left });
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        Grid.SetColumn(sv, 1);
        root.Children.Add(sv);
        Content = root;
        Show(_sec);
    }

    private void Show(int i)
    {
        _sec = i;
        for (int k = 0; k < _nav.Count; k++)
        {
            _nav[k].FontWeight = k == i ? FontWeights.Bold : FontWeights.Normal;
            _nav[k].SetResourceReference(BackgroundProperty, k == i ? "TrackBrush" : "InputBg");
        }
        _body.Children.Clear(); _liveText.Clear(); _charts.Clear();
        _body.Children.Add(Header(i));
        switch (i)
        {
            case 0: Overview(); break;
            case >= 1 and <= 10: ComponentPage(Comp.All[i - 1]); break;
            case 11: Monitoring(); break;
            case 12: TestsPage(); break;
            case 13: SmartPage(); break;
            case 14: HistoryPage(); break;
            default: ReportsPage(); break;
        }
    }

    private UIElement Header(int i)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        p.Children.Add(new TextBlock { Text = $"{Sections[i].Icon}  {L(Sections[i].Pt, Sections[i].En)}", FontSize = 18, FontWeight = FontWeights.SemiBold });
        p.Children.Add(Cap(_cur.Finished is { } f ? L($"Último diagnóstico completo: {f:g}", $"Last full diagnostic: {f:g}") : L("Dados atuais coletados em modo somente leitura. Nenhuma correção é aplicada automaticamente.", "Current data collected read-only. No fix is applied automatically.")));
        return p;
    }

    // ---------- widgets ----------

    private static TextBlock Cap(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0) };

    private Border Card(UIElement child, double? width = null)
    {
        var b = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 10), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 980, Child = child };
        if (width != null) b.Width = width.Value;
        b.SetResourceReference(Border.BackgroundProperty, "CardBg");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        return b;
    }

    private Button Pill(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Style = (Style?)Res("PillButton"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 7, 14, 7) };
        b.Click += click;
        return b;
    }

    private static UIElement Badge(Severity s)
    {
        var b = new Border { Background = BrushOf(s), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = SeverityInfo.Label(s, En), Foreground = Brushes.Black, FontSize = 11, FontWeight = FontWeights.SemiBold } };
        return b;
    }

    private UIElement FactGrid(IEnumerable<Fact> facts)
    {
        var g = new Grid { MinWidth = 520 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        foreach (var f in facts)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = f.Label, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2) };
            bool na = f.Value.StartsWith("Indispon", StringComparison.OrdinalIgnoreCase) || f.Value.StartsWith("Unavail", StringComparison.OrdinalIgnoreCase);
            var v = new TextBlock { Text = f.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = na ? Warn : null, IsHitTestVisible = true };
            if (na) v.ToolTip = L("Dado indisponível: não é tratado como saudável.", "Unavailable data: not treated as healthy.");
            Grid.SetRow(l, r); Grid.SetRow(v, r); Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v); r++;
        }
        return g;
    }

    private UIElement FindingCard(Finding f)
    {
        var p = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Badge(f.Level));
        head.Children.Add(new TextBlock { Text = "  " + f.Problem, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        p.Children.Add(head);
        void Line(string label, string v) { if (v.Length > 0) p.Children.Add(new TextBlock { Text = $"{label}: {v}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Opacity = 0.9 }); }
        Line(L("Componente", "Component"), Comp.Title(f.Component, En));
        Line(L("Evidência", "Evidence"), f.Evidence);
        Line(L("Causa provável", "Probable cause"), f.Cause);
        Line(L("Recomendação", "Recommendation"), f.Recommendation);
        Line(L("Resultado de teste", "Test result"), f.TestResult);
        p.Children.Add(Cap(f.Time.ToString("G")));
        var c = Card(p);
        c.BorderBrush = BrushOf(f.Level); c.Width = 760;
        return c;
    }

    private UIElement HealthTile(string comp)
    {
        var sev = _cur.Health.TryGetValue(comp, out var h) ? h : Severity.Indeterminate;
        var p = new StackPanel { Width = 150 };
        p.Children.Add(new TextBlock { Text = Comp.Title(comp, En), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        p.Children.Add(Badge(sev));
        var c = Card(p);
        c.Cursor = System.Windows.Input.Cursors.Hand;
        int idx = Array.IndexOf(Comp.All, comp) + 1;
        c.MouseLeftButtonUp += (_, _) => Show(idx);
        return c;
    }

    // ---------- overview ----------

    private void Overview()
    {
        var inv = _cur.Inventory;
        string S(string k) => inv.S(k) ?? InventoryCollector.NA;
        var ident = new List<Fact>
        {
            new(L("Computador", "Computer"), Environment.MachineName),
            new(L("Fabricante / modelo", "Manufacturer / model"), $"{S("hardware.maker")} {S("hardware.model")}"),
            new(L("Sistema", "System"), $"{S("os.caption")} (build {S("os.build")})"),
            new(L("Processador", "Processor"), S("cpu.name")),
            new("RAM", inv.N("memory.totalGb") is { } m ? $"{m:0.0} GB" : InventoryCollector.NA),
            new(L("Placa de vídeo", "Graphics"), S("gpu.name")),
            new(L("Administrador", "Administrator"), _vm.LastSnapshot?.IsElevated == true ? L("Sim", "Yes") : L("Não — alguns sensores/dados ficam indisponíveis", "No — some sensors/data stay unavailable")),
        };
        _body.Children.Add(Card(FactGrid(ident)));

        var tiles = new WrapPanel();
        foreach (var k in new[] { "cpu", "ram", "gpu", "disk", "cpuTemp", "gpuTemp" })
        {
            var t = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Text = "—" };
            _liveText[k] = t;
            var p = new StackPanel { Width = 110 };
            p.Children.Add(new TextBlock { Text = k switch { "cpu" => "CPU", "ram" => "RAM", "gpu" => "GPU", "disk" => L("Disco", "Disk"), "cpuTemp" => L("Temp. CPU", "CPU temp"), _ => L("Temp. GPU", "GPU temp") }, Opacity = 0.7, FontSize = 11 });
            p.Children.Add(t);
            tiles.Children.Add(Card(p));
        }
        _body.Children.Add(tiles);
        if (_vm.LastSnapshot is { } snap) UpdateLiveTiles(snap);

        var score = new StackPanel { Orientation = Orientation.Horizontal };
        score.Children.Add(new TextBlock { Text = L("Indicador geral (heurístico): ", "Overall indicator (heuristic): "), VerticalAlignment = VerticalAlignment.Center });
        score.Children.Add(new TextBlock { Text = _cur.Score is { } sc ? $"{sc}/100" : L("indisponível", "unavailable"), FontWeight = FontWeights.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        score.ToolTip = L("100 − 25 por achado crítico − 10 por atenção − 3 por indeterminado. Não é uma métrica certificada.", "100 − 25 per critical − 10 per warning − 3 per undetermined. Not a certified metric.");
        _body.Children.Add(Card(score));

        var health = new WrapPanel();
        foreach (var c in Comp.All) health.Children.Add(HealthTile(c));
        _body.Children.Add(health);

        var run = Pill(_running ? "⏹  " + L("CANCELAR DIAGNÓSTICO", "CANCEL DIAGNOSTIC") : "▶  " + L("INICIAR DIAGNÓSTICO COMPLETO", "START FULL DIAGNOSTIC"), async (_, _) => await ToggleFullAsync());
        run.FontWeight = FontWeights.Bold;
        _body.Children.Add(run);
        _body.Children.Add(Cap(L("Executa inventário, testes de CPU, RAM (256 MB), disco (arquivo temporário de 128 MB), rede e análise. Leva cerca de 30 s. O computador pode ficar mais lento durante o teste de CPU.", "Runs inventory, CPU, RAM (256 MB), disk (128 MB temp file) and network tests plus analysis. Takes about 30 s. The computer may slow down during the CPU test.")));
        _bar = new ProgressBar { Height = 10, Maximum = 100, Value = _progress, Margin = new Thickness(0, 8, 0, 2), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Width = 760 };
        _barText = Cap(_progressText);
        _logBox = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 150, MaxWidth = 760, Width = 760, HorizontalAlignment = HorizontalAlignment.Left, FontFamily = new FontFamily("Cascadia Mono,Consolas"), FontSize = 12, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = string.Join("\n", _log) };
        _body.Children.Add(_bar); _body.Children.Add(_barText); _body.Children.Add(_logBox);

        var top = _cur.Findings.Where(f => f.Level is Severity.Critical or Severity.Warning).Take(4).ToList();
        if (top.Count > 0)
        {
            _body.Children.Add(new TextBlock { Text = L("Principais pontos de atenção", "Top attention points"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
            foreach (var f in top) _body.Children.Add(FindingCard(f));
        }
    }

    private void UpdateLiveTiles(HardwareSnapshot s)
    {
        void Set(string k, string v) { if (_liveText.TryGetValue(k, out var t)) t.Text = v; }
        string P(double? v) => v is { } x ? $"{x:0}%" : "—";
        string T(double? v) => v is > 0 ? $"{v:0} °C" : L("indisp.", "n/a");
        Set("cpu", P(s.CpuLoad)); Set("ram", P(s.RamLoad)); Set("gpu", P(s.GpuLoad));
        Set("disk", P(s.Disks.Select(d => d.Activity).Where(a => a != null).DefaultIfEmpty(null).Max()));
        Set("cpuTemp", T(s.CpuTemp)); Set("gpuTemp", T(s.GpuTemp));
    }

    private void Log(string msg)
    {
        _log.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
        while (_log.Count > 200) _log.RemoveAt(0);
        if (_logBox != null) { _logBox.Text = string.Join("\n", _log); _logBox.ScrollToEnd(); }
    }

    private void Progress(int pct, string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (pct >= 0) { _progress = pct; _progressText = text; if (_bar != null) _bar.Value = pct; if (_barText != null) _barText.Text = $"{pct}% — {text}"; }
            Log(text);
        });
    }

    private async Task ToggleFullAsync()
    {
        if (_cts != null) { _cts.Cancel(); return; }
        _cts = new CancellationTokenSource();
        _log.Clear();
        Show(0);
        try
        {
            var s = await DiagnosticRunner.RunFullAsync(Store, () => _vm.LastSnapshot, Limits, Progress, _cts.Token);
            _cur = s;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("✖ " + ex.Message); Store.Audit("diagnostic error: " + ex.Message); }
        finally { _cts?.Dispose(); _cts = null; Show(_sec == 0 ? 0 : _sec); }
    }

    // ---------- component pages ----------

    private void ComponentPage(string comp)
    {
        if (_cur.Inventory.Sections.TryGetValue(comp, out var facts) && facts.Count > 0) _body.Children.Add(Card(FactGrid(facts)));
        else _body.Children.Add(Card(new TextBlock { Text = _loading ? L("Coletando…", "Collecting…") : InventoryCollector.NA }));

        foreach (var e in _cur.Inventory.Errors.Where(x => x.StartsWith(comp + "/")))
            _body.Children.Add(Cap("⚠ " + L("Falha de coleta: ", "Collection failure: ") + e));

        var health = _cur.Health.TryGetValue(comp, out var h) ? h : Severity.Indeterminate;
        var hp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        hp.Children.Add(new TextBlock { Text = L("Estado do componente: ", "Component state: "), VerticalAlignment = VerticalAlignment.Center });
        hp.Children.Add(Badge(health));
        _body.Children.Add(hp);

        _body.Children.Add(Pill("🔄  " + L("Diagnosticar este componente", "Diagnose this component"), async (_, _) => await DiagnoseComponentAsync(comp)));
        _body.Children.Add(Cap(L("Atualiza a leitura e executa o teste do componente (quando existir).", "Refreshes the reading and runs the component test (when one exists).")));

        foreach (var t in _cur.Tests.Where(x => x.Component == comp))
            _body.Children.Add(TestCard(t));
        foreach (var f in _cur.Findings.Where(x => x.Component == comp)) _body.Children.Add(FindingCard(f));
    }

    private async Task DiagnoseComponentAsync(string comp)
    {
        if (_running || _loading) return;
        _loading = true;
        _cts = new CancellationTokenSource();
        try
        {
            var snap = _vm.LastSnapshot;
            var ses = _cur;
            await Task.Run(() => InventoryCollector.Collect(ses.Inventory, comp, snap));
            ses.Inventory.Errors.RemoveAll(e => false);
            var log = new Progress<string>(m => Log(m));
            ses.Tests.RemoveAll(t => t.Component == comp);
            TestResult? r = comp switch
            {
                Comp.Cpu => await PerformanceTests.CpuAsync(() => _vm.LastSnapshot, log, _cts.Token),
                Comp.Memory => await PerformanceTests.MemoryAsync(log, _cts.Token),
                Comp.Storage => await PerformanceTests.DiskAsync(null, log, _cts.Token),
                Comp.Network => await PerformanceTests.NetworkAsync(log, _cts.Token),
                Comp.Gpu => PerformanceTests.Gpu(snap),
                _ => null,
            };
            if (r != null) ses.Tests.Add(r);
            DiagnosticEngine.Analyze(ses, snap, Limits);
            Store.Audit($"component diagnostic {comp}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("✖ " + ex.Message); }
        finally { _cts?.Dispose(); _cts = null; _loading = false; Show(_sec); }
    }

    private UIElement TestCard(TestResult t)
    {
        var p = new StackPanel();
        var h = new StackPanel { Orientation = Orientation.Horizontal };
        h.Children.Add(Badge(t.Result));
        h.Children.Add(new TextBlock { Text = "  " + t.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        p.Children.Add(h);
        p.Children.Add(new TextBlock { Text = t.Summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        p.Children.Add(Cap($"{t.Time:G} · {t.DurationMs / 1000:0.0} s"));
        return Card(p, 760);
    }

    // ---------- monitoring ----------

    private void Monitoring()
    {
        var periods = new WrapPanel();
        foreach (var m in new[] { 1, 5, 15, 60 })
        {
            int mm = m;
            var b = Pill($"{m} min", (_, _) => { _minutes = mm; Show(11); });
            if (m == _minutes) b.FontWeight = FontWeights.Bold;
            periods.Children.Add(b);
        }
        _body.Children.Add(periods);
        _body.Children.Add(Cap(L("Os gráficos usam as leituras já coletadas pelo aplicativo e ficam em memória (até 60 min) enquanto esta tela está aberta.", "Charts use readings the app already collects and are kept in memory (up to 60 min) while this page is open.")));

        AddChart("cpu", "CPU %", 100); AddChart("ram", "RAM %", 100); AddChart("gpu", "GPU %", 100); AddChart("disk", L("Disco %", "Disk %"), 100);
        AddChart("cpuTemp", L("Temp. CPU °C", "CPU temp °C"), 110); AddChart("gpuTemp", L("Temp. GPU °C", "GPU temp °C"), 110); AddChart("net", L("Rede Mbps", "Network Mbps"), 0);
        RefreshCharts();

        var cfg = Live.Config;
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = "🔔  " + L("Alertas configuráveis", "Configurable alerts"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var en = new CheckBox { Content = L("Ativar alertas", "Enable alerts"), IsChecked = cfg.Enabled };
        en.Click += (_, _) => { cfg.Enabled = en.IsChecked == true; Live.SaveConfig(); };
        p.Children.Add(en);
        void Num(string label, Func<double> get, Action<double> set)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center });
            var tb = new TextBox { Width = 70, Text = get().ToString("0"), Padding = new Thickness(4, 2, 4, 2) };
            tb.LostFocus += (_, _) => { if (double.TryParse(tb.Text, out var v) && v > 0 && v <= 1000) { set(v); Live.SaveConfig(); } else tb.Text = get().ToString("0"); };
            row.Children.Add(tb); p.Children.Add(row);
        }
        Num(L("CPU % acima de", "CPU % above"), () => cfg.Cpu, v => cfg.Cpu = v);
        Num(L("Temp. CPU °C acima de", "CPU temp °C above"), () => cfg.CpuTemp, v => cfg.CpuTemp = v);
        Num(L("RAM % acima de", "RAM % above"), () => cfg.Ram, v => cfg.Ram = v);
        Num(L("GPU % acima de", "GPU % above"), () => cfg.Gpu, v => cfg.Gpu = v);
        Num(L("Temp. GPU °C acima de", "GPU temp °C above"), () => cfg.GpuTemp, v => cfg.GpuTemp = v);
        Num(L("Disco % acima de", "Disk % above"), () => cfg.Disk, v => cfg.Disk = v);
        Num(L("Duração mínima (s)", "Minimum duration (s)"), () => cfg.Seconds, v => cfg.Seconds = (int)v);
        _body.Children.Add(Card(p));

        var a = new StackPanel();
        a.Children.Add(new TextBlock { Text = L("Alertas recentes", "Recent alerts"), FontWeight = FontWeights.SemiBold });
        if (_alerts.Count == 0) a.Children.Add(Cap(L("Nenhum alerta nesta sessão.", "No alerts this session.")));
        foreach (var x in _alerts.Take(10)) a.Children.Add(new TextBlock { Text = "⚠ " + x, Margin = new Thickness(0, 3, 0, 0) });
        _body.Children.Add(Card(a));
    }

    private void AddChart(string key, string title, double max)
    {
        var sp = new Sparkline { Height = 60, Width = 720, Maximum = max, Stroke = (Brush?)Res("Accent") ?? Brushes.DodgerBlue };
        _charts[key] = sp;
        var p = new StackPanel();
        var head = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        p.Children.Add(head); p.Children.Add(sp);
        sp.Tag = head;
        _body.Children.Add(Card(p));
    }

    private void RefreshCharts()
    {
        var w = Live.Window(_minutes);
        void Set(string key, Func<LiveSample, double?> f, string title)
        {
            if (!_charts.TryGetValue(key, out var sp)) return;
            var vals = w.Select(f).Where(v => v != null).Select(v => v!.Value).ToList();
            sp.Values = new ObservableCollection<double>(vals);
            if (sp.Tag is TextBlock tb)
                tb.Text = vals.Count == 0 ? $"{title} — {L("indisponível (sem leituras)", "unavailable (no readings)")}" : $"{title} — {L("atual", "now")} {vals[^1]:0.#} · {L("mín", "min")} {vals.Min():0.#} · {L("média", "avg")} {vals.Average():0.#} · {L("máx", "max")} {vals.Max():0.#}";
            if (key == "net" && vals.Count > 0) sp.Maximum = Math.Max(1, vals.Max());
        }
        Set("cpu", x => x.Cpu, "CPU %"); Set("ram", x => x.Ram, "RAM %"); Set("gpu", x => x.Gpu, "GPU %"); Set("disk", x => x.Disk, L("Disco %", "Disk %"));
        Set("cpuTemp", x => x.CpuTemp, L("Temp. CPU °C", "CPU temp °C")); Set("gpuTemp", x => x.GpuTemp, L("Temp. GPU °C", "GPU temp °C")); Set("net", x => x.NetMbps, L("Rede Mbps", "Network Mbps"));
    }

    // ---------- tests ----------

    private void TestsPage()
    {
        _body.Children.Add(Cap(L("Testes não destrutivos e canceláveis. Cada resultado vem de uma medição real; quando algo não pôde ser medido, aparece como INDETERMINADO.", "Non-destructive, cancellable tests. Every result comes from a real measurement; anything that could not be measured shows as UNDETERMINED.")));
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(Pill(L("Testar CPU", "Test CPU"), async (_, _) => await DiagnoseComponentAsync(Comp.Cpu)));
        row.Children.Add(Pill(L("Testar RAM", "Test RAM"), async (_, _) => await DiagnoseComponentAsync(Comp.Memory)));
        row.Children.Add(Pill(L("Testar disco", "Test disk"), async (_, _) => await DiagnoseComponentAsync(Comp.Storage)));
        row.Children.Add(Pill(L("Testar rede", "Test network"), async (_, _) => await DiagnoseComponentAsync(Comp.Network)));
        row.Children.Add(Pill(L("Avaliar GPU", "Assess GPU"), async (_, _) => await DiagnoseComponentAsync(Comp.Gpu)));
        _body.Children.Add(row);

        var sfc = Pill("🧰  " + L("Verificar arquivos do Windows (SFC /verifyonly)", "Verify Windows files (SFC /verifyonly)"), async (_, _) => await RunSfcAsync());
        _body.Children.Add(sfc);
        _body.Children.Add(Cap(L("Opcional e somente verificação (não repara). Exige administrador e pode levar vários minutos.", "Optional, verify only (does not repair). Requires administrator and may take several minutes.")));
        _body.Children.Add(Cap(L("Limitações: o teste de RAM cobre 256 MB e não substitui o MemTest86; não há teste de estresse 3D de GPU; a leitura do disco pode usar o cache do Windows.", "Limitations: the RAM test covers 256 MB and does not replace MemTest86; there is no 3D GPU stress test; disk reads may be served from the Windows cache.")));
        if (_cur.Tests.Count == 0) _body.Children.Add(Card(new TextBlock { Text = L("Nenhum teste executado nesta sessão.", "No test run in this session.") }));
        foreach (var t in _cur.Tests) _body.Children.Add(TestCard(t));
    }

    private async Task RunSfcAsync()
    {
        if (_running) return;
        if (_vm.LastSnapshot?.IsElevated != true)
        {
            _cur.Tests.Add(new TestResult { Name = "SFC /verifyonly", Component = Comp.Os, Result = Severity.Indeterminate, Summary = L("Não executado: exige executar o aplicativo como administrador.", "Not run: requires running the app as administrator.") });
            Show(12); return;
        }
        _cts = new CancellationTokenSource();
        var sw = Stopwatch.StartNew();
        try
        {
            Log("SFC /verifyonly…");
            var res = await Task.Run(() =>
            {
                var psi = new ProcessStartInfo("sfc.exe", "/verifyonly") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.Unicode };
                using var p = Process.Start(psi)!;
                var outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, outp.Replace("\0", "").Replace("\r", " ").Replace("\n", " ").Trim());
            });
            var sev = res.ExitCode == 0 ? Severity.Normal : Severity.Warning;
            _cur.Tests.RemoveAll(t => t.Name == "SFC /verifyonly");
            _cur.Tests.Add(new TestResult { Name = "SFC /verifyonly", Component = Comp.Os, Result = sev, DurationMs = sw.Elapsed.TotalMilliseconds, Summary = $"{L("Código de saída", "Exit code")} {res.ExitCode}. {res.Item2}" });
            DiagnosticEngine.Analyze(_cur, _vm.LastSnapshot, Limits);
            Store.Audit($"sfc verifyonly exit={res.ExitCode}");
        }
        catch (Exception ex) { _cur.Tests.Add(new TestResult { Name = "SFC /verifyonly", Component = Comp.Os, Result = Severity.Indeterminate, Summary = ex.Message }); }
        finally { _cts?.Dispose(); _cts = null; Show(12); }
    }

    // ---------- smart diagnosis ----------

    private void SmartPage()
    {
        _body.Children.Add(Cap(L("Regras determinísticas aplicadas a evidências medidas. Um achado indica probabilidade, não prova de defeito físico.", "Deterministic rules applied to measured evidence. A finding indicates likelihood, not proof of a physical defect.")));
        var f = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        f.Children.Add(Pill(L("Todos", "All"), (_, _) => { _filter = null; Show(13); }));
        foreach (var s in new[] { Severity.Critical, Severity.Warning, Severity.Indeterminate, Severity.Info, Severity.Normal })
        { var sv = s; f.Children.Add(Pill($"{SeverityInfo.Icon(s)} {SeverityInfo.Label(s, En)}", (_, _) => { _filter = sv; Show(13); })); }
        _body.Children.Add(f);
        var list = _cur.Findings.Where(x => _filter == null || x.Level == _filter).ToList();
        if (list.Count == 0) _body.Children.Add(Card(new TextBlock { Text = L("Nenhum achado para este filtro.", "No findings for this filter.") }));
        foreach (var x in list) _body.Children.Add(FindingCard(x));
    }

    // ---------- history ----------

    private void HistoryPage()
    {
        var all = Store.List();
        _body.Children.Add(Cap(L($"{all.Count} diagnóstico(s) completo(s) salvos em {Store.Dir} (máx. {DiagnosticStore.MaxSessions}).", $"{all.Count} full diagnostic(s) saved in {Store.Dir} (max {DiagnosticStore.MaxSessions}).")));
        if (all.Count == 0) { _body.Children.Add(Card(new TextBlock { Text = L("Nenhum diagnóstico salvo ainda. Execute o diagnóstico completo na Visão geral.", "No diagnostics saved yet. Run the full diagnostic from the Overview.") })); return; }

        foreach (var s in all)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = $"{s.Started:g}  ·  {L("indicador", "indicator")} {(s.Score is { } sc ? sc + "/100" : "—")}  ·  {s.Findings.Count(x => x.Level == Severity.Critical)} {L("críticos", "critical")}, {s.Findings.Count(x => x.Level == Severity.Warning)} {L("atenção", "warning")}", VerticalAlignment = VerticalAlignment.Center, Width = 430 });
            var id = s.Id;
            row.Children.Add(Pill(L("Abrir", "Open"), (_, _) => { if (Store.Load(id) is { } l) { _cur = l; Show(13); } }));
            row.Children.Add(Pill(L("Excluir", "Delete"), (_, _) =>
            {
                if (MessageBox.Show(L("Excluir este diagnóstico do histórico?", "Delete this diagnostic from history?"), "Stats Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { Store.Delete(id); Show(14); }
            }));
            _body.Children.Add(Card(row));
        }

        var ids = all.Select(s => s.Id).ToArray();
        var cmp = new StackPanel();
        cmp.Children.Add(new TextBlock { Text = "⚖  " + L("Comparar dois diagnósticos", "Compare two diagnostics"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        ComboBox Combo(string? sel, Action<string> set)
        {
            var c = new ComboBox { Width = 260, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var s in all) c.Items.Add(new ComboBoxItem { Content = $"{s.Started:g}", Tag = s.Id });
            c.SelectedIndex = Math.Max(0, Array.IndexOf(ids, sel));
            c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem i) set((string)i.Tag); };
            return c;
        }
        _cmpA ??= ids[0]; _cmpB ??= ids.Length > 1 ? ids[1] : ids[0];
        var r = new StackPanel { Orientation = Orientation.Horizontal };
        r.Children.Add(Combo(_cmpA, v => _cmpA = v)); r.Children.Add(Combo(_cmpB, v => _cmpB = v));
        r.Children.Add(Pill(L("Comparar", "Compare"), (_, _) => ShowComparison()));
        cmp.Children.Add(r);
        _body.Children.Add(Card(cmp));
    }

    private void ShowComparison()
    {
        if (_cmpA == null || _cmpB == null || _cmpA == _cmpB) { MessageBox.Show(L("Escolha dois diagnósticos diferentes.", "Choose two different diagnostics."), "Stats Monitor"); return; }
        var a = Store.Load(_cmpA); var b = Store.Load(_cmpB);
        if (a == null || b == null) return;
        var c = DiagnosticStore.Compare(a, b);
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = L($"Antigo: {c.OlderId}  →  Novo: {c.NewerId}", $"Older: {c.OlderId}  →  Newer: {c.NewerId}"), FontWeight = FontWeights.SemiBold });
        p.Children.Add(new TextBlock { Text = L("Variação do indicador: ", "Indicator change: ") + (c.ScoreDelta is { } d ? (d > 0 ? "+" : "") + d : L("indisponível", "unavailable")), Margin = new Thickness(0, 4, 0, 4) });
        if (c.HealthChanges.Count == 0) p.Children.Add(Cap(L("Nenhuma mudança de estado por componente.", "No component state changes.")));
        foreach (var (comp, before, after) in c.HealthChanges)
            p.Children.Add(new TextBlock { Text = $"• {Comp.Title(comp, En)}: {SeverityInfo.Label(before, En)} → {SeverityInfo.Label(after, En)}" });
        p.Children.Add(new TextBlock { Text = L("Novos problemas", "New problems") + $" ({c.NewProblems.Count})", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        foreach (var f in c.NewProblems) p.Children.Add(new TextBlock { Text = $"• [{SeverityInfo.Label(f.Level, En)}] {Comp.Title(f.Component, En)}: {f.Problem}", TextWrapping = TextWrapping.Wrap });
        p.Children.Add(new TextBlock { Text = L("Problemas resolvidos", "Resolved problems") + $" ({c.Resolved.Count})", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        foreach (var f in c.Resolved) p.Children.Add(new TextBlock { Text = $"• {Comp.Title(f.Component, En)}: {f.Problem}", TextWrapping = TextWrapping.Wrap });
        var host = _body.Children.OfType<Border>().FirstOrDefault(x => x.Tag as string == "cmp");
        if (host != null) _body.Children.Remove(host);
        var card = Card(p, 760); card.Tag = "cmp";
        _body.Children.Add(card);
    }

    // ---------- reports ----------

    private void ReportsPage()
    {
        _body.Children.Add(Cap(L("Exporta o diagnóstico exibido no momento (o último executado ou o aberto no histórico).", "Exports the diagnostic currently shown (the last one run or the one opened from history).")));
        if (_cur.Findings.Count == 0) _body.Children.Add(Cap("⚠ " + L("Ainda não há achados; o relatório conterá apenas o inventário.", "There are no findings yet; the report will only contain the inventory.")));
        var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (name, ext) in new[] { ("PDF", ".pdf"), ("HTML", ".html"), ("CSV", ".csv"), ("JSON", ".json") })
        {
            string e = ext;
            row.Children.Add(Pill($"📄  {L("Exportar", "Export")} {name}", (_, _) => Export(e)));
        }
        row.Children.Add(Pill("📂  " + L("Abrir pasta dos diagnósticos", "Open diagnostics folder"), (_, _) => { try { Process.Start(new ProcessStartInfo("explorer.exe", Store.Dir) { UseShellExecute = true }); } catch { } }));
        _body.Children.Add(row);
        _body.Children.Add(Cap(L($"Log de auditoria: {Path.Combine(Store.Dir, "audit.log")}", $"Audit log: {Path.Combine(Store.Dir, "audit.log")}")));
    }

    private void Export(string ext)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"diagnostico-{_cur.Started:yyyyMMdd-HHmm}{ext}", Filter = $"{ext.TrimStart('.').ToUpperInvariant()}|*{ext}", DefaultExt = ext };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ReportExporter.Export(_cur, dlg.FileName);
            Store.Audit($"report exported {Path.GetFileName(dlg.FileName)}");
            MessageBox.Show(L("Relatório exportado.", "Report exported."), "Stats Monitor");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
