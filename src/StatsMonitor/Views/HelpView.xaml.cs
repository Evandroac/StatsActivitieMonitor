using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StatsMonitor.Helpers;

namespace StatsMonitor.Views;

/// <summary>Built-in tutorial: what the app does, how to use it and its caveats. Rebuilt when the language changes.</summary>
public partial class HelpView : UserControl
{
    public static readonly string[] SectionTitles =
    [
        "O que é o Stats Monitor", "Como usar", "Gadget e bandeja do sistema", "Alertas", "Economia de recursos",
        "Ressalvas importantes", "Perguntas frequentes", "Tela de Processos", "Testes de conexão", "Bateria (notebooks)", "Diagnóstico do computador",
    ];

    public HelpView()
    {
        InitializeComponent();
        Build();
        Loc.LanguageChanged += Build;
        Unloaded += (_, _) => Loc.LanguageChanged -= Build;
    }

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;

    private void Build()
    {
        Host.Children.Clear();
        Host.Children.Add(new TextBlock { Text = "📖  " + Loc.T("Ajuda e tutorial"), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
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
            L("☰ Menu lateral: clique no ☰ para mostrar ou esconder os nomes. As páginas são: Principal, Sistema, CPU, GPU, Memória, Discos, Rede, Sensores, Bateria (só em notebooks), Periféricos, Processos, Testes de conexão, Diagnóstico do computador e Configurações. A versão do app aparece abaixo do botão de Configurações.",
              "☰ Side menu: click ☰ to show or hide the names. Pages are: Main, System, CPU, GPU, Memory, Disks, Network, Sensors, Battery (laptops only), Peripherals, Processes, Connection tests, Computer diagnostic and Settings. The app version is shown below the Settings button."),
            L("🔍 Pesquisa: digite um termo para ir direto à informação ou à opção desejada.",
              "🔍 Search: type a term to jump straight to the information or option you want."),
            L("📋 Copiar: clique em qualquer valor ou rótulo para copiá-lo. Uma mensagem rápida confirma a cópia.",
              "📋 Copy: click any value or label to copy it. A short message confirms the copy."),
            L("📈 Páginas de componente: mostram gráficos, detalhes e sensores extras de cada peça.",
              "📈 Component pages: show charts, details and extra sensors for each part."),
            L("⚙️ Configurações (seções Geral, Gadget, Painéis exibidos, Alertas, Registro de dados, Personalização e Perfis): intervalo de leitura (geral e por componente), tela de abertura, modo econômico, painéis visíveis, limites de temperatura e de bateria, alertas, registro CSV, aparência, tamanho da fonte, idioma, unidade °C/°F, atalho global, exportar/importar e perfis (Jogo, Trabalho, Silencioso). Tudo é salvo automaticamente.",
              "⚙️ Settings (sections General, Gadget, Displayed panels, Alerts, Data logging, Customization and Profiles): update interval (general and per component), startup splash, eco mode, visible panels, temperature and battery limits, alerts, CSV logging, appearance, font size, language, °C/°F unit, global hotkey, export/import and profiles (Gaming, Work, Quiet). Everything is saved automatically."),
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
            L("Cabeçalhos: mostram o total de % utilizado em cada coluna. Arraste as divisórias entre os cabeçalhos para ajustar a largura das colunas.",
              "Headers: show the total % used in each column. Drag the dividers between headers to resize columns."),
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
        Card("🩻", 10, [
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
            L("Ao minimizar, o app mostra um gadget compacto com os valores escolhidos. Há vários formatos: flutuante e barra flutuante (arrastáveis) ou fixos nos quatro cantos da tela, em uma faixa no topo ou em uma faixa na parte inferior. Em Configurações › Gadget, a visão pode ser simplificada (somente texto) ou detalhada (mais informações e um mini gráfico de uso de CPU, RAM, GPU, disco e rede).",
              "When minimized, the app shows a compact gadget with the values you chose. Several layouts exist: floating and floating bar (draggable), or pinned to any of the four screen corners, a strip at the top or a strip at the bottom. In Settings › Gadget the view can be simplified (text only) or detailed (more information and a mini usage chart for CPU, RAM, GPU, disk and network)."),
            L("Fechar a janela (X) NÃO encerra o app: ele continua na bandeja do sistema, perto do relógio. Para sair de verdade, clique com o botão direito no ícone da bandeja e escolha Sair.",
              "Closing the window (X) does NOT quit the app: it keeps running in the system tray near the clock. To really exit, right-click the tray icon and choose Exit."),
            L("O ícone da bandeja muda de cor conforme o estado do computador. Clique duas vezes nele para abrir o app.",
              "The tray icon changes color with your computer's state. Double-click it to open the app."),
            L("Atalho Ctrl+Alt+G (configurável) mostra ou esconde o gadget.",
              "Ctrl+Alt+G (configurable) shows or hides the gadget."),
            L("Com a opção de FPS ligada, o gadget mostra os quadros por segundo do jogo em primeiro plano: vermelho abaixo de 30, verde a partir de 30 e azul a partir de 60. Ao lado aparece a média (Média/méd) do jogo atual, calculada desde que ele entrou em primeiro plano, para comparar com o valor instantâneo.",
              "With FPS enabled, the gadget shows the frames per second of the foreground game: red below 30, green from 30 and cyan from 60. Next to it, the average (Average/avg) of the current game is shown, computed since it came to the foreground, to compare with the instant value."),
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
            L("Driver: a leitura usa um driver do LibreHardwareMonitor. Antivírus ou o recurso Integridade de Memória do Windows podem bloqueá-lo.",
              "Driver: reading uses a LibreHardwareMonitor driver. Antivirus or Windows Memory Integrity may block it."),
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
            L("Ao abrir, uma tela de abertura mostra o progresso e os logs do carregamento; a janela principal só aparece quando CPU, GPU, memória, discos e rede estão carregados. Dá para desligá-la em Configurações › Geral.",
              "On launch, a splash screen shows loading progress and logs; the main window only appears once CPU, GPU, memory, disks and network are loaded. You can turn it off in Settings › General."),
            L("O app inicia com o Windows? Só se você ativar a opção em Configurações. Ele também pode iniciar minimizado.",
              "Does the app start with Windows? Only if you enable it in Settings. It can also start minimized."),
            L("Como desligar as notificações? Em Configurações, na seção de alertas.",
              "How do I turn notifications off? In Settings, in the alerts section."),
        ]);
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.8,
    };

    private void Card(string icon, int titleIndex, string[] paragraphs)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = icon + "  " + Loc.T(SectionTitles[titleIndex]), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var p in paragraphs)
            stack.Children.Add(new TextBlock { Text = p, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) });
        var border = new Border { Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = stack };
        border.SetResourceReference(Border.BackgroundProperty, "CardBg");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        Host.Children.Add(border);
    }
}