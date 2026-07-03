namespace ElBruno.FoundryLocalMonitor.Models;

public record FoundryServiceStatus(bool IsRunning, string? Endpoint, string? Version);

public record FoundryModel(string ModelId, string Alias, string? Device, bool IsLoaded);

public record ModelStateChange(FoundryModel Model, ModelChangeType ChangeType, DateTime Timestamp);

public enum ModelChangeType { Loaded, Unloaded }
