using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StatsMonitor.Helpers;

/// <summary>Draws a refined accent outline over data panels (rounded bordered cards) while the mouse is over them.</summary>
public static class HoverHighlight
{
    static readonly Dictionary<Border, Adorner> Active = new();
    static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(Border), UIElement.MouseEnterEvent, new MouseEventHandler(OnEnter));
        EventManager.RegisterClassHandler(typeof(Border), UIElement.MouseLeaveEvent, new MouseEventHandler(OnLeave));
    }

    static bool Qualifies(Border b)
    {
        if (b.CornerRadius.TopLeft < 8 || b.Background == null) return false;
        if (b.BorderThickness == default || b.ActualWidth < 100 || b.ActualHeight < 40) return false;
        if (Window.GetWindow(b) is not Views.MainWindow w) return false;
        if (b.ActualWidth > w.ActualWidth * 0.9 && b.ActualHeight > w.ActualHeight * 0.9) return false;
        for (DependencyObject? p = VisualTreeHelper.GetParent(b); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is ButtonBase or ComboBox or ToolTip or Popup or TextBoxBase or ScrollBar) return false;
        return true;
    }

    static void OnEnter(object sender, MouseEventArgs e)
    {
        var b = (Border)sender;
        if (Active.ContainsKey(b) || !Qualifies(b)) return;
        var layer = AdornerLayer.GetAdornerLayer(b);
        if (layer == null) return;
        var ad = new OutlineAdorner(b);
        Active[b] = ad;
        layer.Add(ad);
        ad.Opacity = 0;
        ad.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    static void OnLeave(object sender, MouseEventArgs e)
    {
        var b = (Border)sender;
        if (!Active.Remove(b, out var ad)) return;
        AdornerLayer.GetAdornerLayer(b)?.Remove(ad);
    }

    sealed class OutlineAdorner : Adorner
    {
        readonly Border _b;
        public OutlineAdorner(Border b) : base(b) { _b = b; IsHitTestVisible = false; }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = (Application.Current.TryFindResource("Accent") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B)), 1.5);
            pen.Freeze();
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(50, accent.R, accent.G, accent.B)), 4);
            glow.Freeze();
            var r = new Rect(0.75, 0.75, Math.Max(0, ActualWidth - 1.5), Math.Max(0, ActualHeight - 1.5));
            double rad = Math.Max(0, _b.CornerRadius.TopLeft - 0.5);
            dc.DrawRoundedRectangle(null, glow, r, rad, rad);
            dc.DrawRoundedRectangle(null, pen, r, rad, rad);
        }
    }
}
