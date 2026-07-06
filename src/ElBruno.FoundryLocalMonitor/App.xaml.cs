using System.IO;
using System.Windows;
using System.Windows.Threading;
using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Services;
using ElBruno.FoundryLocalMonitor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        ThemeManager.Apply(settings.Theme);

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElBruno.FoundryLocalMonitor", "monitor.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(new FileLoggerProvider(logPath));
            })
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
            _host.Services.GetRequiredService<AppSettings>(),
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
        if (_mainWindow == null || !_mainWindow.IsLoaded)
            _mainWindow = _host!.Services.GetRequiredService<MainWindow>();
        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.WindowState = WindowState.Normal;
    }

    private void ShowMiniWindow()
    {
        if (_miniMonitorWindow == null || !_miniMonitorWindow.IsLoaded)
            _miniMonitorWindow = _host!.Services.GetRequiredService<MiniMonitorWindow>();
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
            LogUnhandledException("UI (Dispatcher)", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogUnhandledException("AppDomain", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            LogUnhandledException("Task", args.Exception);
        };
    }

    private static void LogUnhandledException(string source, Exception ex)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElBruno.FoundryLocalMonitor", "monitor.log");
            File.AppendAllText(logPath,
                $"{DateTime.Now:HH:mm:ss.fff} [Critical   ] Unhandled {source} exception: {ex}\n");
        }
        catch { /* last-resort logging — ignore write failures */ }
    }
}

