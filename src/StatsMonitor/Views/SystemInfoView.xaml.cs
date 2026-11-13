using System.Text;
using System.Windows;
using System.Windows.Controls;
using StatsMonitor.Services;

namespace StatsMonitor.Views;

public partial class SystemInfoView : UserControl
{
    private List<InfoGroup> _groups = [];

    public SystemInfoView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            _groups = await SystemInfoService.GetAsync();
            Groups.ItemsSource = _groups;
        };
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string text } || string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var g in _groups)
        {
            sb.AppendLine($"[{g.Title}]");
            foreach (var l in g.Lines) sb.AppendLine($"{l.Label}: {l.Value}");
            sb.AppendLine();
        }
        try { Clipboard.SetText(sb.ToString()); }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}
