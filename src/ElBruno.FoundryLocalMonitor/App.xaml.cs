using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ElBruno.FoundryLocalMonitor.Models;
using ElBruno.FoundryLocalMonitor.Services;
using ElBruno.FoundryLocalMonitor.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElBruno.FoundryLocalMonitor;

public partial class App : Application
{
    private IHost? _host;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpClient<Foundry.FoundryHttpClient>();
                services.AddSingleton<Cli.FoundryCliRunner>();
                services.AddSingleton<Configuration.AppSettings>();
                services.AddSingleton<IFoundryService, Services.FoundryPollingService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MiniMonitorViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<MiniMonitorWindow>();
            })
            .Build();

        await _host.StartAsync();

        SetupTrayIcon();

        var foundryService = _host.Services.GetRequiredService<IFoundryService>();
        foundryService.ModelStateChanged += OnModelStateChanged;
        await foundryService.StartPollingAsync();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Foundry Local Monitor",
            Visibility = Visibility.Visible,
            IconSource = new BitmapImage(new Uri("pack://siteoforigin:,,,/Assets/foundry-tray.png"))
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open Monitor" };
        openItem.Click += (_, _) => ShowMainWindow();

        var miniItem = new System.Windows.Controls.MenuItem { Header = "Mini Window" };
        miniItem.Click += (_, _) => ShowMiniWindow();

        var separator1 = new System.Windows.Controls.Separator();

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(miniItem);
        contextMenu.Items.Add(separator1);
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
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
        var mini = _host!.Services.GetRequiredService<MiniMonitorWindow>();
        mini.Show();
        mini.Activate();
    }

    private void OnModelStateChanged(object? sender, ModelStateChange change)
    {
        var message = change.ChangeType == ModelChangeType.Loaded
            ? $"✅ Model loaded: {change.Model.Alias}"
            : $"⏏ Model unloaded: {change.Model.Alias}";

        Dispatcher.Invoke(() =>
        {
            _trayIcon?.ShowBalloonTip(
                title: "Foundry Local Monitor",
                message: message,
                symbol: BalloonIcon.Info);
        });
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
