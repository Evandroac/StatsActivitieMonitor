using StatsMonitor.Helpers;
using Xunit;

namespace StatsMonitor.Tests;

public class ThemeCatalogTests
{
    [Fact]
    public void ThemesHaveUniqueIdsAndCompletePalettes()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(t => t.Id).Distinct().Count());
        foreach (var t in ThemeCatalog.All)
            foreach (var key in ThemeHelper.ColorKeys.Concat(["InputBg", "ControlBorder", "GadgetBg"]))
                Assert.True(t.Palette.ContainsKey(key), $"{t.Id} missing {key}");
    }

    [Fact]
    public void UnknownThemeIdFallsBackToBase() => Assert.Null(ThemeCatalog.Find("nope"));

    [Fact]
    public void ThemeColorsAreReadable()
    {
        var bad = new List<string>();
        foreach (var t in ThemeCatalog.All)
        {
            var p = t.Palette;
            void Check(string name, double ratio, double min) { if (ratio < min) bad.Add($"{t.Id} {name} {ratio:0.0}"); }
            Check("text/card", ThemeHelper.ContrastRatio(p["TextPrimary"], p["CardBg"]), 4.5);
            Check("text/window", ThemeHelper.ContrastRatio(p["TextPrimary"], p["WindowBg"]), 4.5);
            Check("text2/card", ThemeHelper.ContrastRatio(p["TextSecondary"], p["CardBg"]), 3);
            Check("accent/card", ThemeHelper.ContrastRatio(p["Accent"], p["CardBg"]), 3);
            Check("accent2/card", ThemeHelper.ContrastRatio(p["Accent2"], p["CardBg"]), 3);
            Check("onaccent", ThemeHelper.ContrastRatio(ThemeHelper.ContrastText(p["Accent"]), p["Accent"]), 4.5);
        }
        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }
}