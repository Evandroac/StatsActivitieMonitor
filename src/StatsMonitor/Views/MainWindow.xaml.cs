using System.Windows;
using System.Windows.Controls;
using StatsMonitor.Helpers;
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
        DataContext = _vm = vm;
        _settings = settings;
        RestoreWindowState();
        ChkSimple.IsChecked = settings.Current.SimplifiedView;
        if (settings.Current.SidebarPinned) SetMenu(true);
        ArrangeCards();
        Loaded += (_, _) => { UpdateGadget(); RegisterHotkey(); };
        // Start at (and never shrink below) the size that shows every visible card without scrolling.
        ContentRendered += (_, _) => FitToContent();
        SizeChanged += (_, _) => ArrangeCards();
        StateChanged += (_, _) => UpdateGadget();
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

        _vm.AlertRaised += a =>
        {
            _tray?.ShowBalloonTip(8000, a.Title, a.Message, System.Windows.Forms.ToolTipIcon.Warning);
            if (_settings.Current.AlertSound) System.Media.SystemSounds.Exclamation.Play();
        };
    }

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

        string key = $"{cols}|{string.Join("", cards.Select(c => c.show ? 1 : 0))}|{(s.ShowSensorsMain ? 1 : 0)}|{(s.SimplifiedView ? 1 : 0)}";
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
        for (int r = 0; r < cardRows + 3; r++) CardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        NetCard.Visibility = s.ShowNetworkMain && !s.SimplifiedView ? Visibility.Visible : Visibility.Collapsed;
        SensorsCard.Visibility = s.ShowSensorsMain && !s.SimplifiedView ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(NetCard, cardRows);
        Grid.SetRow(SensorsCard, cardRows + 1);
        Grid.SetRow(FooterCard, cardRows + 2);
        foreach (var f in new FrameworkElement[] { NetCard, SensorsCard, FooterCard })
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
        _vm.SetPaused(s.PauseWhenHidden && !s.NotifyAlerts && !windowShown && !visible);
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
        NavPanel.Width = open ? 220 : 48;
        var v = open ? Visibility.Visible : Visibility.Collapsed;
        LblMenu.Visibility = LblMain.Visibility = LblSystem.Visibility = LblCpu.Visibility = LblGpu.Visibility = LblRam.Visibility = LblDisk.Visibility = LblNet.Visibility = LblSensors.Visibility = LblPeripherals.Visibility = LblSettings.Visibility = v;
        SearchBox.Visibility = v;
        if (!open) SearchPopup.IsOpen = false;
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
            case SearchPage.System: if (PageHost.Content is not SystemInfoView { IsPeripherals: false, Component: null }) OnOpenSystem(this, new RoutedEventArgs()); break;
            case SearchPage.Peripherals: if (PageHost.Content is not SystemInfoView { IsPeripherals: true }) OnOpenPeripherals(this, new RoutedEventArgs()); break;
            case SearchPage.Settings: if (PageHost.Content is not SettingsView) OnOpenSettings(this, new RoutedEventArgs()); break;
            case SearchPage.Appearance: if (PageHost.Content is not SettingsView) OnOpenSettings(this, new RoutedEventArgs()); break;
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
        PageHost.Content = null;
        PageHost.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Visible;
        SetActiveNav(NavMain);
        if (_panelRefitPending) { _panelRefitPending = false; ApplyPanelSettings(); }
    }

    private void ShowPage(FrameworkElement view, Button nav)
    {
        if (PageHost.Content is SettingsView s) s.Discard();
        PageHost.Content = view;
        PageHost.Visibility = Visibility.Visible;
        MainPage.Visibility = Visibility.Collapsed;
        SetActiveNav(nav);
    }

    private void SetActiveNav(Button active)
    {
        foreach (var b in new[] { NavMain, NavSystem, NavCpu, NavGpu, NavRam, NavDisk, NavNet, NavSensors, NavPeripherals, NavSettings }) b.Tag = b == active ? "active" : null;
    }

    private void OnOpenComponent(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string p } b && Enum.TryParse<StatsMonitor.Services.ComponentKind>(p, out var kind))
            ShowPage(new SystemInfoView(kind, () => _vm.LastSnapshot, () => _vm.LastNetwork), b);
    }

    private void OnOpenSystem(object sender, RoutedEventArgs e) => ShowPage(new SystemInfoView(), NavSystem);

    private void OnOpenPeripherals(object sender, RoutedEventArgs e) => ShowPage(new SystemInfoView(peripherals: true), NavPeripherals);

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
