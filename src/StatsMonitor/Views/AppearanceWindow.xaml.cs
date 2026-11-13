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
    }
}
