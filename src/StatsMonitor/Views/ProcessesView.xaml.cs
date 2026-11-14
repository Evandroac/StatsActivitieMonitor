using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StatsMonitor.Helpers;
using StatsMonitor.Services;

namespace StatsMonitor.Views;

/// <summary>Task-Manager-like process list: tree, search, filters, sorting and actions. Samples only while visible.</summary>
public partial class ProcessesView : UserControl
{
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    { "System", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "Registry", "Memory Compression" };

    private readonly ProcessSampler _sampler = new();
    private readonly Dictionary<(int, long), ProcessRow> _rows = [];
    private readonly ObservableCollection<ProcessRow> _items = [];
    private readonly ProcessQuery _query = new();
    private readonly DispatcherTimer _timer = new();
    private bool _busy, _ready, _running;
    private int _detailToken;

    public static readonly string[] Keywords = ["Processos", "Gerenciador de tarefas", "Finalizar tarefa", "Finalizar árvore de processos", "Reiniciar", "Prioridade", "Abrir local do arquivo", "Copiar caminho", "Colunas", "Reordenar colunas", "Ocultar colunas", "Expandir tudo", "Recolher tudo", "Gráfico de linha do tempo", "Pesquisar processo", "CPU", "Memória", "Disco", "Rede"];

    public ProcessesView()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        List.ContextMenu = new ContextMenu();
        PreviewMouseLeftButtonDown += OnClickOutside;
        _timer.Tick += async (_, _) => await TickAsync();
        FillCombos();
        _query.Type = ProcType.App;
        BuildHeader();
        HeaderGrid.ContextMenu = new ContextMenu();
        HeaderGrid.ContextMenuOpening += (_, _) => FillColumnsMenu(HeaderGrid.ContextMenu);
        Loaded += (_, _) => { ProcessColumns.Changed -= OnColumnsChanged; ProcessColumns.Changed += OnColumnsChanged; };
        Unloaded += (_, _) => ProcessColumns.Changed -= OnColumnsChanged;
        ApplyTexts();
        _ready = true;
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loc.LanguageChanged += OnLanguage;
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguage;
    }

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : Loc.T(pt);

    private void OnLanguage()
    {
        _ready = false;
        FillCombos();
        ApplyTexts();
        _ready = true;
        foreach (var r in _rows.Values) r.RefreshTexts();
        Rebuild();
    }

    private static void Fill(ComboBox box, params string[] items)
    {
        int idx = Math.Max(0, box.SelectedIndex);
        box.Items.Clear();
        foreach (var i in items) box.Items.Add(i);
        box.SelectedIndex = idx;
    }

    private void FillCombos()
    {
        Fill(TypeBox, L("Todos os tipos", "All types"), L("Aplicativos", "Apps"), L("Segundo plano", "Background"), L("Processos do Windows", "Windows processes"));
        Fill(StatusBox, L("Todos os status", "All statuses"), L("Em execução", "Running"), L("Não está respondendo", "Not responding"));
        Fill(SortBox, L("Nome", "Name"), "CPU", L("Memória", "Memory"), L("Disco", "Disk"), L("Rede", "Network"), "PID", L("Status", "Status"), L("Tipo", "Type"), "Threads", L("PID pai", "Parent PID"), L("Executável", "Executable"), L("Caminho", "Path"), L("Iniciado em", "Started"));
        if (SortBox.SelectedIndex < 0 || !_ready) SortBox.SelectedIndex = 1;
        if (!_ready) TypeBox.SelectedIndex = 1;
        Fill(MetricBox, "CPU", L("Memória", "Memory"), L("Disco", "Disk"), L("Rede", "Network"));
        Fill(RefreshBox, L("1 segundo", "1 second"), L("2 segundos", "2 seconds"), L("5 segundos", "5 seconds"), L("Pausado", "Paused"));
        if (RefreshBox.SelectedIndex < 0 || !_ready) RefreshBox.SelectedIndex = 1;
    }

    private void ApplyTexts()
    {
        PageTitle.Text = "⚙️  " + L("Processos", "Processes");
        SearchBox.ToolTip = L("Pesquisar por nome, PID ou caminho", "Search by name, PID or path");
        SortBox.ToolTip = L("Ordenar por", "Sort by");
        RefreshBox.ToolTip = L("Velocidade de atualização", "Refresh rate");
        TypeBox.ToolTip = L("Filtrar por tipo de processo", "Filter by process type");
        StatusBox.ToolTip = L("Filtrar por status", "Filter by status");
        MetricBox.ToolTip = L("Recurso exibido no gráfico da linha do tempo", "Resource shown in the timeline chart");
        ExpandButton.ToolTip = L("Expande todos os grupos de processos", "Expands all process groups");
        CollapseButton.ToolTip = L("Recolhe todos os grupos de processos", "Collapses all process groups");
        EndButton.ToolTip = L("Encerra o processo selecionado", "Ends the selected process");
        EndTreeButton.ToolTip = L("Encerra o processo selecionado e todos os seus filhos", "Ends the selected process and all its children");
        RestartButton.ToolTip = L("Encerra e abre novamente o processo selecionado", "Closes and reopens the selected process");
        PriorityButton.ToolTip = L("Altera a prioridade do processo selecionado", "Changes the priority of the selected process");
        LocationButton.ToolTip = L("Abre a pasta do arquivo do processo no Explorador", "Opens the process file folder in Explorer");
        CopyButton.ToolTip = L("Copia o caminho do arquivo do processo", "Copies the process file path");
        DirButton.Content = _query.Descending ? "▼" : "▲";
        DirButton.ToolTip = _query.Descending ? L("Decrescente (maior primeiro)", "Descending (highest first)") : L("Crescente (menor primeiro)", "Ascending (lowest first)");
        SearchHint.Text = "🔍  " + L("Pesquisar processo (nome, PID ou caminho)...", "Search process (name, PID or path)...");
        ActionsHint.Text = L("Selecione um processo na lista para usar as ações", "Select a process in the list to use the actions");
        ExpandButton.Content = L("➕  Expandir tudo", "➕  Expand all");
        CollapseButton.Content = L("➖  Recolher tudo", "➖  Collapse all");
        EndButton.Content = L("⛔  Finalizar tarefa", "⛔  End task");
        EndTreeButton.Content = L("🌳  Finalizar árvore", "🌳  End process tree");
        RestartButton.Content = L("🔄  Reiniciar", "🔄  Restart");
        PriorityButton.Content = L("⚡  Prioridade", "⚡  Priority");
        LocationButton.Content = L("📂  Abrir local do arquivo", "📂  Open file location");
        CopyButton.Content = L("📋  Copiar caminho", "📋  Copy path");
        ColumnsButton.Content = L("🗂  Colunas", "🗂  Columns");
        ColumnsButton.ToolTip = L("Mostrar ou ocultar colunas (arraste os títulos da lista para reordenar)", "Show or hide columns (drag the list headings to reorder)");
        ChartTitle.Text = "📈  " + L("Linha do tempo", "Timeline");
        UpdateHeaders();
        UpdateButtons();
    }

    private double _totCpu, _totMem, _totDisk, _totNet;

    private static string Pct(double v) => v < 0.05 ? "0%" : v < 10 ? $"{v:0.0}%" : $"{v:0}%";

    private static void SetHeader(Button b, string title, string total, TextAlignment align)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, TextAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis });
        sp.Children.Add(new TextBlock { Text = total, TextAlignment = align, FontSize = 15, Margin = new Thickness(0, 2, 0, 0), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary") });
        b.Content = sp;
    }

    private readonly Dictionary<string, Button> _hdr = [];
    private Border? _dropLine;
    private Button? _dragBtn;
    private Point _dragStart;
    private bool _dragging;
    private int _dropIndex;

    private string TotalFor(ColumnDef c) => c.Id switch
    {
        "name" => L($"{_rows.Count} processos", $"{_rows.Count} processes"),
        "cpu" => Pct(_totCpu),
        "mem" => Pct(_totMem),
        "disk" => Pct(_totDisk),
        "net" => ProcessRow.NetAvailable ? Pct(_totNet) : "—",
        _ => " ",
    };

    private void UpdateHeaders()
    {
        string Arrow(ProcSort? s) => s != null && _query.Sort == s ? (_query.Descending ? " ▼" : " ▲") : "";
        foreach (var c in ProcessColumns.Visible)
            if (_hdr.TryGetValue(c.Id, out var b))
            {
                b.ToolTip = c.Sort != null ? L("Clique para ordenar; arraste para mover a coluna", "Click to sort; drag to move the column")
                                           : L("Arraste para mover a coluna", "Drag to move the column");
                SetHeader(b, c.Title + Arrow(c.Sort), TotalFor(c), c.Right ? TextAlignment.Right : TextAlignment.Left);
            }
        DirButton.Content = _query.Descending ? "▼" : "▲";
    }

    private void BuildHeader()
    {
        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();
        _hdr.Clear();
        var style = (Style)FindResource("HeaderButton");
        var split = (Style)FindResource("ColSplit");
        int i = 0;
        foreach (var c in ProcessColumns.Visible)
        {
            var cd = new ColumnDefinition { MinWidth = c.Star ? 200 : 0 };
            cd.SetBinding(ColumnDefinition.WidthProperty, new System.Windows.Data.Binding(nameof(ColumnDef.GridWidth)) { Source = c, Mode = System.Windows.Data.BindingMode.OneWay });
            HeaderGrid.ColumnDefinitions.Add(cd);
            var b = new Button { Style = style, HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = c };
            b.PreviewMouseLeftButtonDown += OnHdrDown;
            b.PreviewMouseMove += OnHdrMove;
            b.PreviewMouseLeftButtonUp += OnHdrUp;
            Grid.SetColumn(b, i);
            HeaderGrid.Children.Add(b);
            _hdr[c.Id] = b;
            if (c.Star)
            {
                var fit = new Button { Style = style, Content = "↔", Width = 24, Height = 24, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Tag = c,
                    ToolTip = L("Ajustar a largura da coluna Nome ao conteúdo (clique de novo para voltar ao automático)", "Fit the Name column width to its content (click again to return to automatic)") };
                fit.Click += OnFitName;
                Grid.SetColumn(fit, i);
                HeaderGrid.Children.Add(fit);
            }
            else
            {
                var th = new System.Windows.Controls.Primitives.Thumb { Style = split, Tag = c };
                th.DragDelta += OnSplitDrag;
                th.DragCompleted += (_, _) => ProcessColumns.Save();
                Grid.SetColumn(th, i);
                HeaderGrid.Children.Add(th);
            }
            i++;
        }
        _dropLine = new Border { Width = 3, Background = (System.Windows.Media.Brush)FindResource("Accent"), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, IsHitTestVisible = false, CornerRadius = new CornerRadius(1.5) };
        Grid.SetColumnSpan(_dropLine, Math.Max(1, i));
        HeaderGrid.Children.Add(_dropLine);
        UpdateHeaders();
    }

    private int ColumnIndexAt(double x)
    {
        double acc = 0;
        for (int i = 0; i < HeaderGrid.ColumnDefinitions.Count; i++)
        {
            acc += HeaderGrid.ColumnDefinitions[i].ActualWidth;
            if (x < acc) return i;
        }
        return Math.Max(0, HeaderGrid.ColumnDefinitions.Count - 1);
    }

    private void OnHdrDown(object s, MouseButtonEventArgs e)
    {
        _dragBtn = (Button)s;
        _dragStart = e.GetPosition(HeaderGrid);
        _dragging = false;
        _dragBtn.CaptureMouse();
        e.Handled = true;
    }

    private void OnHdrMove(object s, MouseEventArgs e)
    {
        if (_dragBtn != s || e.LeftButton != MouseButtonState.Pressed || _dropLine == null) return;
        var p = e.GetPosition(HeaderGrid);
        if (!_dragging && Math.Abs(p.X - _dragStart.X) > 6) { _dragging = true; _dragBtn.Opacity = 0.5; }
        if (!_dragging) return;
        _dropIndex = ColumnIndexAt(p.X);
        int from = Grid.GetColumn(_dragBtn);
        double left = 0;
        for (int i = 0; i < _dropIndex; i++) left += HeaderGrid.ColumnDefinitions[i].ActualWidth;
        if (_dropIndex > from) left += HeaderGrid.ColumnDefinitions[_dropIndex].ActualWidth;
        _dropLine.Margin = new Thickness(Math.Max(0, left - 1.5), 4, 0, 4);
        _dropLine.Visibility = Visibility.Visible;
    }

    private void OnHdrUp(object s, MouseButtonEventArgs e)
    {
        if (_dragBtn != s) return;
        var b = _dragBtn; _dragBtn = null;
        b.ReleaseMouseCapture();
        b.Opacity = 1;
        if (_dropLine != null) _dropLine.Visibility = Visibility.Collapsed;
        e.Handled = true;
        var def = (ColumnDef)b.Tag;
        if (_dragging) { _dragging = false; ProcessColumns.MoveTo(def, _dropIndex); }
        else if (def.Sort is { } sort) SortBy(sort);
    }

    private void OnColumnsChanged()
    {
        BuildHeader();
    }

    private void FillColumnsMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        foreach (var c in ProcessColumns.All)
        {
            var col = c;
            var mi = new MenuItem { Header = col.Title, IsCheckable = true, IsChecked = col.Visible, IsEnabled = !col.Required, StaysOpenOnClick = true };
            mi.Click += (_, _) => ProcessColumns.SetVisible(col, mi.IsChecked);
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = L("Restaurar colunas padrão", "Restore default columns") };
        reset.Click += (_, _) => ProcessColumns.Reset();
        menu.Items.Add(reset);
    }

    private void OnColumnsButton(object s, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)s, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        FillColumnsMenu(menu);
        menu.IsOpen = true;
    }

    private void OnFitName(object s, RoutedEventArgs e)
    {
        var c = (ColumnDef)((FrameworkElement)s).Tag;
        if (c.Width > 0) { c.Width = 0; ProcessColumns.Save(); return; }
        var tf = new System.Windows.Media.Typeface(List.FontFamily, List.FontStyle, List.FontWeight, List.FontStretch);
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip, max = 0;
        foreach (var r in _items)
        {
            var ft = new System.Windows.Media.FormattedText(r.NameText, System.Globalization.CultureInfo.CurrentCulture, FlowDirection, tf, List.FontSize, System.Windows.Media.Brushes.Black, dpi);
            max = Math.Max(max, r.Depth * 18 + 62 + Math.Min(ft.WidthIncludingTrailingWhitespace, 700));
        }
        c.Width = Math.Clamp(Math.Ceiling(max), 200, 800);
        ProcessColumns.Save();
    }

    private void OnSplitDrag(object s, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        var c = (ColumnDef)((FrameworkElement)s).Tag;
        c.Width = Math.Clamp(c.Width + e.HorizontalChange, 50, 800);
    }

    private void SortBy(ProcSort sort)
    {
        if (_query.Sort == sort) _query.Descending = !_query.Descending;
        else { _query.Sort = sort; _query.Descending = DescendingFirst(sort); }
        _ready = false; SortBox.SelectedIndex = (int)sort; _ready = true;
        UpdateHeaders();
        ApplyTexts();
        Rebuild();
    }

    private static bool DescendingFirst(ProcSort s) => s is ProcSort.Cpu or ProcSort.Memory or ProcSort.Disk or ProcSort.Network or ProcSort.Threads or ProcSort.Started;

    private void OnMetricChanged(object s, SelectionChangedEventArgs e) { if (_ready) UpdateChart(); }

    private void UpdateChart()
    {
        Chart.SetData(_items, Math.Max(0, MetricBox.SelectedIndex), Selected);
        ChartHint.Text = Selected is { } r ? $"{r.NameText} (PID {r.Pid})" : L("Clique em um processo para destacá-lo", "Click a process to highlight it");
    }
    private void UpdateRunning()
    {
        if (IsLoaded && IsVisible) Start(); else Stop();
    }

    private void Start()
    {
        if (_running) return;
        var secs = RefreshBox.SelectedIndex switch { 0 => 1, 1 => 2, 2 => 5, _ => 0 };
        if (secs == 0) return;
        _running = true;
        _sampler.Reset();
        _sampler.Net.Start();
        _timer.Interval = TimeSpan.FromSeconds(secs);
        _timer.Start();
        _ = TickAsync();
    }

    private void Stop()
    {
        _running = false;
        _timer.Stop();
        _sampler.Net.Stop();
    }

    private void OnRefreshChanged(object s, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        Stop();
        UpdateRunning();
    }

    private async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var samples = await Task.Run(_sampler.Sample);
            if (!_running) return;
            ProcessRow.TotalRam = _sampler.TotalRam;
            ProcessRow.LinkBytesPerSec = _sampler.LinkBytesPerSec;
            ProcessRow.NetAvailable = !_sampler.Net.Failed;
            Merge(samples);
        }
        catch { }
        finally { _busy = false; }
    }

    private void Merge(List<ProcSample> samples)
    {
        var keep = new HashSet<(int, long)>();
        foreach (var s in samples)
        {
            var key = (s.Pid, s.Created);
            keep.Add(key);
            if (_rows.TryGetValue(key, out var row)) row.Update(s);
            else _rows[key] = new ProcessRow(s);
        }
        foreach (var k in _rows.Keys.Where(k => !keep.Contains(k)).ToList()) _rows.Remove(k);
        foreach (var r in _rows.Values) r.Record();
        _totCpu = Math.Min(100, _rows.Values.Sum(r => r.OwnCpu));
        _totMem = _rows.Values.Sum(r => r.OwnMem) * 100.0 / Math.Max(1, ProcessRow.TotalRam);
        _totDisk = Math.Min(100, _rows.Values.Sum(r => r.OwnDisk) / ProcessRow.DiskRefBps * 100);
        _totNet = Math.Min(100, _rows.Values.Sum(r => r.OwnNet) / ProcessRow.LinkBytesPerSec * 100);
        UpdateHeaders();
        Rebuild();
        var apps = _rows.Values.Count(r => r.Type == ProcType.App);
        SummaryText.Text = L($"{_rows.Count} processos  •  {apps} aplicativos", $"{_rows.Count} processes  •  {apps} apps");
    }

    private void Rebuild()
    {
        var list = ProcessTree.Arrange(_rows.Values, _query);
        for (int i = 0; i < list.Count; i++)
        {
            if (i < _items.Count && ReferenceEquals(_items[i], list[i])) continue;
            int at = -1;
            for (int j = i + 1; j < _items.Count; j++) if (ReferenceEquals(_items[j], list[i])) { at = j; break; }
            if (at >= 0) _items.Move(at, i); else _items.Insert(i, list[i]);
        }
        while (_items.Count > list.Count) _items.RemoveAt(_items.Count - 1);
        UpdateChart();
    }

    private void OnQueryChanged(object s, EventArgs e)
    {
        if (!_ready) return;
        _query.Search = SearchBox.Text;
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _query.Type = TypeBox.SelectedIndex switch { 1 => ProcType.App, 2 => ProcType.Background, 3 => ProcType.Windows, _ => null };
        _query.Hung = StatusBox.SelectedIndex switch { 1 => false, 2 => true, _ => null };
        var sort = (ProcSort)Math.Max(0, SortBox.SelectedIndex);
        if (sort != _query.Sort)
        {
            _query.Sort = sort;
            _query.Descending = DescendingFirst(sort);
        }
        UpdateHeaders();
        Rebuild();
    }

    private void OnToggleDirection(object s, RoutedEventArgs e)
    {
        _query.Descending = !_query.Descending;
        ApplyTexts();
        Rebuild();
    }

    private void SetAllExpanded(bool value)
    {
        foreach (var r in _rows.Values) r.IsExpanded = value;
        Rebuild();
    }

    private void OnExpandAll(object s, RoutedEventArgs e) => SetAllExpanded(true);
    private void OnCollapseAll(object s, RoutedEventArgs e) => SetAllExpanded(false);

    private void OnToggleExpand(object s, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { DataContext: ProcessRow r }) { r.IsExpanded = !r.IsExpanded; Rebuild(); }
        e.Handled = true;
    }

    private void OnRowDoubleClick(object s, MouseButtonEventArgs e)
    {
        if (Selected is { ShowExpander: true } r && e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase)
        { r.IsExpanded = !r.IsExpanded; Rebuild(); }
    }

    private void OnListKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { OnEndTask(s, e); e.Handled = true; }
        else if (Selected is { } r && e.Key == Key.Right && r.ShowExpander && !r.IsExpanded) { r.IsExpanded = true; Rebuild(); e.Handled = true; }
        else if (Selected is { } r2 && e.Key == Key.Left && r2.IsExpanded) { r2.IsExpanded = false; Rebuild(); e.Handled = true; }
    }

    private ProcessRow? Selected => List.SelectedItem as ProcessRow;

    private void OnClickOutside(object s, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d != null; d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ListBoxItem or System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Selector or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Thumb or ProcessTimeline) return;
        List.SelectedItem = null;
    }

    private void OnSelectionChanged(object s, SelectionChangedEventArgs e)
    {
        UpdateButtons();
        UpdateChart();
        _ = ShowDetailsAsync(Selected);
    }

    private void UpdateButtons()
    {
        var r = Selected;
        EndButton.IsEnabled = r != null && !IsProtected(r);
        EndTreeButton.IsEnabled = EndButton.IsEnabled;
        RestartButton.IsEnabled = r != null && !IsProtected(r) && r.Path.Length > 0;
        PriorityButton.IsEnabled = EndButton.IsEnabled;
        LocationButton.IsEnabled = CopyButton.IsEnabled = r != null && r.Path.Length > 0;
    }

    private static bool IsProtected(ProcessRow r) =>
        r.Pid <= 4 || r.Pid == Environment.ProcessId || Critical.Contains(r.Name);

    private async Task ShowDetailsAsync(ProcessRow? r)
    {
        int token = ++_detailToken;
        if (r == null) { DetailsCard.Visibility = Visibility.Collapsed; return; }
        DetailsCard.Visibility = Visibility.Visible;
        var (cmd, prio) = await Task.Run(() =>
        {
            string? c = ProcessSampler.GetCommandLine(r.Pid);
            string p = "—";
            try { p = Process.GetProcessById(r.Pid).PriorityClass.ToString(); } catch { }
            return (c, p);
        });
        if (token != _detailToken) return;

        var parent = _rows.Values.FirstOrDefault(x => x.Pid == r.ParentPid);
        var tb = DetailsText;
        tb.Inlines.Clear();
        void Line(string label, string value)
        {
            tb.Inlines.Add(new System.Windows.Documents.Run(label + ": ") { FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new System.Windows.Documents.Run(value));
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
        }
        tb.Inlines.Add(new System.Windows.Documents.Run(r.NameText) { FontSize = 15, FontWeight = FontWeights.SemiBold });
        tb.Inlines.Add(new System.Windows.Documents.LineBreak());
        Line("PID", $"{r.Pid}   •   " + L("Processo pai", "Parent") + $": {(parent != null ? parent.Name + " (" + parent.Pid + ")" : r.ParentPid.ToString())}");
        Line(L("Arquivo", "File"), r.Name);
        Line(L("Caminho", "Path"), r.Path.Length > 0 ? r.Path : L("Acesso negado ou indisponível", "Access denied or unavailable"));
        if (!string.IsNullOrWhiteSpace(cmd)) Line(L("Linha de comando", "Command line"), cmd!);
        Line(L("Início", "Started"), r.Created > 0 ? DateTime.FromFileTime(r.Created).ToString("g") : "—");
        Line(L("Threads", "Threads"), r.Threads.ToString());
        Line(L("Prioridade", "Priority"), prio);
        Line(L("Memória", "Memory"), r.MemTip + $"   •   {L("Disco", "Disk")}: {r.DiskTip}   •   {L("Rede", "Network")}: {r.NetTip}");
        tb.Inlines.Remove(tb.Inlines.LastInline);
    }

    private void OnRowRightClick(object s, MouseButtonEventArgs e)
    {
        var el = e.OriginalSource as DependencyObject;
        while (el != null && el is not ListBoxItem) el = System.Windows.Media.VisualTreeHelper.GetParent(el);
        if (el is ListBoxItem item) item.IsSelected = true;
    }

    private void OnEndTree(object s, RoutedEventArgs e) { if (Selected is { } r && !IsProtected(r)) EndTree(r); }
    private void OnOpenLocationButton(object s, RoutedEventArgs e) { if (Selected is { } r) OpenLocation(r); }
    private void OnCopyPathButton(object s, RoutedEventArgs e) { if (Selected is { } r) Clipboard.SetText(r.Path); }

    private void OnPriorityButton(object s, RoutedEventArgs e)
    {
        if (Selected is not { } r) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)s, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        AddPriorityItems(menu.Items, r);
        menu.IsOpen = true;
    }

    private static void AddPriorityItems(ItemCollection items, ProcessRow r)
    {
        foreach (var (pc, pt, en) in new[]
        {
            (ProcessPriorityClass.High, "Alta", "High"), (ProcessPriorityClass.AboveNormal, "Acima do normal", "Above normal"),
            (ProcessPriorityClass.Normal, "Normal", "Normal"), (ProcessPriorityClass.BelowNormal, "Abaixo do normal", "Below normal"),
            (ProcessPriorityClass.Idle, "Baixa", "Low"),
        })
        {
            var mi = new MenuItem { Header = L(pt, en) };
            mi.Click += (_, _) => SetPriority(r, pc);
            items.Add(mi);
        }
    }

    private void OnContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        var r = Selected;
        if (r == null) { e.Handled = true; return; }
        var menu = List.ContextMenu!;
        menu.Items.Clear();
        bool prot = IsProtected(r);

        MenuItem Item(string header, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            return mi;
        }

        menu.Items.Add(Item(L("⛔  Finalizar tarefa", "⛔  End task"), () => OnEndTask(this, new RoutedEventArgs()), !prot));
        menu.Items.Add(Item(L("🌳  Finalizar árvore de processos", "🌳  End process tree"), () => EndTree(r), !prot));
        menu.Items.Add(Item(L("🔄  Reiniciar", "🔄  Restart"), () => OnRestart(this, new RoutedEventArgs()), !prot && r.Path.Length > 0));
        menu.Items.Add(new Separator());
        var prio = new MenuItem { Header = L("⚡  Prioridade", "⚡  Priority"), IsEnabled = !prot };
        AddPriorityItems(prio.Items, r);
        menu.Items.Add(prio);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L("📂  Abrir local do arquivo", "📂  Open file location"), () => OpenLocation(r), r.Path.Length > 0));
        menu.Items.Add(Item(L("📋  Copiar nome", "📋  Copy name"), () => Clipboard.SetText(r.Name)));
        menu.Items.Add(Item(L("📋  Copiar PID", "📋  Copy PID"), () => Clipboard.SetText(r.Pid.ToString())));
        menu.Items.Add(Item(L("📋  Copiar caminho", "📋  Copy path"), () => Clipboard.SetText(r.Path), r.Path.Length > 0));
    }

    private static Process? Open(ProcessRow r)
    {
        var p = Process.GetProcessById(r.Pid);
        if (r.Created > 0 && p.StartTime.ToFileTime() != r.Created && Math.Abs(p.StartTime.ToFileTime() - r.Created) > 10_000_000)
            throw new InvalidOperationException(L("O processo já foi encerrado.", "The process has already exited."));
        return p;
    }

    private static bool Confirm(string text) =>
        MessageBox.Show(text, "Stats Monitor", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private static void Fail(Exception ex) =>
        MessageBox.Show(ex.Message, "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Error);

    private void OnEndTask(object s, RoutedEventArgs e)
    {
        if (Selected is not { } r || IsProtected(r)) return;
        if (!Confirm(L($"Finalizar \"{r.NameText}\" (PID {r.Pid})?\nDados não salvos serão perdidos.",
                       $"End \"{r.NameText}\" (PID {r.Pid})?\nUnsaved data will be lost."))) return;
        try { using var p = Open(r)!; p.Kill(); }
        catch (Exception ex) { Fail(ex); }
        _ = TickAsync();
    }

    private void EndTree(ProcessRow r)
    {
        if (!Confirm(L($"Finalizar \"{r.NameText}\" e todos os seus subprocessos?\nDados não salvos serão perdidos.",
                       $"End \"{r.NameText}\" and all of its child processes?\nUnsaved data will be lost."))) return;
        try { using var p = Open(r)!; p.Kill(true); }
        catch (Exception ex) { Fail(ex); }
        _ = TickAsync();
    }

    private async void OnRestart(object s, RoutedEventArgs e)
    {
        if (Selected is not { } r || IsProtected(r) || r.Path.Length == 0) return;
        if (!Confirm(L($"Reiniciar \"{r.NameText}\"?\nO processo será finalizado (dados não salvos serão perdidos) e iniciado novamente.\nObs.: o novo processo será executado com os mesmos privilégios deste aplicativo (administrador).",
                       $"Restart \"{r.NameText}\"?\nThe process will be ended (unsaved data will be lost) and started again.\nNote: the new process runs with this app's privileges (administrator)."))) return;
        try
        {
            var cmd = await Task.Run(() => ProcessSampler.GetCommandLine(r.Pid));
            using (var p = Open(r)!) { p.Kill(); p.WaitForExit(5000); }
            var psi = new ProcessStartInfo(r.Path)
            {
                Arguments = ProcessTree.StripExecutable(cmd ?? "", r.Path),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(r.Path) ?? "",
            };
            Process.Start(psi);
        }
        catch (Exception ex) { Fail(ex); }
        _ = TickAsync();
    }

    private static void SetPriority(ProcessRow r, ProcessPriorityClass pc)
    {
        try { using var p = Open(r)!; p.PriorityClass = pc; }
        catch (Exception ex) { Fail(ex); }
    }

    private static void OpenLocation(ProcessRow r)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{r.Path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Fail(ex); }
    }
}
