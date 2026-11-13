using System.Windows;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class AppearanceWindow : Window
{
    public AppearanceWindow(AppearanceViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Saved += Close;
        MaxHeight = SystemParameters.WorkArea.Height;
        Closed += (_, _) => { if (!vm.IsSaved) vm.Revert(); };
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
