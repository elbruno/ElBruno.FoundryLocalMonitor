using CommunityToolkit.Mvvm.ComponentModel;
using ElBruno.FoundryLocalMonitor.Services;

namespace ElBruno.FoundryLocalMonitor.ViewModels;

public partial class MiniMonitorViewModel : ObservableObject
{
    private readonly IFoundryService _foundryService;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Stopped";
    [ObservableProperty] private string _currentModel = "No model loaded";

    public MiniMonitorViewModel(IFoundryService foundryService)
    {
        _foundryService = foundryService;
        _foundryService.ServiceStatusChanged += (_, running) =>
        {
            IsRunning = running;
            StatusText = running ? "Running" : "Stopped";
        };
        _foundryService.ModelStateChanged += (_, change) =>
        {
            CurrentModel = change.ChangeType == Models.ModelChangeType.Loaded
                ? change.Model.Alias
                : "No model loaded";
        };
    }
}
