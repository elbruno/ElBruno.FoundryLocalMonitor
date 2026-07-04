using System.Windows;
using System.Windows.Threading;
using ElBruno.FoundryLocalMonitor.Services;
using ElBruno.FoundryLocalMonitor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinFormsApp = System.Windows.Forms.Application;

namespace ElBruno.FoundryLocalMonitor;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private MiniMonitorWindow? _miniMonitorWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        RegisterGlobalExceptionHandlers();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpClient<Foundry.FoundryHttpClient>();
                services.AddSingleton<Cli.FoundryCliRunner>();
                services.AddSingleton<Configuration.AppSettings>();
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
            exitAction: Shutdown);

        await foundryService.StartPollingAsync();
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
            // Log but keep running — don't let UI exceptions kill the tray app
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, _) => { };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
        };
    }
}

