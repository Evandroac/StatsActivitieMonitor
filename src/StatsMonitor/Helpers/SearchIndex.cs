using System.Globalization;
using System.Text;
using StatsMonitor.Services;

namespace StatsMonitor.Helpers;

public enum SearchPage { Main, System, Settings, Appearance, Peripherals, Component, Help, Processes, NetTools, Diagnostics }

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
            if (kind == ComponentKind.Battery && !BatteryService.HasBattery) continue;
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
        if (_staticLang != Loc.Code) { _static = null; _staticLang = Loc.Code; }
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
            "Mostrar painel de Pets no menu", "Animações suaves ao abrir e trocar de tela", "Sensores (ventiladores, energia, tensões, bateria)", "Pausar a leitura quando oculto e sem gadget",
            "Ler sensores de disco e placa-mãe com menos frequência", "Modo econômico ao minimizar",
            "Geral", "Gadget", "Alertas", "Registro de dados", "Personalização", "Perfis", "Seções", "Ações", "Restaurar cores do tema");
        Add(SearchPage.Settings, "Configurações › Gadget",
            "Exibir gadget", "Manter gadget sempre visível", "Exibição do gadget ao minimizar", "Visão do gadget", "Simplificada", "Detalhada (mais informações e mini gráficos)", "Posição do gadget",
            "Sobre a tela (flutuante, arrastável)", "Canto superior esquerdo", "Canto superior direito", "Canto inferior esquerdo", "Canto inferior direito", "Faixa fixa na parte inferior da tela",
            "Barra horizontal flutuante (arrastável)", "Faixa fixa no topo da tela", "Não fazer nada (sem gadget)",
            "Pré-visualização do gadget (valores de exemplo)");
        Add(SearchPage.Settings, "Configurações › Painéis exibidos",
            "Painéis exibidos", "Janela principal", "Gadget (minimizado)", "CPU", "GPU", "Memória RAM", "Discos", "Rede / Internet", "Bateria (notebooks)", "FPS (em jogos)");
        Add(SearchPage.Settings, "Configurações › Alertas de temperatura", "Alertas de temperatura (°C)", "Atenção", "Crítica");
        Add(SearchPage.Settings, "Configurações › Alertas", "Notificar ao passar do limite crítico", "Uso crítico (%):",
            "Tempo acima do limite (s):", "Intervalo mínimo entre avisos (min):", "Histórico de alertas", "Limpar", "Nenhum alerta registrado",
            "Níveis de alerta:", "Atenção e críticos", "Somente atenção", "Somente críticos", "Saudações (bom dia, boa tarde, boa noite)",
            "Alertas inteligentes de rede (sem conexão, sem internet, latência alta)", "Bateria fraca - atenção (%):", "Bateria crítica (%):");
        Add(SearchPage.Settings, "Configurações › Perfis", "Aplicar", "Excluir", "Salvar atual no perfil", "Nome do perfil", "Perfis de configuração");
        Add(SearchPage.Settings, "Configurações › Registro de dados", "Registro de dados", "Gravar leituras em arquivo CSV", "Intervalo entre registros (s):", "Abrir pasta de registros");
        Add(SearchPage.Settings, "Configurações › Tamanho da fonte", "Tamanho da fonte", "Tamanho dos textos", "Zoom da interface");
        Add(SearchPage.Settings, "Configurações › Atalho global", "Atalho global", "Ativar atalho para mostrar/ocultar o gadget", "Atalho:");

        Add(SearchPage.Appearance, "Personalização › Presets", "Minhas personalizações", "Salvar como", "Carregar", "Excluir");
        Add(SearchPage.Appearance, "Personalização › Tema", "Tema base", "Escuro", "Claro", "Restaurar padrões", "Temas visuais", "Tema", "Estilo", "Fonte", "Cor de fundo do menu de cores", "Windows XP (Oliva)", "Cyberpunk Neon", "Futurista (Gelo)", "Holograma", "Synthwave anos 80", "Vintage sépia", "Anos 90", "Vaporwave pastel", "Terminal verde", "Terminal âmbar", "Moderno claro", "Grafite", "Nórdico", "Branco puro", "Preto puro", "Sakura kawaii", "Neo Tóquio", "Matcha zen", "Pôr do sol", "Oceano profundo", "Art déco", "🎨  Temas visuais", "Escolha um estilo completo: cores, fonte e clima do aplicativo. Você ainda pode ajustar cada cor depois.", "Windows XP", "Windows 98", "Cyberpunk", "Futurismo", "Retrô", "Vintage", "Anos 80", "Anos 90", "Vaporwave", "Arcade", "8-bit", "CRT", "Terminal", "Minimalismo", "Anime", "Kawaii", "Japonês", "Game Boy", "Synthwave");
        Add(SearchPage.Appearance, "Personalização › Cores",
            "Cores dos alertas de temperatura", "Janela principal (maximizada)", "Fundo da janela", "Fundo dos painéis",
            "Borda dos painéis", "Texto principal", "Texto secundário", "Destaque (CPU, GPU, rede)", "Destaque (memória)",
            "Trilho dos medidores", "Fundo do gadget", "Texto do gadget");

        Add(SearchPage.Main, "Principal › Processador", "Velocidade base", "Sockets", "Núcleos", "Processadores lógicos",
            "Virtualização", "Cache", "Processos", "Threads", "Identificadores");
        Add(SearchPage.Main, "Principal › GPU", "Uso da VRAM", "Frequência da memória", "Consumo energético", "Ventoinhas");
        Add(SearchPage.Main, "Principal › Discos", "Leitura", "Escrita", "Saúde SMART", "Vida útil", "Tempo de atividade");
        Add(SearchPage.Main, "Principal › Rede", "Download", "Upload", "Latência", "Conexão");
        Add(SearchPage.Main, "Principal", "Copiar tudo", "Executar como administrador", "Menu de navegação", "Versão do sistema", "Pesquisa geral", "Tempo ligado", "Última atualização", "Monitor", "Visualização simplificada");
        if (BatteryService.HasBattery)
            Add(SearchPage.Main, "Principal › Bateria", "Bateria", "Nível de carga", "Status da bateria", "Tempo restante", "Tempo para carga completa",
                "Estado de conservação", "Fonte de alimentação", "Capacidade atual (carga máxima)", "Capacidade original (projeto)", "Ciclos de carga", "Economia de bateria");
        Add(SearchPage.Main, "Principal › Memória", "Memória RAM", "Em uso", "Disponível", "Uso da memória", "Memória confirmada (commit)", "Tipo e velocidade", "Módulos instalados");
        Add(SearchPage.System, "Sistema", "Sistema", "Sistema e periféricos");
        Add(SearchPage.Processes, "Processos", [.. StatsMonitor.Views.ProcessesView.Keywords]);
        Add(SearchPage.Diagnostics, "Diagnóstico do computador", [.. StatsMonitor.Views.DiagnosticsView.Keywords]);
        Add(SearchPage.NetTools, "Testes de conexão", [.. StatsMonitor.Views.NetworkToolsView.Keywords]);
        Add(SearchPage.Help, "Ajuda", ["Ajuda e tutorial", .. StatsMonitor.Views.HelpView.SectionTitles]);
        Add(SearchPage.Peripherals, "Periféricos", "Periféricos", "Sistema e periféricos", "Monitores", "Teclados", "Mouses e apontadores", "Áudio", "Câmeras", "Impressoras", "Bluetooth", "Armazenamento externo (USB)", "Outros dispositivos USB");
        Add(SearchPage.Settings, "Configurações › Idioma", Loc.Languages.Select(l => l.Name).ToArray());
        Add(SearchPage.Settings, "Configurações", "Configurações");
        Add(SearchPage.Appearance, "Personalização", "Personalização");
        return list;
    }
}
