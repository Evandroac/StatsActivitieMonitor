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
        Loaded += (_, _) => { UpdateGadget(); ArrangeCards(); };
        SizeChanged += (_, _) => ArrangeCards();
        StateChanged += (_, _) => UpdateGadget();
        Closed += (_, _) =>
        {
            _gadget?.Close();
            Application.Current.Shutdown();
        };
    }

    private int _columns;

    /// <summary>Lays the cards out in 2 columns on wide windows and 1 column on narrow ones.</summary>
    private void ArrangeCards()
    {
        int cols = ActualWidth >= 760 ? 2 : 1;
        if (cols == _columns) return;
        _columns = cols;

        CardsGrid.ColumnDefinitions.Clear();
        CardsGrid.RowDefinitions.Clear();
        for (int c = 0; c < cols; c++) CardsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var cards = new FrameworkElement[] { CpuCard, GpuCard, RamCard, VramCard };
        int rows = (cards.Length + cols - 1) / cols + 2;
        for (int r = 0; r < rows; r++) CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < cards.Length; i++)
        {
            Grid.SetRow(cards[i], i / cols);
            Grid.SetColumn(cards[i], i % cols);
            Grid.SetColumnSpan(cards[i], 1);
        }
        Grid.SetRow(NetCard, rows - 2);
        Grid.SetRow(FooterCard, rows - 1);
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

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var vm = new SettingsViewModel(_settings, () =>
        {
            _vm.ApplySettings();
            UpdateGadget();
        });
        new SettingsWindow(vm) { Owner = this }.ShowDialog();
    }
}
