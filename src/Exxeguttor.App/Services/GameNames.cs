using System.Globalization;
using System.Text;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Nombres de habilidades y objetos escritos en INGLÉS en fuentes externas (sets de Showdown/Smogon,
/// tablas de dominio) → id de PKHeX → nombre en el idioma de los datos (plan i18n, Fase 3, Sets
/// recomendados). Misma idea que <see cref="MoveNames"/>: el inglés es solo la llave de la fuente, nunca el
/// texto mostrado. Un nombre sin id (variantes de Showdown como «Hidden Power Fire») pasa sin cambios.
/// </summary>
public static class GameNames
{
    /// <summary>Clave de comparación: sin mayúsculas, sin acentos, sin espacios ni signos
    /// («Poké Ball» == «Poke Ball» == «pokeball», «Will-O-Wisp» == «will o wisp»).</summary>
    internal static string Norm(string v)
    {
        var sb = new StringBuilder(v.Length);
        foreach (var c in v.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static Dictionary<string, int>? _abilities, _items;

    private static Dictionary<string, int> Index(IReadOnlyList<string> english)
    {
        var d = new Dictionary<string, int>();
        for (var id = 1; id < english.Count; id++)
            if (!string.IsNullOrWhiteSpace(english[id])) d.TryAdd(Norm(english[id]), id); // el primer id gana
        return d;
    }

    public static int AbilityId(string? englishName)
        => string.IsNullOrWhiteSpace(englishName) ? 0
            : (_abilities ??= Index(GameInfo.GetStrings("en").Ability)).GetValueOrDefault(Norm(englishName));

    public static int ItemId(string? englishName)
        => string.IsNullOrWhiteSpace(englishName) ? 0
            : (_items ??= Index(GameInfo.GetStrings("en").Item)).GetValueOrDefault(Norm(englishName));

    /// <summary>Habilidad en el idioma de los datos activo (o el texto original si no se resuelve).</summary>
    public static string Ability(string? englishName)
        => Ability(englishName, DataLanguageService.Instance.Strings.Ability);

    /// <summary>Objeto en el idioma de los datos activo (o el texto original si no se resuelve).</summary>
    public static string Item(string? englishName)
        => Item(englishName, DataLanguageService.Instance.Strings.Item);

    internal static string Ability(string? englishName, IReadOnlyList<string> target)
        => Pick(AbilityId(englishName), target, englishName);

    internal static string Item(string? englishName, IReadOnlyList<string> target)
        => Pick(ItemId(englishName), target, englishName);

    /// <summary>Nombre de un movimiento por id en el idioma de los datos activo; null si PKHeX no lo tiene
    /// (el llamador cae a pokemon.db o a «movimiento #id»).</summary>
    public static string? MoveName(int id) => NameAt(DataLanguageService.Instance.Strings.Move, id);

    /// <summary>Nombre de una especie por id en el idioma de los datos activo; null si PKHeX no lo tiene.</summary>
    public static string? SpeciesName(int id) => NameAt(DataLanguageService.Instance.Strings.Species, id);

    private static string? NameAt(IReadOnlyList<string> list, int id)
        => id > 0 && id < list.Count && !string.IsNullOrEmpty(list[id]) ? list[id] : null;

    private static string Pick(int id, IReadOnlyList<string> target, string? fallback)
        => id > 0 && id < target.Count && !string.IsNullOrEmpty(target[id]) ? target[id] : fallback ?? string.Empty;
}
