using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel _vm;

    public event Action? CloseRequested;

    public SettingsView(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.Saved += () => CloseRequested?.Invoke();

        // Refresh after the bindings have pushed the new value into the view model.
        RoutedEventHandler refresh = (_, _) => Dispatcher.BeginInvoke(RefreshPreview, System.Windows.Threading.DispatcherPriority.Background);
        AddHandler(ToggleButton.CheckedEvent, refresh);
        AddHandler(ToggleButton.UncheckedEvent, refresh);
        Loaded += (_, _) => RefreshPreview();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _vm.Revert();
        CloseRequested?.Invoke();
    }

    /// <summary>Discards anything not applied or saved (used when navigating away).</summary>
    public void Discard() { if (!_vm.IsSaved) _vm.Revert(); }

    private void RefreshPreview()
    {
        var vm = _vm;
        string t(string v) => vm.ShowTemperatures ? $" | {v}" : "";
        var floating = new List<string>();
        var taskbar = new List<string>();
        void Add(bool on, string f, string tb) { if (on) { floating.Add(f); taskbar.Add(tb); } }
        Add(vm.ShowCpuGadget, $"CPU: 23%{t("52°C")}", $"CPU 23%{t("52°C")}");
        Add(vm.ShowRamGadget, "RAM: 41% | 13,2 GB", "RAM 41%");
        Add(vm.ShowGpuGadget, $"GPU: 37%{t("58°C")}", $"GPU 37%{t("58°C")}");
        Add(vm.ShowVramGadget, "VRAM: 29% | 4,6 GB", "VRAM 29%");
        Add(vm.ShowFpsGadget, "FPS: 144", "FPS 144");
        Add(vm.ShowNetworkGadget, "NET: ↓ 12,3 Mbps ↑ 1,2 Mbps", "NET ↓ 12,3 Mbps ↑ 1,2 Mbps");

        bool off = vm.GadgetNone || !vm.ShowGadget;
        if (floating.Count == 0) { floating.Add("Nenhum indicador"); taskbar.Add("Nenhum indicador"); }

        Fill(FloatingPanel, floating, 13, false);
        Fill(TaskbarPanel, taskbar, 12, true);
        Fill(BarPanel, taskbar, 12, true);
        Fill(TopPanel, taskbar, 12, true);

        Mark(FloatingBox, FloatingCaption, "Flutuante", vm.GadgetFloating && !off, off);
        Mark(TaskbarBox, TaskbarCaption, "Barra de tarefas", vm.GadgetOnTaskbar && !off, off);
        Mark(BarBox, BarCaption, "Barra flutuante", vm.GadgetFloatingBar && !off, off);
        Mark(TopBox, TopCaption, "Faixa no topo", vm.GadgetDockedTop && !off, off);
    }

    private static void Fill(Panel panel, List<string> rows, double size, bool horizontal)
    {
        panel.Children.Clear();
        for (int i = 0; i < rows.Count; i++)
            panel.Children.Add(Line(rows[i], size, horizontal ? new Thickness(i > 0 ? 14 : 0, 0, 0, 0) : null));
    }

    // The mode currently selected gets an accent border; everything dims when the gadget won't be shown.
    private static void Mark(Border box, TextBlock caption, string name, bool selected, bool off)
    {
        box.SetResourceReference(Border.BorderBrushProperty, selected ? "Accent" : "CardBorder");
        box.Opacity = off ? 0.45 : selected ? 1 : 0.8;
        caption.Text = selected ? $"{name} ✔ (selecionado)" : name;
    }
    private static TextBlock Line(string text, double size, Thickness? margin)
    {
        var tb = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = size };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "GadgetText");
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }
}
