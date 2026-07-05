# FoundryLocalChat — E2E Monitor Test Sample

A minimal C# console app that uses the **Foundry Local SDK** to start a local AI service, load a model, and run an interactive chat. Use it to verify the **Foundry Local Monitor** detects the service and model correctly.

## How it works

1. **Initializes** `FoundryLocalManager` (the SDK singleton)
2. **Starts the web server** via `mgr.StartWebServerAsync()` → exposes an OpenAI-compatible HTTP endpoint
3. **Loads `phi-3.5-mini`** (downloads it first if not cached)
4. **Runs an interactive chat loop** — type messages, get streamed responses
5. **Shuts down cleanly** on empty input or Ctrl+C

## Running the sample

```powershell
cd samples/FoundryLocalChat
dotnet run
```

## What the Monitor should show

Once the app reaches step 2 you should see in **Foundry Local Monitor**:

| Field    | Value                              |
|----------|------------------------------------|
| Status   | Running                            |
| Endpoint | `http://127.0.0.1:<port>`          |
| Model    | phi-3.5-mini (loaded)              |

This confirms the monitor's endpoint URL parsing and model detection work end-to-end.

## Requirements

- Windows 10 (26100+) for WinML hardware acceleration
- .NET 10 SDK
- Internet access (first run downloads the model ~1–4 GB depending on variant)

## Changing the model

Edit `Program.cs` line:
```csharp
const string ModelAlias = "phi-3.5-mini";
```

Common alternatives: `phi-4-mini`, `phi-3-mini-4k`, `mistral-7b`.
Run `foundry model list` to see all available models.
