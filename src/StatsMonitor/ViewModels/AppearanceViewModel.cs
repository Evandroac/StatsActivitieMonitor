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
    private readonly string _label;

    public ColorOption(string key, string label, string hex, Func<Color> defaultColor)
    {
        Key = key;
        _label = label;
        Loc.LanguageChanged += () => { Raise(nameof(Label)); Raise(nameof(Contrast)); Raise(nameof(IsCustom)); };
        _hex = hex;
        _default = defaultColor;
    }

    public string Key { get; }
    public string Label => Loc.T(_label);

    public string Hex
    {
        get => _hex;
        set
        {
            if (!Set(ref _hex, value)) return;
            Raise(nameof(Preview)); Raise(nameof(R)); Raise(nameof(G)); Raise(nameof(B)); RaiseInfo();
        }
    }

    private Color Current => ThemeHelper.TryParseColor(_hex, out var c) ? c : _default();

    public double R { get => Current.R; set => SetChannel(Color.FromArgb(Current.A, (byte)value, Current.G, Current.B)); }
    public double G { get => Current.G; set => SetChannel(Color.FromArgb(Current.A, Current.R, (byte)value, Current.B)); }
    public double B { get => Current.B; set => SetChannel(Color.FromArgb(Current.A, Current.R, Current.G, (byte)value)); }

    private void SetChannel(Color c) =>
        Hex = c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public double A { get => Current.A; set => SetChannel(Color.FromArgb((byte)value, Current.R, Current.G, Current.B)); }

    public string HexText => Current.A == 255 ? $"#{Current.R:X2}{Current.G:X2}{Current.B:X2}" : $"#{Current.A:X2}{Current.R:X2}{Current.G:X2}{Current.B:X2}";
    public string RgbText => $"rgb({Current.R}, {Current.G}, {Current.B})";
    public string AlphaText => $"{Current.A * 100 / 255}%";
    public string HslText
    {
        get
        {
            double r = Current.R / 255.0, g = Current.G / 255.0, b = Current.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) / 2, h = 0, s = 0;
            if (d > 0)
            {
                s = d / (1 - Math.Abs(2 * l - 1));
                h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
                if (h < 0) h += 360;
            }
            return $"hsl({h:0}°, {s * 100:0}%, {l * 100:0}%)";
        }
    }
    public string Contrast
    {
        get
        {
            double L(byte v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
            var lum = 0.2126 * L(Current.R) + 0.7152 * L(Current.G) + 0.0722 * L(Current.B);
            return Loc.T("Contraste: {0:0.0}:1 c/ branco · {1:0.0}:1 c/ preto", 1.05 / (lum + 0.05), (lum + 0.05) / 0.05);
        }
    }
    public bool IsCustom => ThemeHelper.TryParseColor(_hex, out _);

    private void RaiseInfo() { Raise(nameof(A)); Raise(nameof(HexText)); Raise(nameof(RgbText)); Raise(nameof(AlphaText)); Raise(nameof(HslText)); Raise(nameof(Contrast)); Raise(nameof(IsCustom)); }

    public Brush Preview => new SolidColorBrush(Current);
    public string DefaultHex => _default().ToString();

    public void Refresh() { Raise(nameof(Preview)); Raise(nameof(DefaultHex)); Raise(nameof(R)); Raise(nameof(G)); Raise(nameof(B)); RaiseInfo(); }
    public void Reset() => Hex = "";
}

/// <summary>A selectable visual theme card; selecting it applies the theme through the owning view model.</summary>
public sealed class ThemeChoice : ObservableObject
{
    private readonly Action<ThemeChoice> _pick;
    private readonly Func<string> _current;

    public ThemeChoice(VisualTheme theme, Func<string> current, Action<ThemeChoice> pick)
    {
        Theme = theme; _current = current; _pick = pick;
    }

    public VisualTheme Theme { get; }
    public string Name => Loc.T(Theme.Name);
    public bool IsSelected
    {
        get => _current() == Theme.Id;
        set { if (value) _pick(this); }
    }
    public void Refresh() => Raise(nameof(IsSelected));
}

public sealed class ThemeGroup
{
    public ThemeGroup(string category, IReadOnlyList<ThemeChoice> items) { _category = category; Items = items; }
    private readonly string _category;
    public string Category => Loc.T(_category);
    public IReadOnlyList<ThemeChoice> Items { get; }
}

public sealed class AppearanceViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly Action _onApplied;
    private bool _dark;
    private string _themeId = "";
    private double _gadgetOpacity;

    public AppearanceViewModel(SettingsService service, Action onApplied)
    {
        _service = service;
        _onApplied = onApplied;
        var s = service.Current;
        _dark = s.DarkTheme;
        _themeId = ThemeCatalog.Find(s.ThemeId) is { } known ? known.Id : "";
        if (_themeId != "") _dark = ThemeCatalog.Find(_themeId)!.Dark;
        _gadgetOpacity = s.GadgetOpacity ?? 80;

        string Ov(string key) => s.ColorOverrides.TryGetValue(key, out var v) ? v : "";
        ColorOption Themed(string key, string label) => new(key, label, Ov(key), () => ThemeHelper.ThemeDefault(key, _dark, _themeId));
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
        ThemeGroups = ThemeCatalog.Groups.Select(g => new ThemeGroup(g.Key, g.Select(v => new ThemeChoice(v, () => _themeId, c => SetBase(c.Theme.Dark, c.Theme.Id))).ToList())).ToList();
        InitPresets();
    }

    public IReadOnlyList<ThemeGroup> ThemeGroups { get; }

    public IReadOnlyList<ColorOption> MainOptions { get; }
    public IReadOnlyList<ColorOption> GadgetOptions { get; }
    public IReadOnlyList<ColorOption> AlertOptions { get; }

    public bool DarkTheme { get => _dark && _themeId == ""; set { if (value) SetBase(true, ""); } }

    public bool LightTheme { get => !_dark && _themeId == ""; set { if (value) SetBase(false, ""); } }

    // Picks the base dark/light theme or a visual theme; a new pick also drops custom colors so the theme shows as designed.
    private void SetBase(bool dark, string themeId, bool resetColors = true)
    {
        if (_dark == dark && _themeId == themeId) return;
        _dark = dark;
        _themeId = themeId;
        if (resetColors) foreach (var o in MainOptions.Concat(GadgetOptions)) o.Reset();
        foreach (var o in MainOptions.Concat(GadgetOptions)) o.Refresh();
        Raise(nameof(DarkTheme)); Raise(nameof(LightTheme));
        foreach (var g in ThemeGroups) foreach (var c in g.Items) c.Refresh();
        _themePicked = true;
        ThemePicked?.Invoke();
        AutoSave();
    }

    private bool _themePicked;

    /// <summary>Raised when the user picks a theme, which overrides "follow Windows theme".</summary>
    public event Action? ThemePicked;
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
            ThemeId = _themeId,
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
        SetBase(p.DarkTheme, ThemeCatalog.Find(p.ThemeId) is { } pv ? pv.Id : "", false);
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
        s.ThemeId = _themeId;
        if (_themePicked) { s.FollowSystemTheme = false; _themePicked = false; }
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
