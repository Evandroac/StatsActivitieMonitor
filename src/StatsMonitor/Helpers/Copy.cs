using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace StatsMonitor.Helpers;

/// <summary>Click-to-copy for labels: set Copy.Text and clicking the element copies that text.</summary>
public static class Copy
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Copy), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string? v) => d.SetValue(TextProperty, v);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.MouseLeftButtonUp -= OnClick;
        bool active = !string.IsNullOrWhiteSpace(e.NewValue as string);
        if (active) fe.MouseLeftButtonUp += OnClick;
        fe.Cursor = active ? Cursors.Hand : null;
    }

    private static void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || GetText(fe) is not { Length: > 0 } text) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { return; }
        fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(350)));
        e.Handled = true;
        ShowToast(fe);
    }

    private static System.Windows.Controls.Primitives.Popup? _toast;
    private static System.Windows.Threading.DispatcherTimer? _timer;

    private static void ShowToast(FrameworkElement target)
    {
        var res = Application.Current.Resources;
        var text = new TextBlock { Text = "✓ " + Loc.T("Copiado!"), FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White };
        var border = new Border
        {
            Child = text, Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 4),
            Background = res["Accent"] as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.SteelBlue,
        };
        if (_toast is not null) _toast.IsOpen = false;
        _toast = new System.Windows.Controls.Primitives.Popup
        {
            Child = border, PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            AllowsTransparency = true, StaysOpen = true, IsOpen = true,
        };
        _timer?.Stop();
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _timer.Tick += (_, _) => { _timer.Stop(); if (_toast is not null) _toast.IsOpen = false; };
        _timer.Start();
    }
}