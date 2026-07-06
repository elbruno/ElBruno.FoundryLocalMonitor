using System.Windows;
using System.Windows.Controls;
using ElBruno.FoundryLocalMonitor.Configuration;
using ElBruno.FoundryLocalMonitor.Services;

namespace ElBruno.FoundryLocalMonitor;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadValues();
    }

    private void LoadValues()
    {
        PollingIntervalBox.Text = _settings.PollingIntervalSeconds.ToString();
        EndpointBox.Text = _settings.FoundryEndpointOverride ?? "";
        NotifyOnLoadBox.IsChecked = _settings.ShowNotificationsOnLoad;
        NotifyOnUnloadBox.IsChecked = _settings.ShowNotificationsOnUnload;
        StartMinimizedBox.IsChecked = _settings.StartMinimizedToTray;

        foreach (ComboBoxItem item in ThemeBox.Items)
        {
            if (item.Content?.ToString() == _settings.Theme)
            { ThemeBox.SelectedItem = item; break; }
        }
        if (ThemeBox.SelectedItem == null) ThemeBox.SelectedIndex = 0;

        foreach (ComboBoxItem item in NotificationFilterBox.Items)
        {
            if (item.Content?.ToString() == _settings.NotificationFilter)
            { NotificationFilterBox.SelectedItem = item; break; }
        }
        if (NotificationFilterBox.SelectedItem == null) NotificationFilterBox.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(PollingIntervalBox.Text, out var interval) && interval >= 1 && interval <= 300)
            _settings.PollingIntervalSeconds = interval;

        var endpoint = EndpointBox.Text.Trim();
        _settings.FoundryEndpointOverride = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
        _settings.ShowNotificationsOnLoad = NotifyOnLoadBox.IsChecked == true;
        _settings.ShowNotificationsOnUnload = NotifyOnUnloadBox.IsChecked == true;
        _settings.StartMinimizedToTray = StartMinimizedBox.IsChecked == true;

        var selectedTheme = (ThemeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "System";
        _settings.Theme = selectedTheme;
        ThemeManager.Apply(selectedTheme);

        _settings.NotificationFilter = (NotificationFilterBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Daemon only";

        SettingsService.Save(_settings);
        Hide();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
