using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Convierte la clase de daño ("physical","special","status") al color
/// de fondo tenue de la tarjeta de movimiento.
/// </summary>
public class DamageClassToBrushConverter : IValueConverter
{
    public static readonly DamageClassToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string ?? string.Empty) switch
        {
            "physical" => new SolidColorBrush(Color.Parse("#F5D9C0")),
            "special"  => new SolidColorBrush(Color.Parse("#CBDEF5")),
            "status"   => new SolidColorBrush(Color.Parse("#D9EAC5")),
            _          => new SolidColorBrush(Color.Parse("#E8E8E8")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte la clase de daño al color del borde de la tarjeta.
/// </summary>
public class DamageClassToBorderConverter : IValueConverter
{
    public static readonly DamageClassToBorderConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string ?? string.Empty) switch
        {
            "physical" => new SolidColorBrush(Color.Parse("#C97D3F")),
            "special"  => new SolidColorBrush(Color.Parse("#5588BE")),
            "status"   => new SolidColorBrush(Color.Parse("#6FA23A")),
            _          => new SolidColorBrush(Color.Parse("#AAAAAA")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte la clase de daño al color del punto indicador en el learnset.
/// </summary>
public class DamageClassDotConverter : IValueConverter
{
    public static readonly DamageClassDotConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string ?? string.Empty) switch
        {
            "physical" => new SolidColorBrush(Color.Parse("#C14A1A")),
            "special"  => new SolidColorBrush(Color.Parse("#2860B0")),
            "status"   => new SolidColorBrush(Color.Parse("#4A8220")),
            _          => new SolidColorBrush(Color.Parse("#888888")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte bool IsLegal → color de fondo del badge de legalidad.
/// true  → verde  #2E7D32
/// false → rojo   #C62828
/// </summary>
public class LegalityColorConverter : IValueConverter
{
    public static readonly LegalityColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isLegal = value is bool b && b;
        return new SolidColorBrush(isLegal ? Color.Parse("#2E7D32") : Color.Parse("#C62828"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte bool IsOk → color de fondo CLARO para la píldora de categoría del modal de
/// revisión (mismo verde/rojo que LegalityColorConverter, pero como tinte suave para fondo
/// en vez de color sólido para texto/borde) — antes solo la categoría "Creación" tenía este
/// look de píldora con borde; ahora se aplica a TODAS las categorías cambiadas.
/// </summary>
public class LegalityBackgroundTintConverter : IValueConverter
{
    public static readonly LegalityBackgroundTintConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isOk = value is bool b && b;
        return new SolidColorBrush(Color.Parse(isOk ? "#EAF6F0" : "#FBEAEA"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte bool IsLegal → ícono del badge.
/// true  → ✓
/// false → ✗
/// </summary>
public class LegalityIconConverter : IValueConverter
{
    public static readonly LegalityIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "✓" : "✗";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
