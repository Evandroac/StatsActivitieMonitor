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
        _tip.Content = $"Uso neste ponto: {values[_hover]:0.0}%";
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var values = Values?.Cast<double>().ToList();
        if (values == null || values.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0) return;

        double step = ActualWidth / (values.Count - 1);
        Point P(int i) => new(i * step, ActualHeight - Math.Clamp(values[i], 0, 100) / 100 * (ActualHeight - 2) - 1);

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
