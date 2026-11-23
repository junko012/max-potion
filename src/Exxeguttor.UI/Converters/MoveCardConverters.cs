using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Exxeguttor.UI.Converters;

/// <summary>Primera letra en mayúscula ("physical" → "Physical"). Solo presentación: el valor
/// interno de la clase de daño sigue siendo el de la DB (minúsculas).</summary>
public class CapitalizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        return string.IsNullOrEmpty(s) ? string.Empty : char.ToUpper(s[0], culture) + s[1..];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Punto de PP Ups de la tarjeta de movimiento: <c>value</c> = PP Ups actuales (int),
/// <c>parameter</c> = posición del punto (1..3). Lleno si PP Ups >= posición, vacío si no.</summary>
public class PpUpPipBrushConverter : IValueConverter
{
    private static readonly IBrush On  = new SolidColorBrush(Color.Parse("#7D848E"));
    private static readonly IBrush Off = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int ups = value is int i ? i : 0;
        int pos = int.TryParse(parameter?.ToString(), out var p) ? p : 1;
        return ups >= pos ? On : Off;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
