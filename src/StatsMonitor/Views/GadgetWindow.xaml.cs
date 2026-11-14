using StatsMonitor.Helpers;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using StatsMonitor.Settings;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class GadgetWindow : Window
{
    private readonly SettingsService _settings;
    private readonly MainViewModel _vm;

    public event EventHandler? RestoreRequested;

    public GadgetWindow(MainViewModel vm, SettingsService settings)
    {
        InitializeComponent();
        DataContext = vm;
        _settings = settings;
        _vm = vm;
        UiScale.Set(settings.Current.FontScalePercent);
        UiScale.Apply(Root);
        UiScale.Changed += () => { UiScale.Apply(Root); if (IsVisible) Reposition(); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.FpsActive) && IsVisible) { ApplyMode(); Reposition(); } };

        Loc.LanguageChanged += () => { if (IsVisible) { ApplyMode(); Reposition(); } };
        MouseLeftButtonDown += OnMouseDown;
        LocationChanged += (_, _) => SavePosition();
        SizeChanged += (_, _) => { if (IsVisible && IsFixedPosition) Reposition(); };

    }

    private GadgetDisplayMode Mode => _settings.Current.GadgetMode == GadgetDisplayMode.Taskbar ? GadgetDisplayMode.BottomRight : _settings.Current.GadgetMode;
    private bool IsEdge => Mode is GadgetDisplayMode.DockedTop or GadgetDisplayMode.DockedBottom;
    private bool IsCorner => Mode is GadgetDisplayMode.TopLeft or GadgetDisplayMode.TopRight or GadgetDisplayMode.BottomLeft or GadgetDisplayMode.BottomRight;
    private bool IsHorizontalRow => Mode == GadgetDisplayMode.FloatingBar || IsEdge;
    private bool IsFixedPosition => IsEdge || IsCorner;

    public void ShowGadget()
    {
        ApplyMode();
        if (!IsVisible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Show();
            UpdateLayout();
        }
        Reposition();
    }

    /// <summary>Switches between the 4-line floating layout and the compact taskbar row.</summary>
    private void ApplyMode()
    {
        bool taskbar = IsHorizontalRow;
        var mode = Mode;
        bool detailed = _settings.Current.GadgetDetailed;
        FloatingPanel.Visibility = taskbar || detailed ? Visibility.Collapsed : Visibility.Visible;
        TaskbarPanel.Visibility = taskbar && !detailed ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = detailed ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Orientation = taskbar ? System.Windows.Controls.Orientation.Horizontal : System.Windows.Controls.Orientation.Vertical;
        foreach (FrameworkElement block in DetailPanel.Children)
            block.Margin = taskbar ? new Thickness(0, 0, 14, 0) : new Thickness(0, 0, 0, 8);
        Root.Padding = mode switch
        {
            GadgetDisplayMode.FloatingBar => new Thickness(12, 6, 12, 6),
            GadgetDisplayMode.DockedTop or GadgetDisplayMode.DockedBottom => new Thickness(14, 5, 14, 5),
            _ => new Thickness(12, 8, 12, 8),
        };
        Root.CornerRadius = mode switch
        {
            GadgetDisplayMode.FloatingBar => new CornerRadius(8),
            GadgetDisplayMode.DockedTop => new CornerRadius(0, 0, 10, 10),
            GadgetDisplayMode.DockedBottom => new CornerRadius(10, 10, 0, 0),
            _ => new CornerRadius(10),
        };
        ToolTip = Loc.T(IsFixedPosition ? "Duplo clique para abrir" : "Arraste para mover · duplo clique para abrir");

        var s = _settings.Current;
        static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        FCpu.Visibility = TCpu.Visibility = V(s.ShowCpuGadget);
        FGpu.Visibility = TGpu.Visibility = V(s.ShowGpuGadget);
        FRam.Visibility = TRam.Visibility = V(s.ShowRamGadget);
        FVram.Visibility = TVram.Visibility = V(s.ShowVramGadget);
        FNet.Visibility = TNet.Visibility = V(s.ShowNetworkGadget);
        FFps.Visibility = TFps.Visibility = DFps.Visibility = V(s.ShowFpsGadget && _vm.FpsActive);
        DCpu.Visibility = V(s.ShowCpuGadget); DGpu.Visibility = V(s.ShowGpuGadget); DRam.Visibility = V(s.ShowRamGadget);
        DDisk.Visibility = V(s.ShowVramGadget); DNet.Visibility = V(s.ShowNetworkGadget);

        if (taskbar && !detailed) ReserveTaskbarWidths(s);
    }

    // Fixed slot per indicator (worst-case text) so the widget doesn't resize on every update.
    private void ReserveTaskbarWidths(Settings.AppSettings s)
    {
        double Measure(string text)
        {
            var ft = new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(TCpu.FontFamily, TCpu.FontStyle, TCpu.FontWeight, TCpu.FontStretch),
                TCpu.FontSize, System.Windows.Media.Brushes.Black,
                System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return Math.Ceiling(ft.WidthIncludingTrailingWhitespace);
        }

        string temp = " | 100°C";
        TCpu.MinWidth = Measure("CPU 100%" + (s.ShowTemperatures ? temp : ""));
        TGpu.MinWidth = Measure("GPU 100%" + (s.ShowTemperatures ? temp : ""));
        TRam.MinWidth = Measure("RAM 100%");
        TVram.MinWidth = Measure(Loc.T("Disco") + " 100%");
        TFps.MinWidth = Measure("FPS 999 · méd 999");
        TNet.MinWidth = Measure("NET ↓ 999.9 Mbps ↑ 999.9 Mbps");
    }

    private void Reposition()
    {
        var work = SystemParameters.WorkArea;
        const double gap = 12;
        switch (Mode)
        {
            case GadgetDisplayMode.DockedTop: Left = work.Left + (work.Width - ActualWidth) / 2; Top = work.Top; return;
            case GadgetDisplayMode.DockedBottom: Left = work.Left + (work.Width - ActualWidth) / 2; Top = work.Bottom - ActualHeight; return;
            case GadgetDisplayMode.TopLeft: Left = work.Left + gap; Top = work.Top + gap; return;
            case GadgetDisplayMode.TopRight: Left = work.Right - ActualWidth - gap; Top = work.Top + gap; return;
            case GadgetDisplayMode.BottomLeft: Left = work.Left + gap; Top = work.Bottom - ActualHeight - gap; return;
            case GadgetDisplayMode.BottomRight: Left = work.Right - ActualWidth - gap; Top = work.Bottom - ActualHeight - gap; return;
        }

        var s = _settings.Current;
        double left = s.GadgetLeft ?? work.Right - ActualWidth - gap;
        double top = s.GadgetTop ?? work.Bottom - ActualHeight - gap;
        // Keep the gadget on screen if the saved position is no longer valid (e.g. monitor removed).
        Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
        Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (IsFixedPosition) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void SavePosition()
    {
        if (!IsLoaded || !IsVisible || IsFixedPosition) return;
        _settings.Current.GadgetLeft = Left;
        _settings.Current.GadgetTop = Top;
        _settings.Save();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOPMOST = 0x8;
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
