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
        StatsMonitor.Helpers.HoverHighlight.Install();
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
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StatsMonitor", "crash.log"), DateTime.Now + "\n" + args.Exception + "\n\n"); } catch { }
            MessageBox.Show(args.Exception.Message, "Stats Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var settings = new SettingsService();
        settings.Load();
        if (settings.IsFirstRun) { settings.Current.Language = SettingsService.DetectLanguage(); settings.Save(); StartupService.SetEnabled(settings.Current.RunAtStartup); }
        Loc.SetLanguage(settings.Current.Language);

        ThemeHelper.Apply(settings.Current);
        var splash = new SplashWindow();
        splash.Show();
        async Task Step(string pt, string en, double pct)
        {
            splash.Log(SplashWindow.Text(pt, en), pct);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            if (splash.CancelRequested) throw new OperationCanceledException();
        }

        try
        {
            var boot = DateTime.UtcNow;
            await Step("Carregando configurações…", "Loading settings…", 5);
            await Step($"Sistema: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64" : "32")} bits) · {Environment.ProcessorCount} threads",
                       $"System: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64" : "32")}-bit) · {Environment.ProcessorCount} threads", 8);
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
            static string Gb(double? v) => v is { } g ? $"{g:0.#} GB" : "N/D";
            var checks = new List<(Func<(string pt, string en)> msg, double pct, Func<bool> ready)>
            {
                (() => { var s = vm.LastSnapshot!; return ($"Processador: {s.CpuName}", $"Processor: {s.CpuName}"); }, 62, () => vm.LastSnapshot is not null),
                (() => { var s = vm.LastSnapshot!; return ($"Memória: {Gb(s.RamTotalGb)} instalada", $"Memory: {Gb(s.RamTotalGb)} installed"); }, 68, () => vm.LastSnapshot is not null),
                (() => { var s = vm.LastSnapshot!; string g = s.GpuName ?? "N/D"; string v = s.VramTotalGb is { } x ? $" · {x:0.#} GB VRAM" : ""; return ($"Placa de vídeo: {g}{v}", $"Graphics: {g}{v}"); }, 74, () => vm.LastSnapshot is not null),
                (() => { var s = vm.LastSnapshot!; double tot = s.Volumes.Sum(x => x.TotalGb); return ($"Armazenamento: {s.Disks.Count} disco(s), {s.Volumes.Count} volume(s), {Gb(tot)}", $"Storage: {s.Disks.Count} drive(s), {s.Volumes.Count} volume(s), {Gb(tot)}"); }, 80, () => vm.LastSnapshot is not null),
                (() => { var s = vm.LastSnapshot!; return s.IsElevated ? ("Permissão de administrador: sensores completos", "Administrator rights: full sensors") : ("Sem permissão de administrador: alguns sensores ficam N/D", "No administrator rights: some sensors will be N/A"); }, 84, () => vm.LastSnapshot is not null),
                (() => { var n = vm.LastNetwork!; string a = n.Connected ? $"{n.AdapterName} ({n.ConnectionType}) {n.LocalIp}" : "sem conexão"; string e = n.Connected ? a : "no connection"; return ($"Rede: {a}", $"Network: {e}"); }, 90, () => vm.LastNetwork is not null),
            };
            if (vm.HasBattery) checks.Add((() => ("Bateria detectada e carregada", "Battery detected and loaded"), 94, () => vm.Battery.Present));
            else checks.Add((() => ("Bateria: este computador não possui bateria", "Battery: this computer has no battery"), 94, () => true));

            var started = DateTime.UtcNow;
            foreach (var c in checks)
            {
                if (!c.ready()) await Step("Aguardando leitura dos dados…", "Waiting for the first readings…", c.pct - 5);
                while (!c.ready() && DateTime.UtcNow - started < TimeSpan.FromSeconds(20))
                {
                    await Task.Delay(100);
                    if (splash.CancelRequested) throw new OperationCanceledException();
                }
                if (c.ready()) { var (pt, en) = c.msg(); await Step(pt, en, c.pct); }
                else await Step("Sem resposta (seguindo)", "No response (continuing)", c.pct);
            }

            await Step("Preparando a tela inicial…", "Preparing the home screen…", 98);
            _fps = new FpsMonitor(settings, _viewModel);
            _fps.Start();
            double secs = (DateTime.UtcNow - boot).TotalSeconds;
            await Step($"Tudo pronto! Iniciado em {secs:0.0} s", $"All set! Started in {secs:0.0} s", 100);
            await Task.Delay(250);

            MainWindow = main;
            main.Show();
            await splash.FadeOutAsync();
        }
        catch (OperationCanceledException) { Shutdown(); }
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
