using System.Globalization;
using System.Text;
using StatsMonitor.Services;

namespace StatsMonitor.Helpers;

public enum SearchPage { Main, System, Settings, Appearance }

/// <summary>A searchable item: <paramref name="Match"/> is the text rendered on screen that gets highlighted.</summary>
public sealed record SearchResult(string Match, string Title, string Location, SearchPage Page);

public static class SearchIndex
{
    private static List<SearchResult>? _static;
    private static string? _staticLang;
    private static List<SearchResult> _system = [];

    public static async Task LoadSystemAsync()
    {
        var groups = await SystemInfoService.GetAsync();
        var list = new List<SearchResult>();
        foreach (var g in groups)
        {
            list.Add(new(g.Title, g.Title, Loc.T("Sistema"), SearchPage.System));
            foreach (var l in g.Lines)
            {
                list.Add(new(l.Label, l.Label, $"{Loc.T("Sistema")} › {g.Title}", SearchPage.System));
                if (l.Value != "N/D") list.Add(new(l.Value, $"{l.Label}: {l.Value}", $"{Loc.T("Sistema")} › {g.Title}", SearchPage.System));
            }
        }
        _system = list;
    }

    public static List<SearchResult> Search(string query)
    {
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return [];
        if (_staticLang != Loc.Lang) { _static = null; _staticLang = Loc.Lang; }
        _static ??= BuildStatic();
        return _static.Concat(_system)
            .Select(r => (r, key: Normalize(r.Title + " " + r.Location)))
            .Where(x => terms.All(x.key.Contains))
            .OrderBy(x => Normalize(x.r.Title).StartsWith(terms[0]) ? 0 : 1)
            .Select(x => x.r)
            .DistinctBy(r => (r.Match, r.Location))
            .Take(12)
            .ToList();
    }

    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static List<SearchResult> BuildStatic()
    {
        var list = new List<SearchResult>();
        void Add(SearchPage page, string location, params string[] texts)
        {
            location = string.Join(" › ", location.Split(" › ").Select(Loc.T));
            foreach (var t in texts) { var tt = Loc.T(t); list.Add(new(tt, tt, location, page)); }
        }

        Add(SearchPage.Settings, "Configurações › Geral",
            "Intervalo de atualização (s):", "Iniciar com o Windows", "Iniciar minimizado", "Exibir temperaturas");
        Add(SearchPage.Settings, "Configurações › Gadget",
            "Exibir gadget", "Manter gadget sempre visível", "Exibição do gadget ao minimizar",
            "Sobre a tela (flutuante, arrastável)", "Na barra de tarefas, canto direito (widget)",
            "Barra horizontal flutuante (arrastável)", "Faixa fixa no topo da tela", "Não fazer nada (sem gadget)",
            "Pré-visualização do gadget (valores de exemplo)");
        Add(SearchPage.Settings, "Configurações › Painéis exibidos",
            "Painéis exibidos", "Janela principal", "Gadget (minimizado)", "Memória RAM", "Discos", "Rede / Internet", "FPS (em jogos)");
        Add(SearchPage.Settings, "Configurações › Alertas de temperatura", "Alertas de temperatura (°C)", "Atenção", "Crítica");

        Add(SearchPage.Appearance, "Personalização › Presets", "Minhas personalizações", "Salvar como", "Carregar", "Excluir");
        Add(SearchPage.Appearance, "Personalização › Tema", "Tema base", "Escuro", "Claro", "Restaurar padrões");
        Add(SearchPage.Appearance, "Personalização › Cores",
            "Cores dos alertas de temperatura", "Janela principal (maximizada)", "Fundo da janela", "Fundo dos painéis",
            "Borda dos painéis", "Texto principal", "Texto secundário", "Destaque (CPU, GPU, rede)", "Destaque (memória)",
            "Trilho dos medidores", "Fundo do gadget", "Texto do gadget");

        Add(SearchPage.Main, "Principal › Processador", "Velocidade base", "Sockets", "Núcleos", "Processadores lógicos",
            "Virtualização", "Cache", "Processos", "Threads", "Identificadores");
        Add(SearchPage.Main, "Principal › GPU", "Uso da VRAM", "Frequência da memória", "Consumo energético", "Ventoinhas");
        Add(SearchPage.Main, "Principal › Discos", "Leitura", "Escrita", "Saúde SMART", "Vida útil", "Tempo de atividade");
        Add(SearchPage.Main, "Principal › Rede", "Download", "Upload", "Latência", "Conexão");
        Add(SearchPage.Main, "Principal", "Tempo ligado", "Última atualização");
        return list;
    }
}
