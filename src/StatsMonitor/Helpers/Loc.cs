using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace StatsMonitor.Helpers;

/// <summary>Runtime translation: Portuguese text is the key, English comes from <see cref="LocData"/>.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly Loc Instance = new();
    public static string Lang { get; private set; } = "pt";
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bumped on every language change so bindings re-evaluate.</summary>
    public int Version { get; private set; }

    public static void SetLanguage(string? lang)
    {
        lang = lang == "en" ? "en" : "pt";
        if (lang == Lang) return;
        Lang = lang;
        Instance.Version++;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(Version)));
        LanguageChanged?.Invoke();
    }

    public static string T(string? key)
    {
        if (string.IsNullOrEmpty(key) || Lang != "en") return key ?? "";
        return LocData.En.TryGetValue(key, out var v) ? v : key;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);
}

public sealed class TrConverter : IValueConverter
{
    public static readonly TrConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.T(parameter as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>{h:Tr 'texto em português'} - re-translates live when the language changes.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var b = new Binding(nameof(Loc.Version))
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
            Converter = TrConverter.Instance,
            ConverterParameter = Key
        };
        return b.ProvideValue(serviceProvider);
    }
}
