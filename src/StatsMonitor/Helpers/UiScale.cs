using System.Windows;
using System.Windows.Media;

namespace StatsMonitor.Helpers;

/// <summary>Global text/UI scale chosen by the user (percent, clamped to a safe range).</summary>
public static class UiScale
{
    public const double MinPercent = 90, MaxPercent = 130, DefaultPercent = 110;

    public static double Percent { get; private set; } = DefaultPercent;
    public static double Factor => Percent / 100.0;
    public static event Action? Changed;

    public static double Clamp(double percent) =>
        double.IsNaN(percent) ? DefaultPercent : Math.Clamp(Math.Round(percent / 5) * 5, MinPercent, MaxPercent);

    public static void Set(double percent)
    {
        var p = Clamp(percent);
        if (p == Percent) return;
        Percent = p;
        Changed?.Invoke();
    }

    public static void Apply(FrameworkElement element) =>
        element.LayoutTransform = new ScaleTransform(Factor, Factor);
}