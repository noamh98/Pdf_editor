using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PdfEditor.Core.Annotations;

namespace PdfEditor.App.Views;

/// <summary>Renders an annotation colour as a brush for a swatch button.</summary>
public sealed class AnnotationColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AnnotationColor c
            ? new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B))
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Bolds the current page number in the thumbnail list, so the selection is signalled by weight as
/// well as by the accent border rather than by colour alone.
/// </summary>
public sealed class BoolToWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.SemiBold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
