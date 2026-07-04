using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ElBruno.FoundryLocalMonitor.Models;

namespace ElBruno.FoundryLocalMonitor.Services;

public enum FoundryTrayState
{
    NotReachable,
    Running,
    ModelLoaded
}

public sealed class TrayIconService : IDisposable
{
    private readonly IFoundryService _foundryService;
    private readonly Action _openMonitor;
    private readonly Action _openMiniWindow;
    private readonly Action _openSettings;
    private readonly Action _exitAction;
    private readonly NotifyIcon _notifyIcon;
    private readonly IReadOnlyDictionary<FoundryTrayState, Icon> _icons;
    private FoundryTrayState _currentState = FoundryTrayState.NotReachable;

    public TrayIconService(
        IFoundryService foundryService,
        Action openMonitor,
        Action openMiniWindow,
        Action openSettings,
        Action exitAction)
    {
        _foundryService = foundryService;
        _openMonitor = openMonitor;
        _openMiniWindow = openMiniWindow;
        _openSettings = openSettings;
        _exitAction = exitAction;
        _icons = LoadIcons();

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Foundry Local Monitor — Starting…",
            Icon = _icons[FoundryTrayState.NotReachable],
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => _openMiniWindow();

        _foundryService.ServiceStatusChanged += OnServiceStatusChanged;
        _foundryService.ModelStateChanged += OnModelStateChanged;
        _foundryService.CliAvailabilityChanged += OnCliAvailabilityChanged;
    }

    private void OnCliAvailabilityChanged(object? sender, bool isInstalled)
    {
        if (!isInstalled)
            ShowCliNotInstalledWarning();
    }

    internal void ShowCliNotInstalledWarning()
    {
        _notifyIcon.BalloonTipTitle = "⚠ Foundry CLI not installed";
        _notifyIcon.BalloonTipText =
            "Foundry Local Monitor requires the Foundry CLI to detect the service port.\n" +
            "Run: winget install Microsoft.FoundryLocal";
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(10_000);
        _notifyIcon.Text = "Foundry Local Monitor — CLI not installed";
    }

    private void OnServiceStatusChanged(object? sender, bool isRunning)
    {
        var newState = isRunning
            ? (_foundryService.LoadedModels.Count > 0 ? FoundryTrayState.ModelLoaded : FoundryTrayState.Running)
            : FoundryTrayState.NotReachable;

        UpdateState(newState, isRunning ? "Foundry Local Monitor — Running" : "Foundry Local Monitor — Stopped");
    }

    private void OnModelStateChanged(object? sender, ModelStateChange change)
    {
        if (!_foundryService.IsServiceRunning) return;

        var hasModels = _foundryService.LoadedModels.Count > 0;
        var newState = hasModels ? FoundryTrayState.ModelLoaded : FoundryTrayState.Running;
        var tooltip = hasModels
            ? $"Foundry Local — {_foundryService.LoadedModels.Count} model(s) loaded"
            : "Foundry Local Monitor — Running";

        UpdateState(newState, tooltip);

        // Balloon notification
        var message = change.ChangeType == ModelChangeType.Loaded
            ? $"✅ Model loaded: {change.Model.Alias}"
            : $"⏏ Model unloaded: {change.Model.Alias}";

        _notifyIcon.BalloonTipTitle = "Foundry Local Monitor";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void UpdateState(FoundryTrayState state, string tooltip)
    {
        if (_currentState == state && _notifyIcon.Text == tooltip) return;
        _currentState = state;
        _notifyIcon.Icon = _icons[state];
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip; // NotifyIcon has 64-char limit
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            new ToolStripMenuItem("Open Monitor",    null, (_, _) => _openMonitor()),
            new ToolStripMenuItem("Mini Window",     null, (_, _) => _openMiniWindow()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Settings",        null, (_, _) => _openSettings()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("About",           null, (_, _) => OpenUrl("https://github.com/elbruno/ElBruno.FoundryLocalMonitor")),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit",            null, (_, _) => _exitAction())
        ]);
        return menu;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* swallow */ }
    }

    private static IReadOnlyDictionary<FoundryTrayState, Icon> LoadIcons()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcons");

        return new Dictionary<FoundryTrayState, Icon>
        {
            [FoundryTrayState.NotReachable] = LoadIcon(dir, "tray-gray.ico",  SystemIcons.Error),
            [FoundryTrayState.Running]      = LoadIcon(dir, "tray-green.ico", SystemIcons.Information),
            [FoundryTrayState.ModelLoaded]  = LoadIcon(dir, "tray-blue.ico",  SystemIcons.Shield)
        };
    }

    private static Icon LoadIcon(string dir, string fileName, Icon fallback)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            return (Icon)fallback.Clone();

        using var stream = File.OpenRead(path);
        return new Icon(stream);
    }

    public void Dispose()
    {
        _foundryService.ServiceStatusChanged -= OnServiceStatusChanged;
        _foundryService.ModelStateChanged    -= OnModelStateChanged;
        _foundryService.CliAvailabilityChanged -= OnCliAvailabilityChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var icon in _icons.Values)
            icon.Dispose();
    }
}
