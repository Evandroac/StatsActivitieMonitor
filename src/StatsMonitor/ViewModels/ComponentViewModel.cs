using System.Collections.ObjectModel;
using System.Windows.Media;
using StatsMonitor.Helpers;

namespace StatsMonitor.ViewModels;

/// <summary>Presentation state for one monitored component (CPU, GPU, RAM or VRAM).</summary>
public sealed class ComponentViewModel : ObservableObject
{
    public const int HistoryLength = 60;
    public const string NotAvailable = "N/D";

    private readonly Func<double?, Brush> _tempBrush;
    private double? _minTemp, _maxTemp, _temp;

    private double _load;
    private string _loadText = NotAvailable, _tempText = NotAvailable, _clockText = NotAvailable;
    private string _totalText = NotAvailable, _usedText = NotAvailable, _availableText = NotAvailable;
    private string _minMaxText = NotAvailable, _name = "";
    private Brush _tempBrushValue = Brushes.Gray;
    private bool _showTemperature = true;

    public ComponentViewModel(string title, Func<double?, Brush> tempBrush)
    {
        Title = title;
        _tempBrush = tempBrush;
        for (int i = 0; i < HistoryLength; i++) History.Add(0);
    }

    public string Title { get; }
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
        set { if (Set(ref _showTemperature, value)) Raise(nameof(GadgetTemp)); }
    }

    /// <summary>Temperature suffix for the compact gadget, empty when temperatures are hidden.</summary>
    public string GadgetTemp => _showTemperature ? $" | {_tempText}" : "";

    public void Update(string? name, double? load, double? temp, double? clockMhz = null,
        double? totalGb = null, double? usedGb = null, double? availableGb = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Não detectado" : name;
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
        MinMaxText = _minTemp.HasValue ? $"mín {_minTemp:0}°C · máx {_maxTemp:0}°C" : NotAvailable;
        RefreshBrush();

        History.RemoveAt(0);
        History.Add(load ?? 0);
    }

    public void RefreshBrush() => TempBrush = _tempBrush(_temp);

    public static string Gb(double? v) => v.HasValue ? $"{v.Value:0.0} GB" : NotAvailable;
}
