using System.Windows;
using StatsMonitor.Helpers;
using StatsMonitor.Services;
using StatsMonitor.Settings;
using StatsMonitor.ViewModels;
using StatsMonitor.Views;

namespace StatsMonitor;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "StatsMonitor.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var settings = new SettingsService();
        settings.Load();
        ThemeHelper.Apply(settings.Current);

        _viewModel = new MainViewModel(new LibreHardwareMonitorService(), settings);
        var main = new MainWindow(_viewModel, settings);
        var gadget = new GadgetWindow(_viewModel, settings);
        main.AttachGadget(gadget);

        if (settings.Current.StartMinimized) main.WindowState = WindowState.Minimized;
        main.Show();
        _viewModel.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
