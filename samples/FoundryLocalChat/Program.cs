using Microsoft.AI.Foundry.Local;
using Microsoft.Extensions.Logging.Abstractions;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string ModelAlias = "phi-3.5-mini";

Console.WriteLine("╔══════════════════════════════════════════════════╗");
Console.WriteLine("║       Foundry Local Chat — E2E Monitor Test      ║");
Console.WriteLine("╚══════════════════════════════════════════════════╝");
Console.WriteLine();

// ── 1. Initialize the Foundry Local Manager ──────────────────────────────────
Console.WriteLine("[1/4] Initializing Foundry Local Manager…");
await FoundryLocalManager.CreateAsync(
    new Configuration { AppName = "FoundryLocalChat" },
    NullLogger.Instance);

var mgr = FoundryLocalManager.Instance;

// ── 2. Start the optional web server ─────────────────────────────────────────
//      This makes the service visible to the Foundry Local Monitor via HTTP.
Console.WriteLine("[2/4] Starting web server…");
await mgr.StartWebServiceAsync();
var urls = mgr.Urls ?? [];
var endpoint = urls.Length > 0 ? urls[0] : "(unknown)";
Console.WriteLine($"      ✔ Service running at: {endpoint}");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"  → Open Foundry Local Monitor — it should now show: {endpoint}");
Console.ResetColor();
Console.WriteLine();

// ── 3. Load a model ───────────────────────────────────────────────────────────
Console.WriteLine($"[3/4] Loading model '{ModelAlias}'…");

var catalog = await mgr.GetCatalogAsync();
var model = await catalog.GetModelAsync(ModelAlias)
    ?? throw new Exception($"Model '{ModelAlias}' not found in catalog. Run: foundry model list");

// Download if not cached
var lastPct = -1;
Console.Write("      Downloading (if needed)… ");
await model.DownloadAsync(pct =>
{
    var pctInt = (int)pct;
    if (pctInt != lastPct) { lastPct = pctInt; Console.Write($"\r      Downloading… {pct:F0}%  "); }
});
Console.WriteLine("\r      ✔ Downloaded.                           ");

await model.LoadAsync();
Console.WriteLine($"      ✔ Model '{model.Alias}' ({model.Id}) loaded");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  → Foundry Local Monitor should now list this model as loaded.");
Console.ResetColor();
Console.WriteLine();

// ── 4. Interactive chat loop ──────────────────────────────────────────────────
Console.WriteLine("[4/4] Starting chat (press Enter on an empty line to quit, Ctrl+C to force exit)");
Console.WriteLine(new string('-', 52));

var chatClient = await model.GetChatClientAsync();
var history = new List<ChatMessage>
{
    new() { Role = "system", Content = "You are a helpful assistant. Keep answers concise." }
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.IsCancellationRequested)
{
    Console.Write("\nYou: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        break;

    history.Add(new ChatMessage { Role = "user", Content = input });

    Console.Write("AI:  ");
    var assistantReply = new System.Text.StringBuilder();
    try
    {
        await foreach (var chunk in chatClient.CompleteChatStreamingAsync(history, cts.Token))
        {
            var delta = chunk.Choices?[0]?.Delta?.Content;
            if (delta != null) { Console.Write(delta); assistantReply.Append(delta); }
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }
    Console.WriteLine();

    // Append assistant turn for multi-turn context
    history.Add(new ChatMessage { Role = "assistant", Content = assistantReply.ToString() });
}

// ── 5. Cleanup ────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("Shutting down…");
await model.UnloadAsync();
await mgr.StopWebServiceAsync();
mgr.Dispose();
Console.WriteLine("Done. Goodbye!");
