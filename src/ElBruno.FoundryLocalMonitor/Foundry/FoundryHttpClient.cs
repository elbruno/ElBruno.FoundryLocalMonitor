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
        // Only use /foundry/list — /v1/models lists ALL catalog models, not just loaded ones
        try
        {
            var json = await _http.GetStringAsync($"{_baseUrl}/foundry/list", ct);
            var items = JsonSerializer.Deserialize<List<FoundryModelDto>>(json, JsonOptions);
            return items?.Select(m => new FoundryModel(
                m.ModelId ?? m.Alias ?? "",
                m.Alias ?? m.ModelId ?? "",
                m.Device,
                true)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not get loaded models from /foundry/list");
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record FoundryModelDto(
        [property: JsonPropertyName("modelId")] string? ModelId,
        [property: JsonPropertyName("alias")] string? Alias,
        [property: JsonPropertyName("device")] string? Device
    );
}
