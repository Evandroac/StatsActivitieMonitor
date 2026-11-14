using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace StatsMonitor.Helpers;

/// <summary>
/// WPF draws emoji in a single colour. This tints the leading emoji of every TextBlock
/// (including those inside buttons) with a colour per icon, leaving the rest of the text untouched.
/// </summary>
public static class EmojiColors
{
    private static readonly Dictionary<int, Color> Map = new()
    {
        [0x26A0] = Hex("#F5B301"), [0x1F9E9] = Hex("#8E6CEF"), [0x1F514] = Hex("#F5B301"), [0x1F321] = Hex("#FF6B5B"),
        [0x1F310] = Hex("#3B9EFF"), [0x1F319] = Hex("#8E8CFF"), [0x2600] = Hex("#FFB020"), [0x1F4BD] = Hex("#2EC4B6"),
        [0x2699] = Hex("#8FA3BF"), [0x2328] = Hex("#8FA3BF"), [0x1F9E0] = Hex("#FF7AA8"), [0x1F4F7] = Hex("#7C8CA8"),
        [0x1F4C2] = Hex("#F5B301"), [0x1F504] = Hex("#3FB950"), [0x1F5BC] = Hex("#3B9EFF"), [0x1F4DD] = Hex("#F5B301"),
        [0x1FA9F] = Hex("#3B9EFF"), [0x1F4CB] = Hex("#C98A4B"), [0x1F5A5] = Hex("#4EA8DE"), [0x1F39B] = Hex("#8E6CEF"),
        [0x1F3AE] = Hex("#7C5CFF"), [0x1F50C] = Hex("#3FB950"), [0x1F3A8] = Hex("#FF7A45"), [0x1F50A] = Hex("#3B9EFF"),
        [0x1F520] = Hex("#3B9EFF"), [0x1F441] = Hex("#4EA8DE"), [0x1F6A8] = Hex("#FF4D4F"), [0x1F317] = Hex("#8E8CFF"),
        [0x1F343] = Hex("#3FB950"), [0x1F558] = Hex("#F5B301"), [0x1F4E1] = Hex("#2EC4B6"), [0x1F5A8] = Hex("#8FA3BF"),
        [0x1F4BB] = Hex("#4EA8DE"), [0x1F5B1] = Hex("#8FA3BF"), [0x1F4F6] = Hex("#3FB950"), [0x26A1] = Hex("#FFC400"),
        [0x2139] = Hex("#3B9EFF"), [0x1F6E1] = Hex("#3B9EFF"), [0x1F4CA] = Hex("#3B9EFF"), [0x2630] = Hex("#8FA3BF"),
        [0x1F50D] = Hex("#4EA8DE"), [0x23F1] = Hex("#F5B301"), [0x1FA7A] = Hex("#FF4D6D"), [0x1F4C8] = Hex("#3FB950"),
        [0x2713] = Hex("#3FB950"), [0x1F300] = Hex("#2EC4B6"),
    };

    private static readonly Color[] Fallback = [Hex("#3B9EFF"), Hex("#3FB950"), Hex("#F5B301"), Hex("#FF7A45"), Hex("#8E6CEF"), Hex("#2EC4B6")];
    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji");
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s);

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        Tint(tb);
        var d = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        d.RemoveValueChanged(tb, OnTextChanged);
        d.AddValueChanged(tb, OnTextChanged);
    }

    private static readonly HashSet<TextBlock> Busy = [];

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock tb && !Busy.Contains(tb)) Tint(tb);
    }

    private static bool IsEmoji(int cp) =>
        cp >= 0x1F000 || (cp >= 0x2600 && cp <= 0x27BF) || (cp >= 0x2300 && cp <= 0x23FF) || cp == 0x2139 || cp == 0x2630;

    private static void Tint(TextBlock tb)
    {
        var text = tb.Text;
        if (string.IsNullOrEmpty(text)) return;
        if (tb.Inlines.Count == 2 && tb.Inlines.FirstInline is Run { Tag: "emoji" } && tb.Inlines.LastInline is Run last && last.Text + ((Run)tb.Inlines.FirstInline).Text == text) return;
        if (!char.IsSurrogate(text[0]) && text[0] < 0x2300) return;
        int cp = char.ConvertToUtf32(text, 0);
        if (!IsEmoji(cp)) return;

        int end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        if (end > 8) return;

        var color = Map.TryGetValue(cp, out var c) ? c : Fallback[cp % Fallback.Length];
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        Busy.Add(tb);
        try
        {
            tb.Inlines.Clear();
            tb.Inlines.Add(new Run(text[..end]) { Foreground = brush, FontFamily = EmojiFont, Tag = "emoji" });
            if (end < text.Length) tb.Inlines.Add(new Run(text[end..]));
        }
        finally { Busy.Remove(tb); }
    }
}