using System.Globalization;
using System.Windows.Data;

namespace StatsMonitor.Helpers;

/// <summary>Shows a Celsius value in the selected unit and converts the edited value back to Celsius.</summary>
public sealed class TempUnitConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double c ? Units.FromCelsius(c) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? Math.Round(Units.ToCelsius(d), 1) : value;
}