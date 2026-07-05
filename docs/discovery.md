# Foundry Local Monitor — Port & Service Discovery

This document explains how the monitor discovers Foundry Local instances regardless of which language, SDK, or framework an app uses.

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
└── Discovered endpoints cached + polled every 3-5s
    Discovery re-runs every 30s (catches newly launched apps)
```

### Why parallel?

A machine typically has 15–30 listening localhost ports. At 800ms timeout each, sequential scanning would take up to 24 seconds. The monitor fans out all probes simultaneously — total discovery time ≈ 800ms regardless of port count.

## What Gets Discovered

| Scenario | Port | Discovery path |
|---|---|---|
| CLI: `foundry model load` | Daemon port (e.g. 62652) | HTTP scan OR `Inference.Service.Agent` process |
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
  5. Merge model lists (union by ModelId)
  6. Emit events for model load/unload changes

Every 30 seconds:
  1. Rescan all 127.0.0.1 listeners (parallel, 800ms timeout)
  2. Update cached endpoint list
  3. Newly appeared endpoints start being polled in the next fast cycle
  4. Disappeared endpoints are removed from the cache
```

## Model Sources

Models are merged from three sources in each poll:

| Source | API | What it shows |
|---|---|---|
| Daemon `/v1/models` | `GET http://127.0.0.1:{daemonPort}/v1/models` | CLI-loaded models |
| SDK internal `/v1/models` | `GET http://127.0.0.1:{sdkPort}/v1/models` | SDK-managed models |
| `foundry service ps` | CLI subprocess | CLI-loaded models (cross-reference) |

All sources are merged by `ModelId` (case-insensitive) to avoid duplicates.

## Multiple Apps Simultaneously

When multiple apps use Foundry Local at the same time, each has its own SDK internal server at its own port. The monitor discovers all of them:

```
App A (C#)   → SDK server at :55588 → /v1/models → [model-a]
App B (Py)   → SDK server at :55589 → /v1/models → [model-b]
Aspire proxy → DCP proxy at  :5101  → /v1/models → [model-a, model-b]
                                                       ↑
                                              Monitor merges all
                                              into one unified list
```

The shared foundry daemon (`Inference.Service.Agent`) handles actual inference for all apps. Each SDK instance has its own catalog view of which models it has registered.

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
