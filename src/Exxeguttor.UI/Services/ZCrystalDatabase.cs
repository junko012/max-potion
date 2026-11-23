using System.Linq;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Tabla estática de Z-Crystals (Gen 7: Sun/Moon, Ultra Sun/Ultra Moon).
/// Mismo criterio que MegaStoneDatabase/NatureDatabase: dato de dominio fijo,
/// la tabla Items de pokemon.db no tiene vínculo a tipo/especie/movimiento.
///
/// Dos categorías:
///  - Genéricos por tipo (18): cualquier movimiento dañino de ese tipo se
///    convierte en el Z-Move correspondiente (nombre fijo por tipo).
///  - Exclusivos por especie (17: 11 de SM/USUM + 6 solo de USUM): requieren una
///    especie Y un movimiento puntual. La especie y el movimiento se verifican en
///    PokemonEditorViewModel.RecalcZMove (el movimiento por id, vía MoveDatabase).
///
/// IMPORTANTE — id usado como clave: es el id del objeto EQUIPADO (PKM.HeldItem) en PKHeX
/// (ItemStorage7USUM.ZCrystalHeld: 776-794, 798-806, 836 y 921-926 en USUM). Cada cristal existe
/// ADEMÁS como objeto clave de la mochila con otro id (ZCrystalKey: 807-835 y 927-932): ese es el
/// que guarda ItemGameCodes.RawItemId (la columna sirve para la Mochila, no para PKM.HeldItem) y
/// PKHeX lo muestra con el sufijo " [Z]". Equipar un id de mochila deja al Pokémon ilegal
/// ("Objeto equipado"). El ItemId de pokemon.db coincide con el id equipado salvo Pikashunium Z
/// (DB 835 = mochila; equipado 836, que la DB no tiene).
/// </summary>
public static class ZCrystalDatabase
{
    public sealed record TypeCrystal(int ItemId, string RequiredType, string ZMoveName);
    public sealed record SignatureCrystal(int ItemId, int RequiredSpeciesId, string RequiredMoveName, string ZMoveName);

    private static readonly TypeCrystal[] TypeCrystals =
    {
        new(776, "normal",   "Breakneck Blitz"),
        new(777, "fire",     "Inferno Overdrive"),
        new(778, "water",    "Hydro Vortex"),
        new(779, "electric", "Gigavolt Havoc"),
        new(780, "grass",    "Bloom Doom"),
        new(781, "ice",      "Subzero Slammer"),
        new(782, "fighting", "All-Out Pummeling"),
        new(783, "poison",   "Acid Downpour"),
        new(784, "ground",   "Tectonic Rage"),
        new(785, "flying",   "Supersonic Skystrike"),
        new(786, "psychic",  "Shattered Psyche"),
        new(787, "bug",      "Savage Spin-Out"),
        new(788, "rock",     "Continental Crush"),
        new(789, "ghost",    "Never-Ending Nightmare"),
        new(790, "dragon",   "Devastating Drake"),
        new(791, "dark",     "Black Hole Eclipse"),
        new(792, "steel",    "Corkscrew Crash"),
        new(793, "fairy",    "Twinkle Tackle"),
    };

    private static readonly SignatureCrystal[] SignatureCrystals =
    {
        // NOTA: RequiredSpeciesId no distingue formas (ej. Raichu-Alola vs. Raichu normal,
        // o las gorras de Pikachu para Pikashunium Z) — se valida solo por especie base.
        new(794, 25,  "Volt Tackle",       "Catastropika"),
        new(798, 724, "Spirit Shackle",    "Sinister Arrow Raid"),
        new(799, 727, "Darkest Lariat",    "Malicious Moonsault"),
        new(800, 730, "Sparkling Aria",    "Oceanic Operetta"),
        new(801, 785, "Nature's Madness",  "Guardian of Alola"),   // Tapu Koko (785-788 los 4 Tapu)
        new(802, 802, "Spectral Thief",    "Soul-Stealing 7-Star Strike"),
        new(803, 26,  "Thunderbolt",       "Stoked Sparksurfer"),  // Raichu-Alola (misma especie base 26)
        new(804, 143, "Giga Impact",       "Pulverizing Pancake"),
        new(805, 133, "Last Resort",       "Extreme Evoboost"),
        new(806, 151, "Psychic",           "Genesis Supernova"),
        new(836, 25,  "Thunderbolt",       "10,000,000 Volt Thunderbolt"), // Pikachu con gorra
        // Exclusivos de Ultra Sun / Ultra Moon (ids equipados 921-926; no existen en Sun/Moon — el ViewModel
        // los descarta si el juego no los tiene en su lista de objetos).
        new(921, 791, "Sunsteel Strike",   "Searing Sunraze Smash"),         // Solgaleo
        new(922, 792, "Moongeist Beam",    "Menacing Moonraze Maelstrom"),   // Lunala
        new(923, 800, "Photon Geyser",     "Light That Burns the Sky"),      // Necrozma (Dusk Mane / Dawn Wings)
        new(924, 778, "Play Rough",        "Let's Snuggle Forever"),         // Mimikyu
        new(925, 745, "Stone Edge",        "Splintered Stormshards"),        // Lycanroc (cualquier forma)
        new(926, 784, "Clanging Scales",   "Clangorous Soulblaze"),          // Kommo-o
    };

    private static readonly Dictionary<int, TypeCrystal> TypeByItemId = TypeCrystals.ToDictionary(e => e.ItemId);
    private static readonly Dictionary<int, SignatureCrystal> SignatureByItemId = SignatureCrystals.ToDictionary(e => e.ItemId);

    public static bool TryGetTypeCrystal(int itemId, out TypeCrystal entry) => TypeByItemId.TryGetValue(itemId, out entry!);
    public static bool TryGetSignatureCrystal(int itemId, out SignatureCrystal entry) => SignatureByItemId.TryGetValue(itemId, out entry!);

    /// <summary>Todos los cristales genéricos por tipo, como (ItemId, RequiredType, ZMoveName).</summary>
    public static IEnumerable<(int ItemId, string RequiredType, string ZMoveName)> AllTypeCrystals() =>
        TypeCrystals.Select(e => (e.ItemId, e.RequiredType, e.ZMoveName));

    /// <summary>Todos los cristales exclusivos, como (ItemId, RequiredSpeciesId, RequiredMoveName, ZMoveName).</summary>
    public static IEnumerable<(int ItemId, int RequiredSpeciesId, string RequiredMoveName, string ZMoveName)> AllSignatureCrystals() =>
        SignatureCrystals.Select(e => (e.ItemId, e.RequiredSpeciesId, e.RequiredMoveName, e.ZMoveName));

    /// <summary>Todos los ItemId de Z-Crystals, genéricos + exclusivos (para excluirlos del combo
    /// general de Held Item — se seleccionan solo desde el tab Special).</summary>
    public static IEnumerable<int> AllItemIds() =>
        TypeCrystals.Select(e => e.ItemId).Concat(SignatureCrystals.Select(e => e.ItemId));
}
