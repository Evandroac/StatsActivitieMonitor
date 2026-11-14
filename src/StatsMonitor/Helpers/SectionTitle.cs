using System.Globalization;
using System.Windows.Data;

namespace StatsMonitor.Helpers;

/// <summary>Turns a section key of the unified System page into its localized header.</summary>
public sealed class SectionTitle : IValueConverter
{
    public static readonly SectionTitle Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value as string switch
    {
        "system" => "🖥️  " + Loc.T("Sistema"),
        "peripherals" => "🖱️  " + Loc.T("Periféricos"),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}