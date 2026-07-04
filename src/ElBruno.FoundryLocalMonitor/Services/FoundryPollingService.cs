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
    private Timer? _timer;
    private bool _disposed;
    private bool _firstPoll = true;

    public bool IsServiceRunning => _isServiceRunning;
    public IReadOnlyList<FoundryModel> LoadedModels => _loadedModels;

    public event EventHandler<ModelStateChange>? ModelStateChanged;
    public event EventHandler<bool>? ServiceStatusChanged;

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

            if (status.Endpoint != null)
                _httpClient.SetBaseUrl(status.Endpoint);

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
        if (await _httpClient.IsReachableAsync())
            return await _httpClient.GetLoadedModelsAsync();

        var output = await _cliRunner.RunAsync("service ps");
        return FoundryCliParser.ParseLoadedModels(output);
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
        if (await _httpClient.IsReachableAsync())
            return new FoundryServiceStatus(true, null, null);

        var output = await _cliRunner.RunAsync("service status");
        return FoundryCliParser.ParseServiceStatus(output);
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

