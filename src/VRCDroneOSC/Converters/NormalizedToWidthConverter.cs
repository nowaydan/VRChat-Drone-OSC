using System;
using System.Globalization;
using System.Windows.Data;

namespace VRCDroneOSC.Converters;

/// <summary>
/// Multiplies a normalised 0..1 value by the panel width (passed as parameter)
/// so that an axis bar fills proportionally inside a fixed-width track.
/// Usage: {Binding NormalizedValue, Converter={StaticResource NormToWidth}, ConverterParameter=200}
/// </summary>
public class NormalizedToWidthConverter : IValueConverter
{
    public static readonly NormalizedToWidthConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double norm = value is double d ? d : 0.0;
        double width = parameter is string s && double.TryParse(s, out double w) ? w : 200.0;
        return Math.Clamp(norm * width, 0, width);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
