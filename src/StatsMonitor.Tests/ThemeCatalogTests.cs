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
}
