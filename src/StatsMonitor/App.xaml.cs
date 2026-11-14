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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Helpers.EmojiColors.Install();

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

        var splash = new SplashWindow();
        splash.Show();
        async Task Step(string pt, string en, double pct)
        {
            splash.Log(SplashWindow.Text(pt, en), pct);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        }

        try
        {
            await Step("Carregando configurações…", "Loading settings…", 5);
            Units.SetFahrenheit(settings.Current.TempUnit == "F");
            ThemeHelper.Apply(settings.Current);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(() => ThemeHelper.Apply(settings.Current));

            await Step("Inicializando sensores de hardware…", "Initializing hardware sensors…", 15);
            _viewModel = new MainViewModel(new LibreHardwareMonitorService(), settings);

            await Step("Montando a janela principal…", "Building the main window…", 35);
            var main = new MainWindow(_viewModel, settings);
            var gadget = new GadgetWindow(_viewModel, settings);
            main.AttachGadget(gadget);
            if (settings.Current.StartMinimized) main.WindowState = WindowState.Minimized;

            await Step("Iniciando monitoramento…", "Starting monitoring…", 50);
            _viewModel.Start();

            var vm = _viewModel;
            var checks = new List<(string pt, string en, double pct, Func<bool> ready)>
            {
                ("CPU, GPU, memória e discos carregados", "CPU, GPU, memory and disks loaded", 75, () => vm.LastSnapshot is not null),
                ("Rede carregada", "Network loaded", 88, () => vm.LastNetwork is not null),
            };
            if (vm.HasBattery) checks.Add(("Bateria carregada", "Battery loaded", 94, () => vm.Battery.Present));

            var started = DateTime.UtcNow;
            foreach (var c in checks)
            {
                await Step("Aguardando leitura dos dados…", "Waiting for the first readings…", c.pct - 10);
                while (!c.ready() && DateTime.UtcNow - started < TimeSpan.FromSeconds(20))
                    await Task.Delay(100);
                if (c.ready()) await Step(c.pt, c.en, c.pct);
                else await Step(SplashWindow.Text("Sem resposta (seguindo): ", "No response (continuing): ") + c.pt, c.en, c.pct);
            }

            await Step("Preparando a tela inicial…", "Preparing the home screen…", 98);
            _fps = new FpsMonitor(settings, _viewModel);
            _fps.Start();
            await Step("Tudo pronto!", "All set!", 100);
            await Task.Delay(250);

            MainWindow = main;
            main.Show();
            await splash.FadeOutAsync();
        }
        finally { splash.Close(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _fps?.Dispose();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
