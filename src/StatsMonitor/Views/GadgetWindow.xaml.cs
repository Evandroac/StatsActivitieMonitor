using System.Windows;
using System.Windows.Input;
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
    }

    public void ShowGadget()
    {
        if (IsVisible) return;
        var s = _settings.Current;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Show();
        UpdateLayout();

        var work = SystemParameters.WorkArea;
        double left = s.GadgetLeft ?? work.Right - ActualWidth - 12;
        double top = s.GadgetTop ?? work.Bottom - ActualHeight - 12;
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
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void SavePosition()
    {
        if (!IsLoaded || !IsVisible) return;
        _settings.Current.GadgetLeft = Left;
        _settings.Current.GadgetTop = Top;
        _settings.Save();
    }
}
