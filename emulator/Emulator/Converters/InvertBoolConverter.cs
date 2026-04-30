using System.Globalization;
using Avalonia.Data.Converters;

namespace Emulator.Converters;

/// <summary>Used for the "Fail" radio button — inverts the PaymentSucceeds bool.</summary>
public class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}
