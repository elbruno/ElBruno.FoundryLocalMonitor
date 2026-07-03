# Ripley — History

## Session: 2026-07-03 — Team Kickoff

**Requested by:** elbruno (via Squad Coordinator)

### Context
Team cast and initialized for ElBruno.FoundryLocalMonitor. The project goal is to build a Windows WPF systray application that monitors Foundry Local, adapting the existing pattern from https://github.com/elbruno/ElBruno.OllamaMonitor.

### Project understanding
- Reference project uses WPF + NotifyIcon for systray, separate MiniMonitorWindow and MainWindow
- Foundry Local exposes a REST API (OpenAI-compatible) and CLI (`foundry` binary)
- Key polling: `foundry service ps` for loaded models, `foundry service status` for service health
- Distribution: dotnet tool via NuGet (`ElBruno.FoundryLocalMonitor` package)

### Must-have features (Phase 1)
1. Systray icon with context menu (Open Mini Window, Open Main Window, Settings, Exit)
2. Toast/balloon notifications when a model is loaded or unloaded
3. Mini window: compact always-on-top view showing service status + loaded model(s)
4. dotnet tool packaging + NuGet publication pipeline

### Additional CLI features to analyze (Phase 2)
- Model browser (list, download, info)
- Cache management UI
- Service start/stop/restart from systray
- Model load/unload from context menu

### Decisions made
- Use Alien universe for team casting (engineering/survival/monitoring theme)
- Mirror OllamaMonitor structure with `Foundry/` replacing `Ollama/` service layer
- WPF on net9.0-windows (same as OllamaMonitor)

## Session: 2026-07-03 — Phase 1 Delivery

### Work completed
- Created solution scaffold: `.sln`, 3 projects (`ElBruno.FoundryLocalMonitor`, `ElBruno.FoundryLocalMonitor.Tool`, `ElBruno.FoundryLocalMonitor.Tests`)
- Established folder structure mirroring OllamaMonitor pattern (`Foundry/`, `Views/`, `ViewModels/`)
- Stubbed all layer interfaces so bishop, vasquez, hicks, and hudson could work in parallel
- Build result: ✅ 0 errors

### Phase 1 status
**Complete.** Solution builds clean. All agent work integrated successfully.
