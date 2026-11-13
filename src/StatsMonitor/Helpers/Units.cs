namespace StatsMonitor.Helpers;

/// <summary>Temperature unit selection. Sensor values and alert thresholds are always kept in °C.</summary>
public static class Units
{
    public static bool Fahrenheit { get; private set; }
    public static event Action? Changed;

    public static void SetFahrenheit(bool value)
    {
        if (Fahrenheit == value) return;
        Fahrenheit = value;
        Changed?.Invoke();
    }

    public static string Temp(double celsius) =>
        Fahrenheit ? $"{celsius * 9 / 5 + 32:0}°F" : $"{celsius:0}°C";
}
