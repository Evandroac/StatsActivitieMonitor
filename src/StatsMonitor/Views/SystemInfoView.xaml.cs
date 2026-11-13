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

    public SystemInfoView(bool peripherals = false) : this(
        () => Loc.T(peripherals ? "Periféricos" : "Sistema"),
        () => peripherals ? PeripheralInfoService.GetAsync() : SystemInfoService.GetAsync(),
        peripherals, null, peripherals) { }

    public SystemInfoView(ComponentKind kind, Func<StatsMonitor.Models.HardwareSnapshot?> snap, Func<NetworkSnapshot?> net) : this(
        () => ComponentDetailService.Title(kind),
        () => ComponentDetailService.GetAsync(kind, snap(), net()),
        false, kind, true) { }

    private SystemInfoView(Func<string> title, Func<Task<List<InfoGroup>>> loader, bool peripherals, ComponentKind? comp, bool twoColumns)
    {
        InitializeComponent();
        IsPeripherals = peripherals;
        Component = comp;
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
        timer.Tick += async (_, _) => await Load();
        Action onLang = async () => { SetTitle(); await Load(); };
        Loaded += async (_, _) =>
        {
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
