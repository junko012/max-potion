using System.Globalization;
using Avalonia.Data.Converters;
using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Muestra el nombre de un tipo en el idioma de los datos del juego. Solo para el TEXTO: el
/// <c>Background</c>/<c>Foreground</c> del mismo badge siguen bindeando la cadena cruda con
/// <see cref="TypeColorConverter"/> (que ya normaliza cualquier idioma vía <see cref="TypeNames"/>).
/// Cualquier cadena que no sea un tipo ("Todos", "Stellar", vacío) pasa sin cambios.
/// </summary>
public class TypeNameConverter : IValueConverter
{
    public static readonly TypeNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? TypeNames.Localize(s) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
