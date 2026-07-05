# FoundryLocalChatPy

Python sample application demonstrating Foundry Local usage from Python — companion to the C# `FoundryLocalChat` sample.

## What it does

Runs an automated E2E demo visible in the Foundry Local Monitor:

| Step | Monitor shows |
|------|--------------|
| 1. SDK init | — |
| 2. Start web service on port 55589 | New endpoint discovered |
| 3. Load model via CLI | Model name + device |
| 4. Chat demo (3 questions) | Model stays loaded |
| 5. Unload model | No models loaded |
| 6. Stop service | Service stopped |

Uses port **55589** (not the default 55588) so it can run alongside `FoundryLocalChat` (C#) without a port conflict.

## Requirements

- Python 3.11+
- Foundry Local CLI installed and on `PATH`
- `pip install foundry-local-sdk openai`

## Run

```bash
# Default model (qwen2.5-coder-0.5b) + default port (55589)
python app.py

# Custom model
FOUNDRY_DEMO_MODEL=phi-4-mini python app.py

# Custom port
FOUNDRY_INTERNAL_PORT=55590 python app.py
```

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `FOUNDRY_DEMO_MODEL` | `qwen2.5-coder-0.5b` | Model alias to load |
| `FOUNDRY_INTERNAL_PORT` | `55589` | SDK internal REST server port |
| `FOUNDRY_MONITOR_WAIT` | `10` | Seconds to pause between steps |

## Architecture

```
FoundryLocalChatPy (Python)
  └─ foundry-local-sdk
       ├─ SDK web service  →  http://127.0.0.1:55589  (discovered by monitor)
       └─ Inference.Service.Agent daemon  (shared with all Foundry apps)
```

The monitor discovers port 55589 via **parallel HTTP port scan** — no manual configuration needed.
