using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ElBruno.FoundryLocalMonitor.Services;

public enum FoundryTrayState
{
    NotReachable,
    Running,
    ModelLoaded
}

public sealed class TrayIconService : IDisposable
{
    // AUMID used to identify this app to the Windows notification system.
    // Must match the registry key created in RegisterAppId().
    private const string AppId = "ElBruno.FoundryLocalMonitor";

    private readonly IFoundryService _foundryService;
    private readonly AppSettings _settings;
    private readonly Action _openMonitor;
    private readonly Action _openMiniWindow;
    private readonly Action _openSettings;
    private readonly Action _exitAction;
    private readonly NotifyIcon _notifyIcon;
    private readonly IReadOnlyDictionary<FoundryTrayState, Icon> _icons;
    private FoundryTrayState _currentState = FoundryTrayState.NotReachable;

    public TrayIconService(
        IFoundryService foundryService,
        AppSettings settings,
        Action openMonitor,
        Action openMiniWindow,
        Action openSettings,
        Action exitAction)
    {
        _foundryService = foundryService;
        _settings = settings;
        _openMonitor = openMonitor;
        _openMiniWindow = openMiniWindow;
        _openSettings = openSettings;
        _exitAction = exitAction;
        _icons = LoadIcons();

        RegisterAppId();

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
        _notifyIcon.Text = "Foundry Local Monitor — CLI not installed";
        ShowToast(
            "⚠ Foundry CLI not installed",
            "Foundry Local Monitor requires the Foundry CLI.\nRun: winget install Microsoft.FoundryLocal");
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

        // Apply notification filter before showing toast
        if (change.IsSilent || !ShouldNotify(change)) return;

        var isLoad = change.ChangeType == ModelChangeType.Loaded;
        if (isLoad && !_settings.ShowNotificationsOnLoad) return;
        if (!isLoad && !_settings.ShowNotificationsOnUnload) return;

        var message = isLoad
            ? $"Model loaded: {change.Model.Alias}"
            : $"Model unloaded: {change.Model.Alias}";

        ShowToast("Foundry Local Monitor", message);
    }

    /// <summary>
    /// Returns true if a model change should produce a toast based on the
    /// current NotificationFilter setting:
    ///   "None"         — never notify
    ///   "Daemon only"  — notify only when the source is Inference.Service.Agent
    ///   "All instances"— notify for every endpoint (daemon + SDK proxies)
    /// </summary>
    private bool ShouldNotify(ModelStateChange change)
    {
        return _settings.NotificationFilter switch
        {
            "None" => false,
            "Daemon only" => IsDaemonSource(change.Model.SourceEndpoint),
            _ => true   // "All instances" or any future value
        };
    }

    private static bool IsDaemonSource(string? sourceEndpoint) =>
        sourceEndpoint != null &&
        sourceEndpoint.Contains("Inference.Service.Agent", StringComparison.OrdinalIgnoreCase);

    private void UpdateState(FoundryTrayState state, string tooltip)
    {
        if (_currentState == state && _notifyIcon.Text == tooltip) return;
        _currentState = state;
        _notifyIcon.Icon = _icons[state];
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip; // NotifyIcon has 64-char limit
    }

    // Sends a single Windows Toast notification (no duplicates).
    // ShowBalloonTip() was replaced because on Windows 10/11 it produces two
    // visible notifications: a legacy balloon popup AND an Action Center entry.
    // ToastNotificationManager produces exactly one.
    private static void ShowToast(string title, string message)
    {
        try
        {
            var xml = $"""
                <toast>
                    <visual>
                        <binding template="ToastGeneric">
                            <text>{Escape(title)}</text>
                            <text>{Escape(message)}</text>
                        </binding>
                    </visual>
                </toast>
                """;
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(doc));
        }
        catch { /* toast notifications are best-effort */ }
    }

    // Registers the app AUMID in HKCU so ToastNotificationManager can identify
    // this process as a known notifier. Without this, Show() throws on Win32.
    private static void RegisterAppId()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Classes\AppUserModelId\{AppId}");
            key?.SetValue("DisplayName", "Foundry Local Monitor");
        }
        catch { /* best-effort registry write */ }
    }

    private static string Escape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? value;

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
