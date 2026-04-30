using System.Globalization;
using Avalonia.Data.Converters;

namespace Terminal.Converters;

/// <summary>
/// Returns TrueValue when the bound bool is true, FalseValue otherwise.
/// Used in AXAML where a simple text label needs to change based on a bool.
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public string TrueValue  { get; set; } = "";
    public string FalseValue { get; set; } = "";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
