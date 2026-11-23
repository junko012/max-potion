using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Convierte un double (0-1, fracción del ancho total) en un GridLength Star.
/// Permite usar fracciones calculadas en el ViewModel para dimensionar columnas
/// de una barra segmentada de stats sin código en el code-behind.
/// Ej: 0.35 → GridLength(35, GridUnitType.Star)
/// </summary>
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public static readonly DoubleToGridLengthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value is double d ? Math.Max(0, d) : 0;
        // Usamos escala x100 para que valores pequeños (ej. 0.05) den 5* en vez de 0*
        return new GridLength(Math.Round(v * 100, 2), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
