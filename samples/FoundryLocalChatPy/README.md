# FoundryLocalChatPy

Python sample application demonstrating Foundry Local usage from Python — companion to the C# `FoundryLocalChat` sample.

## What it does

Runs an automated E2E demo visible in the Foundry Local Monitor:

| Step | Monitor shows |
|------|--------------|
| 1. SDK init | — |
| 2. Start web service on port 55589 | New instance card in **Status** tab (`sdk proxy` badge) |
| 3. Load model via CLI | Model row in **Loaded Models** card with device badge and source port |
| 4. Chat demo (3 questions) | Model stays loaded |
| 5. Unload model | Model row disappears from card |
| 6. Stop service | Instance card removed from Status tab |

### Loaded Models card (while model is loaded)

```
FoundryLocalChatPy  [PID NNNN]                               sdk proxy
Ports: :55589
📂 C:\path\to\app.py
────────────────────────────────────────────────────────
[CPU]  qwen2.5-coder-0.5b                              :55589
```

The device badge (`CPU`, `CUDA`, `DirectML`, etc.) is determined automatically from the model's
hardware suffix. The source port (`:55589`) identifies which endpoint reported this model.

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
When both `FoundryLocalChat` (C#, port 55588) and this app run simultaneously, the monitor shows
**two separate instance cards** — one per PID — each with their own model list.
