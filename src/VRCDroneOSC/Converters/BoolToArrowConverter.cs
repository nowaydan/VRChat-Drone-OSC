using System;
using System.Globalization;
using System.Windows.Data;

namespace VRCDroneOSC.Converters;

/// <summary>
/// Converts a boolean to an arrow character: true = down arrow, false = right arrow.
/// Used for expandable panel toggle buttons.
/// </summary>
public class BoolToArrowConverter : IValueConverter
{
    public static readonly BoolToArrowConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Unicode triangles: expanded (down) vs collapsed (right)
        return value is true ? "\u25BC" : "\u25B6";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
