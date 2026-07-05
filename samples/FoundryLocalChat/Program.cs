// =============================================================================
//  FoundryLocalChat — E2E test client for Foundry Local Monitor
//
//  PURPOSE
//  -------
//  This app simulates the same scenario as the FoundryLocalProxy
//  (https://github.com/elbruno/ElBruno.CopilotHarness/tree/main/src/proxies/FoundryLocalProxy)
//  which is a GitHub Copilot BYOK proxy backed by Foundry Local.
//
//  It runs an AUTOMATED demo so the Foundry Local Monitor can detect and display
//  each state transition in real-time. No user input required — just launch it
//  and watch the monitor.
//
//  E2E TEST FLOW
//  -------------
//  1. SDK init             → monitor should show: "Checking…"
//  2. Web service started  → monitor should show: service URL  (http://127.0.0.1:{port})
//  3. Model loaded         → monitor should show: model name + device
//  4. Chat requests sent   → model stays loaded, monitor keeps showing it
//  5. Model unloaded       → monitor should show: "No models loaded"
//  6. Service stopped      → monitor should show: "Service stopped"
//
//  Uses Microsoft.AI.Foundry.Local (cross-platform) — same as FoundryLocalProxy.
//  Chat calls go directly to the SDK REST server via HttpClient, mirroring
//  exactly what the proxy does on the inside.
//  No CLI required; the SDK manages the Foundry Local daemon automatically.
// =============================================================================

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Prefer the model already in cache (detected from live probing).
// Override via env: $env:FOUNDRY_DEMO_MODEL = "phi-4-mini"
const string DefaultModel = "qwen2.5-coder-0.5b";
var modelAlias = Environment.GetEnvironmentVariable("FOUNDRY_DEMO_MODEL") ?? DefaultModel;

// Use NullLogger — the SDK logs nothing to console; we print our own steps.
var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

Banner("Foundry Local Chat — E2E Monitor Test");

// ── STEP 1: Init SDK ─────────────────────────────────────────────────────────
Step(1, 6, "Initializing Foundry Local SDK…");
await FoundryLocalManager.CreateAsync(new Configuration { AppName = "FoundryLocalChat" }, logger);
var mgr = FoundryLocalManager.Instance;
Ok("SDK initialized");

// ── STEP 2: Start web service ─────────────────────────────────────────────────
Step(2, 6, "Starting Foundry Local web service…");
await mgr.StartWebServiceAsync();
var endpoint = mgr.Urls?.FirstOrDefault() ?? "(unknown)";
Ok($"Service running at {endpoint}");

Hint("→ Foundry Local Monitor should now show the service URL.");
await Pause(10, "Giving the monitor time to detect the service");

// ── STEP 3: Load model ────────────────────────────────────────────────────────
Step(3, 6, $"Loading model '{modelAlias}'…");
var catalog = await mgr.GetCatalogAsync();
var model = await catalog.GetModelAsync(modelAlias);
if (model is null)
{
    Warn($"Model '{modelAlias}' not found. Available models:");
    var all = await catalog.ListModelsAsync();
    foreach (var m in all.Take(10)) Console.WriteLine($"  • {m.Alias}");
    Console.WriteLine($"\nSet $env:FOUNDRY_DEMO_MODEL=<alias> and re-run.");
    await mgr.StopWebServiceAsync(); mgr.Dispose(); return;
}

var lastPct = -1;
Console.Write("  Downloading (if needed)…  ");
await model.DownloadAsync(pct =>
{
    var p = (int)pct;
    if (p != lastPct) { lastPct = p; Console.Write($"\r  Downloading…  {p,3}%  "); }
});
Console.WriteLine("\r  ✔ Model cached.                          ");

await model.LoadAsync();
Ok($"Model '{model.Alias}' ({model.Id}) loaded");

Hint("→ Foundry Local Monitor should now show the model name and device.");
await Pause(10, "Giving the monitor time to detect the loaded model");

// ── STEP 4: Automated chat via REST ──────────────────────────────────────────
//   Mirror how FoundryLocalProxy works: call the SDK's REST server directly
//   via HttpClient on the OpenAI-compatible endpoint.
Step(4, 6, "Running automated chat via REST (same as FoundryLocalProxy)…");

using var http = new HttpClient { BaseAddress = new Uri(endpoint) };

string[] questions = [
    "What is Microsoft Foundry Local in one sentence?",
    "Name three popular open-weight LLMs supported by Foundry Local.",
    "What does BYOK mean in the context of GitHub Copilot?"
];

var messages = new List<object>
{
    new { role = "system", content = "You are a helpful assistant. Answer in one or two sentences." }
};

foreach (var (q, i) in questions.Select((q, i) => (q, i)))
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n  Q{i + 1}: {q}");
    Console.ResetColor();
    Console.Write("  A:  ");

    messages.Add(new { role = "user", content = q });

    var body = new
    {
        model = model.Id,
        messages,
        stream = true,
        max_tokens = 200
    };

    var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
    {
        Content = JsonContent.Create(body)
    };

    var reply = new System.Text.StringBuilder();
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new System.IO.StreamReader(stream);

    while (!reader.EndOfStream)
    {
        var line = await reader.ReadLineAsync();
        if (line is null || !line.StartsWith("data: ")) continue;
        var json = line["data: ".Length..];
        if (json == "[DONE]") break;

        try
        {
            var doc = JsonDocument.Parse(json);
            var delta = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta")
                .GetProperty("content")
                .GetString();
            if (delta is not null) { Console.Write(delta); reply.Append(delta); }
        }
        catch { /* skip malformed SSE chunks */ }
    }

    Console.WriteLine();
    messages.Add(new { role = "assistant", content = reply.ToString() });
    await Task.Delay(500);
}

Ok("Chat demo complete");
await Pause(5, "Pause before unloading");

// ── STEP 5: Unload model ──────────────────────────────────────────────────────
Step(5, 6, $"Unloading model '{model.Alias}'…");
await model.UnloadAsync();
Ok("Model unloaded");

Hint("→ Foundry Local Monitor should now show: no models loaded.");
await Pause(8, "Giving the monitor time to detect the unload");

// ── STEP 6: Stop service ──────────────────────────────────────────────────────
Step(6, 6, "Stopping web service…");
await mgr.StopWebServiceAsync();
mgr.Dispose();
Ok("Service stopped");

Hint("→ Foundry Local Monitor should now show: service stopped.");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  ✅  E2E demo complete! Check the Foundry Local Monitor for the full trace.");
Console.ResetColor();
Console.WriteLine();

// ── Helpers ───────────────────────────────────────────────────────────────────
static void Banner(string title)
{
    var border = new string('═', title.Length + 4);
    Console.WriteLine($"╔{border}╗");
    Console.WriteLine($"║  {title}  ║");
    Console.WriteLine($"╚{border}╝");
    Console.WriteLine();
}

static void Step(int n, int total, string msg)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"[{n}/{total}] {msg}");
    Console.ResetColor();
}

static void Ok(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  ✔ {msg}");
    Console.ResetColor();
}

static void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  ⚠ {msg}");
    Console.ResetColor();
}

static void Hint(string msg)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  {msg}");
    Console.ResetColor();
    Console.WriteLine();
}

static async Task Pause(int seconds, string reason)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    for (var i = seconds; i > 0; i--)
    {
        Console.Write($"\r  ⏳ {reason}… ({i}s)  ");
        await Task.Delay(1000);
    }
    Console.WriteLine($"\r  ⏳ {reason}… done          ");
    Console.ResetColor();
}


