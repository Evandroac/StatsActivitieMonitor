using System.Windows;
using System.Windows.Media;
using StatsMonitor.Settings;

namespace StatsMonitor.Helpers;

public static class ThemeHelper
{
    public static void Apply(bool dark)
    {
        var uri = new Uri($"Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Source?.OriginalString.StartsWith("Themes/") == true);
        if (existing != null) dicts.Remove(existing);
        dicts.Insert(0, new ResourceDictionary { Source = uri });
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
