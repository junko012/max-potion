using System.Text.RegularExpressions;

namespace Exxeguttor.App.Services;

/// <summary>
/// Para ítems TM/HM/TR (<c>Items.Category == "all-machines"</c> en pokemon.db), la
/// <c>Description</c> cruda es prosa libre que menciona el movimiento de VARIAS
/// generaciones a la vez en una sola oración — ej. para TM01:
/// <c>"Teaches Hone Claws to a compatible Pokémon. (Gen IV &amp; III: Focus Punch Gen II:
/// DynamicPunch Gen I: Mega Punch)"</c>. Esto viene de que Gen1-5 reciclan el mismo roster de
/// TMs/MOs, cambiando qué movimiento enseña cada slot entre generaciones — confuso mostrado tal
/// cual cuando el usuario está mirando el save de UNA generación puntual (reportado en sesión:
/// "veo que para TMs... sale un texto que dice que para tal generación es tal ataque").
///
/// Esto resuelve, para la generación del save actualmente cargado, cuál es el movimiento real:
/// el override explícito si esa generación está listada entre paréntesis, si no el movimiento
/// "por defecto" al principio de la oración (cubre la generación más reciente del roster
/// reciclado y cualquier generación intermedia no mencionada explícitamente).
///
/// De Gen6 en adelante cada juego tiene su propio set de MTs sin reciclar entre generaciones,
/// así que ahí la Description ya viene sin paréntesis (un solo movimiento) — <see cref="Resolve"/>
/// la devuelve intacta en ese caso, no hay nada que resolver.
///
/// TRs de Espada/Escudo (Category también "all-machines") no tienen Description cargada en
/// pokemon.db en absoluto (gap conocido en la fuente — ver items.json del hotfix
/// `fix_database`, campo vacío ya en origen) — se devuelve tal cual (vacío), nada que resolver.
/// </summary>
public static class TmDescriptionResolver
{
    // Un override es "Gen <romano>[ & <romano>...]: <movimiento>", terminando donde empieza el
    // siguiente "Gen <romano>:" o al final del string.
    private static readonly Regex OverridePattern = new(
        @"Gen\s+([IVX]+(?:\s*&\s*[IVX]+)*)\s*:\s*(.+?)(?=\s*Gen\s+[IVX]+(?:\s*&\s*[IVX]+)*\s*:|\)?\s*$)",
        RegexOptions.Compiled);

    private static readonly Regex LeadingMovePattern = new(
        @"^Teaches\s+(.+?)\s+to a compatible Pokémon\.",
        RegexOptions.Compiled);

    /// <param name="rawDescription">Items.Description tal cual sale de pokemon.db.</param>
    /// <param name="generation">save.Generation del save actualmente cargado (1-9).</param>
    public static string Resolve(string rawDescription, int generation)
    {
        if (string.IsNullOrEmpty(rawDescription)) return rawDescription;

        var leadingMatch = LeadingMovePattern.Match(rawDescription);
        if (!leadingMatch.Success) return rawDescription; // formato inesperado (ítem no-TM, etc.) — no tocar

        foreach (Match m in OverridePattern.Matches(rawDescription))
        {
            var romans = m.Groups[1].Value.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!romans.Any(r => RomanToGen(r) == generation)) continue;

            var move = m.Groups[2].Value.Trim();
            return $"Teaches {move} to a compatible Pokémon.";
        }

        // Ninguna generación vieja listada coincide -> el movimiento "por defecto" (principio
        // de la oración) es el que corresponde a esta generación.
        return $"Teaches {leadingMatch.Groups[1].Value} to a compatible Pokémon.";
    }

    private static int RomanToGen(string roman) => roman.Trim() switch
    {
        "I" => 1, "II" => 2, "III" => 3, "IV" => 4, "V" => 5,
        "VI" => 6, "VII" => 7, "VIII" => 8, "IX" => 9,
        _ => -1
    };
}
