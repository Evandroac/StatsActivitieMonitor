using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace StatsMonitor.Helpers;

/// <summary>Finds a rendered text inside a page, scrolls to it and outlines it for a few seconds.</summary>
public static class SearchHighlighter
{
    public static async void Show(FrameworkElement root, string text)
    {
        // Pages may still be loading (System data is fetched asynchronously).
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            if (!root.IsLoaded) continue;
            if (Find(root, text) is not var (target, tb)) continue;
            target.BringIntoView();
            await Task.Delay(50);
            Outline(target, tb);
            return;
        }
    }

    public static bool Contains(DependencyObject root, string text) => Find(root, text) is not null;

    private static (FrameworkElement target, TextBlock text)? Find(DependencyObject root, string text)
    {
        string wanted = SearchIndex.Normalize(text).Trim();
        TextBlock? partial = null;
        foreach (var tb in Descendants(root).OfType<TextBlock>())
        {
            if (!tb.IsVisible || string.IsNullOrWhiteSpace(tb.Text)) continue;
            string t = SearchIndex.Normalize(tb.Text).Trim();
            if (t == wanted) return Result(tb);
            if (partial is null && t.Contains(wanted)) partial = tb;
        }
        return partial is null ? null : Result(partial);
    }

    private static (FrameworkElement, TextBlock) Result(TextBlock tb) => (Wrap(tb), tb);

    // For checkboxes, radios and buttons, outline the whole control.
    private static FrameworkElement Wrap(TextBlock tb)
    {
        for (DependencyObject? p = VisualTreeHelper.GetParent(tb); p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ButtonBase b) return b;
            if (p is ScrollViewer or Window) break;
        }
        return tb;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static void Outline(FrameworkElement target, TextBlock tb)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;
        var adorner = new HighlightAdorner(target, tb);
        layer.Add(adorner);
        adorner.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(500))
        {
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(4),
        });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.2) };
        timer.Tick += (_, _) => { timer.Stop(); layer.Remove(adorner); };
        timer.Start();
    }

    private sealed class HighlightAdorner : Adorner
    {
        private readonly TextBlock _text;

        public HighlightAdorner(FrameworkElement el, TextBlock text) : base(el) { _text = text; IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = (Application.Current.TryFindResource("Accent") as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
            // Limit the outline to the real text width: the TextBlock/control may be stretched to its column.
            var ft = new FormattedText(_text.Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(_text.FontFamily, _text.FontStyle, _text.FontWeight, _text.FontStretch), _text.FontSize, Brushes.Black,
                VisualTreeHelper.GetDpi(_text).PixelsPerDip);
            double textWidth = Math.Min(_text.ActualWidth, ft.WidthIncludingTrailingWhitespace);
            double left = _text.TranslatePoint(new Point(0, 0), (UIElement)AdornedElement).X;
            double right = left + textWidth;
            double start = AdornedElement == _text ? 0 : Math.Min(0, left);
            var rect = new Rect(start - 4, -2, Math.Min(right, AdornedElement.RenderSize.Width) - start + 8, AdornedElement.RenderSize.Height + 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(50, accent.R, accent.G, accent.B)),
                new Pen(new SolidColorBrush(accent), 2), rect, 5, 5);
        }
    }
}
