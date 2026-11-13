using System.Collections.ObjectModel;
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
        set
        {
            if (!Set(ref _hex, value)) return;
            Raise(nameof(Preview)); Raise(nameof(R)); Raise(nameof(G)); Raise(nameof(B));
        }
    }

    private Color Current => ThemeHelper.TryParseColor(_hex, out var c) ? c : _default();

    public double R { get => Current.R; set => SetChannel(Color.FromArgb(Current.A, (byte)value, Current.G, Current.B)); }
    public double G { get => Current.G; set => SetChannel(Color.FromArgb(Current.A, Current.R, (byte)value, Current.B)); }
    public double B { get => Current.B; set => SetChannel(Color.FromArgb(Current.A, Current.R, Current.G, (byte)value)); }

    private void SetChannel(Color c) =>
        Hex = c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public Brush Preview => new SolidColorBrush(Current);
    public string DefaultHex => _default().ToString();

    public void Refresh() { Raise(nameof(Preview)); Raise(nameof(DefaultHex)); Raise(nameof(R)); Raise(nameof(G)); Raise(nameof(B)); }
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

        foreach (var o in MainOptions.Concat(GadgetOptions).Concat(AlertOptions))
            o.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ColorOption.Hex)) AutoSave(); };
        _timer.Tick += (_, _) => Flush();
        ResetCommand = new RelayCommand(Reset);
        InitPresets();
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
            AutoSave();
        }
    }

    public bool LightTheme { get => !_dark; set => DarkTheme = !value; }

    public double GadgetOpacity { get => _gadgetOpacity; set { if (Set(ref _gadgetOpacity, Math.Clamp(Math.Round(value), 10, 100))) AutoSave(); } }

    public ICommand ResetCommand { get; }

    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    // Every change is applied and persisted automatically (debounced).
    private void AutoSave()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Persists any pending change immediately.</summary>
    public void Flush()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        Apply();
        _service.Save();
    }

    private void Reset()
    {
        foreach (var o in MainOptions.Concat(GadgetOptions)) o.Reset();
        AlertOptions[0].Hex = "#3DDC84";
        AlertOptions[1].Hex = "#FFC107";
        AlertOptions[2].Hex = "#FF4D4F";
        GadgetOpacity = 80;
    }

    private AppearancePreset? _selectedPreset;
    private string _presetName = "";

    public ObservableCollection<AppearancePreset> Presets { get; } = new();
    public ICommand SavePresetCommand { get; private set; } = null!;
    public ICommand LoadPresetCommand { get; private set; } = null!;
    public ICommand DeletePresetCommand { get; private set; } = null!;

    public AppearancePreset? SelectedPreset
    {
        get => _selectedPreset;
        set { if (Set(ref _selectedPreset, value) && value is not null) PresetName = value.Name; }
    }

    public string PresetName { get => _presetName; set => Set(ref _presetName, value); }

    private void InitPresets()
    {
        foreach (var p in _service.Current.Presets) Presets.Add(p);
        SavePresetCommand = new RelayCommand(SavePreset);
        LoadPresetCommand = new RelayCommand(LoadPreset);
        DeletePresetCommand = new RelayCommand(DeletePreset);
    }

    private void SavePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) return;
        var p = new AppearancePreset
        {
            Name = name,
            DarkTheme = _dark,
            GadgetOpacity = (int)_gadgetOpacity,
            ColorOverrides = MainOptions.Concat(GadgetOptions)
                .Where(o => ThemeHelper.TryParseColor(o.Hex, out _))
                .ToDictionary(o => o.Key, o => o.Hex.Trim()),
            NormalColor = AlertOptions[0].Hex,
            WarningColor = AlertOptions[1].Hex,
            CriticalColor = AlertOptions[2].Hex,
        };
        var i = Presets.ToList().FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Presets[i] = p; else Presets.Add(p);
        _service.Current.Presets = Presets.ToList();
        _service.Save();
        SelectedPreset = p;
    }

    private AppearancePreset? FindByName() =>
        Presets.FirstOrDefault(x => string.Equals(x.Name, PresetName.Trim(), StringComparison.OrdinalIgnoreCase));

    private void LoadPreset()
    {
        if (FindByName() is not { } p) return;
        DarkTheme = p.DarkTheme;
        foreach (var o in MainOptions.Concat(GadgetOptions))
            o.Hex = p.ColorOverrides.TryGetValue(o.Key, out var v) ? v : "";
        AlertOptions[0].Hex = p.NormalColor;
        AlertOptions[1].Hex = p.WarningColor;
        AlertOptions[2].Hex = p.CriticalColor;
        GadgetOpacity = p.GadgetOpacity;
        Apply();
    }

    private void DeletePreset()
    {
        if (FindByName() is not { } p) return;
        Presets.Remove(p);
        _service.Current.Presets = Presets.ToList();
        _service.Save();
        SelectedPreset = null;
        PresetName = "";
    }

    private void Apply()
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

        ThemeHelper.Apply(s);
        _onApplied();
    }
}
