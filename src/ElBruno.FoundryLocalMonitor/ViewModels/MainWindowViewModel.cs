using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using System.Collections.ObjectModel;

namespace ElBruno.FoundryLocalMonitor.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFoundryService _foundryService;

    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private string _statusText = "Checking\u2026";

    public ObservableCollection<FoundryModel> LoadedModels { get; } = [];
    public ObservableCollection<FoundryModel> AvailableModels { get; } = [];

    public MainWindowViewModel(IFoundryService foundryService)
    {
        _foundryService = foundryService;
        _foundryService.ServiceStatusChanged += OnServiceStatusChanged;
        _foundryService.ModelStateChanged += OnModelStateChanged;
    }

    private void OnServiceStatusChanged(object? sender, bool isRunning)
    {
        IsServiceRunning = isRunning;
        StatusText = isRunning ? "Running" : "Stopped";
    }

    private void OnModelStateChanged(object? sender, ModelStateChange change)
    {
        if (change.ChangeType == ModelChangeType.Loaded)
            LoadedModels.Add(change.Model);
        else
            LoadedModels.Remove(change.Model);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var status = await _foundryService.GetStatusAsync();
        IsServiceRunning = status.IsRunning;
        StatusText = status.IsRunning ? "Running" : "Stopped";

        var available = await _foundryService.GetAvailableModelsAsync();
        AvailableModels.Clear();
        foreach (var m in available) AvailableModels.Add(m);
    }
}
