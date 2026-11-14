using System.Globalization;
using System.Text;
using StatsMonitor.Services;

namespace StatsMonitor.Helpers;

public enum SearchPage { Main, System, Settings, Appearance, Peripherals, Component, Help }

/// <summary>A searchable item: <paramref name="Match"/> is the text rendered on screen that gets highlighted.</summary>
public sealed record SearchResult(string Match, string Title, string Location, SearchPage Page, ComponentKind? Kind = null);

public static class SearchIndex
{
    private static List<SearchResult>? _static;
    private static string? _staticLang;
    private static List<SearchResult> _system = [];

    public static async Task LoadSystemAsync()
    {
        var list = new List<SearchResult>();
        void AddGroups(IEnumerable<InfoGroup> groups, string page, SearchPage target)
        {
            foreach (var g in groups)
            {
                list.Add(new(g.Title, g.Title, Loc.T(page), target));
                foreach (var l in g.Lines)
                {
                    list.Add(new(l.Label, l.Label, $"{Loc.T(page)} › {g.Title}", target));
                    if (!string.IsNullOrEmpty(l.Value) && l.Value != "N/D" && l.Value != "-")
                        list.Add(new(l.Value, $"{l.Label}: {l.Value}", $"{Loc.T(page)} › {g.Title}", target));
                }
            }
        }
        void AddComponent(ComponentKind kind, IEnumerable<InfoGroup> groups, params string[] extra)
        {
            var page = Loc.T(ComponentDetailService.Title(kind));
            var loc = $"{Loc.T("Principal")} › {page}";
            list.Add(new(page, page, Loc.T("Principal"), SearchPage.Component, kind));
            foreach (var x in extra) list.Add(new(x, x, loc, SearchPage.Component, kind));
            foreach (var g in groups)
            {
                list.Add(new(g.Title, g.Title, loc, SearchPage.Component, kind));
                foreach (var l in g.Lines)
                {
                    list.Add(new(l.Label, l.Label, $"{loc} › {g.Title}", SearchPage.Component, kind));
                    if (!string.IsNullOrEmpty(l.Value) && l.Value != "N/D" && l.Value != "-")
                        list.Add(new(l.Value, $"{l.Label}: {l.Value}", $"{loc} › {g.Title}", SearchPage.Component, kind));
                }
            }
        }
        AddGroups(await SystemInfoService.GetAsync(), "Sistema", SearchPage.System);
        foreach (var kind in Enum.GetValues<ComponentKind>())
        {
            try { AddComponent(kind, await ComponentDetailService.GetAsync(kind, null, null), Loc.T("Exportar gráficos")); }
            catch { /* a failing component must not break the whole index */ }
        }
        AddGroups(await PeripheralInfoService.GetAsync(), "Periféricos", SearchPage.Peripherals);
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
            .Take(40)
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
            "Intervalo de atualização (s):", "Intervalo por componente", "CPU (s):", "GPU e VRAM (s):", "Memória RAM (s):", "Discos e sensores (s):", "Iniciar com o Windows", "Iniciar minimizado", "Exibir temperaturas",
            "Idioma", "Unidade de temperatura", "Visualização simplificada", "Perfis", "Seguir o tema do Windows", "Atalho global",
            "Exportar configurações", "Importar configurações", "Restaurar padrões", "Notificações de alerta", "Tocar som de alerta", "Testar alerta",
            "Sensores (ventiladores, energia, tensões, bateria)", "Pausar a leitura quando oculto e sem gadget",
            "Ler sensores de disco e placa-mãe com menos frequência");
        Add(SearchPage.Settings, "Configurações › Gadget",
            "Exibir gadget", "Manter gadget sempre visível", "Exibição do gadget ao minimizar",
            "Sobre a tela (flutuante, arrastável)", "Na barra de tarefas, canto direito (widget)",
            "Barra horizontal flutuante (arrastável)", "Faixa fixa no topo da tela", "Não fazer nada (sem gadget)",
            "Pré-visualização do gadget (valores de exemplo)");
        Add(SearchPage.Settings, "Configurações › Painéis exibidos",
            "Painéis exibidos", "Janela principal", "Gadget (minimizado)", "CPU", "GPU", "Memória RAM", "Discos", "Rede / Internet", "FPS (em jogos)");
        Add(SearchPage.Settings, "Configurações › Alertas de temperatura", "Alertas de temperatura (°C)", "Atenção", "Crítica");
        Add(SearchPage.Settings, "Configurações › Alertas", "Notificar ao passar do limite crítico", "Uso crítico (%):",
            "Tempo acima do limite (s):", "Intervalo mínimo entre avisos (min):", "Histórico de alertas", "Limpar", "Nenhum alerta registrado");
        Add(SearchPage.Settings, "Configurações › Registro de dados", "Registro de dados", "Gravar leituras em arquivo CSV", "Intervalo entre registros (s):", "Abrir pasta de registros");
        Add(SearchPage.Settings, "Configurações › Tamanho da fonte", "Tamanho da fonte", "Tamanho dos textos", "Zoom da interface");
        Add(SearchPage.Settings, "Configurações › Atalho global", "Atalho global", "Ativar atalho para mostrar/ocultar o gadget", "Atalho:");

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
        Add(SearchPage.Main, "Principal", "Tempo ligado", "Última atualização", "Monitor", "Visualização simplificada");
        Add(SearchPage.Main, "Principal › Memória", "Memória RAM", "Em uso", "Disponível");
        Add(SearchPage.System, "Sistema", "Sistema");
        Add(SearchPage.Help, "Ajuda", ["Ajuda e tutorial", .. StatsMonitor.Views.HelpView.SectionTitles]);
        Add(SearchPage.Peripherals, "Periféricos", "Periféricos", "Monitores", "Teclados", "Mouses e apontadores", "Áudio", "Câmeras", "Impressoras", "Bluetooth", "Armazenamento externo (USB)", "Outros dispositivos USB");
        Add(SearchPage.Settings, "Configurações", "Configurações");
        Add(SearchPage.Appearance, "Personalização", "Personalização");
        return list;
    }
}
