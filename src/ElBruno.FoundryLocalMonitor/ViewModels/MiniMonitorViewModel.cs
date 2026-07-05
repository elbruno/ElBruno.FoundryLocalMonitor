using CommunityToolkit.Mvvm.ComponentModel;
using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using System.Reflection;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace ElBruno.FoundryLocalMonitor.ViewModels;

public partial class MiniMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IFoundryService _foundryService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _countdownTimer;
    private int _secondsUntilRefresh;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "Checking…";
    [ObservableProperty] private string _currentModel = "No model loaded";
    [ObservableProperty] private string _endpointText = "";
    [ObservableProperty] private string _nextRefreshText = "";
    [ObservableProperty] private string _appVersion = "";
    [ObservableProperty] private string _loadedModelCount = "";
    [ObservableProperty] private bool _showCliWarning;
    [ObservableProperty] private string _cliWarningText = "";

    public MiniMonitorViewModel(IFoundryService foundryService, AppSettings settings)
    {
        _foundryService = foundryService;
        _settings = settings;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v0.5";

        _secondsUntilRefresh = settings.PollingIntervalSeconds;

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();

        _foundryService.ServiceStatusChanged += OnServiceStatusChanged;
        _foundryService.ModelStateChanged += OnModelStateChanged;
        _foundryService.CliAvailabilityChanged += OnCliAvailabilityChanged;
        _foundryService.EndpointChanged += OnEndpointChanged;

        UpdateCliWarning(_foundryService.IsCliInstalled);
        if (_foundryService.CurrentEndpoint != null)
            EndpointText = FormatEndpoint(_foundryService.CurrentEndpoint);
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        if (_secondsUntilRefresh > 0) _secondsUntilRefresh--;
        else _secondsUntilRefresh = _settings.PollingIntervalSeconds;

        NextRefreshText = $"Next refresh in {_secondsUntilRefresh}s";
    }

    private void OnServiceStatusChanged(object? sender, bool isRunning)
    {
        _dispatcher.Invoke(() =>
        {
            IsRunning = isRunning;
            StatusText = isRunning ? "Running" : "Stopped";
            _secondsUntilRefresh = _settings.PollingIntervalSeconds;

            if (!isRunning)
            {
                CurrentModel = "No model loaded";
                LoadedModelCount = "";
            }
        });
    }

    private void OnModelStateChanged(object? sender, ModelStateChange _)
    {
        _dispatcher.Invoke(() =>
        {
            var models = _foundryService.LoadedModels;
            if (models.Count == 0)
            {
                CurrentModel = "No model loaded";
                LoadedModelCount = "";
            }
            else
            {
                var first = models[0];
                var device = first.Device != null ? $" [{first.Device}]" : "";
                CurrentModel = $"{first.Alias}{device}";
                LoadedModelCount = models.Count > 1 ? $"+{models.Count - 1} more" : "";
            }
        });
    }

    private void OnEndpointChanged(object? sender, string? endpoint)
        => _dispatcher.Invoke(() => EndpointText = endpoint != null ? FormatEndpoint(endpoint) : "");

    private static string FormatEndpoint(string url)
    {
        // Show scheme + host:port (e.g. http://127.0.0.1:52148)
        try { var uri = new Uri(url); return $"{uri.Scheme}://{uri.Authority}"; }
        catch { return url; }
    }

    private void OnCliAvailabilityChanged(object? sender, bool isInstalled)
        => _dispatcher.Invoke(() => UpdateCliWarning(isInstalled));

    private void UpdateCliWarning(bool isInstalled)
    {
        ShowCliWarning = !isInstalled;
        CliWarningText = isInstalled ? "" : "⚠ Foundry CLI not installed\nRun: winget install Microsoft.FoundryLocal";
        if (!isInstalled) StatusText = "CLI required";
    }

    public void Dispose()
    {
        _countdownTimer.Stop();
        _foundryService.ServiceStatusChanged -= OnServiceStatusChanged;
        _foundryService.ModelStateChanged -= OnModelStateChanged;
        _foundryService.CliAvailabilityChanged -= OnCliAvailabilityChanged;
        _foundryService.EndpointChanged -= OnEndpointChanged;
    }
}
