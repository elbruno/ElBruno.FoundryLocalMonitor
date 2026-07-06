using System.Windows;
using Microsoft.Win32;

namespace ElBruno.FoundryLocalMonitor.Services;

/// <summary>
/// Swaps the active theme ResourceDictionary at runtime.
/// Supported values: "System" (follows OS), "Light", "Dark".
/// </summary>
public static class ThemeManager
{
    private const string DarkThemeUri  = "/Resources/Themes/Dark.xaml";
    private const string LightThemeUri = "/Resources/Themes/Light.xaml";

    /// <summary>
    /// Applies the requested theme. Safe to call at any time — always runs on the UI thread.
    /// </summary>
    public static void Apply(string theme)
    {
        var uri = ResolveUri(theme);
        SwapThemeDictionary(uri);
    }

    private static string ResolveUri(string theme) => theme switch
    {
        "Light"  => LightThemeUri,
        "Dark"   => DarkThemeUri,
        _        => IsSystemDark() ? DarkThemeUri : LightThemeUri   // "System" or unknown
    };

    /// <summary>Reads the Windows "AppsUseLightTheme" registry value.</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;   // 0 = dark, 1 = light
        }
        catch
        {
            return true;   // default to dark on error
        }
    }

    private static void SwapThemeDictionary(string newUri)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        // The theme dict is always the first MergedDictionary inside AppStyles.xaml,
        // which is itself the first entry in Application.Resources.MergedDictionaries.
        var appStyles = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("AppStyles") == true);

        if (appStyles == null) return;

        var newDict = new ResourceDictionary
        {
            Source = new Uri(newUri, UriKind.Relative)
        };

        if (appStyles.MergedDictionaries.Count > 0)
            appStyles.MergedDictionaries[0] = newDict;
        else
            appStyles.MergedDictionaries.Add(newDict);
    }
}
