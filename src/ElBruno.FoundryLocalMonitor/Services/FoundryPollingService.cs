using ElBruno.FoundryLocalMonitor.Cli;
using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Foundry;
using ElBruno.FoundryLocalMonitor.Models;
using Microsoft.Extensions.Logging;
using Timer = System.Threading.Timer;

namespace ElBruno.FoundryLocalMonitor.Services;

public class FoundryPollingService : IFoundryService, IDisposable
{
    private readonly FoundryHttpClient _httpClient;
    private readonly FoundryCliRunner _cliRunner;
    private readonly AppSettings _settings;
    private readonly ILogger<FoundryPollingService>? _logger;

    private IReadOnlyList<FoundryModel> _loadedModels = [];
    private bool _isServiceRunning;
    private bool _isCliInstalled = true;
    private string? _currentEndpoint;
    private Timer? _timer;
    private bool _disposed;
    private bool _firstPoll = true;

    public bool IsServiceRunning => _isServiceRunning;
    public bool IsCliInstalled => _isCliInstalled;
    public string? CurrentEndpoint => _currentEndpoint;
    public IReadOnlyList<FoundryModel> LoadedModels => _loadedModels;

    public event EventHandler<bool>? ServiceStatusChanged;
    public event EventHandler<bool>? CliAvailabilityChanged;
    public event EventHandler<string?>? EndpointChanged;
    public event EventHandler<ModelStateChange>? ModelStateChanged;

    public FoundryPollingService(
        FoundryHttpClient httpClient,
        FoundryCliRunner cliRunner,
        AppSettings settings,
        ILogger<FoundryPollingService>? logger = null)
    {
        _httpClient = httpClient;
        _cliRunner = cliRunner;
        _settings = settings;
        _logger = logger;

        // Apply endpoint override from settings
        if (!string.IsNullOrWhiteSpace(settings.FoundryEndpointOverride))
            _httpClient.SetBaseUrl(settings.FoundryEndpointOverride);
    }

    public async Task StartPollingAsync(CancellationToken ct = default)
    {
        // Check CLI availability once upfront — port is dynamic, CLI is required
        var cliInstalled = await _cliRunner.IsFoundryInstalledAsync();
        if (_isCliInstalled != cliInstalled)
        {
            _isCliInstalled = cliInstalled;
            CliAvailabilityChanged?.Invoke(this, _isCliInstalled);
        }

        // Initial poll immediately so UI reflects real state on startup
        await PollAsync();

        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollingIntervalSeconds));
        _timer = new Timer(async _ => await PollAsync(), null, interval, interval);
    }

    public Task StopPollingAsync()
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public async Task PollOnceAsync() => await PollAsync();

    private async Task PollAsync()
    {
        try
        {
            var status = await GetStatusAsync();
            var wasFirstPoll = _firstPoll;
            _firstPoll = false;

            // Fire on every change, or on first poll so UI gets initial state
            if (status.IsRunning != _isServiceRunning || wasFirstPoll)
            {
                _isServiceRunning = status.IsRunning;
                ServiceStatusChanged?.Invoke(this, _isServiceRunning);
            }

            if (!status.IsRunning)
            {
                if (_loadedModels.Count > 0)
                {
                    var prev = _loadedModels;
                    _loadedModels = [];
                    foreach (var model in prev)
                        ModelStateChanged?.Invoke(this, new ModelStateChange(model, ModelChangeType.Unloaded, DateTime.Now));
                }
                return;
            }

            // Endpoint already updated inside GetStatusAsync — just fetch models
            var currentModels = await GetCurrentlyLoadedModelsAsync();
            DetectChanges(currentModels);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Polling error");
        }
    }

    private async Task<IReadOnlyList<FoundryModel>> GetCurrentlyLoadedModelsAsync()
    {
        // 1. CLI: foundry service ps — authoritative for explicitly loaded models
        var cliOutput = await _cliRunner.RunAsync("service ps");
        var cliModels = cliOutput != null ? FoundryCliParser.ParseLoadedModels(cliOutput) : null;

        // 2. HTTP: /foundry/list — catches on-demand loaded models (e.g. loaded via API/proxy)
        var httpModels = await _httpClient.GetLoadedModelsAsync();

        // Merge: union by ModelId so both sources contribute
        if (cliModels == null || cliModels.Count == 0) return httpModels;
        if (httpModels.Count == 0) return cliModels;

        var merged = cliModels.ToList();
        var existingIds = merged.Select(m => m.ModelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in httpModels.Where(m => !existingIds.Contains(m.ModelId)))
            merged.Add(m);
        return merged;
    }

    private void DetectChanges(IReadOnlyList<FoundryModel> newModels)
    {
        var prevIds = _loadedModels.Select(m => m.ModelId).ToHashSet();
        var newIds = newModels.Select(m => m.ModelId).ToHashSet();

        foreach (var model in newModels.Where(m => !prevIds.Contains(m.ModelId)))
            ModelStateChanged?.Invoke(this, new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now));

        foreach (var model in _loadedModels.Where(m => !newIds.Contains(m.ModelId)))
            ModelStateChanged?.Invoke(this, new ModelStateChange(model, ModelChangeType.Unloaded, DateTime.Now));

        _loadedModels = newModels;
    }

    public async Task<FoundryServiceStatus> GetStatusAsync()
    {
        var output = await _cliRunner.RunAsync("service status");

        var cliNowAvailable = output != null;
        if (cliNowAvailable != _isCliInstalled)
        {
            _isCliInstalled = cliNowAvailable;
            CliAvailabilityChanged?.Invoke(this, _isCliInstalled);
        }

        var cliStatus = FoundryCliParser.ParseServiceStatus(output);

        if (cliStatus.IsRunning)
        {
            if (cliStatus.Endpoint != null)
            {
                _httpClient.SetBaseUrl(ExtractBaseUrl(cliStatus.Endpoint));  // base URL only for HTTP calls
                UpdateEndpoint(cliStatus.Endpoint);                           // full URL (with path) for display
            }
            return cliStatus;
        }

        // CLI unavailable or not running — try HTTP port scan
        if (await _httpClient.IsReachableAsync())
        {
            var baseUrl = ExtractBaseUrl(_httpClient.CurrentBaseUrl);
            _httpClient.SetBaseUrl(baseUrl);
            UpdateEndpoint(baseUrl);
            return new FoundryServiceStatus(true, baseUrl, null);
        }

        UpdateEndpoint(null);
        return new FoundryServiceStatus(false, null, null);
    }

    private void UpdateEndpoint(string? endpoint)
    {
        if (endpoint == _currentEndpoint) return;
        _currentEndpoint = endpoint;
        EndpointChanged?.Invoke(this, endpoint);
    }

    private static string ExtractBaseUrl(string url)
    {
        try { var uri = new Uri(url); return $"{uri.Scheme}://{uri.Authority}"; }
        catch { return url; }
    }

    public async Task<IReadOnlyList<FoundryModel>> GetAvailableModelsAsync()
    {
        var output = await _cliRunner.RunAsync("model list");
        if (string.IsNullOrWhiteSpace(output)) return [];

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1);
        return lines
            .Select(l => l.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 1)
            .Select(p => new FoundryModel(p[0], p.Length >= 2 ? p[1] : p[0], null, false))
            .ToList();
    }

    public async Task LoadModelAsync(string modelId)
    {
        await _cliRunner.RunAsync($"model load {modelId}");
    }

    public async Task UnloadModelAsync(string modelId)
    {
        await _cliRunner.RunAsync($"model unload {modelId}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _disposed = true;
    }
}

