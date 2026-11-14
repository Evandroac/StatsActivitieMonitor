using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StatsMonitor.Helpers;

/// <summary>Small click animations for the icons of the side menu (e.g. the settings gear spins).</summary>
public static class NavIconAnimator
{
    public enum Kind { Spin, Turn, Wiggle, Pulse, Bounce }

    private sealed class State(RotateTransform rot, ScaleTransform scale, TranslateTransform move)
    {
        public RotateTransform Rot = rot;
        public ScaleTransform Scale = scale;
        public TranslateTransform Move = move;
    }

    public static void Attach(Button button, Kind kind, Func<bool> enabled)
    {
        if (FindIcon(button) is not { } icon) return;
        var st = new State(new RotateTransform(), new ScaleTransform(1, 1), new TranslateTransform());
        var group = new TransformGroup();
        group.Children.Add(st.Scale);
        group.Children.Add(st.Rot);
        group.Children.Add(st.Move);
        icon.RenderTransform = group;
        icon.RenderTransformOrigin = new Point(0.5, 0.5);

        button.Click += (_, _) => { if (enabled()) Start(st, kind); };
    }

    private static TextBlock? FindIcon(Button b) =>
        b.Content is StackPanel sp ? sp.Children.OfType<TextBlock>().FirstOrDefault() : b.Content as TextBlock;

    private static DoubleAnimation Anim(double from, double to, double seconds, bool forever = false, bool autoReverse = false, IEasingFunction? ease = null) => new(from, to, TimeSpan.FromSeconds(seconds))
    {
        RepeatBehavior = forever ? RepeatBehavior.Forever : new RepeatBehavior(1),
        AutoReverse = autoReverse,
        EasingFunction = ease,
    };

    private static void Start(State s, Kind kind)
    {
        Stop(s, immediate: true);
        switch (kind)
        {
            case Kind.Spin:
                s.Rot.BeginAnimation(RotateTransform.AngleProperty, Anim(0, 720, 1.2, ease: new CubicEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case Kind.Turn:
                s.Rot.BeginAnimation(RotateTransform.AngleProperty, Anim(0, 360, 0.7, ease: new CubicEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case Kind.Wiggle:
                var w = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(0.6) };
                foreach (var (t, v) in new[] { (0.0, 0.0), (0.12, -18.0), (0.3, 16.0), (0.45, -10.0), (0.6, 0.0) })
                    w.KeyFrames.Add(new EasingDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
                s.Rot.BeginAnimation(RotateTransform.AngleProperty, w);
                break;
            case Kind.Pulse:
                var pulse = Anim(1, 1.3, 0.25, autoReverse: true, ease: new SineEase { EasingMode = EasingMode.EaseInOut });
                s.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                s.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                break;
            case Kind.Bounce:
                s.Move.BeginAnimation(TranslateTransform.YProperty, Anim(0, -5, 0.2, autoReverse: true, ease: new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                break;
        }
    }

    private static void Stop(State s, bool immediate = false)
    {
        s.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        s.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        s.Move.BeginAnimation(TranslateTransform.YProperty, null);
        double angle = s.Rot.Angle % 360;
        s.Rot.BeginAnimation(RotateTransform.AngleProperty, null);
        s.Scale.ScaleX = s.Scale.ScaleY = 1;
        s.Move.Y = 0;
        if (immediate || Math.Abs(angle) < 1) { s.Rot.Angle = 0; return; }
        // Ease the icon back to rest instead of snapping.
        s.Rot.Angle = angle;
        double target = angle > 180 ? 360 : angle < -180 ? -360 : 0;
        var back = Anim(angle, target, 0.25, ease: new QuadraticEase { EasingMode = EasingMode.EaseOut });
        back.Completed += (_, _) => { s.Rot.BeginAnimation(RotateTransform.AngleProperty, null); s.Rot.Angle = 0; };
        s.Rot.BeginAnimation(RotateTransform.AngleProperty, back);
    }
}
