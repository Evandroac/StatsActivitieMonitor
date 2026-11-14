namespace StatsMonitor.Helpers;

/// <summary>Language lookup chains plus regional variants derived from a base language by word substitution.</summary>
public static partial class LocData
{
    private static readonly (string From, string To)[] PtPtWords =
    [
        ("Arquivos", "Ficheiros"), ("arquivos", "ficheiros"), ("Arquivo", "Ficheiro"), ("arquivo", "ficheiro"),
        ("Telas", "Ecrãs"), ("telas", "ecrãs"), ("Tela", "Ecrã"), ("tela", "ecrã"),
        ("Excluir", "Eliminar"), ("excluir", "eliminar"), ("Mouses", "Ratos"), ("mouses", "ratos"), ("Mouse", "Rato"), ("mouse", "rato"),
        ("Salvar", "Guardar"), ("salvar", "guardar"), ("Salva ", "Guarda "), ("Salvo", "Guardado"), ("salvo", "guardado"),
        ("Usuário", "Utilizador"), ("usuário", "utilizador"), ("Aplicativos", "Aplicações"), ("aplicativos", "aplicações"),
        ("Aplicativo", "Aplicação"), ("aplicativo", "aplicação"), ("Gerenciador", "Gestor"), ("gerenciador", "gestor"),
        ("Baixar", "Transferir"), ("baixar", "transferir"), ("Celular", "Telemóvel"), ("celular", "telemóvel"),
        ("Ônibus", "Autocarro"), ("Planilha", "Folha de cálculo"), ("projeto", "projecto"),
    ];

    private static readonly (string From, string To)[] EnGbWords =
    [
        ("Colors", "Colours"), ("colors", "colours"), ("Color", "Colour"), ("color", "colour"),
        ("centered", "centred"), ("Centered", "Centred"), ("center", "centre"), ("Center", "Centre"),
        ("Customize", "Customise"), ("customize", "customise"), ("Optimize", "Optimise"), ("optimize", "optimise"),
        ("Analyze", "Analyse"), ("analyze", "analyse"), ("behavior", "behaviour"), ("Behavior", "Behaviour"),
        ("gray", "grey"), ("Gray", "Grey"), ("Favorites", "Favourites"), ("favorites", "favourites"), ("Favorite", "Favourite"),
        ("Minimize", "Minimise"), ("minimize", "minimise"), ("Maximize", "Maximise"), ("maximize", "maximise"),
        ("Initialize", "Initialise"), ("Organize", "Organise"), ("Utilization", "Utilisation"), ("utilization", "utilisation"),
    ];

    private static readonly (string From, string To)[] EsMxWords =
    [
        ("ordenadores", "computadoras"), ("Ordenadores", "Computadoras"), ("ordenador", "computadora"), ("Ordenador", "Computadora"),
        ("Ratones", "Mouses"), ("ratones", "mouses"), ("Ratón", "Mouse"), ("ratón", "mouse"),
        ("móviles", "celulares"), ("móvil", "celular"), ("Móvil", "Celular"),
    ];

    private static Dictionary<string, string> Derive(IEnumerable<KeyValuePair<string, string>> src, (string From, string To)[] words, bool onKey)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, v) in src)
        {
            string s = onKey ? k : v;
            string r = s;
            foreach (var (from, to) in words) r = r.Replace(from, to);
            if (r != s) d[k] = r;
        }
        return d;
    }

    private static readonly Lazy<Dictionary<string, string>> PtPt = new(() =>
    {
        var d = new Dictionary<string, string>();
        foreach (var k in En.Keys)
        {
            string r = k;
            foreach (var (from, to) in PtPtWords) r = r.Replace(from, to);
            if (r != k) d[k] = r;
        }
        return d;
    });

    private static readonly Lazy<Dictionary<string, string>> EnGb = new(() => Derive(En, EnGbWords, false));
    private static readonly Lazy<Dictionary<string, string>> EsMx = new(() => Derive(Es, EsMxWords, false));

    /// <summary>Dictionaries to try, in order, for a language code. Missing keys fall back to English, then to the Portuguese key.</summary>
    public static IEnumerable<Dictionary<string, string>> Chain(string code)
    {
        switch (code)
        {
            case "pt-PT": yield return PtPt.Value; yield break;
            case "en": break;
            case "en-GB": yield return EnGb.Value; break;
            case "es-MX": yield return EsMx.Value; yield return Es; break;
            case "es": yield return Es; break;
            case "fr": yield return Fr; break;
            case "de": yield return De; break;
            case "it": yield return It; break;
            case "zh-CN": yield return ZhCn; break;
            case "zh-TW": yield return ZhTw; break;
            case "ja": yield return Ja; break;
            case "ko": yield return Ko; break;
            case "ar": yield return Ar; break;
            case "hi": yield return Hi; break;
            case "ru": yield return Ru; break;
        }
        yield return En;
    }
}
