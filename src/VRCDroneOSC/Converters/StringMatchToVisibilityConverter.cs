using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VRCDroneOSC.Converters;

public class StringMatchToVisibilityConverter : IMultiValueConverter
{
    public static readonly StringMatchToVisibilityConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string a && values[1] is string b)
            return a == b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
