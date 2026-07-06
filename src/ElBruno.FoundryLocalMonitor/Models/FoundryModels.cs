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
