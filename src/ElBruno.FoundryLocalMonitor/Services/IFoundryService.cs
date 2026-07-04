using ElBruno.FoundryLocalMonitor.Models;

namespace ElBruno.FoundryLocalMonitor.Services;

public interface IFoundryService
{
    bool IsServiceRunning { get; }
    bool IsCliInstalled { get; }
    string? CurrentEndpoint { get; }
    IReadOnlyList<FoundryModel> LoadedModels { get; }

    event EventHandler<bool>? ServiceStatusChanged;
    event EventHandler<bool>? CliAvailabilityChanged;
    event EventHandler<string?>? EndpointChanged;
    event EventHandler<ModelStateChange>? ModelStateChanged;

    Task StartPollingAsync(CancellationToken ct = default);
    Task StopPollingAsync();
    Task<FoundryServiceStatus> GetStatusAsync();
    Task<IReadOnlyList<FoundryModel>> GetAvailableModelsAsync();
    Task LoadModelAsync(string modelId);
    Task UnloadModelAsync(string modelId);
}
