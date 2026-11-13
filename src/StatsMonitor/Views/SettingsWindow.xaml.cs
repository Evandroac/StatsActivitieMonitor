using System.Windows;
using StatsMonitor.ViewModels;

namespace StatsMonitor.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Saved += Close;
    }
}
