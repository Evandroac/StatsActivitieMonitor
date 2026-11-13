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
    private FpsMonitor? _fps;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "StatsMonitor.SingleInstance", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        // A background monitor should never compete with games or foreground apps.
        try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; } catch { }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var settings = new SettingsService();
        settings.Load();
        if (settings.IsFirstRun) { settings.Current.Language = SettingsService.DetectLanguage(); settings.Save(); }
        Loc.SetLanguage(settings.Current.Language);
        Units.SetFahrenheit(settings.Current.TempUnit == "F");
        ThemeHelper.Apply(settings.Current);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() => ThemeHelper.Apply(settings.Current));

        _viewModel = new MainViewModel(new LibreHardwareMonitorService(), settings);
        var main = new MainWindow(_viewModel, settings);
        var gadget = new GadgetWindow(_viewModel, settings);
        main.AttachGadget(gadget);

        if (settings.Current.StartMinimized) main.WindowState = WindowState.Minimized;
        main.Show();
        _viewModel.Start();

        _fps = new FpsMonitor(settings, _viewModel);
        _fps.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fps?.Dispose();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
