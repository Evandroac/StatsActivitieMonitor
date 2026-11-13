using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using StatsMonitor.Helpers;

namespace StatsMonitor.ViewModels;

/// <summary>Presentation state for one monitored component (CPU, GPU or RAM).</summary>
public sealed class ComponentViewModel : ObservableObject
{
    public const int HistoryLength = 60;
    public const string NotAvailable = "N/D";

    private readonly Func<double?, Brush> _tempBrush;
    private readonly Func<double?, int>? _level;
    private double? _minTemp, _maxTemp, _temp;

    private double _load;
    private string _loadText = NotAvailable, _tempText = NotAvailable, _clockText = NotAvailable;
    private string _totalText = NotAvailable, _usedText = NotAvailable, _availableText = NotAvailable;
    private string _minMaxText = NotAvailable, _name = "";
    private Brush _tempBrushValue = Brushes.Gray;
    private bool _showTemperature = true;

    public ComponentViewModel(string title, Func<double?, Brush> tempBrush, Func<double?, int>? level = null)
    {
        Title = title;
        _level = level;
        _tempBrush = tempBrush;
        for (int i = 0; i < HistoryLength; i++) History.Add(0);
    }

    public string Title { get; }
    public ObservableCollection<DetailItem> Details { get; } = [];
    public bool HasTemperature { get; init; } = true;
    public ObservableCollection<double> History { get; } = [];

    public string Name { get => _name; private set => Set(ref _name, value); }
    public double Load { get => _load; private set => Set(ref _load, value); }
    public string LoadText { get => _loadText; private set => Set(ref _loadText, value); }
    public string TempText
    {
        get => _tempText;
        private set { if (Set(ref _tempText, value)) Raise(nameof(GadgetTemp)); }
    }
    public string MinMaxText { get => _minMaxText; private set => Set(ref _minMaxText, value); }
    public string ClockText { get => _clockText; private set => Set(ref _clockText, value); }
    public string TotalText { get => _totalText; private set => Set(ref _totalText, value); }
    public string UsedText { get => _usedText; private set => Set(ref _usedText, value); }
    public string AvailableText { get => _availableText; private set => Set(ref _availableText, value); }
    public Brush TempBrush { get => _tempBrushValue; private set => Set(ref _tempBrushValue, value); }

    public bool ShowTemperature
    {
        get => _showTemperature;
        set { if (Set(ref _showTemperature, value)) { Raise(nameof(GadgetTemp)); RefreshBrush(); } }
    }

    /// <summary>Temperature suffix for the compact gadget, empty when temperatures are hidden.</summary>
    public string GadgetTemp => _showTemperature ? $" | {_tempText}" : "";

    public void Update(string? name, double? load, double? temp, double? clockMhz = null,
        double? totalGb = null, double? usedGb = null, double? availableGb = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Loc.T("Não detectado") : name;
        Load = load ?? 0;
        LoadText = load.HasValue ? $"{load.Value:0}%" : NotAvailable;
        ClockText = clockMhz.HasValue ? (clockMhz.Value >= 1000 ? $"{clockMhz.Value / 1000:0.00} GHz" : $"{clockMhz.Value:0} MHz") : NotAvailable;
        TotalText = Gb(totalGb);
        UsedText = Gb(usedGb);
        AvailableText = Gb(availableGb);

        _temp = temp;
        if (temp.HasValue)
        {
            _minTemp = _minTemp.HasValue ? Math.Min(_minTemp.Value, temp.Value) : temp.Value;
            _maxTemp = _maxTemp.HasValue ? Math.Max(_maxTemp.Value, temp.Value) : temp.Value;
        }
        TempText = temp.HasValue ? $"{temp.Value:0}°C" : NotAvailable;
        MinMaxText = _minTemp.HasValue ? Loc.T("mín {0:0}°C · máx {1:0}°C", _minTemp!.Value, _maxTemp!.Value) : NotAvailable;
        RefreshBrush();

        History.RemoveAt(0);
        History.Add(load ?? 0);
    }

    private int _alertLevel;
    private Brush _alertBackground = Brushes.Transparent, _alertBorder = Brushes.Transparent;
    /// <summary>0 = normal/unknown, 1 = warning, 2 = critical.</summary>
    public int AlertLevel { get => _alertLevel; private set => Set(ref _alertLevel, value); }
    public Brush AlertBackground { get => _alertBackground; private set => Set(ref _alertBackground, value); }
    public Brush AlertBorder { get => _alertBorder; private set => Set(ref _alertBorder, value); }

    public void RefreshBrush()
    {
        TempBrush = _tempBrush(_temp);
        int lvl = _level is null || !_showTemperature ? 0 : Math.Max(0, _level(_temp));
        if (lvl == 0 || TempBrush is not SolidColorBrush sb)
        {
            AlertLevel = 0;
            return;
        }
        var card = (Application.Current?.TryFindResource("CardBg") as SolidColorBrush)?.Color ?? Color.FromRgb(30, 30, 30);
        var c = sb.Color;
        double k = lvl == 2 ? 0.34 : 0.20;
        Color Mix(double a) => Color.FromRgb(
            (byte)(card.R + (c.R - card.R) * a), (byte)(card.G + (c.G - card.G) * a), (byte)(card.B + (c.B - card.B) * a));
        var g = new LinearGradientBrush(Mix(k), Mix(k * 0.25), new Point(0, 0), new Point(1, 1));
        g.Freeze();
        var border = new SolidColorBrush(Color.FromArgb(lvl == 2 ? (byte)230 : (byte)170, c.R, c.G, c.B));
        border.Freeze();
        AlertBackground = g;
        AlertBorder = border;
        AlertLevel = lvl;
    }

    public static string Gb(double? v) => v.HasValue ? $"{v.Value:0.0} GB" : NotAvailable;
}

/// <summary>A label/value pair shown in a component card.</summary>
public sealed class DetailItem : ObservableObject
{
    private string _value;
    private readonly string _key;
    public DetailItem(string label, string value)
    {
        _key = label; _value = value;
        Loc.LanguageChanged += () => Raise(nameof(Label));
    }
    public string Label => Loc.T(_key);
    public string Value { get => _value; set => Set(ref _value, value); }
}
