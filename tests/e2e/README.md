# E2E Tests — Foundry Local Monitor

Automated end-to-end tests that verify **Foundry Local Monitor** correctly
detects service start, model load, model unload, and service stop — the full
lifecycle of a Foundry Local-backed application.

## What is being tested?

The E2E test simulates the same scenario as the
[FoundryLocalProxy](https://github.com/elbruno/ElBruno.CopilotHarness/tree/main/src/proxies/FoundryLocalProxy)
(a GitHub Copilot BYOK proxy backed by Foundry Local):

```
[FoundryLocalChat sample app]           [Foundry Local Monitor]
        │                                        │
        ├─ SDK init                              │
        ├─ StartWebServiceAsync()                │
        │   → service on http://127.0.0.1:{port} ├─ foundry service status ✓
        │                                        ├─ GET /v1/models → [] ✓
        ├─ LoadModelAsync()                      │
        │   → model in memory                    ├─ GET /v1/models → [model] ✓
        ├─ Chat (3 automated questions)          │
        ├─ UnloadAsync()                         ├─ GET /v1/models → [] ✓
        ├─ StopWebServiceAsync()                 ├─ foundry service status → stopped ✓
        └─ exit
```

## Assertions

| # | What | How | Expected |
|---|------|-----|---------|
| 1 | Service detection | `foundry service status` CLI | URL extracted, `http://127.0.0.1:{port}` |
| 2 | Model load detection | `GET /v1/models` | Non-empty `data[]` with expected alias |
| 3 | Alias match | Model ID contains the requested alias | `qwen2.5-coder-0.5b` (or custom) |
| 4 | Model unload | `GET /v1/models` after unload | `data: []` or service gone |
| 5 | Service stop | `foundry service status` | `Service is not running` |

## Running the test

### Quick run (automated, no GUI)

```powershell
cd tests/e2e
.\Run-E2ETest.ps1
```

### Full visual E2E (monitor + automated driver)

Open **two terminals**, or let the script launch everything:

```powershell
cd tests/e2e
.\Run-E2ETest.ps1 -LaunchMonitor
```

The `-LaunchMonitor` flag starts the WPF systray app automatically and kills it
at the end. Watch the systray icon change as the chat app moves through each step.

### Custom model

```powershell
.\Run-E2ETest.ps1 -ModelAlias phi-4-mini -LaunchMonitor
```

The model must be in the local Foundry cache or downloadable on first run.

### Skip rebuild

```powershell
.\Run-E2ETest.ps1 -SkipBuild     # uses existing binaries
```

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-ModelAlias` | `qwen2.5-coder-0.5b` | Model to load during the test |
| `-ServiceStartTimeout` | `90` | Seconds to wait for service start |
| `-ModelLoadTimeout` | `120` | Seconds to wait for model to appear in `/v1/models` |
| `-LaunchMonitor` | off | Also launch the WPF monitor for visual verification |
| `-SkipBuild` | off | Skip `dotnet build` step |

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | All assertions passed |
| `1` | One or more assertions failed |

## Test output

A run report is saved to `tests/e2e/last-run-results.txt` after every run.
It includes assertion results, elapsed time, and the full chat app console output.

## Why there is no CI workflow for this test

Foundry Local requires the **Foundry CLI**, **GPU drivers**, and **locally cached
model weights** (1–4 GB per model). None of these exist on any GitHub Actions
runner — not `windows-latest`, not `ubuntu-latest`, not self-hosted without
significant custom setup. Adding a workflow would create a job that always fails.

**This test is a local developer tool.** Run it manually before publishing a new
release to verify the full detection loop works end-to-end on your machine.

## Prerequisites

- **Foundry Local CLI** installed — `foundry --version` must work
  Install: https://aka.ms/foundrylocal
- **.NET 10 SDK**
- **Model cached** — run `foundry model list` to see cached models.
  On first run the sample app downloads the model automatically (1–4 GB).
- **Windows** — the Foundry Local Monitor is Windows-only (WPF).

## Troubleshooting

**`foundry CLI not found`**
→ Install Foundry Local: https://aka.ms/foundrylocal

**`Service never started within timeout`**
→ Increase timeout: `.\Run-E2ETest.ps1 -ServiceStartTimeout 180`
→ Check `foundry service status` manually
→ Check Windows Firewall isn't blocking the service

**`No model appeared in /v1/models within timeout`**
→ The model may need to be downloaded first (can take 10+ minutes on first run)
→ Try: `foundry model download qwen2.5-coder-0.5b` separately first
→ Then re-run with `-SkipBuild`

**Test passes but monitor shows nothing**
→ Run with `-LaunchMonitor` and check if the systray icon changes
→ Ensure `foundry` CLI is in PATH (the monitor uses it for discovery)
