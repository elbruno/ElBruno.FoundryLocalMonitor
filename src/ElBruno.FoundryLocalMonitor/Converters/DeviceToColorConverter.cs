using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace ElBruno.FoundryLocalMonitor.Converters;

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class DeviceToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var device = (value as string ?? "").ToUpperInvariant();
        return device switch
        {
            "TENSORRT"  => new SolidColorBrush(WpfColor.FromRgb(16, 185, 129)),   // emerald
            "CUDA"      => new SolidColorBrush(WpfColor.FromRgb(22, 163, 74)),    // green
            "GPU"       => new SolidColorBrush(WpfColor.FromRgb(22, 163, 74)),    // green
            "DIRECTML"  => new SolidColorBrush(WpfColor.FromRgb(147, 51, 234)),   // purple
            "WINML"     => new SolidColorBrush(WpfColor.FromRgb(234, 88, 12)),    // orange
            "NPU"       => new SolidColorBrush(WpfColor.FromRgb(219, 39, 119)),   // pink
            "CPU"       => new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)),    // blue
            _           => new SolidColorBrush(WpfColor.FromRgb(107, 114, 128))   // gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts a full endpoint URL ("http://localhost:55588") to a short port label (":55588").</summary>
[ValueConversion(typeof(string), typeof(string))]
public class EndpointToPortConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string ep || string.IsNullOrWhiteSpace(ep)) return "";
        if (Uri.TryCreate(ep, UriKind.Absolute, out var uri))
            return $":{uri.Port}";
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

