using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Exxeguttor.UI.Converters;

/// <summary>Color de letra legible sobre el color de un tipo: oscuro sobre los tipos claros
/// (Electric, Ground, Ice, Steel, Fairy) y blanco sobre el resto. Usa el mismo color base que
/// <see cref="TypeColorConverter"/>, así que el par fondo/letra siempre es consistente.</summary>
public class TypeTextColorConverter : IValueConverter
{
    private static readonly IBrush Dark  = new SolidColorBrush(Color.Parse("#2C2C2A"));
    private static readonly IBrush Light = Brushes.White;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var c = TypeColorConverter.GetBaseColor(value as string);
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance > 0.62 ? Dark : Light;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
