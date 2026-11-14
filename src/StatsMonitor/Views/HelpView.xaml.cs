using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using StatsMonitor.Helpers;

namespace StatsMonitor.Views;

/// <summary>Built-in tutorial: what the app does, how to use it and its caveats. Rebuilt when the language changes.</summary>
public partial class HelpView : UserControl
{
    public static readonly string[] SectionTitles =
    [
        "O que é o Stats Monitor", "Como usar", "Gadget e bandeja do sistema", "Alertas", "Economia de recursos",
        "Ressalvas importantes", "Perguntas frequentes", "Tela de Processos", "Testes de conexão", "Bateria (notebooks)", "Diagnóstico do computador", "Pets", "Idioma",
    ];

    private int? _initialTopic;
    private readonly Dictionary<int, Action> _jumps = [];
    private readonly Dictionary<int, Border> _cards = [];

    // Pulsing accent border + glow around the card, then back to normal.
    private static void Flash(Border card)
    {
        var glow = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 8, Opacity = 0.9, Color = (Color)Application.Current.FindResource("AccentColor") };
        card.Effect = glow;
        card.SetResourceReference(Border.BorderBrushProperty, "Accent");
        card.BorderThickness = new Thickness(2);
        var pulse = new System.Windows.Media.Animation.DoubleAnimation(8, 28, TimeSpan.FromMilliseconds(450))
        {
            AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3),
        };
        pulse.Completed += (_, _) =>
        {
            card.Effect = null;
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            card.BorderThickness = new Thickness(1);
        };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, pulse);
    }

    public HelpView() : this(null) { }

    /// <summary>Opens the help scrolled to (and highlighting) the given topic index in <see cref="SectionTitles"/>.</summary>
    public HelpView(int? topic)
    {
        InitializeComponent();
        Scroll.ScrollChanged += OnScrollChanged;
        _initialTopic = topic;
        Loaded += (_, _) =>
        {
            if (_initialTopic is { } t && _jumps.TryGetValue(t, out var jump))
            {
                _initialTopic = null;
                UpdateLayout();
                jump();
                if (_cards.TryGetValue(t, out var card)) Flash(card);
            }
        };
        Build();
        Loc.LanguageChanged += Build;
        Unloaded += (_, _) => Loc.LanguageChanged -= Build;
    }

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;

    private void Build()
    {
        Host.Children.Clear();
        NavHost.Children.Clear();
        _navButtons.Clear();
        _navCards.Clear();
        _jumps.Clear();
        _cards.Clear();
        var navTitle = new TextBlock { Text = L("TÓPICOS", "TOPICS"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) };
        navTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        var navHint = new TextBlock { Text = L("Clique em um tópico para ir direto até ele.", "Click a topic to jump straight to it."), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(2, 0, 0, 12) };
        navHint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        NavHost.Children.Add(navTitle);
        NavHost.Children.Add(navHint);
        Host.Children.Add(new TextBlock { Text = "📖  " + Loc.T("Ajuda e tutorial"), FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        Host.Children.Add(Caption(L("Leia com calma: aqui está tudo o que o aplicativo faz, como usá-lo e o que esperar dele.",
                                    "Take your time: this explains what the app does, how to use it and what to expect from it.")));

        Card("👋", 0, [
            L("O Stats Monitor mostra, em tempo real, como o seu computador está: uso e temperatura da CPU e da GPU, memória RAM e VRAM, discos, rede, ventoinhas e outros sensores.",
              "Stats Monitor shows, in real time, how your computer is doing: CPU and GPU usage and temperature, RAM and VRAM, disks, network, fans and other sensors."),
            L("✔ Ele apenas LÊ os sensores. Não altera configurações, não controla ventoinhas, não faz overclock e não mexe nos seus arquivos.",
              "✔ It only READS sensors. It does not change settings, control fans, overclock or touch your files."),
            L("✔ Os dados ficam no seu computador. O app não envia leituras para a internet. A conexão em segundo plano é um ping simples ao DNS 1.1.1.1 para medir a latência e saber se há internet; a tela Testes de conexão só acessa a rede quando você manda executar.",
              "✔ Data stays on your computer. The app does not upload readings. In the background it only sends a simple ping to the 1.1.1.1 DNS to measure latency and detect internet access; the Connection tests page only uses the network when you run something."),
        ]);
        Card("🧭", 1, [
            L("☰ Menu lateral: clique no ☰ para mostrar ou esconder os nomes; com o menu aberto, arraste a borda direita para ajustar a largura (fica salvo). As páginas são: Principal, Sistema e periféricos (com filtro Tudo/Sistema/Periféricos), CPU, GPU, Memória, Discos, Rede, Sensores, Bateria (só em notebooks), Processos, Testes de conexão, Diagnóstico do computador e Configurações. A versão do app aparece abaixo do botão de Configurações.",
              "☰ Side menu: click ☰ to show or hide the names; with the menu open, drag its right edge to resize it (saved). Pages are: Main, System and peripherals (with an All/System/Peripherals filter), CPU, GPU, Memory, Disks, Network, Sensors, Battery (laptops only), Processes, Connection tests, Computer diagnostic and Settings. The app version is shown below the Settings button."),
            L("🔍 Pesquisa: digite um termo para ir direto à informação ou à opção desejada. O botão ? no canto superior direito de cada tela abre esta Ajuda já no tópico daquela tela.",
              "🔍 Search: type a term to jump straight to the information or option you want. The ? button at the top right of each screen opens this Help at the topic for that screen."),
            L("📋 Copiar: clique em qualquer valor ou rótulo para copiá-lo. Uma mensagem rápida confirma a cópia.",
              "📋 Copy: click any value or label to copy it. A short message confirms the copy."),
            L("📈 Páginas de componente: mostram gráficos, detalhes e sensores extras de cada peça.",
              "📈 Component pages: show charts, details and extra sensors for each part."),
            L("⚙️ Configurações (seções Geral, Gadget, Painéis exibidos, Alertas, Registro de dados, Personalização e Perfis): intervalo de leitura (geral e por componente), modo econômico, painéis visíveis, limites de temperatura e de bateria, alertas, registro CSV, aparência, tamanho da fonte, idioma, unidade °C/°F, atalho global, exportar/importar e perfis (Jogo, Trabalho, Silencioso). Tudo é salvo automaticamente.",
              "⚙️ Settings (sections General, Gadget, Displayed panels, Alerts, Data logging, Customization and Profiles): update interval (general and per component), eco mode, visible panels, temperature and battery limits, alerts, CSV logging, appearance, font size, language, °C/°F unit, global hotkey, export/import and profiles (Gaming, Work, Quiet). Everything is saved automatically."),
            L("🖌️ Personalização › Temas visuais: além dos temas Escuro e Claro, há quadros de seleção com estilos completos (cores, fonte e clima): Windows XP e clássicos, Cyberpunk e futurismo, Retrô anos 80/90, Vaporwave, Arcade 8-bit, CRT/Terminal, Moderno, Minimalista, Anime/kawaii e artísticos. Escolher um tema substitui as cores personalizadas; depois você pode ajustar cada cor. Os menus de seleção de cores têm fundo sólido.",
                "🖌️ Customization › Visual themes: besides Dark and Light, there are selection cards with complete styles (colors, font and mood): Windows XP and classics, Cyberpunk and futurism, 80s/90s retro, Vaporwave, 8-bit arcade, CRT/Terminal, Modern, Minimalist, Anime/kawaii and artistic. Picking a theme replaces custom colors; you can then tweak each color. Color pickers have a solid background."),
            L("🎨 Cores: verde, amarelo e vermelho indicam normal, atenção e crítico, de acordo com os limites que você definiu.",
              "🎨 Colors: green, yellow and red mean normal, attention and critical, based on the limits you set."),
        ]);
        Card("⚙️", 7, [
            L("Funciona como o Gerenciador de Tarefas: lista os processos com ícone, nome, status e uso de CPU, memória, disco e rede. Processos com subprocessos aparecem em árvore.",
              "Works like Task Manager: lists processes with icon, name, status and CPU, memory, disk and network usage. Processes with child processes appear as a tree."),
            L("Pesquisa e filtros: busque por nome, PID ou caminho e filtre por tipo e status. Ordene por qualquer coluna (clique no cabeçalho) ou pelo seletor, por exemplo do maior para o menor uso de CPU.",
              "Search and filters: search by name, PID or path and filter by type and status. Sort by any column (click the header) or with the selector, for example highest CPU first."),
            L("Cabeçalhos: mostram o total de % utilizado em cada coluna. Arraste as divisórias para ajustar a largura e arraste o título de uma coluna para outra posição para reordená-la. Use o botão Colunas (ou o botão direito no cabeçalho) para ocultar ou adicionar colunas como PID, Threads, Tipo, Executável, Iniciado em e Caminho; a escolha fica salva.",
              "Headers: show the total % used in each column. Drag the dividers to resize and drag a column title to another position to reorder it. Use the Columns button (or right-click the header) to hide or add columns such as PID, Threads, Type, Executable, Started and Path; your choice is saved."),
            L("Painel de ações (à esquerda): expandir/recolher tudo, finalizar tarefa, finalizar árvore, reiniciar, alterar prioridade, abrir o local do arquivo e copiar o caminho. O menu do botão direito também funciona. Clicar em área vazia remove a seleção.",
              "Actions panel (left): expand/collapse all, end task, end tree, restart, change priority, open file location and copy path. The right-click menu also works. Clicking an empty area clears the selection."),
            L("Gráfico de linha do tempo: abaixo da lista, mostra o histórico recente de cada processo, cada um com uma cor própria (a mesma barra colorida da linha). Escolha a métrica (CPU, memória, disco ou rede). Ao selecionar um processo, a linha dele é destacada.",
              "Timeline chart: below the list, shows the recent history of each process in its own color (the same colored bar as in its row). Pick the metric (CPU, memory, disk or network). Selecting a process highlights its line."),
            L("Ressalvas: processos críticos do Windows e o próprio app não podem ser finalizados. Disco e rede por processo são estimativas, e a rede exige administrador.",
              "Caveats: critical Windows processes and the app itself cannot be ended. Per-process disk and network are estimates, and network requires administrator."),
        ]);
        Card("🔋", 9, [
            L("Em notebooks, o app mostra um painel de bateria na tela Principal e uma página própria no menu (🔋). Em desktops, nada disso aparece.",
              "On laptops, the app shows a battery panel on the Main screen and its own page in the menu (🔋). Desktops show none of this."),
            L("Nível de carga e status: a porcentagem e se a bateria está carregando, totalmente carregada ou descarregando. Fonte de alimentação: indica se o notebook usa a bateria ou o carregador.",
              "Charge level and status: the percentage and whether the battery is charging, fully charged or discharging. Power source: whether the laptop runs on battery or on the charger."),
            L("Tempo restante e tempo para carga completa: estimativas calculadas pela carga atual e pelo consumo (ou velocidade de carga) do momento. Elas variam conforme o uso e aparecem como Calculando… até haver dados.",
              "Time remaining and time to full charge: estimates based on the current charge and the present draw (or charging speed). They vary with usage and show Calculating… until data is available."),
            L("Estado de conservação: compara a capacidade atual (carga máxima) com a capacidade original de fábrica. 80% ou mais é Boa, de 60% a 80% é Desgastada e abaixo de 60% é Ruim. A página também mostra ciclos de carga, fabricante e tecnologia, quando o computador informa.",
              "Battery health: compares the current capacity (full charge) with the original factory capacity. 80% or more is Good, 60% to 80% is Worn and below 60% is Poor. The page also shows charge cycles, manufacturer and chemistry when the computer reports them."),
            L("Alertas: aviso de bateria fraca (20% por padrão, atenção) e crítica (10%), ajustáveis em Configurações › Alertas, enquanto descarrega, respeitando a sua escolha de alertas. Em modo econômico a bateria é lida a cada 30 s. Nem todo notebook informa capacidade, ciclos ou ritmo de carga; o que faltar aparece como N/D.",
              "Alerts: low battery (20% by default, attention) and critical battery (10%), adjustable in Settings › Alerts, while discharging, following your alert choice. In eco mode the battery is read every 30 s. Not every laptop reports capacity, cycles or charge rate; whatever is missing shows as N/A."),
        ]);
        Card("🩺", 10, [
            L("Menu › Diagnóstico do computador reúne inventário, testes e análise do hardware e do Windows em 16 seções: Visão geral, Hardware, Processador, Memória RAM, Placa de vídeo, Armazenamento, Bateria e energia, Sistema operacional, Drivers e dispositivos, Rede, Segurança, Monitoramento em tempo real, Testes de desempenho, Diagnóstico inteligente, Histórico e Relatórios.",
              "Menu › Computer diagnostic gathers inventory, tests and analysis of the hardware and Windows in 16 sections: Overview, Hardware, Processor, RAM, Graphics card, Storage, Battery and power, Operating system, Drivers and devices, Network, Security, Real-time monitoring, Performance tests, Smart diagnosis, History and Reports."),
            L("Somente leitura: nada é corrigido automaticamente. INICIAR DIAGNÓSTICO COMPLETO coleta o inventário e roda testes de CPU, RAM (256 MB), disco (arquivo temporário de 128 MB, apagado ao final), rede e GPU, com progresso, log e cancelamento. Cada componente também tem o botão Diagnosticar este componente.",
              "Read-only: nothing is fixed automatically. START FULL DIAGNOSTIC collects the inventory and runs CPU, RAM (256 MB), disk (128 MB temp file, deleted afterwards), network and GPU tests with progress, log and cancellation. Each component also has a Diagnose this component button."),
            L("Gravidades: NORMAL, INFORMATIVO, ATENÇÃO, CRÍTICO e INDETERMINADO. Dado que não pôde ser lido aparece como Indisponível e nunca conta como saudável. Cada achado traz evidência, causa provável e recomendação; são probabilidades, não prova de defeito físico.",
              "Severities: NORMAL, INFORMATIONAL, WARNING, CRITICAL and UNDETERMINED. Data that could not be read shows as Unavailable and never counts as healthy. Each finding has evidence, probable cause and recommendation; they are likelihoods, not proof of a physical defect."),
            L("Histórico: cada diagnóstico completo é salvo (até 100) e pode ser aberto, excluído e comparado. Relatórios: exporte em PDF, HTML, CSV ou JSON. Monitoramento: gráficos de 1, 5, 15 e 60 min e alertas configuráveis enquanto a tela está aberta.",
              "History: every full diagnostic is saved (up to 100) and can be opened, deleted and compared. Reports: export to PDF, HTML, CSV or JSON. Monitoring: 1, 5, 15 and 60 min charts and configurable alerts while the page is open."),
            L("Limitações: SMART, TPM e BitLocker normalmente exigem administrador; driver antigo é só uma heurística por data; o teste de RAM não substitui o MemTest86; não há teste de estresse 3D da GPU; o SFC é opcional e exige administrador; o PDF é apenas texto.",
              "Limitations: SMART, TPM and BitLocker usually require administrator; old driver is only a date-based heuristic; the RAM test does not replace MemTest86; there is no 3D GPU stress test; SFC is optional and needs administrator; the PDF is text only."),
        ]);
        Card("🌐", 8, [
            L("Ferramentas de rede para diagnosticar problemas de conexão. Nada é executado até você clicar em Executar/Iniciar, e cada teste pode ser interrompido com Parar.",
              "Network tools to troubleshoot connection problems. Nothing runs until you click Run/Start, and every test can be stopped with Stop."),
            L("🩺 Diagnóstico: verifica adaptador, gateway, internet por IP, DNS, portal cativo e IP público, e indica onde está o problema.",
              "🩺 Diagnostics: checks adapter, gateway, internet by IP, DNS, captive portal and public IP, and points to where the problem is."),
            L("📡 Ping: latência, perda, jitter e desvio padrão em tempo real, com gráfico. 🧭 Traceroute: cada salto até o destino.",
              "📡 Ping: latency, loss, jitter and standard deviation in real time, with a chart. 🧭 Traceroute: every hop to the target."),
            L("🔎 DNS: consulta registros (A, AAAA, MX, TXT...) direto em vários servidores e compara tempo e respostas. 🔌 Portas: testa portas TCP abertas, fechadas ou filtradas.",
              "🔎 DNS: queries records (A, AAAA, MX, TXT...) directly on several servers and compares time and answers. 🔌 Ports: tests for open, closed or filtered TCP ports."),
            L("🔗 HTTP/TLS: tempo de DNS, TCP e resposta, código HTTP e validade do certificado. 🚀 Velocidade: download, upload, latência e jitter pela Cloudflare. 🖧 Adaptadores: IP, gateway, DNS, MAC, link, MTU e IP público.",
              "🔗 HTTP/TLS: DNS, TCP and response time, HTTP status and certificate validity. 🚀 Speed: download, upload, latency and jitter via Cloudflare. 🖧 Adapters: IP, gateway, DNS, MAC, link, MTU and public IP."),
            L("Ressalvas: o teste de velocidade consome dados e depende dos servidores da Cloudflare. Diagnóstico, velocidade e IP público contatam serviços externos (Cloudflare e Microsoft); as demais ferramentas falam apenas com o destino que você informar. Firewalls podem bloquear ICMP (ping/traceroute).",
              "Caveats: the speed test consumes data and depends on Cloudflare servers. Diagnostics, speed and public IP contact external services (Cloudflare and Microsoft); the other tools talk only to the target you enter. Firewalls may block ICMP (ping/traceroute)."),
        ]);
        Card("🪟", 2, [
            L("Ao minimizar, o app mostra um gadget compacto com os valores escolhidos. Há vários formatos: flutuante e barra flutuante (arrastáveis) ou fixos nos quatro cantos da tela, em uma faixa no topo ou em uma faixa na parte inferior. Em Configurações › Gadget, a visão pode ser simplificada (somente texto) detalhada (mais informações e um mini gráfico de uso de CPU, RAM, GPU, disco e rede) ou detalhada sem gráficos (mesmas informações, sem os mini gráficos).",
              "When minimized, the app shows a compact gadget with the values you chose. Several layouts exist: floating and floating bar (draggable), or pinned to any of the four screen corners, a strip at the top or a strip at the bottom. In Settings › Gadget the view can be simplified (text only), detailed (more information and a mini usage chart for CPU, RAM, GPU, disk and network) or detailed without charts (same information, no mini charts)."),
            L("Controles do gadget: passe o mouse sobre ele para ver, no canto superior direito, o botão 🔓/🔒 (fixa o gadget e bloqueia posição e tamanho) e, abaixo dele, o botão 👁 (deixa o gadget com a opacidade mínima; clique de novo para voltar à opacidade configurada). Os botões ficam juntos no topo, centralizados (o cadeado fica separado, no canto superior direito), e aparecem ao passar o mouse sem cobrir as informações; ▤/▣ alterna entre visão simplificada e detalhada. Os botões de ação mantêm a opacidade total mesmo com o gadget esmaecido (👁). Segure o botão ↔ ou ↕ (também movem com o gadget fixo) para pausar as leituras enquanto estiver pressionado, e arraste para mover o gadget no eixo horizontal ou vertical (duplo clique volta à posição original nos modos fixos). Arraste o canto inferior direito (◢) para redimensionar: todo o conteúdo escala junto; duplo clique volta ao tamanho padrão.",
              "Gadget controls: hover over it to see, at the top right, the 🔓/🔒 button (locks the gadget, blocking position and size) and, below it, the 👁 button (sets minimum opacity; click again to return to the configured opacity). The buttons sit together at the top centre (the lock is separate, at the top-right corner) and appear on hover without covering the information; ▤/▣ switches between simplified and detailed view. The action buttons keep full opacity even when the gadget is faded (👁). Hold the ↔ or ↕ button (also move while pinned) to pause readings while pressed, and drag to move the gadget along the horizontal or vertical axis (double-click restores the original position in pinned modes). Drag the bottom-right corner (◢) to resize: all content scales together; double-click resets the default size."),
            L("Fechar a janela (X) NÃO encerra o app: ele continua na bandeja do sistema, perto do relógio. Para sair de verdade, clique com o botão direito no ícone da bandeja e escolha Sair.",
              "Closing the window (X) does NOT quit the app: it keeps running in the system tray near the clock. To really exit, right-click the tray icon and choose Exit."),
            L("O ícone da bandeja muda de cor conforme o estado do computador. Clique duas vezes nele para abrir o app.",
              "The tray icon changes color with your computer's state. Double-click it to open the app."),
            L("Atalho Ctrl+Alt+G (configurável) mostra ou esconde o gadget.",
              "Ctrl+Alt+G (configurable) shows or hides the gadget."),
            L("Com a opção de FPS ligada, o gadget mostra os quadros por segundo do jogo em primeiro plano: vermelho abaixo de 30, verde a partir de 30 e azul a partir de 60. Ao lado aparece a média (Média/méd) do jogo atual, calculada desde que ele entrou em primeiro plano, para comparar com o valor instantâneo. O bloco de FPS só aparece enquanto um jogo é detectado em primeiro plano.",
              "With FPS enabled, the gadget shows the frames per second of the foreground game: red below 30, green from 30 and cyan from 60. Next to it, the average (Average/avg) of the current game is shown, computed since it came to the foreground, to compare with the instant value. The FPS block only appears while a game is detected in the foreground."),
        ]);
        Card("🔔", 3, [
            L("Atenção: avisa antes do problema, quando a temperatura passa do limite de atenção ou o uso fica 10 pontos abaixo do limite crítico.",
              "Attention: warns before a problem, when temperature passes the attention limit or usage gets within 10 points of the critical limit."),
            L("Crítico: avisa quando a temperatura ou o uso (CPU, GPU, RAM) passa do limite crítico. Você escolhe receber atenção, críticos ou ambos.",
              "Critical: warns when temperature or usage (CPU, GPU, RAM) passes the critical limit. You choose attention, critical or both."),
            L("Rede: avisa quando não há rede, quando há rede mas sem internet, quando a latência está alta e quando a conexão volta.",
              "Network: warns when there is no network, when there is a network but no internet, when latency is high and when the connection is back."),
            L("Para evitar excesso de avisos, o valor precisa ficar acima do limite por alguns segundos e cada aviso respeita um intervalo mínimo. O histórico fica em Configurações.",
              "To avoid noise, a value must stay above the limit for a few seconds and each alert respects a minimum interval. The history is in Settings."),
            L("Também há saudações de bom dia, boa tarde e boa noite, que podem ser desligadas.",
              "There are also good morning, afternoon and evening greetings, which can be turned off."),
        ]);
        Card("🔋", 4, [
            L("Modo econômico: com a janela minimizada e sem gadget, o app lê os sensores só a cada 10 s, reduz a prioridade e libera memória. Os alertas continuam funcionando.",
              "Eco mode: with the window minimized and no gadget, the app reads sensors only every 10 s, lowers its priority and frees memory. Alerts keep working."),
            L("Modo gadget: com o gadget visível e a janela fechada, só são lidos os componentes que o gadget exibe. Os outros ficam sem leitura e, por isso, sem alertas.",
              "Gadget mode: with the gadget visible and the window closed, only the components the gadget shows are read. The others are not read and so produce no alerts."),
            L("Intervalo por componente: cada peça pode ter o seu próprio tempo de atualização (0 usa o intervalo geral). Intervalos menores deixam o app mais preciso, porém mais pesado.",
              "Per-component interval: each part can have its own refresh time (0 uses the general interval). Shorter intervals are more precise but heavier."),
        ]);
        Card("⚠️", 5, [
            L("Administrador: o app precisa ser executado como administrador para ler temperaturas, voltagens e ventoinhas. Sem isso, muitos valores aparecem vazios.",
              "Administrator: the app must run as administrator to read temperatures, voltages and fans. Without it, many values appear empty."),
            L("Intel/AMD: a temperatura da CPU exige o driver PawnIO (winget install namazso.PawnIO); sem ele o app tenta as zonas térmicas ACPI do Windows. Driver: a leitura usa um driver do LibreHardwareMonitor. Antivírus ou o recurso Integridade de Memória do Windows podem bloqueá-lo.",
              "Intel/AMD: CPU temperature needs the PawnIO driver (winget install namazso.PawnIO); without it the app tries Windows ACPI thermal zones. Driver: reading uses a LibreHardwareMonitor driver. Antivirus or Windows Memory Integrity may block it."),
            L("Nem todo PC mostra tudo: placas-mãe, notebooks, GPUs integradas e peças muito novas podem não expor alguns sensores. Valor vazio significa que o sensor não está disponível, não que o valor é zero.",
              "Not every PC shows everything: motherboards, laptops, integrated GPUs and very new parts may not expose some sensors. An empty value means the sensor is unavailable, not that the value is zero."),
            L("Máquinas virtuais quase não expõem sensores. O app funciona apenas em Windows 10/11 de 64 bits.",
              "Virtual machines expose almost no sensors. The app only works on 64-bit Windows 10/11."),
            L("FPS: só detecta jogos DirectX 9 e DXGI (DirectX 10, 11 e 12) e também exige administrador.",
              "FPS: only detects DirectX 9 and DXGI (DirectX 10, 11 and 12) games and also requires administrator."),
            L("As leituras são informativas e não substituem um diagnóstico profissional. Evite alterar limites e intervalos sem necessidade: valores extremos podem esconder problemas ou deixar o app mais pesado.",
              "Readings are informational and do not replace a professional diagnosis. Avoid changing limits and intervals needlessly: extreme values can hide problems or make the app heavier."),
            L("Gravar leituras em CSV (opcional) guarda os dados em %AppData%\\StatsMonitor\\logs por 14 dias.",
              "Logging readings to CSV (optional) stores data in %AppData%\\StatsMonitor\\logs for 14 days."),
        ]);
        Card("❓", 6, [
            L("Por que a temperatura aparece vazia? Abra o app como administrador e verifique se o antivírus não bloqueou o driver. Se mesmo assim faltar, o seu hardware pode não expor esse sensor.",
              "Why is the temperature empty? Run the app as administrator and check that antivirus did not block the driver. If it is still missing, your hardware may not expose that sensor."),
            L("Por que o app pede permissão de administrador? Porque a leitura de sensores exige acesso ao hardware.",
              "Why does the app ask for administrator permission? Because reading sensors requires hardware access."),
            L("Como deixar o app mais leve? Aumente o intervalo, use o modo econômico, a visão simplificada ou o perfil Silencioso.",
              "How do I make the app lighter? Increase the interval, use eco mode, the simplified view or the Quiet profile."),
            L("Ao abrir, uma tela de abertura mostra o progresso e os logs do carregamento; a janela principal só aparece quando CPU, GPU, memória, discos e rede estão carregados.",
              "On launch, a splash screen shows loading progress and logs; the main window only appears once CPU, GPU, memory, disks and network are loaded."),
            L("O app inicia com o Windows? Só se você ativar a opção em Configurações. Ele também pode iniciar minimizado.",
              "Does the app start with Windows? Only if you enable it in Settings. It can also start minimized."),
            L("Como desligar as notificações? Em Configurações, na seção de alertas.",
              "How do I turn notifications off? In Settings, in the alerts section."),
            L("O app não abre ou não aparece? Ele só permite uma instância: verifique o ícone na bandeja do sistema (perto do relógio) e abra por lá. Se ele fechar com erro, os detalhes ficam em %AppData%\\StatsMonitor\\crash.log.",
              "The app does not open or show up? Only one instance can run: check the tray icon (near the clock) and open it from there. If it crashes, details are saved in %AppData%\\StatsMonitor\\crash.log."),
            L("O que mostra o painel de Memória RAM? Uso (usado/total), memória confirmada (commit), tipo e velocidade (ex.: DDR4 3200 MHz) e os módulos instalados, além do gráfico de uso.",
              "What does the RAM panel show? Usage (used/total), committed memory, type and speed (e.g. DDR4 3200 MHz) and installed modules, plus the usage chart."),
        ]);
        Card("🐾", 11, [
            L("Os Pets são opcionais: ative em Configurações › Geral › Mostrar painel de Pets no menu. O painel aparece no menu lateral (com o menu expandido), acima de Ajuda, e é pensado para distrair enquanto um diagnóstico demora.",
              "Pets are optional: enable them in Settings › General › Show Pets panel in the menu. The panel appears in the side menu (when expanded), above Help, and is meant to keep you entertained while a diagnostic runs."),
            L("Adicionar: digite um nome, escolha o tipo e clique em ➕ (até 12 pets). Clique em um pet para fazer carinho, clique com o botão direito para remover e use 🍖 para alimentar todos. Eles são salvos entre sessões.",
              "Add: type a name, choose the type and click ➕ (up to 12 pets). Click a pet to cuddle it, right-click to remove it and use 🍖 to feed them all. They are saved between sessions."),
            L("Fundo: escolha o cenário na lista (gramado, noite estrelada, praia, neve, pôr do sol). Use ▾/▸ no cabeçalho para recolher ou expandir o painel; as duas escolhas ficam salvas.",
              "Background: pick the scenery from the list (grass, starry night, beach, snow, sunset). Use ▾/▸ in the header to collapse or expand the panel; both choices are saved."),
        ]);
        Card("🌐", 12, [
            L("Em Configurações › Geral › Idioma há 16 opções: Português (Brasil e Portugal), English (US e UK), Español (e México), Français, Deutsch, Italiano, 简体中文, 繁體中文, 日本語, 한국어, العربية, हिन्दी e Русский. A troca é imediata e fica salva.",
              "Settings › General › Language offers 16 options: Portuguese (Brazil and Portugal), English (US and UK), Spanish (and Mexico), French, German, Italian, Simplified and Traditional Chinese, Japanese, Korean, Arabic, Hindi and Russian. Switching is instant and saved."),
            L("Na primeira execução o idioma é escolhido pelo idioma do Windows; se não houver tradução, usa inglês.",
              "On first launch the language follows Windows; if there is no translation, English is used."),
            L("Ressalva: algumas telas avançadas (Diagnóstico, Processos, Testes de conexão e esta Ajuda) estão disponíveis apenas em português e inglês; nos demais idiomas elas aparecem em inglês.",
              "Caveat: some advanced screens (Diagnostics, Processes, Connection tests and this Help) are only available in Portuguese and English; other languages show them in English."),
        ]);
        if (_navButtons.Count > 0) _navButtons[0].Tag = "active";
    }

    private readonly List<Button> _navButtons = [];

    private static TextBlock Caption(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.8,
    };

    private void Card(string icon, int titleIndex, string[] paragraphs)
    {
        string title = Loc.T(SectionTitles[titleIndex]);
        var stack = new StackPanel();
        var head = new TextBlock { Text = icon + "  " + title, FontSize = 17, FontWeight = FontWeights.SemiBold };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        stack.Children.Add(head);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 8), Opacity = 0.7 };
        rule.SetResourceReference(Border.BackgroundProperty, "CardBorder");
        stack.Children.Add(rule);
        foreach (var p in paragraphs) stack.Children.Add(Paragraph(p));
        var border = new Border { Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "CardBg");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        Host.Children.Add(border);

        var btn = new Button { Content = icon + "  " + title, Style = (Style)Application.Current.FindResource("TabButton"), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 6, 10, 6) };
        void Jump()
        {
            foreach (var b in _navButtons) b.Tag = b == btn ? "active" : null;
            Scroll.ScrollToVerticalOffset(Scroll.VerticalOffset + border.TranslatePoint(new Point(0, 0), Scroll).Y - 8);
        }
        btn.Click += (_, _) => Jump();
        _jumps[titleIndex] = Jump;
        _cards[titleIndex] = border;
        _navButtons.Add(btn);
        _navCards.Add(border);
        NavHost.Children.Add(btn);
    }

    private readonly List<Border> _navCards = [];

    // Highlights the topic whose card is currently at the top of the page.
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_navCards.Count == 0 || (e.VerticalChange == 0 && e.ExtentHeightChange == 0)) return;
        int current = 0;
        for (int i = 0; i < _navCards.Count; i++)
        {
            if (!_navCards[i].IsDescendantOf(Scroll)) continue;
            if (_navCards[i].TranslatePoint(new Point(0, 0), Scroll).Y <= 40) current = i;
        }
        if (Scroll.ScrollableHeight > 0 && Scroll.VerticalOffset >= Scroll.ScrollableHeight - 1) current = _navCards.Count - 1;
        for (int i = 0; i < _navButtons.Count; i++)
        {
            object? want = i == current ? "active" : null;
            if (!Equals(_navButtons[i].Tag, want)) _navButtons[i].Tag = want;
        }
    }

    /// <summary>A bullet row; a short lead-in ("Topic:" or a question) is highlighted so the text can be scanned quickly.</summary>
    private static UIElement Paragraph(string text)
    {
        int lead = -1;
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        int question = text.IndexOf("? ", StringComparison.Ordinal);
        if (colon is > 0 and <= 60 && !text[..colon].Contains('.')) lead = colon + 1;
        else if (question is > 0 and <= 140) lead = question + 1;

        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        if (lead > 0)
        {
            var bold = new Run(text[..lead]) { FontWeight = FontWeights.SemiBold };
            bold.SetResourceReference(TextElement.ForegroundProperty, "Accent2");
            tb.Inlines.Add(bold);
            tb.Inlines.Add(new Run(text[lead..]));
        }
        else tb.Inlines.Add(new Run(text));

        var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 7, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        dot.SetResourceReference(Shape.FillProperty, "Accent");
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(tb, 1);
        row.Children.Add(dot);
        row.Children.Add(tb);
        return row;
    }
}