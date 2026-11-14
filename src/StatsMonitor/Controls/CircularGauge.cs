using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StatsMonitor.Controls;

/// <summary>Circular progress indicator (0-100) with smooth animation.</summary>
public sealed class CircularGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CircularGauge), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        nameof(DisplayValue), typeof(double), typeof(CircularGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(CircularGauge),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(CircularGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
        nameof(TextBrush), typeof(Brush), typeof(CircularGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double DisplayValue { get => (double)GetValue(DisplayValueProperty); set => SetValue(DisplayValueProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (CircularGauge)d;
        g.BeginAnimation(DisplayValueProperty, new DoubleAnimation((double)e.NewValue, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double thickness = size * 0.11;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = (size - thickness) / 2;

        dc.DrawEllipse(null, new Pen(Track, thickness), center, radius, radius);

        double pct = Math.Clamp(DisplayValue, 0, 100);
        if (pct > 0.1)
        {
            double angle = pct >= 99.99 ? 359.99 : pct * 3.6;
            double rad = (angle - 90) * Math.PI / 180;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true));
            var geo = new PathGeometry([fig]);
            dc.DrawGeometry(null, new Pen(Accent, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geo);
        }

        var text = new FormattedText($"{pct:0}%", System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), size * 0.24, TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}
