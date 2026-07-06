using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElBruno.FoundryLocalMonitor.Foundry;
using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ElBruno.FoundryLocalMonitor.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryService _foundryService;
    private readonly ILogger<MainWindowViewModel>? _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private string _statusText = "Checking…";
    [ObservableProperty] private string _endpointText = "";

    public ObservableCollection<FoundryModel> LoadedModels { get; } = [];
    public ObservableCollection<FoundryModel> AvailableModels { get; } = [];
    public ObservableCollection<FoundryEndpoint> DiscoveredInstances { get; } = [];
    public ObservableCollection<InstanceGroup> GroupedInstances { get; } = [];

    public MainWindowViewModel(IFoundryService foundryService, ILogger<MainWindowViewModel>? logger = null)
    {
        _foundryService = foundryService;
        _logger = logger;
        _foundryService.ServiceStatusChanged += OnServiceStatusChanged;
        _foundryService.ModelStateChanged += OnModelStateChanged;
        _foundryService.EndpointChanged += OnEndpointChanged;
        _foundryService.DiscoveredEndpointsChanged += OnDiscoveredEndpointsChanged;
    }

    private void OnServiceStatusChanged(object? sender, bool isRunning)
    {
        _dispatcher.Invoke(() =>
        {
            IsServiceRunning = isRunning;
            StatusText = isRunning ? "Running" : "Stopped";
        });
    }

    private void OnModelStateChanged(object? sender, ModelStateChange change)
    {
        _dispatcher.Invoke(() =>
        {
            if (change.ChangeType == ModelChangeType.Loaded)
            {
                LoadedModels.Add(change.Model);
            }
            else
            {
                // Match by both ModelId and SourceEndpoint to handle same model on multiple endpoints
                var existing = LoadedModels.FirstOrDefault(m =>
                    m.ModelId == change.Model.ModelId && m.SourceEndpoint == change.Model.SourceEndpoint)
                    ?? LoadedModels.FirstOrDefault(m => m.ModelId == change.Model.ModelId);
                if (existing != null) LoadedModels.Remove(existing);
            }
            RebuildGroupedInstances();
        });
    }

    private void OnEndpointChanged(object? sender, string? endpoint)
    {
        _dispatcher.Invoke(() =>
        {
            EndpointText = endpoint != null ? ExtractBaseUrl(endpoint) : "";
        });
    }

    private void OnDiscoveredEndpointsChanged(object? sender, IReadOnlyList<FoundryEndpoint> endpoints)
    {
        _dispatcher.Invoke(() =>
        {
            DiscoveredInstances.Clear();
            foreach (var ep in endpoints) DiscoveredInstances.Add(ep);
            RebuildGroupedInstances();
        });
    }

    /// <summary>
    /// Builds GroupedInstances from DiscoveredInstances (grouped by PID/process) and
    /// associates each group's loaded models from LoadedModels by port match.
    /// Same-PID endpoints (e.g., FoundryLocalProxy listening on :50184 and :55588) are
    /// merged into one card so the user sees one process with all its models.
    /// </summary>
    private void RebuildGroupedInstances()
    {
        var groups = DiscoveredInstances
            .GroupBy(ep => ep.Pid.HasValue ? $"pid:{ep.Pid}" : $"name:{ep.ProcessName ?? ep.BaseUrl}")
            .OrderBy(g => g.First().IsDaemon ? 1 : 0); // proxies first, daemon last

        var result = new List<InstanceGroup>();
        foreach (var g in groups)
        {
            var first = g.First();
            var ports = g.Select(ep => ep.Port).OrderBy(p => p).ToList();
            var portsLabel = string.Join("  ·  ", ports.Select(p => $":{p}"));
            var portSet = ports.ToHashSet();

            var models = LoadedModels
                .Where(m => ExtractPort(m.SourceEndpoint) is int p && portSet.Contains(p))
                .OrderBy(m => m.Alias)
                .ToList();

            result.Add(new InstanceGroup(
                ProcessName: first.ProcessName,
                Pid: first.Pid,
                PortsLabel: portsLabel,
                ProcessPath: first.ProcessPath,
                IsProxy: first.IsProxy,
                IsDaemon: first.IsDaemon,
                Models: models));
        }

        _logger?.LogDebug("RebuildGroupedInstances: {Discovered} endpoints → {Groups} groups, LoadedModels={Models}",
            DiscoveredInstances.Count, result.Count, LoadedModels.Count);
        foreach (var g in result)
            _logger?.LogDebug("  Group '{Name}' [PID {Pid}] ports={Ports} models={Count}",
                g.ProcessName, g.Pid, g.PortsLabel, g.Models.Count);

        GroupedInstances.Clear();
        foreach (var g in result) GroupedInstances.Add(g);
    }

    private static int? ExtractPort(string? sourceEndpoint)
    {
        // Format: "FoundryLocalProxy:50184 [PID 34096]"
        if (sourceEndpoint == null) return null;
        var colonIdx = sourceEndpoint.IndexOf(':');
        if (colonIdx < 0) return null;
        var afterColon = sourceEndpoint[(colonIdx + 1)..];
        var spaceIdx = afterColon.IndexOf(' ');
        var portStr = spaceIdx >= 0 ? afterColon[..spaceIdx] : afterColon;
        return int.TryParse(portStr, out var port) ? port : null;
    }

    private static string ExtractBaseUrl(string url)
    {
        try { var uri = new Uri(url); return $"{uri.Scheme}://{uri.Authority}"; }
        catch { return url; }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusText = "Refreshing…";
        var status = await _foundryService.GetStatusAsync();
        _dispatcher.Invoke(() =>
        {
            IsServiceRunning = status.IsRunning;
            StatusText = status.IsRunning ? "Running" : "Stopped";
        });

        var available = await _foundryService.GetAvailableModelsAsync();
        _dispatcher.Invoke(() =>
        {
            AvailableModels.Clear();
            foreach (var m in available) AvailableModels.Add(m);
        });
    }

    public void Dispose()
    {
        _foundryService.ServiceStatusChanged -= OnServiceStatusChanged;
        _foundryService.ModelStateChanged -= OnModelStateChanged;
        _foundryService.EndpointChanged -= OnEndpointChanged;
        _foundryService.DiscoveredEndpointsChanged -= OnDiscoveredEndpointsChanged;
    }
}
