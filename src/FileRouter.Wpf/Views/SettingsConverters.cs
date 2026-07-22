using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FileRouter.Wpf.Theme;

namespace FileRouter.Wpf.Views;

/// <summary>Config color string ("#2e7d32" / "red") → brush; transparent for
/// blank or invalid. Used for the color chips in the settings lists.</summary>
public sealed class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemePalette.ParseColor(value as string) is { } c
            ? ThemeManager.Brush(c)
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Color string → the black/white brush that contrasts with it —
/// the ✓ on the selected swatch stays readable on any swatch color.</summary>
public sealed class ColorStringToForeBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemePalette.ParseColor(value as string) is { } c
            ? ThemeManager.Brush(ThemePalette.IdealForeground(c))
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Count → Visible when zero (empty-state hints).</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i == 0
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>[swatch color, currently chosen color] → "✓" when they match —
/// marks the selected swatch in the palette strip.</summary>
public sealed class SwatchCheckConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var swatch = (values.ElementAtOrDefault(0) as string)?.Trim() ?? "";
        var chosen = (values.ElementAtOrDefault(1) as string)?.Trim() ?? "";
        return swatch.Length > 0 && swatch.Equals(chosen, StringComparison.OrdinalIgnoreCase)
            ? "✓" : "";
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
