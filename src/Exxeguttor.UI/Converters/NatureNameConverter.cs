using System.Globalization;
using Avalonia.Data.Converters;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Muestra una naturaleza (llave inglesa "Adamant", o ya en el idioma de los datos) con su nombre en el
/// idioma de los datos activo. Solo para TEXTO — la llave sigue siendo el nombre inglés. Una cadena que no
/// es una naturaleza ("", "—") pasa sin cambios.
/// </summary>
public sealed class NatureNameConverter : IValueConverter
{
    public static readonly NatureNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && NatureDatabase.Find(s) is { } n ? n.LocalizedName : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
