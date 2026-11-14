using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StatsMonitor.Helpers;
using StatsMonitor.Services;

namespace StatsMonitor.Views;

/// <summary>Timeline chart: one coloured line per listed process; the selected one is highlighted and the rest dimmed.</summary>
public sealed class ProcessTimeline : FrameworkElement
{
    private IReadOnlyList<ProcessRow> _rows = [];
    private int _metric;
    private ProcessRow? _selected;

    public void SetData(IReadOnlyList<ProcessRow> rows, int metric, ProcessRow? selected)
    {
        _rows = rows; _metric = metric; _selected = selected;
        InvalidateVisual();
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 40) return;
        const double left = 40, bottom = 18, top = 6;
        double pw = w - left - 6, ph = h - top - bottom;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = Res("TextSecondary");
        var grid = new Pen(Res("CardBorder"), 1);

        double max = 0;
        foreach (var r in _rows) foreach (var v in r.Hist[_metric]) if (v > max) max = v;
        double top100 = new[] { 5.0, 10, 25, 50, 100 }.FirstOrDefault(s => s >= max, 100);

        void Label(string s, double x, double y, TextAlignment ta)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, text, dpi);
            dc.DrawText(ft, new Point(ta == TextAlignment.Right ? x - ft.Width : x, y - ft.Height / 2));
        }

        for (int i = 0; i <= 2; i++)
        {
            double y = top + ph - ph * i / 2;
            dc.DrawLine(grid, new Point(left, y), new Point(left + pw, y));
            Label($"{top100 * i / 2:0.#}%", left - 4, y, TextAlignment.Right);
        }
        int n = ProcessRow.HistoryLength;
        double step = pw / (n - 1);
        Label(Loc.Lang == "en" ? "older" : "antigo", left, h - bottom / 2 + 2, TextAlignment.Left);
        Label(Loc.Lang == "en" ? "now" : "agora", left + pw - 18, h - bottom / 2 + 2, TextAlignment.Left);

        void Draw(ProcessRow r, double thickness)
        {
            var hist = r.Hist[_metric];
            if (hist.Count < 2) return;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i < hist.Count; i++)
                {
                    double x = left + pw - (hist.Count - 1 - i) * step;
                    double y = top + ph - Math.Min(1, hist[i] / top100) * ph;
                    if (i == 0) g.BeginFigure(new Point(x, y), false, false); else g.LineTo(new Point(x, y), true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, new Pen(r.ColorBrush, thickness) { LineJoin = PenLineJoin.Round }, geo);
        }

        dc.PushClip(new RectangleGeometry(new Rect(left, 0, pw + 6, top + ph + 2)));
        if (_selected != null) dc.PushOpacity(0.15);
        foreach (var r in _rows) if (!ReferenceEquals(r, _selected)) Draw(r, 1.2);
        if (_selected != null) { dc.Pop(); Draw(_selected, 3); }
        dc.Pop();
    }
}
