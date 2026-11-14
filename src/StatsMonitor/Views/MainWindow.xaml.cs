using System.Windows;
using System.Windows.Controls;
using StatsMonitor.Helpers;
using StatsMonitor.Services;
using StatsMonitor.Settings;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly SettingsService _settings;
    private GadgetWindow? _gadget;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;

    public MainWindow(MainViewModel vm, SettingsService settings)
    {
        InitializeComponent();
        VersionText.Text = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        DataContext = _vm = vm;
        _settings = settings;
        RestoreWindowState();
        UpdateCaption();
        UiScale.Set(settings.Current.FontScalePercent);
        ApplyUiScale();
        UiScale.Changed += ApplyUiScale;
        ChkSimple.IsChecked = settings.Current.SimplifiedView;
        NavBattery.Visibility = _vm.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (settings.Current.SidebarPinned) SetMenu(true);
        ArrangeCards();
        AttachNavAnimations();
        settings.Saved += () => Dispatcher.BeginInvoke(ApplyPets);
        Loaded += (_, _) => { UpdateGadget(); RegisterHotkey(); PageAnimator.Play(MainPage, settings.Current.AnimationsEnabled); };
        // Start at (and never shrink below) the size that shows every visible card without scrolling.
        ContentRendered += (_, _) => FitToContent();
        SizeChanged += (_, _) => ArrangeCards();
        StateChanged += (_, _) => { UpdateGadget(); UpdateCaption(); };
        Closing += OnClosing;
        IsVisibleChanged += (_, _) => UpdateGadget();
        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _gadget?.Close();
            Application.Current.Shutdown();
        };
        Application.Current.SessionEnding += (_, _) => { SaveWindowState(); _exiting = true; };
        CreateTrayIcon();
        if (settings.IsFirstRun) ShowFirstRunHealth();
    }

    private void OnCaptionMin(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnCaptionMax(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnCaptionClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateCaption()
    {
        bool max = WindowState == WindowState.Maximized;
        MaxBtn.Content = max ? "\uE923" : "\uE922";
        MaxBtn.ToolTip = Loc.T(max ? "Restaurar" : "Maximizar");
        // A chrome-less maximized window overflows the screen by the resize border.
        FrameRoot.Margin = max ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left + 1) : new Thickness(0);
    }

    private void ShowFirstRunHealth()
    {
        void Handler(StatsMonitor.Models.HardwareSnapshot s)
        {
            _vm.SnapshotApplied -= Handler;
            var (healthy, report) = StatsMonitor.Services.HealthCheck.Evaluate(s, _settings.Current);
            Dispatcher.BeginInvoke(() => System.Windows.MessageBox.Show(report, Loc.T(healthy ? "Saúde do computador: tudo certo" : "Saúde do computador: atenção"),
                MessageBoxButton.OK, healthy ? MessageBoxImage.Information : MessageBoxImage.Warning));
        }
        _vm.SnapshotApplied += Handler;
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = menu.Items.Add(Loc.T("Abrir Stats Monitor"), null, (_, _) => RestoreFromGadget());
        var exitItem = menu.Items.Add(Loc.T("Sair"), null, (_, _) => ExitApp());
        var profilesItem = new System.Windows.Forms.ToolStripMenuItem();
        menu.Items.Insert(1, profilesItem);
        menu.Opening += (_, _) => RebuildProfileMenu(profilesItem);
        RebuildProfileMenu(profilesItem);
        Loc.LanguageChanged += () => { openItem.Text = Loc.T("Abrir Stats Monitor"); exitItem.Text = Loc.T("Sair"); RebuildProfileMenu(profilesItem); };
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Stats Monitor",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
        };
        _tray.DoubleClick += (_, _) => RestoreFromGadget();

        _vm.SnapshotApplied += UpdateTrayHealth;
        _vm.AlertRaised += a =>
        {
            _tray?.ShowBalloonTip(8000, a.Title, a.Message, a.IsInfo ? System.Windows.Forms.ToolTipIcon.Info : a.IsCritical ? System.Windows.Forms.ToolTipIcon.Error : System.Windows.Forms.ToolTipIcon.Warning);
            if (_settings.Current.AlertSound && !a.IsGreeting) System.Media.SystemSounds.Exclamation.Play();
        };
    }

    private int _trayLevel = -1;
    private System.Drawing.Icon? _trayIcon;

    private void UpdateTrayHealth(StatsMonitor.Models.HardwareSnapshot s)
    {
        if (_tray is null) return;
        int level = HealthCheck.Level(s, _settings.Current);
        if (level == _trayLevel) return;
        _trayLevel = level;

        var color = level switch { 0 => System.Drawing.Color.FromArgb(0x2E, 0xCC, 0x71), 1 => System.Drawing.Color.FromArgb(0xF1, 0xC4, 0x0F), _ => System.Drawing.Color.FromArgb(0xE7, 0x4C, 0x3C) };
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var fill = new System.Drawing.SolidBrush(color);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 255, 255, 255), 3);
            g.FillEllipse(fill, 3, 3, 26, 26);
            g.DrawEllipse(pen, 3, 3, 26, 26);
        }
        IntPtr h = bmp.GetHicon();
        var icon = System.Drawing.Icon.FromHandle(h);
        var old = _trayIcon;
        _trayIcon = (System.Drawing.Icon)icon.Clone();
        _tray.Icon = _trayIcon;
        icon.Dispose();
        DestroyIcon(h);
        old?.Dispose();
        _tray.Text = "Stats Monitor — " + Loc.T(level switch { 0 => "Saudável", 1 => "Atenção", _ => "Crítico" });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private void ShowTestAlert()
    {
        _tray?.ShowBalloonTip(8000, Loc.T("Alerta de teste"), Loc.T("Esta é uma notificação de teste."), System.Windows.Forms.ToolTipIcon.Warning);
        if (_settings.Current.AlertSound) System.Media.SystemSounds.Exclamation.Play();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    // Closing the window keeps the app running in the background (tray); only "Sair" really exits.
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowState();
        if (_exiting) return;
        e.Cancel = true;
        WindowState = WindowState.Minimized;
        Hide();
        UpdateGadget();
    }

    private void RestoreWindowState()
    {
        var s = _settings.Current;
        if (s.WindowMaximized)
        {
            SizeToContent = SizeToContent.Manual;
            WindowState = WindowState.Maximized;
        }
        else if (s is { WindowLeft: { } l, WindowTop: { } t, WindowWidth: { } w, WindowHeight: { } h }
                 && l + w > SystemParameters.VirtualScreenLeft + 50 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
                 && t >= SystemParameters.VirtualScreenTop - 10 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (l, t, Math.Max(w, MinWidth), h);
        }
    }

    // A minimized window keeps the previously saved state.
    private void SaveWindowState()
    {
        if (WindowState == WindowState.Minimized) return;
        var s = _settings.Current;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        var b = RestoreBounds;
        if (!b.IsEmpty) (s.WindowLeft, s.WindowTop, s.WindowWidth, s.WindowHeight) = (b.Left, b.Top, b.Width, b.Height);
        _settings.Save();
    }

    private void FitToContent()
    {
        if (SizeToContent == SizeToContent.Manual || WindowState != WindowState.Normal) return;
        double h = Math.Min(ActualHeight, SystemParameters.WorkArea.Height);
        SizeToContent = SizeToContent.Manual;
        MinHeight = h;
        Height = h;
    }

    private bool _panelRefitPending;

    private void ApplyPanelSettings()
    {
        ArrangeCards(force: true);
        if (WindowState != WindowState.Normal) return;
        if (MainPage.Visibility != Visibility.Visible) { _panelRefitPending = true; return; }
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        Dispatcher.BeginInvoke(FitToContent, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private double _baseMinWidth = double.NaN;

    private void ApplyUiScale()
    {
        var first = double.IsNaN(_baseMinWidth);
        if (first) _baseMinWidth = MinWidth;
        var old = MinWidth;
        UiScale.Apply(RootScale);
        MinWidth = _baseMinWidth * UiScale.Factor;
        if (WindowState != WindowState.Normal) return;
        Width = first ? Math.Max(MinWidth, Width) : Math.Max(MinWidth, Width * MinWidth / old);
        if (!first && MainPage.Visibility == Visibility.Visible)
        {
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
            Dispatcher.BeginInvoke(FitToContent, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private string _layoutKey = "";

    private bool ShowBattery(StatsMonitor.Settings.AppSettings s) => s.ShowBatteryMain && _vm.HasBattery;

    /// <summary>Lays out the enabled cards in 2 columns on wide windows and 1 column on narrow ones.</summary>
    private void ArrangeCards(bool force = false)
    {
        var s = _settings.Current;
        int cols = (ActualWidth > 0 ? ActualWidth : Width) >= 760 ? 2 : 1;
        var cards = new (FrameworkElement el, bool show)[]
        {
            (CpuCard, s.ShowCpuMain), (GpuCard, s.ShowGpuMain), (RamCard, s.ShowRamMain), (VramCard, s.ShowVramMain),
        };

        string key = $"{cols}|{string.Join("", cards.Select(c => c.show ? 1 : 0))}|{(s.ShowSensorsMain ? 1 : 0)}|{(ShowBattery(s) ? 1 : 0)}|{(s.SimplifiedView ? 1 : 0)}";
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
        for (int r = 0; r < cardRows + 4; r++) CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        NetCard.Visibility = s.ShowNetworkMain && !s.SimplifiedView ? Visibility.Visible : Visibility.Collapsed;
        SensorsCard.Visibility = s.ShowSensorsMain && !s.SimplifiedView ? Visibility.Visible : Visibility.Collapsed;
        BatteryCard.Visibility = ShowBattery(s) ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(BatteryCard, cardRows);
        Grid.SetRow(NetCard, cardRows + 1);
        Grid.SetRow(SensorsCard, cardRows + 2);
        Grid.SetRow(FooterCard, cardRows + 3);
        foreach (var f in new FrameworkElement[] { BatteryCard, NetCard, SensorsCard, FooterCard })
        {
            Grid.SetColumn(f, 0);
            Grid.SetColumnSpan(f, cols);
        }
    }

    public void AttachGadget(GadgetWindow gadget)
    {
        _gadget = gadget;
        gadget.RestoreRequested += (_, _) => RestoreFromGadget();
        gadget.ExclusiveFullscreenDetected += (_, _) =>
        {
            if (!_settings.Current.NotifyAlerts) return;
            _tray?.ShowBalloonTip(10000, Loc.T("Gadget oculto pelo jogo"),
                Loc.T("O jogo está em tela cheia exclusiva e o Windows não permite exibir o gadget sobre ele. Use o modo janela sem bordas (borderless) para ver o gadget."),
                System.Windows.Forms.ToolTipIcon.Warning);
        };
    }

    /// <summary>Shows the gadget while minimized (or always, if configured) and when enabled in settings.</summary>
    private bool? _gadgetOverride;
    private GlobalHotkey? _hotkey;

    public void UpdateGadget()
    {
        if (_gadget == null) return;
        var s = _settings.Current;
        bool visible = _gadgetOverride ?? (s.ShowGadget && s.GadgetMode != GadgetDisplayMode.None && (s.AlwaysShowGadget || WindowState == WindowState.Minimized));
        if (visible && s.GadgetMode == GadgetDisplayMode.None) visible = false;
        if (visible) _gadget.ShowGadget(); else _gadget.Hide();
        bool windowShown = IsVisible && WindowState != WindowState.Minimized;
        _vm.SetGadgetOnly(!windowShown && visible);
        _vm.SetEco(s.EcoWhenMinimized && !windowShown && !visible);
        _vm.SetPaused(s.PauseWhenHidden && !s.NotifyAlerts && !s.LogEnabled && !windowShown && !visible);
    }

    private void ToggleGadget()
    {
        _gadgetOverride = !(_gadget?.IsVisible ?? false);
        UpdateGadget();
    }

    public void RegisterHotkey()
    {
        var s = _settings.Current;
        _hotkey ??= CreateHotkey();
        if (s.HotkeyEnabled) _hotkey.Register(s.GadgetHotkey); else _hotkey.Unregister();
    }

    private GlobalHotkey CreateHotkey()
    {
        var h = new GlobalHotkey();
        h.Pressed += ToggleGadget;
        Closed += (_, _) => h.Dispose();
        return h;
    }

    /// <summary>Applies a named profile, persists it and refreshes every dependent view.</summary>
    public void ApplyProfile(string name)
    {
        if (!StatsMonitor.Services.ProfileService.Apply(_settings.Current, name)) return;
        _settings.Save();
        _gadgetOverride = null;
        _vm.ApplySettings();
        ApplyPanelSettings();
        RegisterHotkey();
        UpdateGadget();
        if (PageHost.Content is SettingsView cur) OpenSettings(cur.CurrentSection);
    }

    private void RebuildProfileMenu(System.Windows.Forms.ToolStripMenuItem root)
    {
        root.Text = Loc.T("Perfis");
        root.DropDownItems.Clear();
        StatsMonitor.Services.ProfileService.EnsureDefaults(_settings.Current);
        foreach (var p in _settings.Current.Profiles)
        {
            var name = p.Name;
            var item = new System.Windows.Forms.ToolStripMenuItem(Loc.T(name))
            {
                Checked = string.Equals(_settings.Current.ActiveProfile, name, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => { ApplyProfile(name); RebuildProfileMenu(root); };
            root.DropDownItems.Add(item);
        }
    }

    private void RestoreFromGadget()
    {
        WindowState = _settings.Current.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string text } || string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (PageHost.Visibility == Visibility.Visible && PageHost.Content is SystemInfoView sys)
        {
            sys.CopyAll();
            return;
        }
        if (PageHost.Visibility == Visibility.Visible && PageHost.Content is DependencyObject page)
        {
            CopyPage(page);
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Stats Monitor - {_vm.LastUpdateText}");
        void Processor(UIElement card, ViewModels.ComponentViewModel c)
        {
            if (card.Visibility != Visibility.Visible) return;
            sb.AppendLine().AppendLine($"[{c.Title}] {c.Name}");
            sb.AppendLine($"{Loc.T("Uso")}: {c.LoadText}");
            sb.AppendLine($"{Loc.T("Frequência")}: {c.ClockText}");
            if (_vm.ShowTemperatures) sb.AppendLine($"{Loc.T("Temperatura")}: {c.TempText} ({c.MinMaxText})");
            foreach (var d in c.Details) sb.AppendLine($"{d.Label}: {d.Value}");
        }
        Processor(CpuCard, _vm.Cpu);
        Processor(GpuCard, _vm.Gpu);
        if (RamCard.Visibility == Visibility.Visible)
        {
            var r = _vm.Ram;
            sb.AppendLine().AppendLine($"[{r.Title}] {r.Name}");
            sb.AppendLine($"{Loc.T("Uso")}: {r.LoadText}");
            sb.AppendLine($"{Loc.T("Total")}: {r.TotalText} | {Loc.T("Em uso")}: {r.UsedText} | {Loc.T("Disponível")}: {r.AvailableText}");
        }
        if (VramCard.Visibility == Visibility.Visible)
        {
            var d = _vm.Disk;
            sb.AppendLine().AppendLine($"[{Loc.T("Discos")}] {Loc.T("Atividade")}: {d.LoadText}");
            foreach (var v in d.Volumes) sb.AppendLine($"{v.Name}: {v.Usage}, {v.Free}");
            foreach (var x in d.Drives)
                sb.AppendLine(Loc.T("{0}: temp {1}, leitura {2}, escrita {3}, atividade {4}, saúde {5}, vida útil {6}", x.Name, x.Temp, x.Read, x.Write, x.Activity, x.Health, x.Life));
        }
        if (NetCard.Visibility == Visibility.Visible)
        {
            var n = _vm.Network;
            sb.AppendLine().AppendLine($"[{Loc.T("Rede")}] {n.Status}");
            sb.AppendLine(Loc.T("Adaptador: {0} | IP: {1} | Gateway: {2} | Link: {3}", n.Adapter, n.Ip, n.Gateway, n.Link));
            sb.AppendLine(Loc.T("Download: {0} | Upload: {1} | Latência: {2}", n.Download, n.Upload, n.Latency));
            sb.AppendLine(Loc.T("Total recebido: {0} | Total enviado: {1}", n.TotalReceived, n.TotalSent));
        }
        if (BatteryCard.Visibility == Visibility.Visible)
        {
            var b = _vm.Battery;
            sb.AppendLine().AppendLine($"[{Loc.T("Bateria")}] {b.PercentText} - {b.StatusText}");
            sb.AppendLine($"{Loc.T("Fonte de alimentação")}: {b.SourceText}");
            sb.AppendLine($"{Loc.T("Tempo restante")}: {b.RemainingText} | {Loc.T("Tempo para carga completa")}: {b.ToFullText}");
            sb.AppendLine($"{Loc.T("Estado de conservação")}: {b.HealthText} ({b.CapacityText})");
        }
        if (SensorsCard.Visibility == Visibility.Visible && _vm.SensorLines.Count > 0)
        {
            sb.AppendLine().AppendLine($"[{Loc.T("Sensores")}]");
            foreach (var x in _vm.SensorLines) sb.AppendLine($"{x.Category} - {x.Name}: {x.Value}");
        }
        sb.AppendLine().AppendLine($"{Loc.T("Tempo ligado")}: {_vm.UptimeText}");
        sb.AppendLine($"{Loc.T("Status do hardware")}: {_vm.StatusText}");
        try { Clipboard.SetText(sb.ToString()); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    // Collects the useful visible texts and control states of a page, in on-screen order (no icons or buttons).
    private static void CopyPage(DependencyObject page)
    {
        var lines = new List<string>();
        static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"^[^\p{L}\p{N}\[(]+", "").Trim();
        void Walk(DependencyObject d)
        {
            if (d is UIElement { IsVisible: false }) return;
            switch (d)
            {
                case System.Windows.Controls.Primitives.ToggleButton { Content: string label } tb when tb is CheckBox or RadioButton:
                    if (Clean(label) is { Length: > 0 } l) lines.Add($"{(tb.IsChecked == true ? "[x]" : "[ ]")} {l}");
                    return;
                case RadioButton { Tag: string name } rb:
                    lines.Add($"{(rb.IsChecked == true ? "[x]" : "[ ]")} {Loc.T(name)}");
                    return;
                case System.Windows.Controls.Primitives.ButtonBase:
                case System.Windows.Controls.Primitives.Popup:
                    return;
                case TextBlock tx:
                    if (Clean(tx.Text) is { Length: > 0 } c) lines.Add(c);
                    return;
                case TextBox box:
                    if (!string.IsNullOrWhiteSpace(box.Text))
                    {
                        if (lines.Count > 0 && !lines[^1].EndsWith(':') && !lines[^1].Contains(": ")) lines[^1] += $": {box.Text.Trim()}";
                        else lines.Add(box.Text.Trim());
                    }
                    return;
            }
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        Walk(page);
        try { Clipboard.SetText("Stats Monitor" + Environment.NewLine + string.Join(Environment.NewLine, lines)); }
        catch (System.Runtime.InteropServices.COMException) { }
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
            _exiting = true;
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt.
        }
    }

    private bool _menuOpen;

    private void OnToggleMenu(object sender, RoutedEventArgs e)
    {
        SetMenu(!_menuOpen);
        _settings.Current.SidebarPinned = _menuOpen;
        _settings.Save();
    }

    private void SetMenu(bool open)
    {
        _menuOpen = open;
        NavPanel.Width = open ? _settings.Current.SidebarWidth : 48;
        NavResizer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        var v = open ? Visibility.Visible : Visibility.Collapsed;
        LblMenu.Visibility = LblMain.Visibility = LblSystem.Visibility = LblCpu.Visibility = LblGpu.Visibility = LblRam.Visibility = LblDisk.Visibility = LblNet.Visibility = LblSensors.Visibility = LblBattery.Visibility = LblProcesses.Visibility = LblNetTools.Visibility = LblDiagnostics.Visibility = LblSettings.Visibility = LblHelp.Visibility = v;
        SearchBox.Visibility = v;
        ApplyPets();
        NavTreeToggle.Visibility = v;
        NavTree.Margin = new Thickness(open ? 14 : 0, 0, 0, 0);
        ApplyTree();
        if (!open) SearchPopup.IsOpen = false;
    }

    private void ApplyPets()
    {
        bool show = _menuOpen && _settings.Current.PetsEnabled;
        if (show && PetsHost.Child == null) PetsHost.Child = new PetsView();
        else if (!show) PetsHost.Child = null;
        PetsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavResize(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (!_menuOpen) return;
        var w = Math.Clamp(NavPanel.Width + e.HorizontalChange, 160, 420);
        _settings.Current.SidebarWidth = w;
        NavPanel.Width = w;
    }

    private void OnNavResizeDone(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _settings.Save();

    private bool _treeOpen = true;

    private void ApplyTree()
    {
        NavTree.Visibility = !_menuOpen || _treeOpen ? Visibility.Visible : Visibility.Collapsed;
        NavTreeToggle.Content = _treeOpen ? "▾" : "▸";
    }

    private void OnOpenWindowsUpdate(object sender, RoutedEventArgs e) => StatsMonitor.Services.WindowsUpdateChecker.OpenWindowsUpdate();

    private void OnToggleTree(object sender, RoutedEventArgs e)
    {
        _treeOpen = !_treeOpen;
        ApplyTree();
    }

    private void OpenComponent(StatsMonitor.Services.ComponentKind kind)
    {
        var nav = kind switch
        {
            StatsMonitor.Services.ComponentKind.Cpu => NavCpu, StatsMonitor.Services.ComponentKind.Gpu => NavGpu,
            StatsMonitor.Services.ComponentKind.Memory => NavRam, StatsMonitor.Services.ComponentKind.Disk => NavDisk,
            StatsMonitor.Services.ComponentKind.Network => NavNet, StatsMonitor.Services.ComponentKind.Battery => NavBattery, _ => NavSensors,
        };
        OnOpenComponent(nav, new RoutedEventArgs());
    }

    private void AttachNavAnimations()
    {
        Func<bool> on = () => _settings.Current.AnimationsEnabled;
        void A(Button b, NavIconAnimator.Kind k) => NavIconAnimator.Attach(b, k, on);
        A(NavSettings, NavIconAnimator.Kind.Spin);
        A(NavProcesses, NavIconAnimator.Kind.Bounce);
        A(NavMenu, NavIconAnimator.Kind.Bounce);
        A(NavDisk, NavIconAnimator.Kind.Bounce);
        A(NavNetTools, NavIconAnimator.Kind.Bounce);
        A(NavNet, NavIconAnimator.Kind.Bounce);
        A(SearchIcon, NavIconAnimator.Kind.Bounce);
        A(NavHelp, NavIconAnimator.Kind.Bounce);
        A(NavGpu, NavIconAnimator.Kind.Bounce);
        A(NavMain, NavIconAnimator.Kind.Bounce);
        A(NavRam, NavIconAnimator.Kind.Bounce);
        A(NavSystem, NavIconAnimator.Kind.Bounce);
        A(NavCpu, NavIconAnimator.Kind.Bounce);
        A(NavSensors, NavIconAnimator.Kind.Bounce);
        A(NavBattery, NavIconAnimator.Kind.Bounce);
        A(NavDiagnostics, NavIconAnimator.Kind.Bounce);
    }

    private void OnShortcut(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        StatsMonitor.Services.ComponentKind? kind = fe.DataContext == _vm.Cpu ? StatsMonitor.Services.ComponentKind.Cpu
            : fe.DataContext == _vm.Gpu ? StatsMonitor.Services.ComponentKind.Gpu
            : fe.DataContext == _vm.Ram ? StatsMonitor.Services.ComponentKind.Memory
            : Enum.TryParse<StatsMonitor.Services.ComponentKind>((fe as Button)?.CommandParameter as string, out var k) ? k : null;
        if (kind is null) return;
        OpenComponent(kind.Value);
    }

    // Saved preference: pinned keeps the menu expanded, unpinned keeps it collapsed.
    private void OnToggleSimplified(object sender, RoutedEventArgs e)
    {
        _settings.Current.SimplifiedView = ChkSimple.IsChecked == true;
        _settings.Save();
        _vm.ApplySettings();
        ApplyPanelSettings();
    }

    private void OnSearchIcon(object sender, RoutedEventArgs e)
    {
        SetMenu(true);
        SearchBox.Focus();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _ = SearchIndex.LoadSystemAsync();
        var results = SearchIndex.Search(SearchBox.Text);
        SearchList.ItemsSource = results;
        SearchList.SelectedIndex = -1;
        SearchPopup.IsOpen = results.Count > 0;
    }

    private void OnSearchKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Down:
                SearchList.SelectedIndex = Math.Min(SearchList.SelectedIndex + 1, SearchList.Items.Count - 1);
                e.Handled = true; break;
            case System.Windows.Input.Key.Up:
                SearchList.SelectedIndex = Math.Max(SearchList.SelectedIndex - 1, 0);
                e.Handled = true; break;
            case System.Windows.Input.Key.Enter:
                if (SearchList.Items.Count > 0) GoToResult((SearchResult)SearchList.Items[Math.Max(SearchList.SelectedIndex, 0)]);
                e.Handled = true; break;
            case System.Windows.Input.Key.Escape:
                SearchPopup.IsOpen = false; e.Handled = true; break;
        }
    }

    private void OnSearchClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(SearchList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (item?.DataContext is SearchResult r) GoToResult(r);
    }

    private void GoToResult(SearchResult r)
    {
        SearchPopup.IsOpen = false;
        switch (r.Page)
        {
            case SearchPage.Main: ShowMain(); break;
            case SearchPage.System: if (PageHost.Content is not SystemInfoView { Component: null }) OnOpenSystem(this, new RoutedEventArgs()); (PageHost.Content as SystemInfoView)?.ShowSection("system"); break;
            case SearchPage.Peripherals: if (PageHost.Content is not SystemInfoView { Component: null }) OnOpenSystem(this, new RoutedEventArgs()); (PageHost.Content as SystemInfoView)?.ShowSection("peripherals"); break;
            case SearchPage.Processes: if (PageHost.Content is not ProcessesView) OnOpenProcesses(this, new RoutedEventArgs()); break;
            case SearchPage.Diagnostics: if (PageHost.Content is not DiagnosticsView) OnOpenDiagnostics(this, new RoutedEventArgs()); break;
        case SearchPage.NetTools: if (PageHost.Content is not NetworkToolsView) OnOpenNetTools(this, new RoutedEventArgs()); break;
            case SearchPage.Help: if (PageHost.Content is not HelpView) OnOpenHelp(this, new RoutedEventArgs()); break;
            case SearchPage.Settings: if (PageHost.Content is not SettingsView) OnOpenSettings(this, new RoutedEventArgs()); break;
            case SearchPage.Appearance: if (PageHost.Content is not SettingsView) OnOpenSettings(this, new RoutedEventArgs()); break;
            case SearchPage.Component when r.Kind is { } kind:
                if (PageHost.Content is not SystemInfoView { Component: { } cur } || cur != kind) OpenComponent(kind);
                break;
        }
        FrameworkElement root = r.Page == SearchPage.Main ? MainPage : PageHost;
        if (PageHost.Content is SettingsView sv) sv.RevealText(r.Page == SearchPage.Appearance);
        SearchHighlighter.Show(root, r.Match);
    }

    private void OnNavMain(object sender, RoutedEventArgs e) => ShowMain();

    // Leaving Settings/Personalization discards whatever was not applied or saved.
    private void ShowMain()
    {
        switch (PageHost.Content)
        {
            case SettingsView s: s.Discard(); break;
        }
        bool hadPage = PageHost.Content != null;
        PageHost.Content = null;
        if (hadPage) StatsMonitor.Helpers.PowerSaver.TrimSoon();
        PageHost.Visibility = Visibility.Collapsed;
        PageHelpBtn.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Visible;
        if (hadPage) PageAnimator.Play(MainPage, _settings.Current.AnimationsEnabled);
        SetActiveNav(NavMain);
        if (_panelRefitPending) { _panelRefitPending = false; ApplyPanelSettings(); }
    }

    private void ShowPage(FrameworkElement view, Button nav)
    {
        if (PageHost.Content is SettingsView s) s.Discard();
        bool hadPage = PageHost.Content != null;
        PageHost.Content = view;
        if (hadPage) StatsMonitor.Helpers.PowerSaver.TrimSoon();
        PageHost.Visibility = Visibility.Visible;
        PageAnimator.Play(PageHost, _settings.Current.AnimationsEnabled);
        MainPage.Visibility = Visibility.Collapsed;
        SetActiveNav(nav);
        bool onHelp = view is HelpView;
        PageHelpBtn.Visibility = onHelp ? Visibility.Collapsed : Visibility.Visible;
        PageCopyBtn.Margin = new Thickness(0, 18, onHelp ? 24 : 72, 0);
    }

    private void SetActiveNav(Button active)
    {
        foreach (var b in new[] { NavMain, NavSystem, NavCpu, NavGpu, NavRam, NavDisk, NavNet, NavSensors, NavBattery, NavProcesses, NavNetTools, NavDiagnostics, NavSettings, NavHelp }) b.Tag = b == active ? "active" : null;
    }

    private void OnOpenComponent(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string p } b && Enum.TryParse<StatsMonitor.Services.ComponentKind>(p, out var kind))
            ShowPage(new SystemInfoView(kind, () => _vm.LastSnapshot, () => _vm.LastNetwork), b);
    }

    private void OnOpenSystem(object sender, RoutedEventArgs e) => ShowPage(new SystemInfoView(), NavSystem);


    private void OnOpenProcesses(object sender, RoutedEventArgs e) => ShowPage(new ProcessesView(), NavProcesses);

    private void OnOpenNetTools(object sender, RoutedEventArgs e) => ShowPage(new NetworkToolsView(), NavNetTools);

    private DiagnosticsView? _diagView;
    private void OnOpenDiagnostics(object sender, RoutedEventArgs e)
    {
        if (_diagView == null || !_diagView.IsBusy) _diagView = new DiagnosticsView(_vm);
        ShowPage(_diagView, NavDiagnostics);
    }

    private void OnOpenHelp(object sender, RoutedEventArgs e) => ShowPage(new HelpView(), NavHelp);

    // Opens Help focused on the topic that matches the screen currently shown.
    private void OnHelpForPage(object sender, RoutedEventArgs e)
    {
        int topic = PageHost.Visibility != Visibility.Visible ? 0 : PageHost.Content switch
        {
            ProcessesView => 7,
            NetworkToolsView => 8,
            DiagnosticsView => 10,
            SystemInfoView { Component: StatsMonitor.Services.ComponentKind.Battery } => 9,
            _ => 1,
        };
        ShowPage(new HelpView(topic), NavHelp);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettings(null);

    private void OpenSettings(string? section)
    {
        var vm = new SettingsViewModel(_settings, () =>
        {
            _vm.ApplySettings();
            ApplyPanelSettings();
            RegisterHotkey();
            UpdateGadget();
        });
        vm.ProfileRequested += ApplyProfile;
        var avm = new AppearanceViewModel(_settings, () =>
        {
            _vm.ApplySettings();
            UpdateGadget();
        });
        var view = new SettingsView(vm, new AppearanceView(avm));
        avm.ThemePicked += () => vm.FollowSystemTheme = false;
            view.TestAlertRequested += ShowTestAlert;
        view.CloseRequested += ShowMain;
        ShowPage(view, NavSettings);
        if (section is not null) view.SelectSection(section);
    }
}
