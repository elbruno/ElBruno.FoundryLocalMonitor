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
            var d when d.Contains("GPU") => new SolidColorBrush(WpfColor.FromRgb(22, 163, 74)),   // green
            var d when d.Contains("NPU") => new SolidColorBrush(WpfColor.FromRgb(147, 51, 234)),  // purple
            var d when d.Contains("CPU") => new SolidColorBrush(WpfColor.FromRgb(37, 99, 235)),   // blue
            _                            => new SolidColorBrush(WpfColor.FromRgb(107, 114, 128))  // gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
