# E2E Tests — Foundry Local Monitor

Automated end-to-end tests that verify **Foundry Local Monitor** correctly detects service start, model load, model unload, and service stop — from both a C# and a Python client simultaneously.

## Tests

### `Run-E2ETest.ps1` — Single-client test (C#)

Runs the C# `FoundryLocalChat` sample app and verifies the monitor detects all 6 lifecycle steps.

```powershell
cd tests/e2e
.\Run-E2ETest.ps1 -LaunchMonitor
```

### `Run-E2EMultiClient.ps1` — Multi-client test (C# + Python)

Runs both the C# and Python sample apps **simultaneously** and verifies the monitor discovers both SDK endpoints and detects model events from each.

```powershell
cd tests/e2e
.\Run-E2EMultiClient.ps1 -LaunchMonitor
```

## What is being tested?

### Single-client flow

```
[FoundryLocalChat (C#)]                    [Foundry Local Monitor]
        │                                           │
        ├─ SDK init                                 │
        ├─ StartWebServiceAsync() → port 55588      ├─ Discovers port 55588 ✓
        ├─ foundry model load qwen2.5-coder-0.5b    ├─ GET /v1/models → [model] ✓
        ├─ Chat (3 questions)                       │
        ├─ foundry model unload                     ├─ GET /v1/models → [] ✓
        └─ Service stop → port 55588 closes         ├─ Endpoint gone ✓
```

### Multi-client flow

```
[FoundryLocalChat (C#, port 55588)]   [FoundryLocalChatPy (Python, port 55589)]
        │                                          │
        ├─ SDK init (C# SDK)                       ├─ SDK init (Python SDK)
        ├─ Service → port 55588                    ├─ Service → port 55589
        │              [Monitor discovers both ports simultaneously]
        ├─ Load model A                            ├─ Load model B
        │              [Monitor shows models from both clients]
        ├─ Unload A                                ├─ Unload B
        └─ Stop                                    └─ Stop
                       [Monitor: no active endpoints]
```

## Discovery mechanism

The monitor's parallel port scanner (`FoundryEndpointDiscovery`) fans out `GET /v1/models` to all `127.0.0.1` listeners simultaneously (800ms timeout each). Any port responding with `{"object":"list"}` is a Foundry endpoint.

This is why both clients are discovered without any manual configuration — port 55589 (Python) is discovered the same way as port 55588 (C#). See [docs/discovery.md](../../docs/discovery.md) for full details.

## Parameters

### `Run-E2ETest.ps1`

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-ModelAlias` | `qwen2.5-coder-0.5b` | Model to load |
| `-ServiceStartTimeout` | `90` | Seconds to wait for service start |
| `-ModelLoadTimeout` | `120` | Seconds to wait for model |
| `-LaunchMonitor` | off | Launch WPF monitor for visual E2E |
| `-SkipBuild` | off | Skip `dotnet build` |

### `Run-E2EMultiClient.ps1`

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-ModelAlias` | `qwen2.5-coder-0.5b` | Model used by both clients |
| `-MonitorWait` | `12` | Seconds to pause between steps |
| `-ServiceStartTimeout` | `90` | Seconds to wait for service |
| `-ModelLoadTimeout` | `120` | Seconds to wait for model |
| `-LaunchMonitor` | off | Launch WPF monitor for visual E2E |
| `-SkipBuild` | off | Skip `dotnet build` |

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | All assertions passed |
| `1` | One or more assertions failed |

## Output files

| File | Contents |
|------|----------|
| `last-run-results.txt` | Single-client test results |
| `last-multi-client-results.txt` | Multi-client test results |
| `e2e-csharp.log` | C# client console output |
| `e2e-python.log` | Python client console output |

## Prerequisites

- **Foundry Local CLI** — `foundry --version` must work
  Install: https://aka.ms/foundrylocal
- **.NET 10 SDK**
- **Python 3.11+** with `pip install foundry-local-sdk openai`
- **Model cached** — run `foundry model list` to confirm `qwen2.5-coder-0.5b` is available
- **Windows** — Foundry Local Monitor is Windows-only (WPF)

## Why there is no CI workflow

Foundry Local requires the Foundry CLI, GPU drivers, and locally cached model weights (1–4 GB per model). None of these exist on any standard CI runner. **This test is a local developer tool** — run it manually before publishing a new release.

## Troubleshooting

**`foundry CLI not found`**
→ Install Foundry Local: https://aka.ms/foundrylocal

**C# service never starts**
→ Try `.\Run-E2ETest.ps1 -ServiceStartTimeout 180`

**Python service never starts**
→ Run `python app.py` from `samples/FoundryLocalChatPy/` directly to see errors
→ Check `pip show foundry-local-sdk` confirms package is installed

**Model never loads**
→ Pre-download: `foundry model download qwen2.5-coder-0.5b`
→ Re-run with `-ModelLoadTimeout 300`

**Monitor shows nothing**
→ Run with `-LaunchMonitor` and wait 30s for discovery cycle
→ Ensure `foundry` CLI is in `PATH`

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
