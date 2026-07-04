# Squad Decisions

## Active Decisions

### 2026-07-03: Project Architecture — ElBruno.FoundryLocalMonitor
**By:** Squad Coordinator (requested by elbruno)

**Decision:** Adapt ElBruno.OllamaMonitor pattern for Foundry Local. WPF app on net10.0-windows.

**Key choices:**
- **Foundry integration:** Hybrid — HTTP API (OpenAI-compatible endpoint) primary, CLI (`foundry` binary) fallback
- **Systray:** `Hardcodet.NotifyIcon.Wpf` (same as OllamaMonitor)
- **MVVM:** `CommunityToolkit.Mvvm` with source generators
- **Packaging:** dotnet global tool — `PackAsTool=true`, command `foundry-monitor`, NuGet ID `ElBruno.FoundryLocalMonitor`
- **CI/CD:** GitHub Actions, `windows-latest` runner; GitHub Release-driven NuGet publish via OIDC Trusted Publishing

---

### 2026-07-03: Team Casting
**By:** Squad Coordinator

**Decision:** Alien universe. Engineering/survival/monitoring resonance. 

| Cast Name | Role |
|-----------|------|
| Ripley | Lead / Architect |
| Bishop | Backend Dev (Foundry integration) |
| Vasquez | WPF / UI Developer |
| Hudson | Tester |
| Hicks | DevOps / NuGet |

---

### 2026-07-03: Implementation Phases

**Phase 1 — Must Have (MVP):**
1. Solution scaffold (`.sln`, `src/`, `tests/`, `global.json`)
2. `IFoundryService` + `FoundryPollingService` (Bishop)
3. Foundry CLI wrapper + HTTP client (Bishop)
4. Systray icon + context menu (Vasquez)
5. Model load/unload balloon notifications (Vasquez)
6. MiniMonitorWindow — compact status + current model (Vasquez)
7. MainWindowViewModel + basic MainWindow (Vasquez)
8. Unit tests for service layer (Hudson)
9. dotnet tool csproj + NuGet metadata (Hicks)
10. GitHub Actions CI workflow (Hicks)

**Phase 2 — Extended Features (analyze with team after Phase 1):**
- Service start/stop/restart from systray context menu
- Model browser tab (list, download, info)
- Cache management tab (list, remove)
- Model load/unload from UI
- Execution provider info display (`foundry model info <model>`)
- Polling interval + endpoint override settings
- GitHub Actions CD for NuGet publish

### 2026-07-03: NuGet Release Automation

**Decision:** Publish the `ElBruno.FoundryLocalMonitor` dotnet tool from `src/ElBruno.FoundryLocalMonitor.Tool/` using GitHub Releases plus OIDC Trusted Publishing.

**Key choices:**
- `publish.yml` triggers on `release: published` and `workflow_dispatch`
- `NuGet/login@v1` replaces long-lived NuGet API keys
- `NUGET_USER` remains the only NuGet-related secret
- Release automation lives in `.github/workflows/squad-release.yml`
- Pack target is the tool project, not the solution

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
