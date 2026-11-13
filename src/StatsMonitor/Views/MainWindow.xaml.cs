using System.Windows;
using System.Windows.Controls;
using StatsMonitor.Settings;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsService _settings;
    private GadgetWindow? _gadget;

    public MainWindow(MainViewModel vm, SettingsService settings)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _settings = settings;
        ArrangeCards();
        Loaded += (_, _) => UpdateGadget();
        // Start at (and never shrink below) the size that shows every visible card without scrolling.
        ContentRendered += (_, _) => FitToContent();
        SizeChanged += (_, _) => ArrangeCards();
        StateChanged += (_, _) => UpdateGadget();
        Closed += (_, _) =>
        {
            _gadget?.Close();
            Application.Current.Shutdown();
        };
    }

    private void FitToContent()
    {
        if (SizeToContent == SizeToContent.Manual) return;
        double h = Math.Min(ActualHeight, SystemParameters.WorkArea.Height);
        SizeToContent = SizeToContent.Manual;
        MinHeight = h;
        Height = h;
    }

    private void ApplyPanelSettings()
    {
        ArrangeCards(force: true);
        if (WindowState != WindowState.Normal) return;
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        Dispatcher.BeginInvoke(FitToContent, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private string _layoutKey = "";

    /// <summary>Lays out the enabled cards in 2 columns on wide windows and 1 column on narrow ones.</summary>
    private void ArrangeCards(bool force = false)
    {
        var s = _settings.Current;
        int cols = (ActualWidth > 0 ? ActualWidth : Width) >= 760 ? 2 : 1;
        var cards = new (FrameworkElement el, bool show)[]
        {
            (CpuCard, s.ShowCpuMain), (GpuCard, s.ShowGpuMain), (RamCard, s.ShowRamMain), (VramCard, s.ShowVramMain),
        };
        string key = $"{cols}|{string.Join("", cards.Select(c => c.show ? 1 : 0))}";
        if (!force && key == _layoutKey) return;
        _layoutKey = key;

        CardsGrid.ColumnDefinitions.Clear();
        CardsGrid.RowDefinitions.Clear();
        for (int c = 0; c < cols; c++) CardsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        int visible = 0;
        foreach (var (el, show) in cards)
        {
            el.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) continue;
            Grid.SetRow(el, visible / cols);
            Grid.SetColumn(el, visible % cols);
            visible++;
        }
        int cardRows = (visible + cols - 1) / cols;
        for (int r = 0; r < cardRows + 2; r++) CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        NetCard.Visibility = s.ShowNetworkMain ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(NetCard, cardRows);
        Grid.SetRow(FooterCard, cardRows + 1);
        foreach (var f in new FrameworkElement[] { NetCard, FooterCard })
        {
            Grid.SetColumn(f, 0);
            Grid.SetColumnSpan(f, cols);
        }
    }

    public void AttachGadget(GadgetWindow gadget)
    {
        _gadget = gadget;
        gadget.RestoreRequested += (_, _) => RestoreFromGadget();
    }

    /// <summary>Shows the gadget while minimized (or always, if configured) and when enabled in settings.</summary>
    public void UpdateGadget()
    {
        if (_gadget == null) return;
        var s = _settings.Current;
        bool visible = s.ShowGadget && (s.AlwaysShowGadget || WindowState == WindowState.Minimized);
        if (visible) _gadget.ShowGadget(); else _gadget.Hide();
    }

    private void RestoreFromGadget()
    {
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void OnRunAsAdmin(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt.
        }
    }

    private void OnOpenAppearance(object sender, RoutedEventArgs e)
    {
        var vm = new AppearanceViewModel(_settings, () =>
        {
            _vm.ApplySettings();
            UpdateGadget();
        });
        new AppearanceWindow(vm) { Owner = this }.ShowDialog();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var vm = new SettingsViewModel(_settings, () =>
        {
            _vm.ApplySettings();
            ApplyPanelSettings();
            UpdateGadget();
        });
        new SettingsWindow(vm) { Owner = this }.ShowDialog();
    }
}
