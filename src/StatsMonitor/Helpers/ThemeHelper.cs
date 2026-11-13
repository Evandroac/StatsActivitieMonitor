using System.Windows;
using System.Windows.Media;
using StatsMonitor.Settings;

namespace StatsMonitor.Helpers;

public static class ThemeHelper
{
    public static void Apply(AppSettings s)
    {
        bool hc = SystemTheme.IsHighContrast;
        bool dark = ResolveDark(s);
        var uri = new Uri($"Themes/{(hc ? "HighContrast" : dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString.StartsWith("Themes/") == true);
        if (existing != null) dicts.Remove(existing);
        var theme = new ResourceDictionary { Source = uri };
        dicts.Insert(0, theme);

        // Overrides live in Application.Resources, which take precedence over the merged theme.
        foreach (var key in ColorKeys)
        {
            Application.Current.Resources.Remove(key);
            if (!hc && s.ColorOverrides.TryGetValue(key, out var hex) && TryParseColor(hex, out var c))
                Application.Current.Resources[key] = Frozen(c);
        }

        ApplyCardChrome(dark);

        Application.Current.Resources.Remove("GadgetBg");
        if (hc) return;
        var baseColor = s.ColorOverrides.TryGetValue("GadgetBg", out var gh) && TryParseColor(gh, out var gc)
            ? gc
            : ((SolidColorBrush)theme["GadgetBg"]).Color;
        if (s.GadgetOpacity is { } pct) baseColor.A = (byte)Math.Round(Math.Clamp(pct, 10, 100) * 255 / 100.0);
        else if (!s.ColorOverrides.ContainsKey("GadgetBg")) return;
        Application.Current.Resources["GadgetBg"] = Frozen(baseColor);
    }

    public static bool ResolveDark(AppSettings s) => s.FollowSystemTheme ? SystemTheme.IsDark() : s.DarkTheme;

    // Card fill/edge/shadow derive from the active theme colors (including user overrides).
    private static void ApplyCardChrome(bool dark)
    {
        var res = Application.Current.Resources;
        Color Get(string k, Color fb) => res[k] is SolidColorBrush b ? b.Color : fb;
        var bg = Get("CardBg", Colors.Gray);
        var border = Get("CardBorder", Colors.DimGray);
        var accent = Get("Accent", Colors.DodgerBlue);
        Color Mix(Color a, Color b, double k) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

        var fill = new LinearGradientBrush(Mix(bg, accent, dark ? 0.07 : 0.045), bg, new Point(0, 0), new Point(0.9, 1));
        var edge = new LinearGradientBrush(Mix(border, accent, dark ? 0.55 : 0.45), border, new Point(0, 0), new Point(0, 1));
        var shadow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = dark ? Mix(Colors.Black, accent, 0.25) : Mix(Color.FromRgb(40, 55, 90), accent, 0.2),
            BlurRadius = dark ? 16 : 14,
            ShadowDepth = dark ? 2 : 3,
            Direction = 270,
            Opacity = dark ? 0.55 : 0.16,
        };
        fill.Freeze(); edge.Freeze(); shadow.Freeze();
        res["CardFill"] = fill;
        res["CardEdge"] = edge;
        res["CardEffect"] = shadow;
    }

    public static readonly string[] ColorKeys =
        ["WindowBg", "CardBg", "CardBorder", "TextPrimary", "TextSecondary", "TrackBrush", "Accent", "Accent2", "GadgetText"];

    /// <summary>Theme default color for a resource key, ignoring user overrides.</summary>
    public static Color ThemeDefault(string key, bool dark)
    {
        var d = new ResourceDictionary { Source = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        return ((SolidColorBrush)d[key]).Color;
    }

    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static Brush ParseBrush(string hex, string fallback)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch
        {
            return ParseBrush(fallback, "#808080");
        }
    }

    public static (Brush Normal, Brush Warning, Brush Critical) AlertBrushes(AppSettings s) =>
        (ParseBrush(s.NormalColor, "#3DDC84"), ParseBrush(s.WarningColor, "#FFC107"), ParseBrush(s.CriticalColor, "#FF4D4F"));
}
