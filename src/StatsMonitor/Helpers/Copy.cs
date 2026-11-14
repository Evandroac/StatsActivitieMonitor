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
    }
}