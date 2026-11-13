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

    public event EventHandler? RestoreRequested;

    public GadgetWindow(MainViewModel vm, SettingsService settings)
    {
        InitializeComponent();
        DataContext = vm;
        _settings = settings;

        MouseLeftButtonDown += OnMouseDown;
        LocationChanged += (_, _) => SavePosition();
        SizeChanged += (_, _) => { if (IsVisible && IsTaskbarMode) Reposition(); };

        // Re-check periodically: other tray widgets may appear/disappear, and the taskbar may cover us.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (!IsVisible || !IsTaskbarMode) return;
            Reposition();
            var h = new WindowInteropHelper(this).Handle;
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // TOPMOST, NOSIZE|NOMOVE|NOACTIVATE
        };
        timer.Start();
    }

    private bool IsTaskbarMode => _settings.Current.GadgetMode == GadgetDisplayMode.Taskbar;

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
        bool taskbar = IsTaskbarMode;
        FloatingPanel.Visibility = taskbar ? Visibility.Collapsed : Visibility.Visible;
        TaskbarPanel.Visibility = taskbar ? Visibility.Visible : Visibility.Collapsed;
        Root.Padding = taskbar ? new Thickness(10, 4, 10, 4) : new Thickness(12, 8, 12, 8);
        Root.CornerRadius = new CornerRadius(taskbar ? 6 : 10);
        ToolTip = taskbar ? "Duplo clique para abrir" : "Arraste para mover · duplo clique para abrir";
    }

    private void Reposition()
    {
        if (IsTaskbarMode && TryPlaceInTaskbar()) return;

        var s = _settings.Current;
        var work = SystemParameters.WorkArea;
        double left = s.GadgetLeft ?? work.Right - ActualWidth - 12;
        double top = s.GadgetTop ?? work.Bottom - ActualHeight - 12;
        // Keep the gadget on screen if the saved position is no longer valid (e.g. monitor removed).
        Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - ActualWidth);
        Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - ActualHeight);
    }

    /// <summary>Docks the gadget on a horizontal taskbar, just left of the notification area.</summary>
    private bool TryPlaceInTaskbar()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var bar)) return false;
        if (bar.Right - bar.Left < bar.Bottom - bar.Top) return false; // vertical taskbar: use floating placement

        int rightEdge = bar.Right;
        var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var n)) rightEdge = n.Left;

        var dpi = VisualTreeHelper.GetDpi(this);
        int widthPx = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int gapPx = (int)Math.Round(4 * dpi.DpiScaleX);
        rightEdge = AvoidNeighbours(rightEdge, widthPx, gapPx, bar);
        double newLeft = rightEdge / dpi.DpiScaleX - ActualWidth - 4;
        double newTop = (bar.Top + bar.Bottom) / 2.0 / dpi.DpiScaleY - ActualHeight / 2;
        if (Math.Abs(Left - newLeft) > 0.5) Left = newLeft;
        if (Math.Abs(Top - newTop) > 0.5) Top = newTop;
        return true;
    }

    /// <summary>
    /// Moves the right edge left while another visible topmost window (other widgets/tools
    /// docked beside the notification area) occupies the slot the gadget would take.
    /// </summary>
    private int AvoidNeighbours(int rightEdge, int widthPx, int gapPx, RECT bar)
    {
        var self = new WindowInteropHelper(this).Handle;
        var others = new List<RECT>();
        int barHeight = bar.Bottom - bar.Top, barWidth = bar.Right - bar.Left;

        EnumWindows((h, _) =>
        {
            if (h == self || !IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == Environment.ProcessId) return true;
            if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) return true;
            if (!GetWindowRect(h, out var r)) return true;
            int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            if (w <= 0 || ht <= 0 || w > barWidth / 2 || ht > barHeight + 8) return true;
            // Must sit on the taskbar band.
            if (r.Bottom <= bar.Top || r.Top >= bar.Bottom) return true;
            var cls = new System.Text.StringBuilder(64);
            GetClassName(h, cls, cls.Capacity);
            var name = cls.ToString();
            if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow") return true;
            others.Add(r);
            return true;
        }, IntPtr.Zero);

        bool moved = true;
        for (int i = 0; i < 16 && moved; i++)
        {
            moved = false;
            int left = rightEdge - widthPx;
            foreach (var r in others)
            {
                if (r.Right > left && r.Left < rightEdge)
                {
                    rightEdge = r.Left - gapPx;
                    moved = true;
                    break;
                }
            }
        }
        return rightEdge;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (IsTaskbarMode) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void SavePosition()
    {
        if (!IsLoaded || !IsVisible || IsTaskbarMode) return;
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
