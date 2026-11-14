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

    public static readonly string[] Keywords = ["Processos", "Gerenciador de tarefas", "Finalizar tarefa", "Reiniciar", "CPU", "Memória", "Disco", "Rede"];

    public ProcessesView()
    {
        InitializeComponent();
        List.ItemsSource = _items;
        List.ContextMenu = new ContextMenu();
        _timer.Tick += async (_, _) => await TickAsync();
        FillCombos();
        ApplyTexts();
        _ready = true;
        Loaded += (_, _) => UpdateRunning();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateRunning();
        Loc.LanguageChanged += OnLanguage;
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguage;
    }

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;

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
        Fill(SortBox, L("Nome", "Name"), "CPU", L("Memória", "Memory"), L("Disco", "Disk"), L("Rede", "Network"), "PID");
        if (SortBox.SelectedIndex < 0 || !_ready) SortBox.SelectedIndex = 1;
        Fill(RefreshBox, L("1 segundo", "1 second"), L("2 segundos", "2 seconds"), L("5 segundos", "5 seconds"), L("Pausado", "Paused"));
        if (RefreshBox.SelectedIndex < 0 || !_ready) RefreshBox.SelectedIndex = 1;
    }

    private void ApplyTexts()
    {
        PageTitle.Text = "⚙️  " + L("Processos", "Processes");
        SearchBox.ToolTip = L("Pesquisar por nome, PID ou caminho", "Search by name, PID or path");
        SortBox.ToolTip = L("Ordenar por", "Sort by");
        RefreshBox.ToolTip = L("Velocidade de atualização", "Refresh rate");
        DirButton.Content = _query.Descending ? "▼" : "▲";
        DirButton.ToolTip = _query.Descending ? L("Decrescente (maior primeiro)", "Descending (highest first)") : L("Crescente (menor primeiro)", "Ascending (lowest first)");
        ExpandButton.Content = L("➕  Expandir tudo", "➕  Expand all");
        CollapseButton.Content = L("➖  Recolher tudo", "➖  Collapse all");
        EndButton.Content = L("⛔  Finalizar tarefa", "⛔  End task");
        RestartButton.Content = L("🔄  Reiniciar", "🔄  Restart");
        UpdateHeaders();
        UpdateButtons();
    }

    private void UpdateHeaders()
    {
        string Arrow(ProcSort s) => _query.Sort == s ? (_query.Descending ? " ▼" : " ▲") : "";
        HName.Content = L("Nome", "Name") + Arrow(ProcSort.Name);
        HStatus.Content = L("Status", "Status");
        HCpu.Content = "CPU" + Arrow(ProcSort.Cpu);
        HMem.Content = L("Memória", "Memory") + Arrow(ProcSort.Memory);
        HDisk.Content = L("Disco", "Disk") + Arrow(ProcSort.Disk);
        HNet.Content = L("Rede", "Network") + Arrow(ProcSort.Network);
        DirButton.Content = _query.Descending ? "▼" : "▲";
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
    }

    private void OnQueryChanged(object s, EventArgs e)
    {
        if (!_ready) return;
        _query.Search = SearchBox.Text;
        _query.Type = TypeBox.SelectedIndex switch { 1 => ProcType.App, 2 => ProcType.Background, 3 => ProcType.Windows, _ => null };
        _query.Hung = StatusBox.SelectedIndex switch { 1 => false, 2 => true, _ => null };
        var sort = (ProcSort)Math.Max(0, SortBox.SelectedIndex);
        if (sort != _query.Sort)
        {
            _query.Sort = sort;
            _query.Descending = sort is not (ProcSort.Name or ProcSort.Pid);
        }
        UpdateHeaders();
        Rebuild();
    }

    private void OnHeaderClick(object s, RoutedEventArgs e)
    {
        var sort = s == HName ? ProcSort.Name : s == HCpu ? ProcSort.Cpu : s == HMem ? ProcSort.Memory : s == HDisk ? ProcSort.Disk : ProcSort.Network;
        if (_query.Sort == sort) _query.Descending = !_query.Descending;
        else { _query.Sort = sort; _query.Descending = sort != ProcSort.Name; }
        _ready = false; SortBox.SelectedIndex = (int)sort; _ready = true;
        UpdateHeaders();
        ApplyTexts();
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
        if (((FrameworkElement)s).DataContext is ProcessRow r) { r.IsExpanded = !r.IsExpanded; Rebuild(); }
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

    private void OnSelectionChanged(object s, SelectionChangedEventArgs e)
    {
        UpdateButtons();
        _ = ShowDetailsAsync(Selected);
    }

    private void UpdateButtons()
    {
        var r = Selected;
        EndButton.IsEnabled = r != null && !IsProtected(r);
        RestartButton.IsEnabled = r != null && !IsProtected(r) && r.Path.Length > 0;
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
        foreach (var (pc, pt, en) in new[]
        {
            (ProcessPriorityClass.High, "Alta", "High"), (ProcessPriorityClass.AboveNormal, "Acima do normal", "Above normal"),
            (ProcessPriorityClass.Normal, "Normal", "Normal"), (ProcessPriorityClass.BelowNormal, "Abaixo do normal", "Below normal"),
            (ProcessPriorityClass.Idle, "Baixa", "Low"),
        })
            prio.Items.Add(Item(L(pt, en), () => SetPriority(r, pc)));
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
