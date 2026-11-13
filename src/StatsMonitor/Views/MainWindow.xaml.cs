using System.Windows;
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
        Loaded += (_, _) => UpdateGadget();
        StateChanged += (_, _) => UpdateGadget();
        Closed += (_, _) =>
        {
            _gadget?.Close();
            Application.Current.Shutdown();
        };
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
