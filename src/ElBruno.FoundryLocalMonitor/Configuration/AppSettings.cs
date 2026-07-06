namespace ElBruno.FoundryLocalMonitor.Configuration;

public class AppSettings
{
    public int PollingIntervalSeconds { get; set; } = 5;
    public string? FoundryEndpointOverride { get; set; }
    public bool ShowNotificationsOnLoad { get; set; } = true;
    public bool ShowNotificationsOnUnload { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = false;
    public bool LaunchOnStartup { get; set; } = false;
    /// <summary>Theme name: "System" (default), "Light", or "Dark".</summary>
    public string Theme { get; set; } = "System";
}
