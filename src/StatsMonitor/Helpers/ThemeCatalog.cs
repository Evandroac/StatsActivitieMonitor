using System.Windows.Media;

namespace StatsMonitor.Helpers;

/// <summary>A named visual style: a complete palette plus a font, applied on top of the base dark/light theme.</summary>
public sealed class VisualTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required bool Dark { get; init; }
    public required string Font { get; init; }
    public required IReadOnlyDictionary<string, Color> Palette { get; init; }

    public Brush WindowBrush => Brush("WindowBg");
    public Brush CardBrush => Brush("CardBg");
    public Brush BorderBrush => Brush("CardBorder");
    public Brush TextBrush => Brush("TextPrimary");
    public Brush AccentBrush => Brush("Accent");
    public Brush Accent2Brush => Brush("Accent2");
    public FontFamily FontFamily => new(Font);

    private Brush Brush(string key)
    {
        var b = new SolidColorBrush(Palette[key]);
        b.Freeze();
        return b;
    }
}

public static class ThemeCatalog
{
    public static VisualTheme? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(t => t.Id == id);

    public static readonly IReadOnlyList<VisualTheme> All = Build();

    public static IEnumerable<IGrouping<string, VisualTheme>> Groups => All.GroupBy(t => t.Category);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static Color Mix(Color a, Color b, double k) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

    private static VisualTheme T(string id, string name, string cat, bool dark, string font,
        string win, string card, string border, string text, string text2, string accent, string accent2)
    {
        Color w = C(win), c = C(card), b = C(border), t = C(text), t2 = C(text2);
        var p = new Dictionary<string, Color>
        {
            ["WindowBg"] = w, ["CardBg"] = c, ["CardBorder"] = b, ["TextPrimary"] = t, ["TextSecondary"] = t2,
            ["TrackBrush"] = b, ["Accent"] = C(accent), ["Accent2"] = C(accent2),
            ["InputBg"] = Mix(c, t, 0.06),
            ["ControlBorder"] = Mix(b, t, 0.2), ["ControlBorderHover"] = Mix(b, t, 0.45),
            ["ScrollThumb"] = Mix(b, t, 0.25), ["ScrollThumbHover"] = Mix(b, t, 0.5),
            ["GadgetText"] = t,
        };
        var gb = Mix(c, w, 0.3); gb.A = 0xCC; p["GadgetBg"] = gb;
        var gh = Mix(c, t, 0.12); gh.A = 0xE6; p["GadgetHoverBg"] = gh;
        return new VisualTheme { Id = id, Name = name, Category = cat, Dark = dark, Font = font, Palette = p };
    }

    private static List<VisualTheme> Build()
    {
        const string xp = "Windows XP e clássicos";
        const string cyber = "Cyberpunk e futurismo";
        const string retro = "Retrô, vintage e anos 80/90";
        const string vapor = "Vaporwave";
        const string arcade = "Arcade / 8-bit";
        const string crt = "CRT / Terminal";
        const string modern = "Minimalismo e interfaces modernas";
        const string mini = "Minimalista";
        const string anime = "Anime, cultura japonesa e kawaii";
        const string art = "Estilos alternativos e artísticos";

        return
        [
            T("xp-luna", "Windows XP (Luna)", xp, false, "Tahoma", "#ECE9D8", "#FFFFFF", "#7F9DB9", "#000000", "#4D4D4D", "#245EDC", "#3C9B3C"),
            T("xp-olive", "Windows XP (Oliva)", xp, false, "Tahoma", "#ECE9D8", "#F8F7EE", "#A3A68B", "#1F2A10", "#5A6040", "#7A8F3A", "#B5651D"),
            T("win98", "Windows 98", xp, false, "Tahoma", "#C0C0C0", "#DFDFDF", "#808080", "#000000", "#404040", "#000080", "#008080"),

            T("cyberpunk", "Cyberpunk Neon", cyber, true, "Bahnschrift", "#0B0014", "#160028", "#4A1B6B", "#F5F5FF", "#A59AC7", "#00F0FF", "#FF2A6D"),
            T("future-ice", "Futurista (Gelo)", cyber, true, "Bahnschrift", "#050B14", "#0A1624", "#1C3A5A", "#E6F4FF", "#7FA2C2", "#35E0FF", "#7C8CFF"),
            T("hologram", "Holograma", cyber, true, "Bahnschrift", "#04120F", "#082420", "#12574D", "#DFFFF6", "#78B8A9", "#2DFFC4", "#F2FF3D"),

            T("synthwave", "Synthwave anos 80", retro, true, "Corbel", "#1A0B2E", "#2A1250", "#5B2A9B", "#FDF2FF", "#B79BDB", "#FF6EC7", "#FFD319"),
            T("vintage-sepia", "Vintage sépia", retro, false, "Georgia", "#E8DCC4", "#F6EEDD", "#BFA77A", "#3B2A1A", "#7A634A", "#A0522D", "#5E7D4F"),
            T("retro-90s", "Anos 90", retro, false, "Verdana", "#00A0A0", "#D4D0C8", "#404040", "#000000", "#303030", "#0A246A", "#800080"),

            T("vaporwave", "Vaporwave", vapor, true, "Segoe UI", "#1F1147", "#2E1A6B", "#7B5CD6", "#FFE6FF", "#B9A7F0", "#01CDFE", "#FF71CE"),
            T("vapor-pastel", "Vaporwave pastel", vapor, false, "Segoe UI", "#F3E8FF", "#FFFFFF", "#D8B4FE", "#3B1B5E", "#7E5BA8", "#E0489F", "#0A8DAA"),

            T("arcade", "Arcade 8-bit", arcade, true, "Consolas", "#000000", "#0E0E2C", "#3B3BFF", "#FFFFFF", "#9AA0FF", "#FFE600", "#FF3B3B"),
            T("gameboy", "Game Boy", arcade, false, "Consolas", "#8BAC0F", "#9BBC0F", "#306230", "#0F380F", "#306230", "#0F380F", "#306230"),

            T("terminal-green", "Terminal verde", crt, true, "Consolas", "#000800", "#001A00", "#0A5A0A", "#33FF33", "#1FAA1F", "#33FF33", "#B6FF33"),
            T("terminal-amber", "Terminal âmbar", crt, true, "Consolas", "#120A00", "#1F1200", "#5A3A00", "#FFB000", "#B87A00", "#FFB000", "#FF7A00"),

            T("fluent", "Moderno claro", modern, false, "Segoe UI", "#F3F3F3", "#FFFFFF", "#E1E1E1", "#1B1B1B", "#6B6B6B", "#0067C0", "#8764B8"),
            T("graphite", "Grafite", modern, true, "Segoe UI", "#1C1C1E", "#2C2C2E", "#3A3A3C", "#F2F2F7", "#98989D", "#0A84FF", "#BF5AF2"),
            T("nord", "Nórdico", modern, true, "Segoe UI", "#2E3440", "#3B4252", "#4C566A", "#ECEFF4", "#A3ABBA", "#88C0D0", "#B48EAD"),

            T("mini-white", "Branco puro", mini, false, "Segoe UI", "#FFFFFF", "#FAFAFA", "#E5E5E5", "#111111", "#777777", "#111111", "#777777"),
            T("mini-black", "Preto puro", mini, true, "Segoe UI", "#000000", "#0A0A0A", "#222222", "#FFFFFF", "#888888", "#FFFFFF", "#888888"),

            T("sakura", "Sakura kawaii", anime, false, "Trebuchet MS", "#FFF0F5", "#FFFFFF", "#FFC1D9", "#5A2A45", "#A86A8C", "#C42F68", "#2A8CC8"),
            T("neo-tokyo", "Neo Tóquio", anime, true, "Bahnschrift", "#14091F", "#20123A", "#4A2A7A", "#FFF0FA", "#B89AD0", "#FF4F9A", "#4FD1FF"),
            T("matcha", "Matcha zen", anime, false, "Yu Gothic UI", "#EEF1E6", "#FAFBF5", "#C5CDB2", "#2F3A2A", "#6B7A5E", "#5E8C4A", "#C0504D"),

            T("sunset", "Pôr do sol", art, true, "Segoe UI", "#1E0F1F", "#2E1630", "#6A2F4A", "#FFF1E6", "#C79AA0", "#FF7B54", "#FFB26B"),
            T("deep-ocean", "Oceano profundo", art, true, "Segoe UI", "#04141F", "#08283A", "#14506E", "#E6F7FF", "#7FB3CC", "#2EC4B6", "#5AA9FF"),
            T("art-deco", "Art déco", art, true, "Georgia", "#121212", "#1D1B16", "#8C7A3B", "#F5ECD0", "#B5A66E", "#D4AF37", "#B87333"),
        ];
    }
}
