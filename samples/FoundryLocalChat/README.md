# FoundryLocalChat — E2E Monitor Test Client

A C# console app that simulates the same scenario as the
[FoundryLocalProxy](https://github.com/elbruno/ElBruno.CopilotHarness/tree/main/src/proxies/FoundryLocalProxy)
(a GitHub Copilot BYOK proxy backed by Foundry Local), but as an automated demo.

Use it to verify the **Foundry Local Monitor** detects every state transition
in real-time without any user input.

## E2E Test Flow

```
[Step 1] SDK init          → monitor: "Checking…"
[Step 2] Web service start → monitor: service URL  (http://127.0.0.1:{port})
[Step 3] Model loaded      → monitor: model name + device
[Step 4] Chat demo (auto)  → model stays loaded, monitor shows it
[Step 5] Model unloaded    → monitor: "No models loaded"
[Step 6] Service stopped   → monitor: "Service stopped"
```

Each step has a deliberate pause so the monitor has time to poll and display
the new state before the app moves on.

## Running the sample

```powershell
cd samples/FoundryLocalChat
dotnet run
```

Override the model (must be in local cache or downloadable):

```powershell
$env:FOUNDRY_DEMO_MODEL = "phi-4-mini"
dotnet run
```

## What the Monitor should show

| Step | Monitor status | Monitor model |
|------|---------------|---------------|
| After step 2 | `Running` — `http://127.0.0.1:{port}` | — |
| After step 3 | `Running` | `qwen2.5-coder-0.5b [GPU]` |
| After step 5 | `Running` | *(empty)* |
| After step 6 | `Stopped` | — |

## Requirements

- .NET 10 SDK
- Internet access on first run (downloads model weights — skipped when cached)
- No CLI required — the SDK manages the Foundry Local daemon automatically

## Package used

`Microsoft.AI.Foundry.Local` — the cross-platform variant (same as FoundryLocalProxy).
Handles daemon, model downloads, hardware detection, and the internal REST server.
