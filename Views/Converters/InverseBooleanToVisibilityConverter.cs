using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DevLauncher.Views.Converters;

/// <summary>Collapsed when true, visible when false.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}
