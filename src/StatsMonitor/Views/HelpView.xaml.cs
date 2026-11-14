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
        "Ressalvas importantes", "Perguntas frequentes",
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
            L("✔ Os dados ficam no seu computador. O app não envia leituras para a internet. A única conexão é um ping simples ao DNS 1.1.1.1 para medir a latência e saber se há internet.",
              "✔ Data stays on your computer. The app does not upload readings. Its only connection is a simple ping to the 1.1.1.1 DNS to measure latency and detect internet access."),
        ]);
        Card("🧭", 1, [
            L("☰ Menu lateral: clique no ☰ para mostrar ou esconder os nomes. As páginas são: Principal, Sistema, CPU, GPU, Memória, Discos, Rede, Sensores, Periféricos e Configurações.",
              "☰ Side menu: click ☰ to show or hide the names. Pages are: Main, System, CPU, GPU, Memory, Disks, Network, Sensors, Peripherals and Settings."),
            L("🔍 Pesquisa: digite um termo para ir direto à informação ou à opção desejada.",
              "🔍 Search: type a term to jump straight to the information or option you want."),
            L("📋 Copiar: clique em qualquer valor ou rótulo para copiá-lo. Uma mensagem rápida confirma a cópia.",
              "📋 Copy: click any value or label to copy it. A short message confirms the copy."),
            L("📈 Páginas de componente: mostram gráficos, detalhes e sensores extras de cada peça.",
              "📈 Component pages: show charts, details and extra sensors for each part."),
            L("⚙️ Configurações: intervalo de leitura (geral e por componente), painéis visíveis, limites de temperatura, aparência, tamanho da fonte, idioma, unidade °C/°F e perfis (Jogo, Trabalho, Silencioso).",
              "⚙️ Settings: update interval (general and per component), visible panels, temperature limits, appearance, font size, language, °C/°F unit and profiles (Gaming, Work, Quiet)."),
            L("🎨 Cores: verde, amarelo e vermelho indicam normal, atenção e crítico, de acordo com os limites que você definiu.",
              "🎨 Colors: green, yellow and red mean normal, attention and critical, based on the limits you set."),
        ]);
        Card("🪟", 2, [
            L("Ao minimizar, o app mostra um gadget compacto com os valores escolhidos. Há vários formatos: flutuante, na barra de tarefas, barra flutuante e acoplado ao topo.",
              "When minimized, the app shows a compact gadget with the values you chose. Several layouts exist: floating, taskbar, floating bar and docked to the top."),
            L("Fechar a janela (X) NÃO encerra o app: ele continua na bandeja do sistema, perto do relógio. Para sair de verdade, clique com o botão direito no ícone da bandeja e escolha Sair.",
              "Closing the window (X) does NOT quit the app: it keeps running in the system tray near the clock. To really exit, right-click the tray icon and choose Exit."),
            L("O ícone da bandeja muda de cor conforme o estado do computador. Clique duas vezes nele para abrir o app.",
              "The tray icon changes color with your computer's state. Double-click it to open the app."),
            L("Atalho Ctrl+Alt+G (configurável) mostra ou esconde o gadget.",
              "Ctrl+Alt+G (configurable) shows or hides the gadget."),
            L("Com a opção de FPS ligada, o gadget mostra os quadros por segundo do jogo em primeiro plano: vermelho abaixo de 30, verde a partir de 30 e azul a partir de 60.",
              "With FPS enabled, the gadget shows the frames per second of the foreground game: red below 30, green from 30 and cyan from 60."),
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