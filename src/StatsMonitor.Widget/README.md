# Stats Monitor Widget (Windows 11)

Provedor de widget para o painel de widgets do Windows 11 (Win+W), em Adaptive Cards.

- O app **Stats Monitor** (com administrador) grava um resumo a cada 5 s em `%LocalAppData%\StatsMonitor\widget.json`.
- Este projeto lê o arquivo e atualiza o widget a cada 5 s, só enquanto ele está visível. Se o arquivo tiver mais de 30 s, mostra "Abra o Stats Monitor".
- O provedor roda sem privilégios; toda a leitura de sensores continua no app principal.

## Compilar

```
dotnet build src\StatsMonitor.Widget -p:Platform=x64
```

## Instalar (teste)

Requer um pacote MSIX assinado. Gere o pacote pelo Visual Studio (Project > Package) ou com `makeappx`, assine com um certificado confiável (o `Publisher` do `Package.appxmanifest` deve coincidir com o certificado) e instale. Depois abra Win+W > "+" e adicione "Stats Monitor".

Os ícones em `Assets\` são provisórios.
