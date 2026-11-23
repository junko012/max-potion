using System.Linq;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Tabla estática de Mega Stones (Gen 6 XY/ORAS + Gen 7 SM/USUM).
/// No viene de pokemon.db porque la tabla Items no tiene vínculo a especie —
/// mismo criterio que NatureDatabase: dato de dominio fijo, no consultable por SQL.
///
/// Clave = ItemId crudo de PKHeX (coincide con pokemon.db.Items.ItemId).
/// BaseSpeciesId  = especie que debe tener el Pokémon para que la piedra aplique.
/// MegaSpeciesId  = fila de pokemon.db.Species correspondiente a la forma Mega
///                  (rango 10033+, ya presente en la DB con stats propios).
/// </summary>
public static class MegaStoneDatabase
{
    public sealed record MegaStoneEntry(int ItemId, int BaseSpeciesId, int MegaSpeciesId, string MegaSlug);

    private static readonly MegaStoneEntry[] Entries =
    {
        new(656, 94,  10038, "gengar-mega"),
        new(657, 282, 10051, "gardevoir-mega"),
        new(658, 181, 10045, "ampharos-mega"),
        new(659, 3,   10033, "venusaur-mega"),
        new(660, 6,   10034, "charizard-mega-x"),
        new(661, 9,   10036, "blastoise-mega"),
        new(662, 150, 10043, "mewtwo-mega-x"),
        new(663, 150, 10044, "mewtwo-mega-y"),
        new(664, 257, 10050, "blaziken-mega"),
        new(665, 308, 10054, "medicham-mega"),
        new(666, 229, 10048, "houndoom-mega"),
        new(667, 306, 10053, "aggron-mega"),
        new(668, 354, 10056, "banette-mega"),
        new(669, 248, 10049, "tyranitar-mega"),
        new(670, 212, 10046, "scizor-mega"),
        new(671, 127, 10040, "pinsir-mega"),
        new(672, 142, 10042, "aerodactyl-mega"),
        new(673, 448, 10059, "lucario-mega"),
        new(674, 460, 10060, "abomasnow-mega"),
        new(675, 115, 10039, "kangaskhan-mega"),
        new(676, 130, 10041, "gyarados-mega"),
        new(677, 359, 10057, "absol-mega"),
        new(678, 6,   10035, "charizard-mega-y"),
        new(679, 65,  10037, "alakazam-mega"),
        new(680, 214, 10047, "heracross-mega"),
        new(681, 303, 10052, "mawile-mega"),
        new(682, 310, 10055, "manectric-mega"),
        new(683, 445, 10058, "garchomp-mega"),
        new(684, 380, 10062, "latias-mega"),
        new(685, 381, 10063, "latios-mega"),
        new(752, 260, 10064, "swampert-mega"),
        new(753, 254, 10065, "sceptile-mega"),
        new(754, 302, 10066, "sableye-mega"),
        new(755, 334, 10067, "altaria-mega"),
        new(756, 475, 10068, "gallade-mega"),
        new(757, 531, 10069, "audino-mega"),
        new(758, 376, 10076, "metagross-mega"),
        new(759, 319, 10070, "sharpedo-mega"),
        new(760, 80,  10071, "slowbro-mega"),
        new(761, 208, 10072, "steelix-mega"),
        new(762, 18,  10073, "pidgeot-mega"),
        new(763, 362, 10074, "glalie-mega"),
        new(764, 719, 10075, "diancie-mega"),
        new(767, 323, 10087, "camerupt-mega"),
        new(768, 428, 10088, "lopunny-mega"),
        new(769, 373, 10089, "salamence-mega"),
        new(770, 15,  10090, "beedrill-mega"),
    };

    private static readonly Dictionary<int, MegaStoneEntry> ByItemId =
        Entries.ToDictionary(e => e.ItemId);

    private static readonly ILookup<int, MegaStoneEntry> BySpeciesId =
        Entries.ToLookup(e => e.BaseSpeciesId);

    /// <summary>Busca la entrada de Mega Stone a partir del ItemId crudo (pkm.HeldItem).</summary>
    public static bool TryGet(int itemId, out MegaStoneEntry entry) => ByItemId.TryGetValue(itemId, out entry!);

    /// <summary>Todos los ItemId de Mega Stones (para excluirlas del combo general de Held Item —
    /// se seleccionan solo desde el tab Special).</summary>
    public static IEnumerable<int> AllItemIds() => Entries.Select(e => e.ItemId);

    /// <summary>Todas las Mega Stones compatibles con una especie (0, 1 o 2 — ej. Charizard/Mewtwo).</summary>
    public static IReadOnlyList<MegaStoneEntry> GetForSpecies(int baseSpeciesId) =>
        BySpeciesId[baseSpeciesId].ToList();
}
