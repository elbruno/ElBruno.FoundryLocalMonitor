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

using System.Diagnostics;
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

Banner("Foundry Local Chat -- E2E Monitor Test");

try
{

// ── STEP 1: Init SDK ─────────────────────────────────────────────────────────
Step(1, 6, "Initializing Foundry Local SDK...");

// The Configuration.Web.Urls tells the SDK which address to bind the internal
// OpenAI-compatible REST server to. Without it, StartWebServiceAsync throws.
// Using port 0 lets the OS pick a free port (same as FoundryLocalProxy default).
const string InternalUrl = "http://127.0.0.1:55588";
var sdkConfig = new Configuration
{
    AppName = "FoundryLocalChat",
    Web     = new Configuration.WebService { Urls = InternalUrl }
};
await FoundryLocalManager.CreateAsync(sdkConfig, logger);
var mgr = FoundryLocalManager.Instance;
Ok("SDK initialized");

// ── STEP 2: Start web service ─────────────────────────────────────────────────
Step(2, 6, "Starting Foundry Local web service...");
await mgr.StartWebServiceAsync();
var endpoint = mgr.Urls?.FirstOrDefault() ?? "(unknown)";
Ok($"Service running at {endpoint}");

Hint("-> Monitor should now show the service URL.");
await Pause(10, "Giving the monitor time to detect the service");

// ── STEP 3: Load model via CLI ────────────────────────────────────────────────
// We use the CLI instead of the SDK's DownloadAsync+LoadAsync because the SDK
// triggers GPU EP (CUDA/TensorRT) package registration on every invocation,
// which can take 10-22 minutes. The CLI uses cached state and loads in ~30s.
Step(3, 6, $"Loading model '{modelAlias}' via CLI...");
Console.WriteLine($"  Running: foundry model load {modelAlias}");
RunCli("foundry", $"model load {modelAlias}", timeoutMs: 1_800_000);  // 30 min max
Console.WriteLine("  CLI model load command completed.");

// Poll /v1/models until the model appears (up to 30 minutes — EP autoregistration
// can take 10-22 minutes on first run; cached runs are faster).
Console.WriteLine("  Polling /v1/models for loaded model...");
string? modelId = null;
var pollDeadline = DateTime.UtcNow.AddSeconds(1800);
while (DateTime.UtcNow < pollDeadline)
{
    try
    {
        // Re-read the current endpoint each poll.
        // Use a direct process-table lookup to avoid the slow 'foundry service status'
        // CLI call (which can block 5-10s when EP autoregistration is running).
        var inferenceProc = System.Diagnostics.Process.GetProcessesByName("Inference.Service.Agent")
                                .FirstOrDefault();
        if (inferenceProc is not null)
        {
            // Use netstat to find the listening port for this PID (Windows).
            var netOut = RunCliCapture("netstat", $"-ano", timeoutMs: 3_000);
            var pidStr = inferenceProc.Id.ToString();
            foreach (var line in netOut.Split('\n'))
            {
                if (!line.Contains(pidStr)) continue;
                if (!line.Contains("LISTENING")) continue;
                var parts = line.Split(new char[]{' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1].StartsWith("127.0.0.1:"))
                {
                    endpoint = $"http://{parts[1]}";
                    break;
                }
            }
        }

        using var hc = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(5) };
        var json = await hc.GetStringAsync("/v1/models");
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        if (data.GetArrayLength() > 0)
        {
            modelId = data[0].GetProperty("id").GetString();
            Ok($"Model loaded — id: {modelId}");
            break;
        }
    }
    catch { }
    Console.Write($"\r  Still waiting... {(int)(pollDeadline - DateTime.UtcNow).TotalSeconds}s remaining   ");
    await Task.Delay(3000);
}
Console.WriteLine();

if (modelId is null)
{
    Console.Error.WriteLine("  [ERROR] Model never appeared in /v1/models. Stopping.");
    RunCli("foundry", "service stop", timeoutMs: 15_000);
    await mgr.StopWebServiceAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    mgr.Dispose();
    Environment.Exit(1);
    return;
}

Hint("-> Monitor should now show the model name and device.");
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
    var reply = new System.Text.StringBuilder();

    try
    {
        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new { model = modelId, messages, stream = true, max_tokens = 200 })
        };
        using var response = await http.SendAsync(chatRequest, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (!line.StartsWith("data: ")) continue;
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
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[chat error] {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine();
    messages.Add(new { role = "assistant", content = reply.ToString() });
    await Task.Delay(500);
}

Ok("Chat demo complete");
await Pause(5, "Pause before unloading");

// ── STEP 5: Unload model ──────────────────────────────────────────────────────
Step(5, 6, $"Unloading model '{modelAlias}'...");

// CLI unload removes the model from the daemon's active list.
RunCli("foundry", $"model unload {modelAlias}", timeoutMs: 15_000);
Ok("Model unloaded (CLI)");

Hint("-> Monitor should now show: no models loaded.");
await Pause(8, "Giving the monitor time to detect the unload");

// ── STEP 6: Stop service ──────────────────────────────────────────────────────
Step(6, 6, "Stopping web service...");

// CLI stop FIRST to ensure the daemon actually exits before SDK tries to
// disconnect — calling StopWebServiceAsync() on an already-stopped daemon can
// hang indefinitely waiting for a response that never comes.
RunCli("foundry", "service stop");
Ok("Service stopped (CLI)");
await Task.Delay(2000);

try { await mgr.StopWebServiceAsync().WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
mgr.Dispose();

Hint("-> Monitor should now show: service stopped.");
Console.WriteLine();
Console.WriteLine("  E2E demo complete!");
Console.WriteLine();

} // end global try
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] {ex.GetType().FullName}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Environment.Exit(1);
}

// ── Helpers ───────────────────────────────────────────────────────────────────
static void Banner(string title)
{
    var bar = new string('=', title.Length + 4);
    Console.WriteLine(bar);
    Console.WriteLine($"  {title}");
    Console.WriteLine(bar);
    Console.WriteLine();
}

static void Step(int n, int total, string msg)
{
    Console.WriteLine($"[{n}/{total}] {msg}");
}

static void Ok(string msg)
{
    Console.WriteLine($"  [OK] {msg}");
}

static void Hint(string msg) => Console.WriteLine($"  --> {msg}");

static void RunCli(string exe, string args, int timeoutMs = 15_000)
{
    try
    {
        var psi = new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi);
        p?.WaitForExit(timeoutMs);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[cli warn] {exe} {args}: {ex.Message}");
    }
}

static string RunCliCapture(string exe, string args, int timeoutMs = 10_000)
{
    try
    {
        var psi = new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var sb = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit(timeoutMs);
        return sb.ToString();
    }
    catch { return string.Empty; }
}

static async Task Pause(int seconds, string reason)
{
    for (var i = seconds; i > 0; i--)
    {
        Console.Write($"\r  Waiting {i}s: {reason}  ");
        await Task.Delay(1000);
    }
    Console.WriteLine($"\r  Done: {reason}              ");
}


