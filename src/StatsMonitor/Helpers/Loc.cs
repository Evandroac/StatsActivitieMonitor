using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace StatsMonitor.Helpers;

/// <summary>Runtime translation: Portuguese text is the key, other languages come from <see cref="LocData"/>.</summary>
public sealed record LangInfo(string Code, string Name);

/// <summary>Runtime translation.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly Loc Instance = new();
    /// <summary>Base language used by inline pt/en text: "pt" (pt-BR, pt-PT) or "en" (everything else).</summary>
    public static string Lang { get; private set; } = "pt";
    /// <summary>Full UI language code, e.g. "pt", "pt-PT", "en", "en-GB", "es-MX", "zh-CN".</summary>
    public static string Code { get; private set; } = "pt";
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Bumped on every language change so bindings re-evaluate.</summary>
    public int Version { get; private set; }

    /// <summary>Supported languages (code, native name). "pt" and "en" are the stored codes for pt-BR and en-US.</summary>
    public static readonly LangInfo[] Languages =
    [
        new("pt", "Português (Brasil)"), new("pt-PT", "Português (Portugal)"),
        new("en", "English (US)"), new("en-GB", "English (UK)"),
        new("es", "Español"), new("es-MX", "Español (México)"),
        new("fr", "Français"), new("de", "Deutsch"), new("it", "Italiano"),
        new("zh-CN", "简体中文"), new("zh-TW", "繁體中文"),
        new("ja", "日本語"), new("ko", "한국어"), new("ar", "العربية"), new("hi", "हिन्दी"), new("ru", "Русский"),
    ];

    /// <summary>Maps any code (including legacy pt-BR / en-US) to a supported one; unknown values become "en".</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        if (code.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)) return "pt";
        if (code.Equals("en-US", StringComparison.OrdinalIgnoreCase)) return "en";
        foreach (var l in Languages) if (l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) return l.Code;
        return "en";
    }

    public static bool IsSupported(string? code) =>
        code != null && Languages.Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Picks the best supported language for an OS culture.</summary>
    public static string FromCulture(CultureInfo culture)
    {
        string n = culture.Name;
        if (n.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return n.Contains("TW", StringComparison.OrdinalIgnoreCase) || n.Contains("HK", StringComparison.OrdinalIgnoreCase)
                   || n.Contains("Hant", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        if (n.StartsWith("pt-PT", StringComparison.OrdinalIgnoreCase)) return "pt-PT";
        if (n.StartsWith("en-GB", StringComparison.OrdinalIgnoreCase)) return "en-GB";
        if (n.StartsWith("es-MX", StringComparison.OrdinalIgnoreCase)) return "es-MX";
        return Normalize(culture.TwoLetterISOLanguageName);
    }

    public static void SetLanguage(string? lang)
    {
        lang = Normalize(lang);
        if (lang == Code) return;
        Code = lang;
        Lang = lang is "pt" or "pt-PT" ? "pt" : "en";
        Instance.Version++;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(Version)));
        LanguageChanged?.Invoke();
    }

    public static string T(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (Code == "pt") return key;
        foreach (var d in LocData.Chain(Code))
            if (d.TryGetValue(key, out var v)) return v;
        return key;
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
