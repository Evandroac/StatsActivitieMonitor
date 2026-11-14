using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using StatsMonitor.Services;

namespace StatsMonitor.Views;

/// <summary>A process row whose cells follow the user-configured columns (order, visibility, width).</summary>
public sealed class ProcRowPanel : Grid
{
    private int _version = -1;

    public ProcRowPanel()
    {
        MinHeight = 30;
        Loaded += (_, _) =>
        {
            ProcessColumns.Changed += OnChanged;
            if (_version != ProcessColumns.Version) Build();
        };
        Unloaded += (_, _) => ProcessColumns.Changed -= OnChanged;
    }

    private void OnChanged() => Build();

    private static Binding B(string path) => new(path);

    private static TextBlock Text(string path, bool right, bool tip = false)
    {
        var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetBinding(TextBlock.TextProperty, B(path));
        if (right) t.HorizontalAlignment = HorizontalAlignment.Right;
        if (tip) t.SetBinding(ToolTipProperty, B(path));
        return t;
    }

    private Border Metric(string text, string brush, string? tip)
    {
        var b = new Border { Style = TryFindResource("MetricCell") as Style };
        b.SetBinding(Border.BackgroundProperty, B(brush));
        if (tip != null) b.SetBinding(ToolTipProperty, B(tip));
        var t = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        t.SetBinding(TextBlock.TextProperty, B(text));
        b.Child = t;
        return b;
    }

    private UIElement Cell(ColumnDef c)
    {
        switch (c.Id)
        {
            case "name":
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                sp.SetBinding(MarginProperty, B("Indent"));
                var btn = new Button { Width = 22, Height = 22, Padding = new Thickness(0), Focusable = false, Style = TryFindResource("HeaderButton") as Style };
                btn.SetBinding(ContentControl.ContentProperty, B("Glyph"));
                btn.SetBinding(VisibilityProperty, B("ExpanderVisibility"));
                var bar = new Border { Width = 4, Height = 18, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, 0, 0, 0) };
                bar.SetBinding(Border.BackgroundProperty, B("ColorBrush"));
                var img = new Image { Width = 18, Height = 18, Margin = new Thickness(4, 0, 8, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                img.SetBinding(Image.SourceProperty, B("Icon"));
                var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
                name.SetBinding(TextBlock.TextProperty, B("NameText"));
                sp.Children.Add(btn); sp.Children.Add(bar); sp.Children.Add(img); sp.Children.Add(name);
                return sp;
            }
            case "status":
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
                var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
                dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, B("StatusBrush"));
                var t = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Style = TryFindResource("Caption") as Style };
                t.SetBinding(TextBlock.TextProperty, B("StatusText"));
                sp.Children.Add(dot); sp.Children.Add(t);
                return sp;
            }
            case "cpu": return Metric("CpuText", "CpuBrush", null);
            case "mem": return Metric("MemText", "MemBrush", "MemTip");
            case "disk": return Metric("DiskText", "DiskBrush", "DiskTip");
            case "net": return Metric("NetText", "NetBrush", "NetTip");
            case "pid": return Text("PidText", true);
            case "parent": return Text("ParentText", true);
            case "threads": return Text("ThreadsText", true);
            case "type": return Text("TypeText", false);
            case "exe": return Text("Name", false, true);
            case "started": return Text("StartedText", false);
            default: return Text("Path", false, true);
        }
    }

    private void Build()
    {
        _version = ProcessColumns.Version;
        Children.Clear();
        ColumnDefinitions.Clear();
        int i = 0;
        foreach (var c in ProcessColumns.Visible)
        {
            var cd = new ColumnDefinition { MinWidth = c.Star ? 200 : 0 };
            cd.SetBinding(ColumnDefinition.WidthProperty, new Binding(nameof(ColumnDef.GridWidth)) { Source = c, Mode = BindingMode.OneWay });
            ColumnDefinitions.Add(cd);
            var el = Cell(c);
            SetColumn(el, i++);
            Children.Add(el);
        }
    }
}
