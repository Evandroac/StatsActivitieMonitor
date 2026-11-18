# Stats Monitor

Monitor de hardware leve para Windows 10/11 (C#, .NET 8, WPF, MVVM). Exibe em tempo real CPU, GPU, RAM, VRAM, discos, rede, bateria e temperaturas, com painel principal, gadget compacto e telas dedicadas (processos, testes de conexão e diagnóstico do computador). Disponível em 16 idiomas (português, inglês, espanhol, francês, alemão, italiano, russo, chinês, japonês, coreano, árabe, hindi e variantes regionais).

> 💡 **Sobre o projeto:** o Stats Monitor é open-source e faz parte de um estudo sobre o desenvolvimento de software com inteligência artificial. Foi desenhado, programado e feito inteiramente com a ajuda de IA, e por isso pode conter falhas. Sugestões e relatos de problemas são muito bem-vindos em [Evandroac/StatsActivityMonitor](https://github.com/Evandroac/StatsActivityMonitor).

## Arquitetura

```
src/StatsMonitor
├─ Models/      HardwareSnapshot (leitura imutável; null = sensor indisponível → "N/D")
├─ Services/    IHardwareMonitorService (abstração) + LibreHardwareMonitorService, StartupService, bateria, ferramentas de rede, processos
├─ Diagnostics/ Módulo de diagnóstico: inventário, motor de regras, testes, histórico, relatórios e monitoramento
├─ ViewModels/  MainViewModel, ComponentViewModel (CPU/GPU/RAM/VRAM), SettingsViewModel
├─ Views/       MainWindow, GadgetWindow, SettingsWindow, Processos, Testes de conexão, Diagnóstico, Bateria, Ajuda, Splash
├─ Controls/    CircularGauge (indicador circular animado), Sparkline (gráfico em tempo real)
├─ Helpers/     MVVM (ObservableObject, RelayCommand), ThemeHelper
├─ Settings/    AppSettings + SettingsService (JSON em %APPDATA%\StatsMonitor\settings.json)
└─ Themes/      Dark.xaml / Light.xaml
```

- **Biblioteca de hardware:** [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) atrás de `IHardwareMonitorService`, podendo ser trocada sem alterar UI/ViewModels.
- **Segundo plano:** a coleta roda em uma `Task` própria (fora da thread de UI) e continua com a janela aberta, minimizada ou oculta. Recursos são liberados ao encerrar.
- **Gadget:** janela WPF compacta, sem borda, translúcida e *topmost*, que pode ficar flutuante (arrastável) ou fixa em um dos quatro cantos da tela, em uma faixa no topo ou em uma faixa na parte inferior. A visão pode ser **simplificada** (texto) ou **detalhada** (mais informações e mini gráfico de uso de CPU, RAM, GPU, disco e rede). Não usa o recurso descontinuado de barras de ferramentas (*appbar/deskband*) e não interfere em outros programas. Aparece ao minimizar a janela principal (ou sempre, se configurado); arraste para mover (posição salva) e dê duplo clique para restaurar a janela principal.
- **Sensores:** detectados automaticamente. Nunca são exibidos valores fictícios: sensor indisponível = **N/D**. Alguns sensores de temperatura da CPU exigem privilégios de administrador; o app roda sem elevação e informa isso na interface.

## Telas principais

- **Principal / Sistema e periféricos (página unificada com filtro) / CPU / GPU / Memória / Discos / Rede / Sensores:** leituras em tempo real, com intervalo de atualização individual por componente e pesquisa geral.
- **Bateria:** nível, status, tempo restante, tempo para carga completa, saúde e fonte de energia. O item fica sempre no menu; em computadores sem bateria ele aparece esmaecido e a página informa que não há bateria.
- **Processos:** visão estilo Gerenciador de Tarefas (árvore, CPU/RAM/disco/rede, filtros — padrão: Aplicativos —, ordenação, finalizar/reiniciar, gráfico timeline). As colunas podem ser reordenadas (arrastando o título), ocultadas ou adicionadas (PID, PID pai, Threads, Tipo, Executável, Iniciado em, Caminho), redimensionadas (o botão ↔ ajusta a coluna Nome ao conteúdo) e a escolha fica salva.
- **Testes de conexão:** diagnóstico, ping, traceroute, DNS, portas, HTTP e velocidade.
- **Diagnóstico do computador:** veja abaixo.
- **Ajuda:** tutorial com funcionamento e ressalvas, menu de tópicos com destaque do painel em foco e links para o código-fonte e o autor.
- **Outros:** tela de abertura (splash) moderna com botão de minimizar, barra de título personalizada no estilo do tema, gadget com botões que aparecem ao passar o mouse e animações suaves, FPS, mascotes (Pets), pesquisa geral, destaque refinado do painel sob o mouse, alertas de atenção/críticos (padrão: somente críticos), aviso discreto \

## Diagnóstico do computador

Módulo somente leitura (nenhuma correção automática) com 16 seções: Visão geral, Hardware, Processador, Memória RAM, Placa de vídeo, Armazenamento, Bateria e energia, Sistema operacional, Drivers e dispositivos, Rede, Segurança, Monitoramento em tempo real, Testes de desempenho, Diagnóstico inteligente, Histórico e Relatórios.

- Coleta via WMI, registro, log de eventos e serviços já existentes no app; dado que não pôde ser lido aparece como **Indisponível** e nunca conta como saudável.
- Testes não destrutivos e canceláveis: CPU, RAM (256 MB), disco (arquivo temporário de 128 MB) e rede; avaliação da GPU; SFC `/verifyonly` opcional.
- Achados com evidência, causa provável e recomendação; indicador geral heurístico (0–100).
- Histórico (até 100 diagnósticos) com comparação, em `%APPDATA%\StatsMonitor\diagnostics`, com log de auditoria.
- Exportação em PDF, HTML, CSV e JSON.
- Gráficos de 1/5/15/60 min e alertas configuráveis (enquanto a tela está aberta).
- Limitações: SMART, TPM e BitLocker costumam exigir administrador; "driver antigo" é heurística por data; não há teste de estresse 3D da GPU; o teste de RAM não substitui o MemTest86; o PDF é apenas texto.
## Compilar e executar (Visual Studio)

Requisitos: Windows 10/11, Visual Studio 2022 com a carga de trabalho **Desenvolvimento para desktop com .NET**, SDK .NET 8+. Para ler a temperatura da CPU (ex.: AMD Ryzen) é necessário instalar o driver **PawnIO** (`winget install namazso.PawnIO`), exigido pelo LibreHardwareMonitor. O aplicativo também solicita **privilégios de administrador** (UAC) ao iniciar; sem isso o sensor retorna 0. Abra o Visual Studio como administrador para depurar com F5.

1. Abra `StatsMonitor.slnx`.
2. Restaure os pacotes NuGet (automático) e compile (`Ctrl+Shift+B`).
3. Execute com `F5`. Para ler todas as temperaturas, execute o Visual Studio / o executável como administrador.

Via CLI: `dotnet run --project src/StatsMonitor`

## Configurações

Valores padrão: iniciar com o Windows, animações suaves, modo econômico ao minimizar, pausar a leitura (quando aplicável), ler sensores de disco, exibir temperaturas e alertas somente críticos. A tela de abertura (splash) é sempre exibida.

Opções: intervalo de atualização, iniciar com o Windows, iniciar minimizado, exibir temperaturas, exibir gadget, gadget sempre visível, visão do gadget (simplificada ou detalhada), posição do gadget (flutuante, cantos, faixa superior ou inferior), painéis exibidos na janela principal e no gadget (CPU, GPU, RAM, VRAM, Rede) e limites de temperatura (atenção/crítica) por CPU e GPU.

## Personalização

Botão "Personalização" na janela principal (separado das configurações): tema base escuro/claro, **temas visuais** em quadros de seleção (Windows XP/98, Cyberpunk e futurismo, Retrô 80/90, Vaporwave, Arcade 8-bit, CRT/Terminal, Moderno, Minimalista, Anime/kawaii, artísticos — cada um com paleta e fonte próprias, definidos em `Helpers\ThemeCatalog.cs`), cores de fundo, painéis, bordas, textos e destaques da janela principal, cor/texto/opacidade do gadget e cores dos alertas. Campos vazios usam o padrão do tema; "Restaurar padrões" limpa tudo.

## Testes

`dotnet test src\StatsMonitor.Tests` executa os testes xUnit (Loc e cobertura de traduções, Units, alertas, configurações, perfis, atalho, diagnóstico).

Release: `dotnet build StatsMonitor.slnx -c Release`.

## Instalador

Requer o .NET SDK e o Inno Setup 6. Execute `.\installer\build.ps1 -Version 1.0.0` para gerar `artifacts\StatsMonitor-Setup-<versão>.exe`.
Para assinar o código, defina `SM_SIGN_PFX` (caminho do .pfx) e `SM_SIGN_PASSWORD` antes de executar (requer o signtool do Windows SDK).

## Recursos adicionais

- Notificações de alerta (toast + som), perfis (Jogo/Trabalho/Silencioso, também na bandeja), atalho global (padrão Ctrl+Alt+G), tema Seguir o sistema e alto contraste, exportar/importar configurações e sensores extras (ventiladores, energia, tensões, bateria).

## Limitações

- Temperatura/frequência dependem do suporte de cada CPU/GPU e do acesso ao driver de sensores.
- VRAM total/usada é lida de GPUs NVIDIA/AMD; GPUs integradas Intel podem exibir N/D.
- Temperatura de RAM só aparece quando os módulos (DIMM) expõem sensor.
