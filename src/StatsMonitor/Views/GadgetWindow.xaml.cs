using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
        Left = rightEdge / dpi.DpiScaleX - ActualWidth - 4;
        Top = (bar.Top + bar.Bottom) / 2.0 / dpi.DpiScaleY - ActualHeight / 2;
        return true;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
