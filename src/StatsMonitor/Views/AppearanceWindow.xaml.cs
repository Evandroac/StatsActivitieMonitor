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

    private void OnPresetPicked(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        PresetPopup.IsOpen = false;
        if (e.AddedItems.Count == 0 || DataContext is not AppearanceViewModel vm) return;
        vm.LoadPresetCommand.Execute(null);
        vm.SelectedPreset = null;
    }
}
