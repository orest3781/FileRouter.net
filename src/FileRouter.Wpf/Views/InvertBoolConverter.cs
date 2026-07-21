using System.Globalization;
using System.Windows.Data;

namespace FileRouter.Wpf.Views;

/// <summary>Two radio buttons, one bool.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
