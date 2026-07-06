namespace ElBruno.FoundryLocalMonitor.Models;

public record FoundryServiceStatus(bool IsRunning, string? Endpoint, string? Version);

public record FoundryModel(string ModelId, string Alias, string? Device, bool IsLoaded, string? SourceEndpoint = null)
{
    /// <summary>"Daemon" when loaded by Inference.Service.Agent, "CLI" for CLI-managed, otherwise "SDK Proxy".</summary>
    public string SourceType => SourceEndpoint == null ? "CLI"
        : SourceEndpoint.Contains("Inference.Service.Agent", StringComparison.OrdinalIgnoreCase) ? "Daemon"
        : "SDK Proxy";
}

public record ModelStateChange(FoundryModel Model, ModelChangeType ChangeType, DateTime Timestamp, bool IsSilent = false);

public enum ModelChangeType { Loaded, Unloaded }

/// <summary>
/// Groups all endpoints that belong to the same OS process (same PID) together,
/// collecting all their loaded models in one place.  Two proxy ports that share a PID
/// (e.g., FoundryLocalProxy listening on :50184 and :55588) become a single card.
/// </summary>
public record InstanceGroup(
    string? ProcessName,
    int? Pid,
    string PortsLabel,
    string? ProcessPath,
    bool IsProxy,
    bool IsDaemon,
    IReadOnlyList<FoundryModel> Models)
{
    public string Header =>
        $"{ProcessName ?? "Unknown"}{(Pid.HasValue ? $"  [PID {Pid}]" : "")}";
    public bool HasModels => Models.Count > 0;
    public int ModelCount => Models.Count;
}
