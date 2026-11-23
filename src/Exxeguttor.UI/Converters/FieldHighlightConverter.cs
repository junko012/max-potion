using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Etapa 4.6.4-fix(4) — pivot pragmático: la versión anterior de este converter devolvía un
/// Brush para bindear directo a Background, pero por algún motivo que no logré diagnosticar
/// sin poder compilar yo mismo, esa aproximación nunca pintó nada (ni para EV_TOTAL, con el
/// FieldFix confirmado bien generado — descarta que sea un problema de datos). En vez de
/// seguir insistiendo con Background, este converter ahora devuelve bool para bindear
/// IsVisible en un ícono "!" superpuesto — mismo patrón exacto (bool a través de un
/// converter → IsVisible) que ya se usa en decenas de lugares de esta app y que sabemos que
/// funciona, en vez de reintentar la misma aproximación (Background/Brush) que nunca anduvo.
///
/// Se bindea el Value a <c>PokemonEditorViewModel.HighlightedFieldIds</c> (un
/// <see cref="HashSet{T}"/> de FieldId que se reemplaza entero en cada recálculo en vivo — ver
/// EditSessionService.BuildLiveDiagnosticSteps) y el ConverterParameter al FieldId literal del
/// control puntual (ej. <c>'IV_HP'</c>, <c>'MetLocation'</c>).
/// </summary>
public class FieldHighlightConverter : IValueConverter
{
    public static readonly FieldHighlightConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ISet<string> highlighted) return false;
        if (parameter is not string fieldId) return false;
        return highlighted.Contains(fieldId);
    }

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}
