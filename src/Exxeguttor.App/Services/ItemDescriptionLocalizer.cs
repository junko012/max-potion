using System.Text.RegularExpressions;

namespace Exxeguttor.App.Services;

/// <summary>
/// Descripción de un objeto en el idioma de los DATOS (el mismo que el nombre del objeto). Los textos viven en
/// <c>pokemon.db</c> (tabla <c>ItemDescriptions</c>; MT/MO/DT en <c>MachineMoves</c>, ver hotfixes/fix_item_descriptions).
/// Respaldo: <c>Items.Description</c> (inglés) cuando la base no trae texto o es una base anterior a esas tablas.
/// </summary>
public static class ItemDescriptionLocalizer
{
    private static readonly Regex TeachesPattern = new(
        @"^Teaches\s+(.+?)\s+to a compatible Pokémon\.", RegexOptions.Compiled);

    /// <summary>«Enseña {movimiento} a un Pokémon compatible»: plantilla en el idioma de la UI, movimiento en el de los datos.</summary>
    public static string TeachesMove(int moveId)
    {
        var moves = DataLanguageService.Instance.Strings.Move;
        var name = moveId > 0 && moveId < moves.Count ? moves[moveId] : string.Empty;
        return string.IsNullOrEmpty(name) ? string.Empty : AppText.F("Bag_TmTeaches", name);
    }

    /// <summary>Respaldo para MT/MO/DT sin fila en MachineMoves: deduce el movimiento del texto inglés de la base.</summary>
    public static string LegacyMachine(string englishDescription)
    {
        var m = TeachesPattern.Match(englishDescription ?? string.Empty);
        return m.Success
            ? AppText.F("Bag_TmTeaches", MoveNames.Localize(m.Groups[1].Value))
            : englishDescription ?? string.Empty;
    }

    /// <summary>Consulta puntual (tooltips). Para listas grandes usar <see cref="PokemonDatabase.GetItemDescriptions"/>.</summary>
    public static string Get(int dbItemId, string englishDescription, bool isMachine)
    {
        if (isMachine) return LegacyMachine(englishDescription);
        var map = PokemonDatabase.Instance.GetItemDescriptions(new[] { dbItemId }, DataLanguageService.Instance.PkhexCode);
        return map.TryGetValue(dbItemId, out var text) ? text : englishDescription ?? string.Empty;
    }
}
