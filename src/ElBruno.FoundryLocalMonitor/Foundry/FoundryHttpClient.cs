using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElBruno.FoundryLocalMonitor.Models;
using Microsoft.Extensions.Logging;

namespace ElBruno.FoundryLocalMonitor.Foundry;

public class FoundryHttpClient
{
    private readonly HttpClient _http;
    private readonly ILogger<FoundryHttpClient>? _logger;
    private string _baseUrl = "http://localhost:5273";

    // Ports Foundry Local is known to use (dynamic, but these cover common cases)
    private static readonly int[] KnownPorts = [5273, 5101, 5102, 5103, 5000, 5001, 11434, 8080];

    public FoundryHttpClient(HttpClient http, ILogger<FoundryHttpClient>? logger = null)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(5);
        _logger = logger;
    }

    public void SetBaseUrl(string url) => _baseUrl = url.TrimEnd('/');
    public string CurrentBaseUrl => _baseUrl;

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        // Try the currently configured URL first (fast path)
        if (await TryUrlAsync(_baseUrl, ct)) return true;

        // Scan other known ports in parallel
        var found = await ScanPortsAsync(ct);
        if (found != null)
        {
            _baseUrl = found;
            _logger?.LogInformation("Foundry discovered at {Url}", found);
            return true;
        }

        return false;
    }

    private async Task<bool> TryUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var response = await _http.GetAsync($"{url}/v1/models", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Scans known ports in parallel; returns the first URL that responds.</summary>
    private async Task<string?> ScanPortsAsync(CancellationToken ct)
    {
        var tasks = KnownPorts
            .Select(port => $"http://localhost:{port}")
            .Where(url => url != _baseUrl)
            .Select(async url =>
            {
                var ok = await TryUrlAsync(url, ct);
                return ok ? url : null;
            })
            .ToList();

        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            var result = await done;
            if (result != null) return result;
        }

        return null;
    }

    public async Task<IReadOnlyList<FoundryModel>> GetLoadedModelsAsync(CancellationToken ct = default)
    {
        // /v1/models returns ONLY loaded/registered models (data:[] when nothing loaded).
        // /foundry/list returns ALL catalog models (156 KB+) — do NOT use for loaded detection.
        try
        {
            var json = await _http.GetStringAsync($"{_baseUrl}/v1/models", ct);
            var response = JsonSerializer.Deserialize<V1ModelsResponse>(json, JsonOptions);
            return response?.Data?.Select(m => ParseModel(m)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not get loaded models from /v1/models");
            return [];
        }
    }

    /// <summary>
    /// Parses a /v1/models entry into a FoundryModel.
    /// Model IDs look like: "qwen2.5-coder-0.5b-instruct-trtrtx-gpu:2"
    /// Format: {alias}-{device-suffix}:{version}
    /// </summary>
    private static FoundryModel ParseModel(V1ModelEntry m)
    {
        var fullId = m.Id ?? "";
        // Strip version suffix (:N)
        var noVersion = fullId.Contains(':') ? fullId[..fullId.LastIndexOf(':')] : fullId;

        // Known device suffixes (longest first to avoid partial matches)
        string[] deviceSuffixes = ["-trtrtx-gpu", "-cuda-gpu", "-generic-gpu", "-generic-cpu",
                                   "-winml-directml", "-winml-cpu", "-directml-gpu", "-cpu", "-gpu"];
        string alias = noVersion;
        string? device = null;
        foreach (var suffix in deviceSuffixes)
        {
            if (noVersion.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                alias = noVersion[..^suffix.Length];
                device = suffix.Contains("cpu", StringComparison.OrdinalIgnoreCase) ? "CPU" : "GPU";
                break;
            }
        }

        return new FoundryModel(fullId, alias, device, true);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record V1ModelsResponse(
        [property: JsonPropertyName("data")] List<V1ModelEntry>? Data
    );

    private record V1ModelEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("owned_by")] string? OwnedBy
    );
}
