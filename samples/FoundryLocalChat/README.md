# FoundryLocalChat — E2E Monitor Test Client

A C# console app that simulates the same scenario as the
[FoundryLocalProxy](https://github.com/elbruno/ElBruno.CopilotHarness/tree/main/src/proxies/FoundryLocalProxy)
(a GitHub Copilot BYOK proxy backed by Foundry Local), but as an automated demo.

Use it to verify the **Foundry Local Monitor** detects every state transition
in real-time without any user input.

## E2E Test Flow

```
[Step 1] SDK init          → monitor: "Checking…"
[Step 2] Web service start → monitor: new instance card in Status tab (http://127.0.0.1:{port})
[Step 3] Model loaded      → monitor: model row appears in Loaded Models tab with device badge
[Step 4] Chat demo (auto)  → model stays loaded, monitor shows it
[Step 5] Model unloaded    → monitor: model row disappears from card
[Step 6] Service stopped   → monitor: instance card removed from Status tab
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

### Status tab

| Step | Instance card | Badge |
|------|--------------|-------|
| After step 2 | `http://127.0.0.1:{port}` — Process: your-app, PID: N | `sdk proxy` |
| After step 6 | Card disappears | — |

### Loaded Models tab

| Step | Card | Model row |
|------|------|-----------|
| After step 3 | `your-app [PID N]` · Ports: :{port} | `[CUDA]` or `[CPU]` · `qwen2.5-coder-0.5b` · `:55588` |
| After step 5 | Card still visible (process running) | *(empty — model unloaded)* |
| After step 6 | Card disappears | — |

The source port in the model row (e.g. `:55588`) identifies which endpoint reported the model.
Hovering the model row shows the full `ModelId` with device suffix in the tooltip.

## Requirements

- .NET 10 SDK
- Internet access on first run (downloads model weights — skipped when cached)
- No CLI required — the SDK manages the Foundry Local daemon automatically

## Package used

`Microsoft.AI.Foundry.Local` — the cross-platform variant (same as FoundryLocalProxy).
Handles daemon, model downloads, hardware detection, and the internal REST server.
