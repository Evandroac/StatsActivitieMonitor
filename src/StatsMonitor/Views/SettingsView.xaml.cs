using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using StatsMonitor.Helpers;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel _vm;
    private readonly AppearanceView? _appearance;

    public event Action? CloseRequested;

    public string CurrentSection { get; private set; } = "general";

    public SettingsView(SettingsViewModel vm, AppearanceView? appearance = null)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _appearance = appearance;
        AlertHistoryList.ItemsSource = StatsMonitor.Services.AlertHistory.Entries;
        StatsMonitor.Services.AlertHistory.Entries.CollectionChanged += (_, _) => UpdateHistoryEmpty();
        UpdateHistoryEmpty();
        if (appearance is not null)
        {
            appearance.Embedded();
            AppearanceHost.Content = appearance;
        }
        _syncingTabs = true;
        TabGeneral.IsChecked = true;
        _syncingTabs = false;
        SettingsScroll.ScrollChanged += OnScrollChanged;
        if (appearance is not null)
            appearance.PreviewMouseWheel += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInsideList(d)) return;
                e.Handled = true;
                SettingsScroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = s });
            };

        // Refresh after the bindings have pushed the new value into the view model.
        RoutedEventHandler refresh = (_, _) => Dispatcher.BeginInvoke(RefreshPreview, System.Windows.Threading.DispatcherPriority.Background);
        AddHandler(ToggleButton.CheckedEvent, refresh);
        AddHandler(ToggleButton.UncheckedEvent, refresh);
        AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => Dispatcher.BeginInvoke(RefreshPreview, System.Windows.Threading.DispatcherPriority.Background)));
        Action onLang = () => { RefreshPreview(); RefreshTitles(); };
        Loaded += (_, _) => { RefreshPreview(); RefreshTitles(); Loc.LanguageChanged += onLang; Units.Changed += onLang; };
        Unloaded += (_, _) => { Loc.LanguageChanged -= onLang; Units.Changed -= onLang; };
    }

    private void UpdateHistoryEmpty() =>
        AlertHistoryEmpty.Visibility = StatsMonitor.Services.AlertHistory.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => StatsMonitor.Services.DataLogger.OpenFolder();

    private void OnClearAlertHistory(object sender, RoutedEventArgs e) => StatsMonitor.Services.AlertHistory.Clear();

    public event Action? TestAlertRequested;

    private void OnTestAlert(object sender, RoutedEventArgs e) => TestAlertRequested?.Invoke();

    private void RefreshTitles()
    {
        TxtTempAlerts.Text = Loc.T("🌡️  Alertas de temperatura ({0})", Units.Symbol);
        AppearanceTitle.Text = Loc.T("🎨  Personalização");
    }

    /// <summary>Persists any pending change (used when navigating away).</summary>
    public void Discard() { _vm.Flush(); _appearance?.Discard(); }

    private static bool IsInsideList(DependencyObject d)
    {
        for (; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is ListBox) return true;
        return false;
    }

    private bool _syncingTabs;

    private (string Tag, FrameworkElement Section)[] Sections => new (string, FrameworkElement)[]
    {
        ("general", SecGeneral), ("gadget", SecGadget), ("panels", SecPanels),
        ("alerts", SecAlerts), ("logging", SecLogging), ("appearance", SecAppearance), ("profiles", SecProfiles),
    };

    private void OnSection(object sender, RoutedEventArgs e)
    {
        if (_syncingTabs || sender is not RadioButton { Tag: string tag }) return;
        CurrentSection = tag;
        ScrollTo(tag);
    }

    private void ScrollTo(string tag)
    {
        var section = Sections.FirstOrDefault(s => s.Tag == tag).Section;
        if (section is null) return;
        if (!IsLoaded)
        {
            RoutedEventHandler? h = null;
            h = (_, _) => { Loaded -= h; ScrollTo(tag); };
            Loaded += h;
            return;
        }
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            double y = section.TransformToAncestor(SectionsStack).Transform(new Point(0, 0)).Y;
            SettingsScroll.ScrollToVerticalOffset(Math.Max(0, y + SectionsStack.Margin.Top - 6));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Highlights in the menu the section currently at the top of the page.
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;
        string current = "general";
        foreach (var (tag, section) in Sections)
        {
            double top = section.TransformToAncestor(SectionsStack).Transform(new Point(0, 0)).Y + SectionsStack.Margin.Top;
            if (top <= SettingsScroll.VerticalOffset + 40) current = tag;
        }
        if (SettingsScroll.ScrollableHeight > 0 && SettingsScroll.VerticalOffset >= SettingsScroll.ScrollableHeight - 1) current = "profiles";
        if (current == CurrentSection) return;
        CurrentSection = current;
        _syncingTabs = true;
        foreach (var rb in LogicalChildren<RadioButton>(this))
            if ((string?)rb.Tag == current) rb.IsChecked = true;
        _syncingTabs = false;
    }

    public void SelectSection(string tag)
    {
        foreach (var rb in LogicalChildren<RadioButton>(this))
            if ((string?)rb.Tag == tag) { rb.IsChecked = true; return; }
    }

    private static IEnumerable<T> LogicalChildren<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var c in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (c is T t) yield return t;
            foreach (var d in LogicalChildren<T>(c)) yield return d;
        }
    }

    /// <summary>All sections are visible, so the highlighter finds any text; appearance results are also scrolled into view.</summary>
    public void RevealText(bool preferAppearance) { if (preferAppearance) SelectSection("appearance"); }
    private void OnResetColors(object sender, RoutedEventArgs e) => _appearance?.ResetColors();

    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Loc.T("Restaurar todas as configurações para o padrão?"), "Stats Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _vm.ResetDefaults();
        RefreshPreview();
    }

    private void OnApplyProfile(object sender, RoutedEventArgs e) => _vm.ApplySelectedProfile();
    private void OnSaveProfile(object sender, RoutedEventArgs e) => _vm.SaveProfile();
    private void OnDeleteProfile(object sender, RoutedEventArgs e) => _vm.DeleteProfile();

    private const string JsonFilter = "JSON (*.json)|*.json";

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = JsonFilter, FileName = "StatsMonitor-settings.json" };
        if (dlg.ShowDialog() != true) return;
        try { _vm.Export(dlg.FileName); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = JsonFilter };
        if (dlg.ShowDialog() != true) return;
        if (!_vm.Import(dlg.FileName))
            MessageBox.Show(Loc.T("Arquivo de configurações inválido."), "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
        else RefreshPreview();
    }

    private void RefreshPreview()
    {
        var vm = _vm;
        string t(string v) => vm.ShowTemperatures ? $" | {v}" : "";
        var floating = new List<string>();
        var taskbar = new List<string>();
        void Add(bool on, string f, string tb) { if (on) { floating.Add(f); taskbar.Add(tb); } }
        Add(vm.ShowCpuGadget, $"CPU: 23%{t(Units.Temp(52))}", $"CPU 23%{t(Units.Temp(52))}");
        Add(vm.ShowRamGadget, Loc.T("RAM: 41% | 13,2 GB"), "RAM 41%");
        Add(vm.ShowGpuGadget, $"GPU: 37%{t(Units.Temp(58))}", $"GPU 37%{t(Units.Temp(58))}");
        Add(vm.ShowVramGadget, $"{Loc.T("Disco")}: 12%", $"{Loc.T("Disco")} 12%");
        Add(vm.ShowFpsGadget, "FPS: 144", "FPS 144");
        Add(vm.ShowNetworkGadget, Loc.T("NET: ↓ 12,3 Mbps ↑ 1,2 Mbps"), Loc.T("NET ↓ 12,3 Mbps ↑ 1,2 Mbps"));

        bool off = vm.GadgetNone || !vm.ShowGadget;
        if (floating.Count == 0) { floating.Add(Loc.T("Nenhum indicador")); taskbar.Add(Loc.T("Nenhum indicador")); }

        Fill(FloatingPanel, floating, 13, false);
        Fill(BottomPanel, taskbar, 12, true);
        Fill(BarPanel, taskbar, 12, true);
        Fill(TopPanel, taskbar, 12, true);

        bool corner = vm.GadgetTopLeft || vm.GadgetTopRight || vm.GadgetBottomLeft || vm.GadgetBottomRight;
        FloatingBox.Visibility = Show((vm.GadgetFloating || corner) && !off);
        FloatingBox.HorizontalAlignment = vm.GadgetTopLeft || vm.GadgetBottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        FloatingBox.VerticalAlignment = vm.GadgetBottomLeft || vm.GadgetBottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        FloatingBox.Margin = vm.GadgetFloating ? new Thickness(0, 24, 24, 0)
            : new Thickness(vm.GadgetTopLeft || vm.GadgetBottomLeft ? 10 : 0, vm.GadgetTopLeft || vm.GadgetTopRight ? 10 : 0, vm.GadgetTopRight || vm.GadgetBottomRight ? 10 : 0, vm.GadgetBottomLeft || vm.GadgetBottomRight ? 42 : 0);
        BottomBox.Visibility = Show(vm.GadgetDockedBottom && !off);
        BarBox.Visibility = Show(vm.GadgetFloatingBar && !off);
        TopBox.Visibility = Show(vm.GadgetDockedTop && !off);
        OffText.Parent.SetValue(UIElement.VisibilityProperty, Show(off));
        ModeBadge.Text = Loc.T(off ? "Desativado" : vm.GadgetFloating ? "Flutuante" : vm.GadgetTopLeft ? "Canto superior esquerdo" : vm.GadgetTopRight ? "Canto superior direito" : vm.GadgetBottomLeft ? "Canto inferior esquerdo" : vm.GadgetBottomRight ? "Canto inferior direito" : vm.GadgetDockedBottom ? "Faixa na parte inferior" : vm.GadgetFloatingBar ? "Barra flutuante" : "Faixa no topo");
    }

    private static Visibility Show(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    private static void Fill(Panel panel, List<string> rows, double size, bool horizontal)
    {
        panel.Children.Clear();
        for (int i = 0; i < rows.Count; i++)
            panel.Children.Add(Line(rows[i], size, horizontal ? new Thickness(i > 0 ? 14 : 0, 0, 0, 0) : null));
    }

    private static TextBlock Line(string text, double size, Thickness? margin)
    {
        var tb = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = size };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "GadgetText");
        if (margin.HasValue) tb.Margin = margin.Value;
        return tb;
    }
}
