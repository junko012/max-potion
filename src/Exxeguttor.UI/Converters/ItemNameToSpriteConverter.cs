using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Convierte un nombre de ítem display (ej. "Leftovers", "Restos", "Poke Ball") en su sprite Bitmap.
/// Usado tanto en el ComboBox principal (selected item) como en cada fila del dropdown.
/// </summary>
/// <remarks>
/// Los sprites se buscan por el nombre INGLÉS del ítem. Con el idioma de los datos ≠ inglés el combo trae
/// nombres localizados, así que se traducen primero a inglés por id (mismo índice en la lista de PKHeX de
/// cada idioma). Un nombre que no está en la tabla (ya inglés, "Ball"…) se usa tal cual.
/// </remarks>
public sealed class ItemNameToSpriteConverter : IValueConverter
{
    public static readonly ItemNameToSpriteConverter Instance = new();

    private static string? _indexLang;
    private static Dictionary<string, string> _toEnglish = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name)) return null;
        return SpriteService.Instance.GetItemSprite(ToEnglish(name));
    }

    private static string ToEnglish(string name)
    {
        var code = DataLanguageService.Instance.PkhexCode;
        if (code == DataLanguageService.Fallback) return name;
        if (_indexLang != code) // se reconstruye solo cuando cambia el idioma de los datos
        {
            var localized = DataLanguageService.Instance.Strings.Item;
            var english = GameInfo.GetStrings(DataLanguageService.Fallback).Item;
            var map = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            for (var i = 1; i < localized.Count && i < english.Count; i++)
                if (!string.IsNullOrWhiteSpace(localized[i]) && !string.IsNullOrWhiteSpace(english[i]))
                    map.TryAdd(localized[i], english[i]); // el primer id gana si hay nombres repetidos
            _toEnglish = map;
            _indexLang = code;
        }
        return _toEnglish.TryGetValue(name, out var en) ? en : name;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
