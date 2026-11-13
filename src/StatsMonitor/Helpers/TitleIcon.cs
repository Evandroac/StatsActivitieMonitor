using System.Globalization;
using System.Windows.Data;

namespace StatsMonitor.Helpers;

/// <summary>Maps a section title to a colorful emoji icon.</summary>
public sealed class TitleIcon : IValueConverter
{
    public static readonly TitleIcon Instance = new();

    private static readonly (string Key, string Icon)[] Map =
    {
        ("cpu", "🧠"), ("processador", "🧠"), ("gpu", "🎮"), ("vídeo", "🎮"), ("video", "🎮"),
        ("ram", "🧩"), ("memória", "🧩"), ("disco", "💽"), ("armazen", "💽"),
        ("rede", "🌐"), ("internet", "🌐"), ("computador", "💻"), ("sistema", "🪟"),
        ("placa", "🔌"), ("bios", "🔌"),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string ?? "").ToLowerInvariant();
        foreach (var (key, icon) in Map)
            if (s.Contains(key)) return icon;
        return "▫️";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
