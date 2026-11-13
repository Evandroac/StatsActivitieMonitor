using System.Windows.Input;
using System.Windows.Media;
using StatsMonitor.Helpers;
using StatsMonitor.Settings;

namespace StatsMonitor.ViewModels;

/// <summary>One editable color: empty hex means "use the theme default".</summary>
public sealed class ColorOption : ObservableObject
{
    private readonly Func<Color> _default;
    private string _hex;

    public ColorOption(string key, string label, string hex, Func<Color> defaultColor)
    {
        Key = key;
        Label = label;
        _hex = hex;
        _default = defaultColor;
    }

    public string Key { get; }
    public string Label { get; }

    public string Hex
    {
        get => _hex;
        set { if (Set(ref _hex, value)) Raise(nameof(Preview)); }
    }

    public Brush Preview => new SolidColorBrush(ThemeHelper.TryParseColor(_hex, out var c) ? c : _default());
    public string DefaultHex => _default().ToString();

    public void Refresh() => Raise(nameof(Preview));
    public void Reset() => Hex = "";
}

public sealed class AppearanceViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly Action _onApplied;
    private bool _dark;
    private double _gadgetOpacity;

    public AppearanceViewModel(SettingsService service, Action onApplied)
    {
        _service = service;
        _onApplied = onApplied;
        var s = service.Current;
        _dark = s.DarkTheme;
        _gadgetOpacity = s.GadgetOpacity ?? 80;

        string Ov(string key) => s.ColorOverrides.TryGetValue(key, out var v) ? v : "";
        ColorOption Themed(string key, string label) => new(key, label, Ov(key), () => ThemeHelper.ThemeDefault(key, _dark));
        ColorOption Fixed(string key, string label, string value, string def) =>
            new(key, label, value, () => (Color)ColorConverter.ConvertFromString(def));

        MainOptions =
        [
            Themed("WindowBg", "Fundo da janela"),
            Themed("CardBg", "Fundo dos painéis"),
            Themed("CardBorder", "Borda dos painéis"),
            Themed("TextPrimary", "Texto principal"),
            Themed("TextSecondary", "Texto secundário"),
            Themed("Accent", "Destaque (CPU, GPU, rede)"),
            Themed("Accent2", "Destaque (memória)"),
            Themed("TrackBrush", "Trilho dos medidores"),
        ];
        GadgetOptions =
        [
            Themed("GadgetBg", "Fundo do gadget"),
            Themed("GadgetText", "Texto do gadget"),
        ];
        AlertOptions =
        [
            Fixed("Normal", "Normal", s.NormalColor, "#3DDC84"),
            Fixed("Warning", "Atenção", s.WarningColor, "#FFC107"),
            Fixed("Critical", "Crítica", s.CriticalColor, "#FF4D4F"),
        ];

        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
    }

    public IReadOnlyList<ColorOption> MainOptions { get; }
    public IReadOnlyList<ColorOption> GadgetOptions { get; }
    public IReadOnlyList<ColorOption> AlertOptions { get; }

    public bool DarkTheme
    {
        get => _dark;
        set
        {
            if (!Set(ref _dark, value)) return;
            Raise(nameof(LightTheme));
            foreach (var o in MainOptions.Concat(GadgetOptions)) o.Refresh();
        }
    }

    public bool LightTheme { get => !_dark; set => DarkTheme = !value; }

    public double GadgetOpacity { get => _gadgetOpacity; set => Set(ref _gadgetOpacity, Math.Clamp(Math.Round(value), 10, 100)); }

    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }

    public event Action? Saved;

    private void Reset()
    {
        foreach (var o in MainOptions.Concat(GadgetOptions)) o.Reset();
        AlertOptions[0].Hex = "#3DDC84";
        AlertOptions[1].Hex = "#FFC107";
        AlertOptions[2].Hex = "#FF4D4F";
        GadgetOpacity = 80;
    }

    private void Save()
    {
        var s = _service.Current;
        s.DarkTheme = _dark;
        s.GadgetOpacity = (int)_gadgetOpacity;
        s.ColorOverrides = MainOptions.Concat(GadgetOptions)
            .Where(o => ThemeHelper.TryParseColor(o.Hex, out _))
            .ToDictionary(o => o.Key, o => o.Hex.Trim());

        string Alert(int i, string fallback) => ThemeHelper.TryParseColor(AlertOptions[i].Hex, out _) ? AlertOptions[i].Hex.Trim() : fallback;
        s.NormalColor = Alert(0, "#3DDC84");
        s.WarningColor = Alert(1, "#FFC107");
        s.CriticalColor = Alert(2, "#FF4D4F");

        _service.Save();
        ThemeHelper.Apply(s);
        _onApplied();
        Saved?.Invoke();
    }
}
