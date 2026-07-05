using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace ElBruno.FoundryLocalMonitor.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryService _foundryService;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private string _statusText = "Checking…";
    [ObservableProperty] private string _endpointText = "";

    public ObservableCollection<FoundryModel> LoadedModels { get; } = [];
    public ObservableCollection<FoundryModel> AvailableModels { get; } = [];

    public MainWindowViewModel(IFoundryService foundryService)
    {
        _foundryService = foundryService;
        _foundryService.ServiceStatusChanged += OnServiceStatusChanged;
        _foundryService.ModelStateChanged += OnModelStateChanged;
        _foundryService.EndpointChanged += OnEndpointChanged;
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
                LoadedModels.Add(change.Model);
            else
                LoadedModels.Remove(change.Model);
        });
    }

    private void OnEndpointChanged(object? sender, string? endpoint)
    {
        _dispatcher.Invoke(() =>
        {
            EndpointText = endpoint != null ? ExtractBaseUrl(endpoint) : "";
        });
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
    }

}
