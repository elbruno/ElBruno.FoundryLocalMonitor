using ElBruno.FoundryLocalMonitor.Models;

namespace ElBruno.FoundryLocalMonitor.Services;

public interface IFoundryService
{
    bool IsServiceRunning { get; }
    IReadOnlyList<FoundryModel> LoadedModels { get; }
    event EventHandler<ModelStateChange>? ModelStateChanged;
    event EventHandler<bool>? ServiceStatusChanged;
    Task StartPollingAsync(CancellationToken ct = default);
    Task StopPollingAsync();
    Task<FoundryServiceStatus> GetStatusAsync();
    Task<IReadOnlyList<FoundryModel>> GetAvailableModelsAsync();
    Task LoadModelAsync(string modelId);
    Task UnloadModelAsync(string modelId);
}
