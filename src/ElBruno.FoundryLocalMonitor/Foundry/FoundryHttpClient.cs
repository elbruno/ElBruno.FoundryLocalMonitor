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

    public FoundryHttpClient(HttpClient http, ILogger<FoundryHttpClient>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public void SetBaseUrl(string url) => _baseUrl = url.TrimEnd('/');

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/v1/models", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<FoundryModel>> GetLoadedModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var foundryModels = await TryGetFoundryListAsync(ct);
            if (foundryModels != null) return foundryModels;

            var response = await _http.GetFromJsonAsync<OpenAiModelListResponse>($"{_baseUrl}/v1/models", ct);
            return response?.Data?.Select(m => new FoundryModel(m.Id, m.Id, null, true)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not get loaded models from HTTP API");
            return [];
        }
    }

    private async Task<IReadOnlyList<FoundryModel>?> TryGetFoundryListAsync(CancellationToken ct)
    {
        try
        {
            var json = await _http.GetStringAsync($"{_baseUrl}/foundry/list", ct);
            var items = JsonSerializer.Deserialize<List<FoundryModelDto>>(json, JsonOptions);
            return items?.Select(m => new FoundryModel(m.ModelId ?? m.Alias ?? "", m.Alias ?? "", m.Device, true)).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record OpenAiModelListResponse(List<OpenAiModelDto>? Data);
    private record OpenAiModelDto(string Id);
    private record FoundryModelDto(
        [property: JsonPropertyName("modelId")] string? ModelId,
        [property: JsonPropertyName("alias")] string? Alias,
        [property: JsonPropertyName("device")] string? Device
    );
}
