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
    private readonly FoundryEndpointDiscovery _discovery;
    private readonly AppSettings _settings;
    private readonly ILogger<FoundryPollingService>? _logger;

    private IReadOnlyList<FoundryModel> _loadedModels = [];
    private bool _isServiceRunning;
    private bool _isCliInstalled = true;
    private string? _currentEndpoint;
    private Timer? _timer;
    private Timer? _discoveryTimer;
    private bool _disposed;
    private bool _firstPoll = true;

    // Cache of discovered Foundry API ports; refreshed on discovery cycles
    private IReadOnlyList<FoundryEndpoint> _discoveredEndpoints = [];
    private DateTime _lastDiscovery = DateTime.MinValue;
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(30);

    public bool IsServiceRunning => _isServiceRunning;
    public bool IsCliInstalled => _isCliInstalled;
    public string? CurrentEndpoint => _currentEndpoint;
    public IReadOnlyList<FoundryModel> LoadedModels => _loadedModels;
    public IReadOnlyList<FoundryEndpoint> DiscoveredEndpoints => _discoveredEndpoints;

    public event EventHandler<bool>? ServiceStatusChanged;
    public event EventHandler<bool>? CliAvailabilityChanged;
    public event EventHandler<string?>? EndpointChanged;
    public event EventHandler<ModelStateChange>? ModelStateChanged;
    public event EventHandler<IReadOnlyList<FoundryEndpoint>>? DiscoveredEndpointsChanged;

    public FoundryPollingService(
        FoundryHttpClient httpClient,
        FoundryCliRunner cliRunner,
        FoundryEndpointDiscovery discovery,
        AppSettings settings,
        ILogger<FoundryPollingService>? logger = null)
    {
        _httpClient = httpClient;
        _cliRunner = cliRunner;
        _discovery = discovery;
        _settings = settings;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(settings.FoundryEndpointOverride))
            _httpClient.SetBaseUrl(settings.FoundryEndpointOverride);
    }

    public async Task StartPollingAsync(CancellationToken ct = default)
    {
        var cliInstalled = await _cliRunner.IsFoundryInstalledAsync();
        if (_isCliInstalled != cliInstalled)
        {
            _isCliInstalled = cliInstalled;
            CliAvailabilityChanged?.Invoke(this, _isCliInstalled);
        }

        // Run discovery immediately on startup
        await RunDiscoveryAsync(ct);

        // Initial poll so UI reflects real state immediately
        await PollAsync();

        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.PollingIntervalSeconds));
        _timer = new Timer(async _ => await PollAsync(), null, interval, interval);

        // Background discovery refresh every 30s to catch new apps
        _discoveryTimer = new Timer(async _ => await RunDiscoveryAsync(), null,
            DiscoveryInterval, DiscoveryInterval);
    }

    public Task StopPollingAsync()
    {
        _timer?.Dispose();
        _timer = null;
        _discoveryTimer?.Dispose();
        _discoveryTimer = null;
        return Task.CompletedTask;
    }

    public async Task PollOnceAsync() => await PollAsync();

    private async Task PollAsync()
    {
        try
        {
            // Re-run discovery if daemon just appeared or interval elapsed
            var daemonRunning = FoundryEndpointDiscovery.IsDaemonRunning();
            var discoveryStale = DateTime.UtcNow - _lastDiscovery > DiscoveryInterval;
            if (discoveryStale || (_discoveredEndpoints.Count == 0 && daemonRunning))
                await RunDiscoveryAsync();

            var status = await GetStatusAsync();
            var wasFirstPoll = _firstPoll;
            _firstPoll = false;

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

            var currentModels = await GetCurrentlyLoadedModelsAsync();

            // On the first poll, snapshot pre-existing models into the UI without
            // firing toast notifications (IsSilent=true). Changes during the session fire normally.
            if (wasFirstPoll)
            {
                _logger?.LogDebug("First poll: silently snapshotted {Count} pre-existing model(s)", currentModels.Count);
                DetectChanges(currentModels, isSilent: true);
                return;
            }

            DetectChanges(currentModels);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Polling error");
        }
    }

    /// <summary>
    /// Probes all localhost listeners in parallel to discover every Foundry API endpoint.
    /// Runs on startup and every 30 seconds to catch newly launched apps.
    /// </summary>
    private async Task RunDiscoveryAsync(CancellationToken ct = default)
    {
        try
        {
            var endpoints = (await _discovery.DiscoverAsync(ct)).ToList();

            // The Foundry daemon (Inference.Service.Agent) exposes /openai/status and
            // /openai/loadedmodels but does NOT implement the OpenAI /v1/models shim,
            // so the HTTP probe won't find it. Explicitly add it when running.
            var daemonPort = FoundryEndpointDiscovery.GetDaemonPort();
            if (daemonPort.HasValue && !endpoints.Any(e => e.Port == daemonPort.Value))
            {
                var daemonProc = System.Diagnostics.Process.GetProcessesByName("Inference.Service.Agent").FirstOrDefault();
                string? daemonPath = null;
                try { daemonPath = daemonProc?.MainModule?.FileName; } catch { }
                endpoints.Add(new FoundryEndpoint(
                    $"http://127.0.0.1:{daemonPort.Value}",
                    daemonPort.Value,
                    "Inference.Service.Agent",
                    [],
                    IsDaemon: true,
                    IsProxy: false,
                    Pid: daemonProc?.Id,
                    ProcessPath: daemonPath));
                _logger?.LogDebug("Daemon added explicitly at :{Port}", daemonPort.Value);
            }

            _discoveredEndpoints = endpoints;
            _lastDiscovery = DateTime.UtcNow;
            _logger?.LogDebug("Discovery: {Count} endpoint(s) found", endpoints.Count);
            DiscoveredEndpointsChanged?.Invoke(this, _discoveredEndpoints);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Endpoint discovery error");
        }
    }

    /// <summary>
    /// Fetches currently loaded models from ALL discovered endpoints.
    ///
    /// Two endpoint types need different strategies:
    ///  - Daemon (Inference.Service.Agent): GET /openai/loadedmodels — authoritative loaded list
    ///  - SDK proxy (FoundryLocalProxy, FoundryProxy): GET /v1/models — the proxy only lists
    ///    models it currently has loaded in-process (no /openai/loadedmodels endpoint exists)
    /// </summary>
    private async Task<IReadOnlyList<FoundryModel>> GetCurrentlyLoadedModelsAsync()
    {
        var merged = new List<FoundryModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ep in _discoveredEndpoints)
        {
            IReadOnlyList<FoundryModel> models;
            if (ep.IsProxy)
            {
                // SDK proxy: /v1/models lists what's loaded in-process. Re-query each poll
                // so we get fresh data (not the stale snapshot from the last discovery cycle).
                models = await _httpClient.GetLoadedModelsFromUrlAsync(ep.BaseUrl);
            }
            else
            {
                // Daemon: use the authoritative /openai/loadedmodels endpoint
                models = await _httpClient.GetLoadedModelsFromEndpointAsync(ep.BaseUrl);
            }

            foreach (var m in models)
            {
                // Key on (port, modelId) — same model on different endpoints = different rows
                var rowKey = $"{ep.Port}:{m.ModelId}";
                if (seen.Add(rowKey))
                {
                    var pidLabel = ep.Pid.HasValue ? $" [PID {ep.Pid}]" : "";
                    var source = $"{ep.ProcessName ?? ep.BaseUrl}:{ep.Port}{pidLabel}";
                    merged.Add(m with { SourceEndpoint = source });
                }
            }
        }

        // CLI fallback for models not visible via HTTP (e.g., CLI-managed sessions)
        var cliOutput = await _cliRunner.RunAsync("service ps");
        var cliModels = cliOutput != null ? FoundryCliParser.ParseLoadedModels(cliOutput) : null;
        if (cliModels != null)
            foreach (var m in cliModels)
                if (seen.Add(m.ModelId)) merged.Add(m);

        _logger?.LogDebug("GetCurrentlyLoadedModels: {Count} model(s) total — {Ids}",
            merged.Count, string.Join(", ", merged.Select(m => m.ModelId)));

        return merged;
    }

    private void DetectChanges(IReadOnlyList<FoundryModel> newModels, bool isSilent = false)
    {
        var prevIds = _loadedModels.Select(m => m.ModelId).ToHashSet();
        var newIds = newModels.Select(m => m.ModelId).ToHashSet();

        foreach (var model in newModels.Where(m => !prevIds.Contains(m.ModelId)))
            ModelStateChanged?.Invoke(this, new ModelStateChange(model, ModelChangeType.Loaded, DateTime.Now, isSilent));

        foreach (var model in _loadedModels.Where(m => !newIds.Contains(m.ModelId)))
            ModelStateChanged?.Invoke(this, new ModelStateChange(model, ModelChangeType.Unloaded, DateTime.Now, isSilent));

        _loadedModels = newModels;
    }

    public async Task<FoundryServiceStatus> GetStatusAsync()
    {
        // 1. Fast path: daemon process alive?
        if (FoundryEndpointDiscovery.IsDaemonRunning())
        {
            var daemonPort = FoundryEndpointDiscovery.GetDaemonPort();
            if (daemonPort.HasValue)
            {
                var daemonUrl = $"http://127.0.0.1:{daemonPort}/openai/status";
                _httpClient.SetBaseUrl($"http://127.0.0.1:{daemonPort}");
                UpdateEndpoint(daemonUrl);
                return new FoundryServiceStatus(true, daemonUrl, null);
            }
        }

        // 2. Any discovered endpoints alive?
        if (_discoveredEndpoints.Count > 0)
        {
            var first = _discoveredEndpoints[0];
            _httpClient.SetBaseUrl(first.BaseUrl);
            var displayUrl = $"{first.BaseUrl}/v1/models";
            UpdateEndpoint(displayUrl);
            return new FoundryServiceStatus(true, displayUrl, null);
        }

        // 3. CLI check as fallback
        var output = await _cliRunner.RunAsync("service status");
        var cliNowAvailable = output != null;
        if (cliNowAvailable != _isCliInstalled)
        {
            _isCliInstalled = cliNowAvailable;
            CliAvailabilityChanged?.Invoke(this, _isCliInstalled);
        }

        var cliStatus = FoundryCliParser.ParseServiceStatus(output);
        if (cliStatus.IsRunning && cliStatus.Endpoint != null)
        {
            _httpClient.SetBaseUrl(ExtractBaseUrl(cliStatus.Endpoint));
            UpdateEndpoint(cliStatus.Endpoint);
            // Daemon appeared — trigger immediate rediscovery next poll
            _lastDiscovery = DateTime.MinValue;
            return cliStatus;
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
        => await _cliRunner.RunAsync($"model load {modelId}");

    public async Task UnloadModelAsync(string modelId)
        => await _cliRunner.RunAsync($"model unload {modelId}");

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _discoveryTimer?.Dispose();
        _disposed = true;
    }
}

