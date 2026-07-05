using System.Windows;
using System.Windows.Threading;
using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Services;
using ElBruno.FoundryLocalMonitor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElBruno.FoundryLocalMonitor;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private MiniMonitorWindow? _miniMonitorWindow;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        RegisterGlobalExceptionHandlers();

        var settings = SettingsService.Load();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(settings);
                services.AddHttpClient<Foundry.FoundryHttpClient>();
                services.AddHttpClient<Foundry.FoundryEndpointDiscovery>();
                services.AddSingleton<Cli.FoundryCliRunner>();
                services.AddSingleton<IFoundryService, FoundryPollingService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MiniMonitorViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<MiniMonitorWindow>();
            })
            .Build();

        await _host.StartAsync();

        _mainWindow = _host.Services.GetRequiredService<MainWindow>();
        _miniMonitorWindow = _host.Services.GetRequiredService<MiniMonitorWindow>();

        var foundryService = _host.Services.GetRequiredService<IFoundryService>();

        _trayIconService = new TrayIconService(
            foundryService,
            openMonitor: ShowMainWindow,
            openMiniWindow: ShowMiniWindow,
            openSettings: ShowSettingsWindow,
            exitAction: Shutdown);

        await foundryService.StartPollingAsync();

        // Show CLI warning immediately if not installed (balloon needs the tray icon to exist first)
        if (!foundryService.IsCliInstalled)
            _trayIconService.ShowCliNotInstalledWarning();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.WindowState = WindowState.Normal;
    }

    private void ShowMiniWindow()
    {
        if (_miniMonitorWindow == null) return;
        _miniMonitorWindow.Show();
        _miniMonitorWindow.Activate();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            var settings = _host!.Services.GetRequiredService<AppSettings>();
            _settingsWindow = new SettingsWindow(settings);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, _) => { };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };
    }
}

