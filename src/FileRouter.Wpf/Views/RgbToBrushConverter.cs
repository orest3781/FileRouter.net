using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.Views;

/// <summary>Bridges the view models' WPF-free Rgb colors into brushes.</summary>
public sealed class RgbToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Rgb c ? ThemeManager.Brush(c) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
