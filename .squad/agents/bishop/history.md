# Bishop — History

## Session: 2026-07-03 — Team Kickoff

**Requested by:** elbruno (via Squad Coordinator)

### Context
Backend Dev for ElBruno.FoundryLocalMonitor. Responsible for all Foundry Local integration — CLI wrapping, HTTP API polling, model state event system.

### Key learnings
- Foundry Local exposes OpenAI-compatible REST API on a dynamic port (get it from `foundry service status`)
- CLI binary is `foundry` — installed via winget on Windows
- `foundry service ps` lists currently loaded models
- `foundry service status` gives running status + local endpoint URL
- The service must be running for HTTP API access; graceful fallback to CLI needed
- Reference project (OllamaMonitor) uses `Ollama/` folder with similar polling pattern

### Architecture decision (captured by Ripley)
- Hybrid polling: HTTP API first, CLI fallback
- Polling interval: configurable, default 5 seconds
- Model state change events: compare previous vs current model list, emit on diff

## Session: 2026-07-03 — Phase 1 Delivery

### Work completed
- `AppSettings` — configurable polling interval and endpoint override
- `FoundryCliRunner` — wraps `foundry` binary, captures stdout/stderr
- `FoundryHttpClient` — OpenAI-compatible HTTP client for Foundry Local REST API
- `FoundryCliParser` — parses `foundry service ps` and `foundry service status` output
- `FoundryPollingService` — full implementation: hybrid HTTP-first / CLI-fallback polling, model state diff events
- `App.xaml.cs` — DI container wiring for all services
- Build result: ✅ 0 errors 0 warnings

### Phase 1 status
**Complete.** Full backend integration layer shipped.
