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

    public bool IsPeripherals { get; }

    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        RescanPanelButton.IsEnabled = false;
        try
        {
            if (!IsPeripherals) SystemInfoService.Invalidate();
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
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public void Tick()
        {
            double v = Sample();
            Values.RemoveAt(0);
            Values.Add(v);
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
        System.Windows.Media.Brush warn = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x9F, 0x43));
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
            _ => [],
        };
    }

    private void SampleCharts() { foreach (var s in _series) s.Tick(); }

    public SystemInfoView(bool peripherals = false) : this(
        () => Loc.T(peripherals ? "Periféricos" : "Sistema"),
        () => peripherals ? PeripheralInfoService.GetAsync() : SystemInfoService.GetAsync(),
        peripherals, null, peripherals) { }

    public SystemInfoView(ComponentKind kind, Func<StatsMonitor.Models.HardwareSnapshot?> snap, Func<NetworkSnapshot?> net) : this(
        () => ComponentDetailService.Title(kind),
        () => ComponentDetailService.GetAsync(kind, snap(), net()),
        false, kind, true, (snap, net)) { }

    private SystemInfoView(Func<string> title, Func<Task<List<InfoGroup>>> loader, bool peripherals, ComponentKind? comp, bool twoColumns, (Func<StatsMonitor.Models.HardwareSnapshot?> snap, Func<NetworkSnapshot?> net)? chartSrc = null)
    {
        InitializeComponent();
        IsPeripherals = peripherals;
        Component = comp;
        if (comp is { } ck && chartSrc is not null)
        {
            _series = BuildSeries(ck, chartSrc.Value.snap, chartSrc.Value.net);
            Charts.ItemsSource = _series;
            Charts.Visibility = _series.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
            Groups.ItemsSource = _groups;
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
