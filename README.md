# ElBruno.FoundryLocalMonitor

![Foundry Local Monitor banner](src/ElBruno.FoundryLocalMonitor/Assets/repo-banner.png)

Windows systray monitor for [Foundry Local](https://learn.microsoft.com/en-us/azure/foundry-local/) — shows model load/unload notifications and a mini status window.

## Features

- 🔔 Balloon notifications when models load/unload
- 📊 Compact mini status window (always-on-top)
- 🖥️ Full dashboard: status, loaded models, available models
- ⚙️ Systray icon with context menu

## Install

```bash
dotnet tool install -g ElBruno.FoundryLocalMonitor
foundry-monitor
```

## Requirements

- Windows 10/11
- .NET 9 runtime
- [Foundry Local](https://learn.microsoft.com/en-us/azure/foundry-local/how-to/how-to-install-foundry-local) installed

## Build from source

```bash
dotnet restore
dotnet build
dotnet test
```

## License

MIT
