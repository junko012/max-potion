using System.Globalization;
using Avalonia.Data.Converters;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// true  → 1.0 (campo activo, completamente visible)
/// false → 0.3 (campo no disponible en esta generación, atenuado)
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.3;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
