using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Mezcla el color saturado de un tipo (de <see cref="TypeColorConverter"/>) hacia blanco,
/// para usar como fondo tenue de tarjeta — mismo estilo pastel que
/// <see cref="DamageClassToBrushConverter"/> en el tab Moves, pero generalizado a los 18 tipos
/// en vez de solo 3 clases de daño.
/// </summary>
public class TypeToSoftBrushConverter : IValueConverter
{
    public static readonly TypeToSoftBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var baseColor = TypeColorConverter.GetBaseColor(value as string);
        return new SolidColorBrush(Blend(baseColor, Colors.White, 0.86));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static Color Blend(Color color, Color toward, double towardRatio)
    {
        byte Mix(byte a, byte b) => (byte)(a * (1 - towardRatio) + b * towardRatio);
        return Color.FromRgb(Mix(color.R, toward.R), Mix(color.G, toward.G), Mix(color.B, toward.B));
    }
}

/// <summary>Igual que <see cref="TypeToSoftBrushConverter"/> pero para el borde (mezcla menos hacia blanco).</summary>
public class TypeToSoftBorderConverter : IValueConverter
{
    public static readonly TypeToSoftBorderConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var baseColor = TypeColorConverter.GetBaseColor(value as string);
        return new SolidColorBrush(TypeToSoftBrushConverter.Blend(baseColor, Colors.White, 0.45));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Borde de tarjeta por tipo que se intensifica cuando la mecánica está activa (Mega equipada,
/// Z-Move disponible, etc.) — mismo color base que <see cref="TypeToSoftBorderConverter"/>,
/// pero mezclado menos hacia blanco (más saturado) cuando el segundo valor (bool) es true.
/// Uso: <c>&lt;MultiBinding Converter="{StaticResource TypeToActiveBorder}"&gt;
/// &lt;Binding Path="MegaType1"/&gt;&lt;Binding Path="MegaCompatible"/&gt;&lt;/MultiBinding&gt;</c>
/// </summary>
public class TypeToActiveBorderConverter : IMultiValueConverter
{
    public static readonly TypeToActiveBorderConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = values.Count > 0 ? values[0] as string : null;
        var active = values.Count > 1 && values[1] is true;
        var baseColor = TypeColorConverter.GetBaseColor(type);
        var ratio = active ? 0.10 : 0.45;
        return new SolidColorBrush(TypeToSoftBrushConverter.Blend(baseColor, Colors.White, ratio));
    }
}
