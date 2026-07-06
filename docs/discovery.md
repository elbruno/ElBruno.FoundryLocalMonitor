# Foundry Local Monitor — Port & Service Discovery

This document explains how the monitor discovers Foundry Local instances regardless of which language, SDK, or framework an app uses, and how it groups them into per-process instance cards.

## The Problem

Foundry Local does not run on a fixed, well-known port. Every component chooses its port dynamically:

| Component | How port is chosen | Who manages it |
|---|---|---|
| `Inference.Service.Agent` daemon | Random OS-assigned at startup | Foundry CLI |
| C# SDK internal server | Configurable, default 55588 | `FoundryLocalManager` |
| Python SDK internal server | Configurable, default 55589 | `FoundryLocalManager` |
| Aspire DCP proxy | Random, assigned by Aspire | .NET Aspire |

The foundry CLI (`foundry service status`, `foundry service ps`) only sees what the CLI itself manages. Apps using the SDK directly are **invisible to the CLI**.

## The Solution: Parallel HTTP Port Scan

The monitor runs a parallel discovery scan across **all active TCP listeners on `127.0.0.1`**. Any port that responds to `GET /v1/models` with `{"object":"list"}` is a Foundry Local API endpoint.

```
Monitor startup
│
├── IPGlobalProperties.GetActiveTcpListeners()
│   Returns all listening ports on 127.0.0.1 (fast, kernel call)
│
├── For each port (in parallel, 800ms timeout each):
│   └── GET http://127.0.0.1:{port}/v1/models
│       ├── {"object":"list"} ← FOUNDRY API ✓
│       └── error / other    ← skip
│
├── For each discovered endpoint → enrich with process metadata:
│   ├── Process name (from PID lookup via netstat / GetActiveTcpListeners)
│   ├── PID
│   ├── Process path (full executable path)
│   └── Type: sdk proxy (IsProxy) or daemon (IsDaemon)
│
└── Group endpoints by PID → one card per OS process in the UI
    Discovery re-runs every 30s (catches newly launched apps)
```

### Why parallel?

A machine typically has 15–30 listening localhost ports. At 800ms timeout each, sequential scanning would take up to 24 seconds. The monitor fans out all probes simultaneously — total discovery time ≈ 800ms regardless of port count.

## Grouping by Process (PID)

When a single process listens on multiple ports (e.g. `FoundryLocalProxy` listens on both `:50184` and `:55588`), the monitor **merges them into one card** keyed on PID. This prevents duplicate cards for the same app.

```
Discovered endpoints:
  FoundryLocalProxy  PID 34096  :50184  (sdk proxy)
  FoundryLocalProxy  PID 34096  :55588  (sdk proxy)
                         ↓
  Grouped card:
  FoundryLocalProxy [PID 34096]
  Ports: :50184 · :55588
  Models: [list of models served across both ports]
```

Each model row in the card shows its **source port** (e.g. `:55588`) so you can see which endpoint reported it.

## What Gets Discovered

| Scenario | Port | Discovery path |
|---|---|---|
| CLI: `foundry model load` | Daemon port (e.g. 57284) | HTTP scan + process table |
| C# SDK: `FoundryLocalManager` | 55588 (default) | HTTP scan |
| Python SDK: `FoundryLocalManager` | 55589 (default, or configured) | HTTP scan |
| .NET Aspire proxy | e.g. 5099, 5100, 5101 | HTTP scan |
| Any OpenAI-compatible local server | Any port | HTTP scan |

## Fast-Path: Process Detection

For the foundry daemon specifically, the monitor also uses a fast process-level check:

```csharp
// Instant — no HTTP needed
var daemon = Process.GetProcessesByName("Inference.Service.Agent").FirstOrDefault();
var port   = GetListeningPortForPid(daemon.Id);  // netstat lookup
```

This is used to determine `IsServiceRunning` and fires immediately when the daemon starts or stops, without waiting for the next HTTP probe.

## Poll Cycle

```
Every 3-5 seconds (configurable):
  1. IsDaemonRunning? (process table, ~1ms)
  2. If daemon alive and no endpoints cached → trigger immediate rediscovery
  3. If discovery stale (>30s) → trigger background rediscovery
  4. Query /v1/models on each cached endpoint (parallel, 3s timeout)
  5. Merge model lists — keyed on (port, ModelId) to avoid cross-endpoint duplicates
  6. Group models by PID → update Loaded Models tab cards
  7. Emit events for model load/unload changes (with IsSilent flag for SDK-internal models)

Every 30 seconds:
  1. Rescan all 127.0.0.1 listeners (parallel, 800ms timeout)
  2. Update cached endpoint list
  3. Newly appeared endpoints start being polled in the next fast cycle
  4. Disappeared endpoints are removed from the cache
```

## Model Sources and Device Detection

Models are fetched from each discovered endpoint via one of two APIs:

| Endpoint type | API called | What it returns |
|---|---|---|
| SDK proxy (`IsProxy = true`) | `GET /v1/models` | All models the SDK instance knows about |
| Daemon (`IsDaemon = true`) | `GET /openai/loadedmodels` | Models currently in memory |

### Device parsing

Model IDs from the API encode the device backend as a suffix, e.g.:
`Phi-4-mini-instruct-cuda-gpu:5`

The monitor strips the version suffix (`:5`) and extracts the device type:

| Suffix | Device label | Badge colour |
|---|---|---|
| `-cuda-gpu` | `CUDA` | 🟢 green |
| `-trtrtx-gpu` | `TensorRT` | 🟩 emerald |
| `-generic-gpu` / `-gpu` | `GPU` | 🟢 green |
| `-generic-cpu` / `-cpu` | `CPU` | 🔵 blue |
| `-winml-directml` / `-directml-gpu` | `DirectML` | 🟣 purple |
| `-winml-cpu` | `WinML` | 🟠 orange |
| *(none matched)* | `?` | ⬜ gray |

The friendly alias (e.g. `Phi-4-mini-instruct`) is shown in the UI; the full ModelId with device suffix is shown in the tooltip.

## Smart Notifications

The monitor emits toast notifications for model load/unload events. To avoid noise from SDK-internal background activity, events discovered on SDK proxy ports (55588, 55589) are marked **silent** by default. Only events from external apps or the daemon trigger visible notifications.

This behaviour is configurable in Settings.

## Multiple Apps Simultaneously

When multiple apps use Foundry Local at the same time, each has its own SDK internal server at its own port. The monitor discovers all of them and shows a separate card per process:

```
App A (C#)   → SDK server at :55588  [PID 1234]  → card: AppA [PID 1234]
App B (Py)   → SDK server at :55589  [PID 5678]  → card: AppB [PID 5678]
Aspire proxy → DCP proxy at  :5101   [PID 9012]  → card: FoundryProxy [PID 9012]
Daemon       → agent at      :57284  [PID 21180] → card: Inference.Service.Agent [PID 21180]
```

Each card is independent. Models shown in the SDK proxy cards reflect that process's registered model catalog; models shown in the daemon card reflect what is actually in GPU/CPU memory.

## Implementing Discovery in Your App

If you build a tool that monitors Foundry Local, here's the pattern:

```python
# Python: scan localhost for Foundry API endpoints
import socket, concurrent.futures, requests

def probe_port(port):
    try:
        r = requests.get(f"http://127.0.0.1:{port}/v1/models", timeout=0.8)
        if r.json().get("object") == "list":
            return port
    except:
        pass
    return None

def discover_foundry_ports():
    # Get all listening ports (platform-specific)
    import subprocess, re
    out = subprocess.check_output(["netstat", "-ano"], text=True)
    ports = set(int(m) for m in re.findall(r"127\.0\.0\.1:(\d+).*LISTENING", out))
    with concurrent.futures.ThreadPoolExecutor(max_workers=50) as ex:
        return [p for p in ex.map(probe_port, ports) if p]
```

```csharp
// C#: same approach using IPGlobalProperties + HttpClient
var props   = IPGlobalProperties.GetIPGlobalProperties();
var ports   = props.GetActiveTcpListeners()
                   .Where(ep => ep.Address == IPAddress.Loopback)
                   .Select(ep => ep.Port);
var tasks   = ports.Select(p => ProbePortAsync(p));
var results = await Task.WhenAll(tasks);
var foundry = results.Where(r => r != null);
```

## Configuration

The monitor respects a `FoundryEndpointOverride` setting (in Settings window) that pins it to a specific endpoint — useful for production scenarios where you know the exact port. When set, discovery still runs but the override endpoint is always included.
