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
            var target = Find(root, text);
            if (target is null) continue;
            target.BringIntoView();
            await Task.Delay(50);
            Outline(target);
            return;
        }
    }

    private static FrameworkElement? Find(DependencyObject root, string text)
    {
        string wanted = SearchIndex.Normalize(text).Trim();
        TextBlock? partial = null;
        foreach (var tb in Descendants(root).OfType<TextBlock>())
        {
            if (!tb.IsVisible || string.IsNullOrWhiteSpace(tb.Text)) continue;
            string t = SearchIndex.Normalize(tb.Text).Trim();
            if (t == wanted) return Wrap(tb);
            if (partial is null && t.Contains(wanted)) partial = tb;
        }
        return partial is null ? null : Wrap(partial);
    }

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

    private static void Outline(FrameworkElement target)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null) return;
        var adorner = new HighlightAdorner(target);
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
        public HighlightAdorner(UIElement el) : base(el) { IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = (Application.Current.TryFindResource("Accent") as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
            var rect = new Rect(-4, -2, AdornedElement.RenderSize.Width + 8, AdornedElement.RenderSize.Height + 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(50, accent.R, accent.G, accent.B)),
                new Pen(new SolidColorBrush(accent), 2), rect, 5, 5);
        }
    }
}
