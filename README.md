# Stats Monitor

Monitor de hardware leve para Windows 10/11 (C#, .NET 8, WPF, MVVM). Exibe em tempo real CPU, GPU, RAM, VRAM, discos, rede, bateria e temperaturas, com painel principal, gadget compacto e telas dedicadas (processos, testes de conexão e diagnóstico do computador). Disponível em português e inglês.

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
- **Gadget:** janela WPF compacta, sem borda, translúcida e *topmost*, posicionada por padrão no canto da área de trabalho junto à barra de tarefas. Não usa o recurso descontinuado de barras de ferramentas (*appbar/deskband*) e não interfere em outros programas. Aparece ao minimizar a janela principal (ou sempre, se configurado); arraste para mover (posição salva) e dê duplo clique para restaurar a janela principal.
- **Sensores:** detectados automaticamente. Nunca são exibidos valores fictícios: sensor indisponível = **N/D**. Alguns sensores de temperatura da CPU exigem privilégios de administrador; o app roda sem elevação e informa isso na interface.

## Telas principais

- **Principal / Sistema / CPU / GPU / Memória / Discos / Rede / Sensores / Periféricos:** leituras em tempo real, com intervalo de atualização individual por componente e pesquisa geral.
- **Bateria (notebooks):** nível, status, tempo restante, tempo para carga completa, saúde e fonte de energia.
- **Processos:** visão estilo Gerenciador de Tarefas (árvore, CPU/RAM/disco/rede, filtros, ordenação, finalizar/reiniciar, gráfico timeline).
- **Testes de conexão:** diagnóstico, ping, traceroute, DNS, portas, HTTP e velocidade.
- **Diagnóstico do computador:** veja abaixo.
- **Ajuda:** tutorial com funcionamento e ressalvas. Há ainda tela de abertura (splash), alertas de atenção/críticos e modo econômico ao minimizar.

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

1. Abra `StatsMonitor.sln`.
2. Restaure os pacotes NuGet (automático) e compile (`Ctrl+Shift+B`).
3. Execute com `F5`. Para ler todas as temperaturas, execute o Visual Studio / o executável como administrador.

Via CLI: `dotnet run --project src/StatsMonitor`

## Configurações

Intervalo de atualização, iniciar com o Windows, iniciar minimizado, exibir temperaturas, exibir gadget, gadget sempre visível, modo do gadget (flutuante ou na barra de tarefas), painéis exibidos na janela principal e no gadget (CPU, GPU, RAM, VRAM, Rede) e limites de temperatura (atenção/crítica) por CPU e GPU.

## Personalização

Botão "Personalização" na janela principal (separado das configurações): tema base escuro/claro, cores de fundo, painéis, bordas, textos e destaques da janela principal, cor/texto/opacidade do gadget e cores dos alertas. Campos vazios usam o padrão do tema; "Restaurar padrões" limpa tudo.

## Testes

`dotnet test src\StatsMonitor.Tests` executa os testes xUnit (Loc, Units, alertas, configurações, perfis, atalho, diagnóstico).

## Instalador

Requer o .NET SDK e o Inno Setup 6. Execute `.\installer\build.ps1 -Version 1.0.0` para gerar `artifacts\StatsMonitor-Setup-<versão>.exe`.
Para assinar o código, defina `SM_SIGN_PFX` (caminho do .pfx) e `SM_SIGN_PASSWORD` antes de executar (requer o signtool do Windows SDK).

## Recursos adicionais

- Notificações de alerta (toast + som), perfis (Jogo/Trabalho/Silencioso, também na bandeja), atalho global (padrão Ctrl+Alt+G), tema Seguir o sistema e alto contraste, exportar/importar configurações e sensores extras (ventiladores, energia, tensões, bateria).

## Limitações

- Temperatura/frequência dependem do suporte de cada CPU/GPU e do acesso ao driver de sensores.
- VRAM total/usada é lida de GPUs NVIDIA/AMD; GPUs integradas Intel podem exibir N/D.
- Temperatura de RAM só aparece quando os módulos (DIMM) expõem sensor.
