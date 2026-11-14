using System.Text;
using System.Windows;
using System.Windows.Controls;
using StatsMonitor.Helpers;
using StatsMonitor.Services;

namespace StatsMonitor.Views;

public partial class SystemInfoView : UserControl
{
    private List<InfoGroup> _groups = [];

    private Func<Task> _load = () => Task.CompletedTask;

    private string _filter = "all";

    /// <summary>Filters the unified page: "all", "system" or "peripherals".</summary>
    public void ShowSection(string section)
    {
        _filter = section;
        FilterAll.IsChecked = section == "all";
        FilterSystem.IsChecked = section == "system";
        FilterPeripherals.IsChecked = section == "peripherals";
        ApplyFilter();
    }

    private void OnFilter(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && tag != _filter) { _filter = tag; ApplyFilter(); }
    }

    private void ApplyFilter()
    {
        if (Component is not null) { Groups.ItemsSource = _groups; return; }
        var shown = _groups.Where(g => _filter == "all" || g.Section == _filter).ToList();
        var view = new System.Windows.Data.ListCollectionView(shown);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(InfoGroup.Section)));
        Groups.ItemsSource = view;
        EmptyHint.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = Loc.T("{0} grupos · {1} itens", shown.Count, shown.Sum(g => g.Lines.Count));
    }

    private static async Task<List<InfoGroup>> LoadUnified()
    {
        var system = await SystemInfoService.GetAsync();
        var peripherals = await PeripheralInfoService.GetAsync();
        return [.. system.Select(g => g with { Section = "system" }), .. peripherals.Select(g => g with { Section = "peripherals" })];
    }

    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        RescanPanelButton.IsEnabled = false;
        try
        {
            if (Component is null) SystemInfoService.Invalidate();
            ComponentDetailService.Invalidate();
            await _load();
        }
        finally { RescanPanelButton.IsEnabled = true; }
    }

    public ComponentKind? Component { get; }

    private sealed class ChartSeries : System.ComponentModel.INotifyPropertyChanged
    {
        public string Title { get; init; } = "";
        public string Unit { get; init; } = "%";
        public System.Windows.Media.Brush Stroke { get; init; } = System.Windows.Media.Brushes.DodgerBlue;
        public double Floor { get; init; } = 100;
        public bool Auto { get; init; }
        public Func<double> Sample { get; init; } = () => 0;
        public Func<double, string> Format { get; init; } = v => $"{v:0.0}%";
        public System.Collections.ObjectModel.ObservableCollection<double> Values { get; } = new(new double[60]);
        private string _current = "-";
        public string Current { get => _current; private set { _current = value; PropertyChanged?.Invoke(this, new(nameof(Current))); } }
        private double _max = 100;
        public double Maximum { get => _max; private set { _max = value; PropertyChanged?.Invoke(this, new(nameof(Maximum))); } }
        private string _stats = "";
        public string Stats { get => _stats; private set { _stats = value; PropertyChanged?.Invoke(this, new(nameof(Stats))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private int _samples;

        public void Tick()
        {
            double v = Sample();
            Values.RemoveAt(0);
            Values.Add(v);
            _samples = Math.Min(_samples + 1, Values.Count);
            var recent = Values.Skip(Values.Count - _samples).ToList();
            Stats = $"{Loc.T("mín")} {Format(recent.Min())}  ·  {Loc.T("méd")} {Format(recent.Average())}  ·  {Loc.T("máx")} {Format(recent.Max())}";
            Current = Format(v);
            Maximum = Auto ? Math.Max(Floor, Values.Max() * 1.25) : Floor;
        }
    }

    private List<ChartSeries> _series = [];

    private static System.Windows.Media.Brush Res(string key) =>
        Application.Current.TryFindResource(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue;

    private static List<ChartSeries> BuildSeries(ComponentKind kind, Func<StatsMonitor.Models.HardwareSnapshot?> snap, Func<NetworkSnapshot?> net)
    {
        string T(string pt, string en) => Loc.Lang == "en" ? en : pt;
        var a1 = Res("Accent"); var a2 = Res("Accent2");
        System.Windows.Media.Brush warn = System.Windows.SystemParameters.HighContrast ? System.Windows.SystemColors.HighlightBrush : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x43));
        double Disks(Func<StatsMonitor.Models.DiskSnapshot, double?> f) => snap()?.Disks.Select(d => f(d) ?? 0).DefaultIfEmpty(0).Max() ?? 0;
        ChartSeries Pct(string title, Func<double> f, System.Windows.Media.Brush b) => new() { Title = title, Stroke = b, Sample = f };
        ChartSeries Temp(string title, Func<double> f) => new()
        {
            Title = title, Stroke = warn, Unit = "°C", Floor = 100, Sample = f,
            Format = v => v > 0 ? Units.Temp(v) : "-",
        };
        ChartSeries Rate(string title, Func<double> f, System.Windows.Media.Brush b, Func<double, string> fmt, string unit, double floor) =>
            new() { Title = title, Stroke = b, Auto = true, Floor = floor, Unit = unit, Sample = f, Format = fmt };

        string Mb(double v) => $"{v / 1048576.0:0.0} MB/s";
        string Mbps(double v) => $"{v * 8 / 1e6:0.0} Mbps";

        return kind switch
        {
            ComponentKind.Cpu => [Pct(T("Uso da CPU", "CPU usage"), () => snap()?.CpuLoad ?? 0, a1), Temp(T("Temperatura da CPU", "CPU temperature"), () => snap()?.CpuTemp ?? 0)],
            ComponentKind.Gpu =>
            [
                Pct(T("Uso da GPU", "GPU usage"), () => snap()?.GpuLoad ?? 0, a2),
                Pct(T("Uso da VRAM", "VRAM usage"), () => snap()?.VramLoad ?? 0, a1),
                Temp(T("Temperatura da GPU", "GPU temperature"), () => snap()?.GpuTemp ?? 0),
            ],
            ComponentKind.Memory =>
            [
                Pct(T("Uso da memória", "Memory usage"), () => snap()?.RamLoad ?? 0, a1),
                new() { Title = T("Memória em uso", "Memory in use"), Stroke = a2, Unit = "GB", Auto = true, Floor = 4, Sample = () => snap()?.RamUsedGb ?? 0, Format = v => $"{v:0.0} GB" },
            ],
            ComponentKind.Disk =>
            [
                Pct(T("Atividade do disco", "Disk activity"), () => Disks(d => d.Activity), a1),
                Rate(T("Leitura", "Read"), () => Disks(d => d.ReadBps), a2, Mb, "bytes/s", 1048576),
                Rate(T("Gravação", "Write"), () => Disks(d => d.WriteBps), a1, Mb, "bytes/s", 1048576),
            ],
            ComponentKind.Network =>
            [
                Rate("Download", () => net()?.DownloadBps ?? 0, a1, Mbps, "bytes/s", 125000),
                Rate("Upload", () => net()?.UploadBps ?? 0, a2, Mbps, "bytes/s", 125000),
            ],
            ComponentKind.Battery =>
            [
                Pct(T("Nível de carga", "Charge level"), () => BatteryService.Latest.Percent ?? 0, a1),
                Rate(T("Potência (carga ou consumo)", "Power (charge or draw)"), () => BatteryService.Latest.RateW ?? 0, a2, v => $"{v:0.0} W", "W", 10),
            ],
            _ => [],
        };
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (Charts.ActualWidth < 1 || Charts.ActualHeight < 1) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = Loc.T("Imagem PNG|*.png"),
            FileName = $"stats-monitor-{Component}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
        };
        if (dlg.ShowDialog() != true) return;

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Charts);
        var bg = Application.Current.TryFindResource("WindowBg") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(Charts.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(Charts.ActualHeight * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(bg, null, new Rect(0, 0, Charts.ActualWidth, Charts.ActualHeight));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(Charts), null, new Rect(0, 0, Charts.ActualWidth, Charts.ActualHeight));
        }
        bmp.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        try { using var fs = System.IO.File.Create(dlg.FileName); enc.Save(fs); }
        catch (System.IO.IOException) { }
    }

    private void SampleCharts() { foreach (var s in _series) s.Tick(); }

    public SystemInfoView() : this(
        () => Loc.T("Sistema e periféricos"), LoadUnified, null, false) { }

    public SystemInfoView(ComponentKind kind, Func<StatsMonitor.Models.HardwareSnapshot?> snap, Func<NetworkSnapshot?> net) : this(
        () => ComponentDetailService.Title(kind),
        () => ComponentDetailService.GetAsync(kind, snap(), net()),
        kind, true, (snap, net)) { }

    private SystemInfoView(Func<string> title, Func<Task<List<InfoGroup>>> loader, ComponentKind? comp, bool twoColumns, (Func<StatsMonitor.Models.HardwareSnapshot?> snap, Func<NetworkSnapshot?> net)? chartSrc = null)
    {
        InitializeComponent();
        Component = comp;
        FilterBar.Visibility = comp is null ? Visibility.Visible : Visibility.Collapsed;
        if (comp is { } ck && chartSrc is not null)
        {
            _series = BuildSeries(ck, chartSrc.Value.snap, chartSrc.Value.net);
            Charts.ItemsSource = _series;
            Charts.Visibility = ExportButton.Visibility = _series.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (twoColumns)
        {
            var panel = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
            panel.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, 2);
            Groups.ItemsPanel = new ItemsPanelTemplate(panel);
        }

        void SetTitle() => PageTitle.Text = title();
        SetTitle();
        bool busy = false;
        async Task Load()
        {
            if (busy) return;
            busy = true;
            try { SetGroups(await loader()); }
            finally { busy = false; }
        }
        _load = Load;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += async (_, _) => { SampleCharts(); await Load(); };
        Action onLang = async () => { SetTitle(); await Load(); };
        Loaded += async (_, _) =>
        {
            SampleCharts();
            await Load();
            Loc.LanguageChanged += onLang;
            if (comp is not null) timer.Start();
        };
        Unloaded += (_, _) => { Loc.LanguageChanged -= onLang; timer.Stop(); };
    }

    private void SetGroups(List<InfoGroup> fresh)
    {
        bool same = _groups.Count == fresh.Count && _groups.Zip(fresh).All(p =>
            p.First.Title == p.Second.Title && p.First.Lines.Count == p.Second.Lines.Count &&
            p.First.Lines.Zip(p.Second.Lines).All(l => l.First.Label == l.Second.Label));
        if (!same)
        {
            _groups = fresh;
            ApplyFilter();
            if (Component is not null) Groups.ItemsSource = _groups;
            return;
        }
        for (int i = 0; i < _groups.Count; i++)
            for (int j = 0; j < _groups[i].Lines.Count; j++)
            {
                _groups[i].Lines[j].Value = fresh[i].Lines[j].Value;
                _groups[i].Lines[j].CopyText = fresh[i].Lines[j].CopyText;
            }
    }
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string text } || string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    public void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (var g in _groups)
        {
            sb.AppendLine($"[{g.Title}]");
            foreach (var l in g.Lines) sb.AppendLine(l.Value.Length == 0 ? l.Label : $"{l.Label}: {l.Value}");
            sb.AppendLine();
        }
        try { Clipboard.SetText(sb.ToString()); }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
