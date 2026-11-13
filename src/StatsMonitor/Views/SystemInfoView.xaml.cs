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
        RescanButton.IsEnabled = RescanPanelButton.IsEnabled = false;
        try
        {
            if (!IsPeripherals) SystemInfoService.Invalidate();
            await _load();
        }
        finally { RescanButton.IsEnabled = RescanPanelButton.IsEnabled = true; }
    }

    public SystemInfoView(bool peripherals = false)
    {
        InitializeComponent();
        IsPeripherals = peripherals;
        if (peripherals) { ActionsPanel.Visibility = Visibility.Visible; RescanButton.Visibility = Visibility.Collapsed; ContentPanel.Margin = new Thickness(20); }
        void SetTitle() => PageTitle.Text = Loc.T(peripherals ? "Periféricos" : "Sistema");
        SetTitle();
        async Task Load()
        {
            _groups = await (peripherals ? PeripheralInfoService.GetAsync() : SystemInfoService.GetAsync());
            Groups.ItemsSource = _groups;
        }
        _load = Load;
        Action onLang = async () => { SetTitle(); await Load(); };
        Loaded += async (_, _) => { await Load(); Loc.LanguageChanged += onLang; };
        Unloaded += (_, _) => Loc.LanguageChanged -= onLang;
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
