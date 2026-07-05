#!/usr/bin/env python3
"""
FoundryLocalChatPy — Python E2E test client for Foundry Local Monitor
======================================================================

PURPOSE
-------
Demonstrates using Foundry Local from Python via the foundry-local-sdk.
Runs an automated demo that generates monitor-visible events:
  service start → model load → chat → model unload → service stop

Uses the SDK's web service (http://127.0.0.1:55589 by default) so the
Foundry Local Monitor's parallel port scanner can discover it alongside
any other running Foundry apps.

Uses port 55589 (not the default 55588) to allow running alongside the
C# FoundryLocalChat sample without a port conflict.

USAGE
-----
    python app.py                          # default model + port
    FOUNDRY_DEMO_MODEL=phi-4-mini python app.py
    FOUNDRY_INTERNAL_PORT=55590 python app.py

DEPENDENCIES
------------
    pip install foundry-local-sdk openai
"""

import os
import sys
import subprocess
import time
import json
import urllib.request
import urllib.error

# ── Configuration ─────────────────────────────────────────────────────────────
MODEL_ALIAS    = os.environ.get("FOUNDRY_DEMO_MODEL", "qwen2.5-coder-0.5b")
INTERNAL_PORT  = int(os.environ.get("FOUNDRY_INTERNAL_PORT", "55589"))
INTERNAL_URL   = f"http://127.0.0.1:{INTERNAL_PORT}"
MONITOR_WAIT   = int(os.environ.get("FOUNDRY_MONITOR_WAIT", "10"))   # seconds

TOTAL_STEPS = 6

# ── Helpers ───────────────────────────────────────────────────────────────────
def banner(title: str) -> None:
    bar = "=" * (len(title) + 4)
    print(bar)
    print(f"  {title}")
    print(bar)
    print()

def step(n: int, msg: str) -> None:
    print(f"[{n}/{TOTAL_STEPS}] {msg}")

def ok(msg: str) -> None:
    print(f"  [OK] {msg}")

def hint(msg: str) -> None:
    print(f"  --> {msg}")

def err(msg: str) -> None:
    print(f"  [ERROR] {msg}", file=sys.stderr)

def run_cli(args: list[str], timeout: int = 30) -> tuple[int, str]:
    """Run a CLI command, return (returncode, stdout+stderr)."""
    try:
        result = subprocess.run(
            args,
            capture_output=True, text=True,
            timeout=timeout, check=False
        )
        return result.returncode, (result.stdout + result.stderr).strip()
    except subprocess.TimeoutExpired:
        return -1, "timeout"
    except FileNotFoundError:
        return -1, f"command not found: {args[0]}"

def poll_models(endpoint: str, timeout_s: int = 120) -> str | None:
    """Poll GET /v1/models until a model appears. Returns first model id or None."""
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        remaining = int(deadline - time.monotonic())
        try:
            req = urllib.request.Request(f"{endpoint}/v1/models")
            with urllib.request.urlopen(req, timeout=5) as resp:
                data = json.loads(resp.read())
                models = data.get("data", [])
                if models:
                    return models[0]["id"]
        except Exception:
            pass
        print(f"\r  Polling... {remaining}s remaining   ", end="", flush=True)
        time.sleep(3)
    print()
    return None

# ── Main ──────────────────────────────────────────────────────────────────────
def main() -> None:
    banner("Foundry Local Chat Py -- E2E Monitor Test (Python)")

    # ── STEP 1: Init SDK ───────────────────────────────────────────────────────
    step(1, "Initializing Foundry Local SDK (Python)...")
    from foundry_local_sdk import FoundryLocalManager, Configuration

    config = Configuration(
        app_name="FoundryLocalChatPy",
        web=Configuration.WebService(urls=INTERNAL_URL),
    )
    FoundryLocalManager.initialize(config)
    mgr = FoundryLocalManager.instance
    ok("SDK initialized")

    # ── STEP 2: Start web service ──────────────────────────────────────────────
    step(2, f"Starting Foundry Local web service on port {INTERNAL_PORT}...")
    mgr.start_web_service()
    ok(f"Service running at {INTERNAL_URL}")
    hint("-> Monitor should detect a new Foundry API endpoint.")
    print(f"  Waiting {MONITOR_WAIT}s for monitor to discover the service...")
    time.sleep(MONITOR_WAIT)

    # ── STEP 3: Load model via CLI ─────────────────────────────────────────────
    # Use CLI to load the model; avoids triggering GPU EP package download
    # (DownloadAndRegisterEps can take 10-22 min on first run).
    step(3, f"Loading model '{MODEL_ALIAS}' via CLI...")
    print(f"  Running: foundry model load {MODEL_ALIAS}")
    code, out = run_cli(["foundry", "model", "load", MODEL_ALIAS], timeout=1800)
    if out:
        for line in out.splitlines():
            print(f"  {line}")

    print("  Polling /v1/models for loaded model...")
    model_id = poll_models(INTERNAL_URL, timeout_s=120)
    if model_id is None:
        err("Model never appeared. Stopping.")
        run_cli(["foundry", "service", "stop"], timeout=15)
        try:
            mgr.stop_web_service()
        except Exception:
            pass
        sys.exit(1)
    ok(f"Model loaded — id: {model_id}")
    hint("-> Monitor should show the model name and device.")
    print(f"  Waiting {MONITOR_WAIT}s for monitor to display the loaded model...")
    time.sleep(MONITOR_WAIT)

    # ── STEP 4: Chat via REST ──────────────────────────────────────────────────
    step(4, "Running chat demo via REST...")

    questions = [
        "What is Python used for in one sentence?",
        "Name two Python AI/ML frameworks.",
        "Why is Python popular for data science?",
    ]
    messages: list[dict] = [
        {"role": "system", "content": "You are a helpful assistant. Answer in one or two sentences."}
    ]

    for i, question in enumerate(questions):
        print(f"\n  Q{i + 1}: {question}")
        print("  A:  ", end="", flush=True)
        messages.append({"role": "user", "content": question})

        payload = json.dumps({
            "model": model_id,
            "messages": messages,
            "max_tokens": 150,
            "stream": False,
        }).encode()
        try:
            req = urllib.request.Request(
                f"{INTERNAL_URL}/v1/chat/completions",
                data=payload,
                headers={"Content-Type": "application/json"},
            )
            with urllib.request.urlopen(req, timeout=30) as resp:
                response_data = json.loads(resp.read())
                reply = response_data["choices"][0]["message"]["content"]
                print(reply)
                messages.append({"role": "assistant", "content": reply})
        except Exception as ex:
            print(f"[chat error] {ex}")

    ok("Chat demo complete")
    time.sleep(3)

    # ── STEP 5: Unload model ───────────────────────────────────────────────────
    step(5, f"Unloading model '{MODEL_ALIAS}'...")
    run_cli(["foundry", "model", "unload", MODEL_ALIAS], timeout=15)
    ok("Model unloaded")
    hint("-> Monitor should show: no models loaded.")
    print(f"  Waiting {MONITOR_WAIT}s for monitor to detect the unload...")
    time.sleep(MONITOR_WAIT)

    # ── STEP 6: Stop service ───────────────────────────────────────────────────
    step(6, "Stopping service...")
    run_cli(["foundry", "service", "stop"], timeout=15)
    ok("Service stopped (CLI)")
    time.sleep(2)
    try:
        mgr.stop_web_service()
    except Exception:
        pass
    hint("-> Monitor should show: service stopped.")

    print()
    print("  E2E demo complete! (Python)")
    print()


if __name__ == "__main__":
    main()
