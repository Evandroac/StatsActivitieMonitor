# Stats Monitor

Monitor de hardware leve para Windows 10/11 (C#, .NET 8, WPF, MVVM). Exibe em tempo real CPU, GPU, RAM, VRAM e temperaturas, com painel principal e um gadget compacto.

## Arquitetura

```
src/StatsMonitor
├─ Models/      HardwareSnapshot (leitura imutável; null = sensor indisponível → "N/D")
├─ Services/    IHardwareMonitorService (abstração) + LibreHardwareMonitorService, StartupService
├─ ViewModels/  MainViewModel, ComponentViewModel (CPU/GPU/RAM/VRAM), SettingsViewModel
├─ Views/       MainWindow, GadgetWindow, SettingsWindow
├─ Controls/    CircularGauge (indicador circular animado), Sparkline (gráfico em tempo real)
├─ Helpers/     MVVM (ObservableObject, RelayCommand), ThemeHelper
├─ Settings/    AppSettings + SettingsService (JSON em %APPDATA%\StatsMonitor\settings.json)
└─ Themes/      Dark.xaml / Light.xaml
```

- **Biblioteca de hardware:** [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) atrás de `IHardwareMonitorService`, podendo ser trocada sem alterar UI/ViewModels.
- **Segundo plano:** a coleta roda em uma `Task` própria (fora da thread de UI) e continua com a janela aberta, minimizada ou oculta. Recursos são liberados ao encerrar.
- **Gadget:** janela WPF compacta, sem borda, translúcida e *topmost*, posicionada por padrão no canto da área de trabalho junto à barra de tarefas. Não usa o recurso descontinuado de barras de ferramentas (*appbar/deskband*) e não interfere em outros programas. Aparece ao minimizar a janela principal (ou sempre, se configurado); arraste para mover (posição salva) e dê duplo clique para restaurar a janela principal.
- **Sensores:** detectados automaticamente. Nunca são exibidos valores fictícios: sensor indisponível = **N/D**. Alguns sensores de temperatura da CPU exigem privilégios de administrador; o app roda sem elevação e informa isso na interface.

## Compilar e executar (Visual Studio)

Requisitos: Windows 10/11, Visual Studio 2022 com a carga de trabalho **Desenvolvimento para desktop com .NET**, SDK .NET 8+. Para ler a temperatura da CPU (ex.: AMD Ryzen) é necessário instalar o driver **PawnIO** (`winget install namazso.PawnIO`), exigido pelo LibreHardwareMonitor. O aplicativo também solicita **privilégios de administrador** (UAC) ao iniciar; sem isso o sensor retorna 0. Abra o Visual Studio como administrador para depurar com F5.

1. Abra `StatsMonitor.sln`.
2. Restaure os pacotes NuGet (automático) e compile (`Ctrl+Shift+B`).
3. Execute com `F5`. Para ler todas as temperaturas, execute o Visual Studio / o executável como administrador.

Via CLI: `dotnet run --project src/StatsMonitor`

## Configurações

Intervalo de atualização, iniciar com o Windows, iniciar minimizado, exibir temperaturas, exibir gadget, gadget sempre visível, modo do gadget (flutuante ou na barra de tarefas), painéis exibidos na janela principal e no gadget (CPU, GPU, RAM, VRAM, Rede) e limites de temperatura (atenção/crítica) por CPU e GPU.

## Personalização

Botão "Personalização" na janela principal (separado das configurações): tema base escuro/claro, cores de fundo, painéis, bordas, textos e destaques da janela principal, cor/texto/opacidade do gadget e cores dos alertas. Campos vazios usam o padrão do tema; "Restaurar padrões" limpa tudo.

## Limitações

- Temperatura/frequência dependem do suporte de cada CPU/GPU e do acesso ao driver de sensores.
- VRAM total/usada é lida de GPUs NVIDIA/AMD; GPUs integradas Intel podem exibir N/D.
- Temperatura de RAM só aparece quando os módulos (DIMM) expõem sensor.
