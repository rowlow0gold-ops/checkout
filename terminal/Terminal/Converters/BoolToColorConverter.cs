using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Terminal.Converters;

/// <summary>
/// Converts a bool hardware-status flag → green (connected) or red (disconnected).
/// Used in MainWindow.axaml for the Scanner / Payment / Printer indicator dots.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new SolidColorBrush(Color.Parse("#22C55E"))   // green-500
                         : new SolidColorBrush(Color.Parse("#EF4444"));  // red-500

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
