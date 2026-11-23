using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// true  → FontWeight.Bold (nodo "esta especie" en el flujo de evolución del reverso)
/// false → FontWeight.Normal
/// </summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public static readonly BoolToFontWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// true  → fondo celeste tenue (resalta el nodo "esta especie" en la rueda/flujo de
///         evolución del reverso de la tarjeta Pokédex)
/// false → transparente
/// </summary>
public sealed class BoolToHighlightBrushConverter : IValueConverter
{
    public static readonly BoolToHighlightBrushConverter Instance = new();
    private static readonly IBrush Highlight = new SolidColorBrush(Color.Parse("#EEF4FF"));
    private static readonly IBrush None = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Highlight : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
