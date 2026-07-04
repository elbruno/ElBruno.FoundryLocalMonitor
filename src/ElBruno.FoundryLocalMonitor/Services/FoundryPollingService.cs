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
    }

    public Task StartPollingAsync(CancellationToken ct = default)
    {
        var interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);
        _timer = new Timer(async _ => await PollAsync(), null, TimeSpan.Zero, interval);
        return Task.CompletedTask;
    }

    public Task StopPollingAsync()
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    private async Task PollAsync()
    {
        try
        {
            var status = await GetStatusAsync();

            if (status.IsRunning != _isServiceRunning)
            {
                _isServiceRunning = status.IsRunning;
                ServiceStatusChanged?.Invoke(this, _isServiceRunning);
            }

            if (!status.IsRunning) return;

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
