using System.Windows;
using System.Windows.Media;
using StatsMonitor.Helpers;
using StatsMonitor.Services;

namespace StatsMonitor.ViewModels;

/// <summary>Texts and colors for the battery panel on the main screen.</summary>
public sealed class BatteryViewModel : ObservableObject
{
    private static readonly Brush Green = Frozen("#3CC47C"), Amber = Frozen("#E0A030"), Red = Frozen("#FF5252");
    private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

    private bool _present;
    private double _percent;
    private string _percentText = "--", _statusText = "--", _sourceText = "--", _remainingText = "--", _toFullText = "--";
    private string _healthText = "--", _capacityText = "--", _powerText = "--";
    private double _health;
    private Brush _levelBrush = Brushes.Gray, _healthBrush = Brushes.Gray;
    private BatteryInfo? _last;

    public bool Present { get => _present; private set => Set(ref _present, value); }
    public double Percent { get => _percent; private set => Set(ref _percent, value); }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }
    public string ToFullText { get => _toFullText; private set => Set(ref _toFullText, value); }
    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }
    public string CapacityText { get => _capacityText; private set => Set(ref _capacityText, value); }
    public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
    public double Health { get => _health; private set => Set(ref _health, value); }
    public Brush LevelBrush { get => _levelBrush; private set => Set(ref _levelBrush, value); }
    public Brush HealthBrush { get => _healthBrush; private set => Set(ref _healthBrush, value); }

    public void Refresh() { if (_last is { } l) Update(l); }

    public void Update(BatteryInfo b)
    {
        _last = b;
        Present = b.Present;
        if (!b.Present) return;

        Percent = b.Percent ?? 0;
        PercentText = b.Percent is { } p ? $"{p}%" : "--";
        StatusText = StateText(b.State);
        SourceText = b.PluggedIn ? Loc.T("Carregador (tomada)") : Loc.T("Bateria");
        RemainingText = b.State switch
        {
            BatteryState.Discharging => b.Remaining is { } r ? FormatDuration(r) : Loc.T("Calculando…"),
            _ => Loc.T("Na tomada"),
        };
        ToFullText = b.State switch
        {
            BatteryState.Charging => b.ToFull is { } t ? FormatDuration(t) : Loc.T("Calculando…"),
            BatteryState.Full => Loc.T("Totalmente carregada"),
            BatteryState.PluggedNotCharging => Loc.T("Carregamento pausado"),
            _ => Loc.T("Não está carregando"),
        };
        PowerText = b.RateW is { } w ? $"{w:0.#} W" : "--";
        HealthText = b.HealthPercent is { } h ? $"{HealthLabel(h)} · {h:0}%" : Loc.T("Indisponível");
        Health = b.HealthPercent ?? 0;
        CapacityText = b.FullMWh is { } f && b.DesignMWh is { } d
            ? Loc.T("{0} de {1} (original)", Wh(f), Wh(d))
            : b.FullMWh is { } f2 ? Wh(f2) : "--";

        LevelBrush = b.State == BatteryState.Charging ? Green
            : b.Percent is <= 10 && b.State == BatteryState.Discharging ? Red
            : b.Percent is <= 20 && b.State == BatteryState.Discharging ? Amber
            : Application.Current?.TryFindResource("Accent") as Brush ?? Green;
        HealthBrush = BatteryService.HealthLevel(b.HealthPercent) switch { 0 => Green, 1 => Amber, 2 => Red, _ => Brushes.Gray };
    }

    public static string StateText(BatteryState s) => s switch
    {
        BatteryState.Charging => Loc.T("Carregando"),
        BatteryState.Full => Loc.T("Totalmente carregada"),
        BatteryState.Discharging => Loc.T("Descarregando"),
        BatteryState.PluggedNotCharging => Loc.T("Conectada, sem carregar"),
        _ => Loc.T("Indisponível"),
    };

    public static string HealthLabel(double health) => BatteryService.HealthLevel(health) switch
    {
        0 => Loc.T("Boa"),
        1 => Loc.T("Desgastada"),
        _ => Loc.T("Ruim (considere trocar)"),
    };

    public static string Wh(int mWh) => $"{mWh / 1000.0:0.0} Wh";

    public static string FormatDuration(TimeSpan t)
    {
        int minutes = (int)Math.Round(t.TotalMinutes);
        if (minutes < 1) return "< 1 min";
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
    }
}
