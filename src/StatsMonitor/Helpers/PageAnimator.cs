using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StatsMonitor.Helpers;

/// <summary>Short fade + upward slide used when a screen opens or the user switches screens.</summary>
public static class PageAnimator
{
    private static readonly Duration Length = new(TimeSpan.FromMilliseconds(240));

    public static void Play(FrameworkElement element, bool enabled)
    {
        if (!enabled) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(0, 14);
        element.RenderTransform = slide;

        var fade = new DoubleAnimation(0, 1, Length) { EasingFunction = ease };
        fade.Completed += (_, _) => { element.BeginAnimation(UIElement.OpacityProperty, null); element.RenderTransform = Transform.Identity; };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, Length) { EasingFunction = ease });
    }
}
