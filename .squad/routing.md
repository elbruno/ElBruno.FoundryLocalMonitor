# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|---------|
| Architecture, design, trade-off decisions | Ripley | Solution structure, WPF framework choice, API vs CLI polling strategy |
| Code review, PR approval/rejection | Ripley | Review Bishop's Foundry layer, Vasquez's XAML, Hicks's csproj |
| Issue triage (`squad` label) | Ripley | Analyze GitHub issues, assign `squad:{member}` labels |
| Work decomposition, backlog prioritization | Ripley | Break features into tasks, sequence work |
| Foundry Local integration, CLI wrapper | Bishop | `IFoundryService`, polling, model state events, HTTP API client |
| REST API / HTTP client, CLI process runner | Bishop | Foundry OpenAI-compatible endpoint, `foundry service ps` output parsing |
| Service lifecycle, model state detection | Bishop | Detect loaded/unloaded models, emit events |
| WPF UI, XAML, windows | Vasquez | MainWindow, MiniMonitorWindow, SettingsWindow |
| Systray icon, context menu, notifications | Vasquez | `NotifyIcon`, balloon tips, tray context menu |
| ViewModels, MVVM, data binding | Vasquez | `MainWindowViewModel`, `MiniMonitorViewModel` |
| Unit tests, integration tests | Hudson | xUnit tests, Moq mocks, edge case coverage |
| Bug reports | Hudson | Reproduction steps, expected vs actual |
| Test coverage review | Hudson | May reject work with insufficient coverage |
| NuGet packaging, dotnet tool setup | Hicks | `.Tool` csproj, `PackAsTool`, `ToolCommandName` |
| GitHub Actions CI/CD | Hicks | Build workflow, NuGet publish workflow |
| Build scripts, versioning | Hicks | `global.json`, SemVer, release tags |
| Session logging | Scribe | Automatic — never needs routing |
| Work queue management | Ralph | Backlog scanning, keep-alive loop |
| RAI review | Rai | Content safety, bias checks, credential detection, ethical review |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. The Lead handles all `squad` (base label) triage.
