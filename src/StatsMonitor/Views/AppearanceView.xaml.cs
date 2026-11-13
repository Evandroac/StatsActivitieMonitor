using System.Windows;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class AppearanceView : System.Windows.Controls.UserControl
{
    public event Action? CloseRequested;

    public AppearanceView(AppearanceViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    /// <summary>Persists any pending change (used when navigating away).</summary>
    public void Discard() { if (DataContext is AppearanceViewModel vm) vm.Flush(); }

    /// <summary>Hides the own actions panel when shown as a section of the settings page.</summary>
    public void Embedded()
    {
        ActionsPanel.Visibility = Visibility.Collapsed;
        BodyScroll.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled;
    }

    public void ResetColors() { if (DataContext is AppearanceViewModel vm) vm.ResetCommand.Execute(null); }

    private void OnSwatch(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string hex, DataContext: not null } b
            && FindOption(b) is { } o) o.Hex = hex;
    }

    private void OnSwatchDefault(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && FindOption(b) is { } o) o.Reset();
    }

    // The swatch buttons live inside the popup of a color row, whose data context is the ColorOption.
    private static ColorOption? FindOption(FrameworkElement el)
    {
        for (DependencyObject? d = el; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: ColorOption o }) return o;
        return null;
    }

    private bool _autoFilling;

    private void OnPresetTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_autoFilling || DataContext is not AppearanceViewModel vm) return;
        if (!e.Changes.Any(c => c.AddedLength > 0)) return;
        var typed = PresetBox.Text;
        if (typed.Length == 0 || PresetBox.CaretIndex != typed.Length) return;

        var match = vm.Presets.FirstOrDefault(p =>
            p.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && p.Name.Length > typed.Length);
        if (match is null) return;

        _autoFilling = true;
        PresetBox.Text = typed + match.Name[typed.Length..];
        PresetBox.Select(typed.Length, match.Name.Length - typed.Length);
        _autoFilling = false;
    }

    private void OnPresetPicked(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        PresetPopup.IsOpen = false;
        if (e.AddedItems.Count == 0 || DataContext is not AppearanceViewModel vm) return;
        vm.LoadPresetCommand.Execute(null);
        vm.SelectedPreset = null;
    }
}
