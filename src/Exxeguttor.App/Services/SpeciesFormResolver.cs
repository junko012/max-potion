using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Resuelve (especie, índice de forma de PKHeX) → fila de <c>Species</c> de pokemon.db que tiene
/// los datos PROPIOS de esa forma (tipos, stats base, habilidades). Sin esto, un Exeggutor de
/// Alola o un Giratina Origen consultaban la fila de la especie base y mostraban sus datos.
///
/// pokemon.db guarda cada forma como una fila aparte con id &gt; 10000 (estilo PokeAPI:
/// "exeggutor-alola" = 10114, "giratina-origin" = 10007). Dos caminos:
///  • Formas regionales (<see cref="RegionalFormCatalog"/>): por nombre de fila — ya validado en
///    las 57 formas.
///  • Cualquier otra forma guardable (Giratina, Deoxys, Rotom, Kyurem, Therian...): por CONTENIDO.
///    Se toma la entrada de PKHeX de esa forma (stats base + tipos) y se elige, entre la fila base
///    y las filas de forma de la misma especie, la que más se le parece. Si la forma no cambia
///    datos (Unown, Vivillon, colores...) gana la fila base, así que no hay riesgo de "inventar"
///    una forma: lo peor que puede pasar es caer a la especie base.
/// </summary>
public static class SpeciesFormResolver
{
    private sealed record Entry(int DbSpeciesId, IReadOnlyList<string> Types);

    private static readonly object Lock = new();
    private static (Dictionary<int, PokemonDatabase.DbFormRow> Bases, List<PokemonDatabase.DbFormRow> Forms)? _source;
    private static readonly Dictionary<(int, byte), Entry?> Cache = new();

    /// <summary>Id de la fila de <c>Species</c> a usar para (especie, forma). Forma 0 o forma sin
    /// fila propia → la propia especie.</summary>
    public static int ResolveDbSpeciesId(int species, byte form) =>
        form == 0 ? species : Get(species, form)?.DbSpeciesId ?? species;

    /// <summary>True si (especie, forma) tiene fila propia de forma en la DB.</summary>
    public static bool HasOwnRow(int species, byte form) => ResolveDbSpeciesId(species, form) != species;

    /// <summary>Tipos de la forma, en el formato del selector ("Grass", "Dragon"). Vacío si la
    /// forma no tiene fila propia.</summary>
    public static IReadOnlyList<string> GetFormTypes(int species, byte form) =>
        form != 0 && Get(species, form) is { } e && e.DbSpeciesId != species ? e.Types : [];

    private static Entry? Get(int species, byte form)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue((species, form), out var cached)) return cached;
            // Si la DB no está disponible no se cachea nada: se reintenta en la próxima llamada.
            _source ??= PokemonDatabase.Instance.LoadFormSource();
            if (_source is not { } src) return null;

            var entry = RegionalFormCatalog.Find(species, form) is { } rf
                ? ByName(src, rf)
                : ByContent(src, species, form);
            entry ??= Base(src, species);
            return Cache[(species, form)] = entry;
        }
    }

    private static Entry? Base(
        (Dictionary<int, PokemonDatabase.DbFormRow> Bases, List<PokemonDatabase.DbFormRow> Forms) src, int species) =>
        src.Bases.TryGetValue(species, out var b) ? new Entry(species, b.Types.Select(Capitalize).ToList()) : null;

    // ── Formas regionales: por nombre de fila ──────────────────────────────────

    private static Entry? ByName(
        (Dictionary<int, PokemonDatabase.DbFormRow> Bases, List<PokemonDatabase.DbFormRow> Forms) src, RegionalForm rf)
    {
        if (!src.Bases.TryGetValue(rf.Species, out var b)) return null;
        var baseName = b.Name;
        // La fila base de Darmanitan se llama "darmanitan-standard"; las de forma, "darmanitan-galar-*".
        if (baseName.EndsWith("-standard", StringComparison.Ordinal)) baseName = baseName[..^"-standard".Length];
        // "Paldea Combat" -> "tauros-paldea-combat" (la fila es "tauros-paldea-combat-breed").
        var key = $"{baseName}-{rf.Region.ToLowerInvariant().Replace(' ', '-')}";

        var row = src.Forms
            .Where(f => f.Name == key || f.Name.StartsWith(key + "-", StringComparison.Ordinal))
            .Where(f => !f.Name.Contains("totem") && !f.Name.Contains("cap") && !f.Name.Contains("zen"))
            .OrderBy(f => f.Name.Length)
            .FirstOrDefault();
        return row == null ? null : new Entry(row.Id, row.Types.Select(Capitalize).ToList());
    }

    // ── Resto de formas guardables: por contenido ──────────────────────────────

    // De la más nueva a la más vieja: la primera donde la forma existe da sus datos de referencia.
    private static readonly IPersonalTable[] Tables =
    [
        PersonalTable.ZA, PersonalTable.SV, PersonalTable.SWSH, PersonalTable.LA, PersonalTable.BDSP,
        PersonalTable.GG, PersonalTable.USUM, PersonalTable.SM, PersonalTable.AO, PersonalTable.XY,
        PersonalTable.B2W2, PersonalTable.BW, PersonalTable.HGSS, PersonalTable.Pt, PersonalTable.DP,
    ];

    private static Entry? ByContent(
        (Dictionary<int, PokemonDatabase.DbFormRow> Bases, List<PokemonDatabase.DbFormRow> Forms) src, int species, byte form)
    {
        if (!src.Bases.TryGetValue(species, out var baseRow)) return null;
        var reference = GetReference((ushort)species, form);
        if (reference is null) return null;

        // Mismas filas "de la misma especie": el primer tramo del nombre ("giratina", "deoxys", "mr").
        var stem = baseRow.Name.Split('-')[0] + "-";
        var best = baseRow;
        var bestScore = Score(baseRow, reference.Value);
        foreach (var f in src.Forms.Where(f => f.Name.StartsWith(stem, StringComparison.Ordinal)))
        {
            // Mega, Gigamax, Primigenia, Totem... son formas de batalla: nunca se guardan en un save.
            if (IsBattleOnlyRow(f.Name)) continue;
            var score = Score(f, reference.Value);
            if (score < bestScore) { best = f; bestScore = score; }   // empate → gana la base
        }
        return new Entry(best.Id, best.Types.Select(Capitalize).ToList());
    }

    private static bool IsBattleOnlyRow(string name) =>
        name.Contains("-mega") || name.Contains("-gmax") || name.Contains("-primal") || name.Contains("-totem");

    private readonly record struct Reference(int[] Stats, string Type1, string Type2, HashSet<string> Abilities);

    private static Reference? GetReference(ushort species, byte form)
    {
        var typeNames = GameInfo.GetStrings("en").Types;
        foreach (var table in Tables)
        {
            try
            {
                if (!table.IsPresentInGame(species, form)) continue;
                var info = table.GetFormEntry(species, form);
                var t = (IPersonalType)info;
                var abilityNames = GameInfo.GetStrings("en").Ability;
                var abilities = new HashSet<string>();
                for (var i = 0; i < info.AbilityCount; i++)
                {
                    var id = info.GetAbilityAtIndex(i);
                    if (id > 0 && id < abilityNames.Count) abilities.Add(Norm(abilityNames[id]));
                }
                return new Reference(
                    [info.HP, info.ATK, info.DEF, info.SPA, info.SPD, info.SPE],
                    typeNames[t.Type1].ToLowerInvariant(), typeNames[t.Type2].ToLowerInvariant(), abilities);
            }
            catch (Exception) { /* tabla sin esa forma: probar la siguiente */ }
        }
        return null;
    }

    /// <summary>Distancia entre una fila de la DB y la entrada de PKHeX: tipos distintos pesan
    /// más que habilidades distintas, y estas más que cualquier diferencia de stats. Las
    /// habilidades hacen falta porque hay formas que SOLO cambian eso (Basculin, Meowstic hembra,
    /// Toxtricity Low Key).</summary>
    private static int Score(PokemonDatabase.DbFormRow row, Reference r)
    {
        var t1 = row.Types.Count > 0 ? row.Types[0] : string.Empty;
        var t2 = row.Types.Count > 1 ? row.Types[1] : t1;   // PKHeX repite el tipo si es simple
        var score = (t1 == r.Type1 ? 0 : 1000) + (t2 == r.Type2 ? 0 : 1000);
        for (var i = 0; i < 6; i++) score += Math.Abs(row.Stats[i] - r.Stats[i]);
        var rowAbilities = row.Abilities.Select(Norm).ToHashSet();
        score += 100 * (rowAbilities.Except(r.Abilities).Count() + r.Abilities.Except(rowAbilities).Count());
        return score;
    }

    /// <summary>Solo letras/dígitos en minúscula: "neutralizing-gas" y "Neutralizing Gas" coinciden.</summary>
    private static string Norm(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
