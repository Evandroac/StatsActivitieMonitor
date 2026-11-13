using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.Saved += Close;
        Closed += (_, _) => { if (!vm.IsSaved) vm.Revert(); };

        // Refresh after the bindings have pushed the new value into the view model.
        RoutedEventHandler refresh = (_, _) => Dispatcher.BeginInvoke(RefreshPreview, System.Windows.Threading.DispatcherPriority.Background);
        AddHandler(ToggleButton.CheckedEvent, refresh);
        AddHandler(ToggleButton.UncheckedEvent, refresh);
        Loaded += (_, _) => RefreshPreview();
    }

    private void RefreshPreview()
    {
        var vm = _vm;
        PreviewPanel.Children.Clear();

        if (vm.GadgetNone || !vm.ShowGadget)
        {
            PreviewBox.CornerRadius = new CornerRadius(6);
            PreviewBox.Padding = new Thickness(10, 4, 10, 4);
            PreviewPanel.Children.Add(Line(vm.GadgetNone ? "Sem gadget ao minimizar" : "Gadget desativado", 12, null));
            return;
        }

        bool taskbar = vm.GadgetOnTaskbar;
        string t(string v) => vm.ShowTemperatures ? $" | {v}" : "";
        var rows = new List<string>();
        if (vm.ShowCpuGadget) rows.Add(taskbar ? $"CPU 23%{t("52°C")}" : $"CPU: 23%{t("52°C")}");
        if (vm.ShowRamGadget) rows.Add(taskbar ? "RAM 41%" : "RAM: 41% | 13,2 GB");
        if (vm.ShowGpuGadget) rows.Add(taskbar ? $"GPU 37%{t("58°C")}" : $"GPU: 37%{t("58°C")}");
        if (vm.ShowVramGadget) rows.Add(taskbar ? "VRAM 29%" : "VRAM: 29% | 4,6 GB");
        if (vm.ShowFpsGadget) rows.Add(taskbar ? "FPS 144" : "FPS: 144");
        if (vm.ShowNetworkGadget) rows.Add(taskbar ? "NET ↓ 12,3 Mbps ↑ 1,2 Mbps" : "NET: ↓ 12,3 Mbps ↑ 1,2 Mbps");

        PreviewBox.CornerRadius = new CornerRadius(taskbar ? 6 : 10);
        PreviewBox.Padding = taskbar ? new Thickness(10, 4, 10, 4) : new Thickness(12, 8, 12, 8);

        var panel = new StackPanel { Orientation = taskbar ? Orientation.Horizontal : Orientation.Vertical };
        if (rows.Count == 0) rows.Add("Nenhum indicador selecionado");
        for (int i = 0; i < rows.Count; i++)
            panel.Children.Add(Line(rows[i], taskbar ? 12 : 13, taskbar && i > 0 ? new Thickness(14, 0, 0, 0) : null));
        PreviewPanel.Children.Add(panel);
    }

    private static TextBlock Line(string text, double size, Thickness? margin)
    {
        var tb = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = size };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "GadgetText");
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }
}
