using System.Windows;
using Microsoft.Win32;

namespace StatsMonitor.Helpers;

/// <summary>Windows theme detection ("Follow system" and high contrast).</summary>
public static class SystemTheme
{
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return true;
        }
    }

    public static bool IsHighContrast => SystemParameters.HighContrast;
}
