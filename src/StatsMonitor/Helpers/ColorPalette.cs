using System.Globalization;
using System.Windows.Data;

namespace StatsMonitor.Helpers;

public static class ColorPalette
{
    public static readonly string[] Colors =
    {
        "#FFFFFF", "#C8CDD6", "#6B7280", "#1E1E24", "#000000",
        "#FF4D4F", "#FF8A3D", "#FFC107", "#3DDC84", "#00C2A8",
        "#00B4FF", "#4C8DFF", "#7C5CFF", "#C45CFF", "#FF5CA8",
    };
}

public sealed class BoolTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "";
    public string FalseText { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
