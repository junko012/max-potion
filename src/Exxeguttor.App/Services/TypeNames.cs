using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Puente entre la LLAVE de un tipo (nombre inglés, como lo entrega pokemon.db: "Fire", "fire") y su
/// TEXTO en el idioma de los datos (<see cref="DataLanguageService"/>). Plan i18n, Fase 3 tanda 3.
/// </summary>
/// <remarks>
/// El proyecto usa la cadena del tipo como llave de colores, iconos y efectividad
/// (<c>TypeColorConverter</c>, <c>TypeEffectivenessDatabase</c>…). Por eso esas llaves NO se
/// traducen: los ViewModels siguen guardando el nombre tal cual y solo el texto que se pinta pasa
/// por <see cref="Localize"/>. Algunos valores del editor ya vienen en el idioma de los datos
/// (<c>strings.Types[...]</c>: Tera, Poder Oculto), así que <see cref="IndexOf"/> acepta el nombre
/// inglés Y el del idioma activo, y <see cref="ToKey"/> los devuelve siempre a la llave inglesa.
/// Solo se consultan los 18 tipos base; "Stellar" y cualquier otro texto pasan sin tocar.
/// </remarks>
public static class TypeNames
{
    private static readonly string[] English =
    {
        "Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel",
        "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark", "Fairy",
    };

    /// <summary>Índice PKHeX del tipo (0 = Normal … 17 = Fairy), o -1 si no es un tipo base.
    /// Acepta el nombre en inglés (cualquier capitalización) o en el idioma de los datos activo.</summary>
    public static int IndexOf(string? name)
        => IndexOf(name, DataLanguageService.Instance.Strings.Types);

    /// <remarks>Overload con la lista de nombres explícita — permite probar cada idioma sin tocar
    /// el singleton (que lee/escribe las preferencias reales del usuario).</remarks>
    internal static int IndexOf(string? name, IReadOnlyList<string> localizedTypes)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        var n = name.Trim();
        for (var i = 0; i < English.Length; i++)
            if (string.Equals(English[i], n, StringComparison.OrdinalIgnoreCase)) return i;

        for (var i = 0; i < English.Length && i < localizedTypes.Count; i++)
            if (string.Equals(localizedTypes[i], n, StringComparison.CurrentCultureIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Llave inglesa en minúsculas ("fire") — la que esperan colores y efectividad.
    /// Si no es un tipo base, devuelve el texto tal cual.</summary>
    public static string ToKey(string? name) => ToKey(name, DataLanguageService.Instance.Strings.Types);

    internal static string ToKey(string? name, IReadOnlyList<string> localizedTypes)
    {
        var i = IndexOf(name, localizedTypes);
        return i >= 0 ? English[i].ToLowerInvariant() : name ?? string.Empty;
    }

    /// <summary>Nombre del tipo en el idioma de los datos activo; si no es un tipo base (o PKHeX no
    /// tiene el nombre), devuelve el texto original.</summary>
    public static string Localize(string? name)
    {
        var list = DataLanguageService.Instance.Strings.Types;
        return Localize(name, list, list);
    }

    /// <summary>Localiza <paramref name="name"/> (inglés o de <paramref name="inputTypes"/>) a la lista destino.</summary>
    internal static string Localize(string? name, IReadOnlyList<string> inputTypes, IReadOnlyList<string> targetTypes)
    {
        var i = IndexOf(name, inputTypes);
        if (i < 0) return name ?? string.Empty;
        return i < targetTypes.Count && !string.IsNullOrEmpty(targetTypes[i]) ? targetTypes[i] : name ?? string.Empty;
    }
}
