using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace StatsMonitor.Controls;

/// <summary>Simple area chart of the last N values (0-100).</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IEnumerable), typeof(Sparkline), new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Sparkline), new PropertyMetadata("%"));

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public IEnumerable? Values { get => (IEnumerable?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (Sparkline)d;
        if (e.OldValue is INotifyCollectionChanged o) o.CollectionChanged -= s.OnChanged;
        if (e.NewValue is INotifyCollectionChanged n) n.CollectionChanged += s.OnChanged;
        s.InvalidateVisual();
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private readonly System.Windows.Controls.ToolTip _tip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private int _hover = -1;

    public Sparkline()
    {
        ToolTip = _tip;
        System.Windows.Controls.ToolTipService.SetShowDuration(this, 60000);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var values = Values?.Cast<double>().ToList();
        if (values == null || values.Count < 2 || ActualWidth <= 0) return;
        _hover = Math.Clamp((int)Math.Round(e.GetPosition(this).X / (ActualWidth / (values.Count - 1))), 0, values.Count - 1);
        _tip.Content = StatsMonitor.Helpers.Loc.T("Uso neste ponto: {0}%", values[_hover].ToString("0.0")).Replace("%", Unit == "%" ? "%" : " " + Unit);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    private void DrawGrid(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var color = (TryFindResource("TextSecondary") as SolidColorBrush)?.Color ?? Colors.Gray;
        var light = new Pen(new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)), 1);
        var strong = new Pen(new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)), 1);
        light.Freeze(); strong.Freeze();

        for (int pct = 0; pct <= 100; pct += 10)
        {
            double y = Math.Round(ActualHeight - pct / 100.0 * (ActualHeight - 2) - 1) + 0.5;
            dc.DrawLine(pct % 50 == 0 ? strong : light, new Point(0, y), new Point(ActualWidth, y));
        }
        for (double x = 0; x <= ActualWidth; x += 20)
            dc.DrawLine(light, new Point(Math.Round(x) + 0.5, 0), new Point(Math.Round(x) + 0.5, ActualHeight));
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        DrawGrid(dc);
        var values = Values?.Cast<double>().ToList();
        if (values == null || values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;

        double step = ActualWidth / (values.Count - 1);
        Point P(int i) => new(i * step, ActualHeight - Math.Clamp(values[i], 0, Maximum) / Math.Max(Maximum, 0.0001) * (ActualHeight - 2) - 1);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (int i = 1; i < values.Count; i++) ctx.LineTo(P(i), true, true);
        }
        line.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(0, ActualHeight), true, true);
            for (int i = 0; i < values.Count; i++) ctx.LineTo(P(i), true, false);
            ctx.LineTo(new Point(ActualWidth, ActualHeight), true, false);
        }
        area.Freeze();

        var fill = Stroke.Clone();
        fill.Opacity = 0.18;
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);

        if (_hover >= 0 && _hover < values.Count)
        {
            var p = P(_hover);
            dc.DrawLine(new Pen(Stroke, 1) { DashStyle = DashStyles.Dot }, new Point(p.X, 0), new Point(p.X, ActualHeight));
            dc.DrawEllipse(Stroke, null, p, 3.5, 3.5);
        }
    }
}
