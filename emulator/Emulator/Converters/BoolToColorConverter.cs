using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Emulator.Converters;

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new SolidColorBrush(Color.Parse("#22C55E"))
                         : new SolidColorBrush(Color.Parse("#EF4444"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
