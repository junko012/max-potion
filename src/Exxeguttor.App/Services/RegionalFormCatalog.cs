using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>Una forma regional (Alola/Galar/Hisui/Paldea) ofrecida como tarjeta propia en el
/// selector de "crear Pokémon" (Fase 2 de formas). <see cref="Form"/> es el índice de forma de
/// PKHeX.Core (<c>PKM.Form</c>), no el id de PokeAPI.</summary>
public readonly record struct RegionalForm(int Species, byte Form, string Region);

/// <summary>
/// Catálogo de formas regionales creables. Lista fija, generada a partir de los nombres de
/// forma de PKHeX.Core 25.11.07 (los que contienen Alola/Galar/Hisui/Paldea) cruzados con los
/// ids de forma de PokeAPI para los sprites (<c>Assets/sprites/pokemon/forms/{especie}_{forma}.png</c>).
/// Excluidas a propósito: la Pikachu con gorra "Alola" (cosmética, no es una variante regional)
/// y Darmanitan Galar Zen (forma de solo batalla, nunca guardable).
/// Si PKHeX.Core suma formas regionales nuevas en una actualización, hay que regenerar esta lista
/// Y agregar el sprite correspondiente — no se descubre sola.
/// </summary>
public static class RegionalFormCatalog
{
    public static readonly IReadOnlyList<RegionalForm> All =
    [
        new(19, 1, "Alola"),  // Rattata — PokeAPI rattata-alola (10091)
        new(20, 1, "Alola"),  // Raticate — PokeAPI raticate-alola (10092)
        new(26, 1, "Alola"),  // Raichu — PokeAPI raichu-alola (10100)
        new(27, 1, "Alola"),  // Sandshrew — PokeAPI sandshrew-alola (10101)
        new(28, 1, "Alola"),  // Sandslash — PokeAPI sandslash-alola (10102)
        new(37, 1, "Alola"),  // Vulpix — PokeAPI vulpix-alola (10103)
        new(38, 1, "Alola"),  // Ninetales — PokeAPI ninetales-alola (10104)
        new(50, 1, "Alola"),  // Diglett — PokeAPI diglett-alola (10105)
        new(51, 1, "Alola"),  // Dugtrio — PokeAPI dugtrio-alola (10106)
        new(52, 1, "Alola"),  // Meowth — PokeAPI meowth-alola (10107)
        new(52, 2, "Galar"),  // Meowth — PokeAPI meowth-galar (10161)
        new(53, 1, "Alola"),  // Persian — PokeAPI persian-alola (10108)
        new(58, 1, "Hisui"),  // Growlithe — PokeAPI growlithe-hisui (10229)
        new(59, 1, "Hisui"),  // Arcanine — PokeAPI arcanine-hisui (10230)
        new(74, 1, "Alola"),  // Geodude — PokeAPI geodude-alola (10109)
        new(75, 1, "Alola"),  // Graveler — PokeAPI graveler-alola (10110)
        new(76, 1, "Alola"),  // Golem — PokeAPI golem-alola (10111)
        new(77, 1, "Galar"),  // Ponyta — PokeAPI ponyta-galar (10162)
        new(78, 1, "Galar"),  // Rapidash — PokeAPI rapidash-galar (10163)
        new(79, 1, "Galar"),  // Slowpoke — PokeAPI slowpoke-galar (10164)
        new(80, 2, "Galar"),  // Slowbro — PokeAPI slowbro-galar (10165)
        new(83, 1, "Galar"),  // Farfetch’d — PokeAPI farfetchd-galar (10166)
        new(88, 1, "Alola"),  // Grimer — PokeAPI grimer-alola (10112)
        new(89, 1, "Alola"),  // Muk — PokeAPI muk-alola (10113)
        new(100, 1, "Hisui"),  // Voltorb — PokeAPI voltorb-hisui (10231)
        new(101, 1, "Hisui"),  // Electrode — PokeAPI electrode-hisui (10232)
        new(103, 1, "Alola"),  // Exeggutor — PokeAPI exeggutor-alola (10114)
        new(105, 1, "Alola"),  // Marowak — PokeAPI marowak-alola (10115)
        new(110, 1, "Galar"),  // Weezing — PokeAPI weezing-galar (10167)
        new(122, 1, "Galar"),  // Mr. Mime — PokeAPI mr-mime-galar (10168)
        new(128, 1, "Paldea Combat"),  // Tauros — PokeAPI tauros-paldea-combat-breed (10250)
        new(128, 2, "Paldea Blaze"),  // Tauros — PokeAPI tauros-paldea-blaze-breed (10251)
        new(128, 3, "Paldea Aqua"),  // Tauros — PokeAPI tauros-paldea-aqua-breed (10252)
        new(144, 1, "Galar"),  // Articuno — PokeAPI articuno-galar (10169)
        new(145, 1, "Galar"),  // Zapdos — PokeAPI zapdos-galar (10170)
        new(146, 1, "Galar"),  // Moltres — PokeAPI moltres-galar (10171)
        new(157, 1, "Hisui"),  // Typhlosion — PokeAPI typhlosion-hisui (10233)
        new(194, 1, "Paldea"),  // Wooper — PokeAPI wooper-paldea (10253)
        new(199, 1, "Galar"),  // Slowking — PokeAPI slowking-galar (10172)
        new(211, 1, "Hisui"),  // Qwilfish — PokeAPI qwilfish-hisui (10234)
        new(215, 1, "Hisui"),  // Sneasel — PokeAPI sneasel-hisui (10235)
        new(222, 1, "Galar"),  // Corsola — PokeAPI corsola-galar (10173)
        new(263, 1, "Galar"),  // Zigzagoon — PokeAPI zigzagoon-galar (10174)
        new(264, 1, "Galar"),  // Linoone — PokeAPI linoone-galar (10175)
        new(503, 1, "Hisui"),  // Samurott — PokeAPI samurott-hisui (10236)
        new(549, 1, "Hisui"),  // Lilligant — PokeAPI lilligant-hisui (10237)
        new(554, 1, "Galar"),  // Darumaka — PokeAPI darumaka-galar (10176)
        new(555, 2, "Galar"),  // Darmanitan — PokeAPI darmanitan-galar-standard (10177)
        new(562, 1, "Galar"),  // Yamask — PokeAPI yamask-galar (10179)
        new(570, 1, "Hisui"),  // Zorua — PokeAPI zorua-hisui (10238)
        new(571, 1, "Hisui"),  // Zoroark — PokeAPI zoroark-hisui (10239)
        new(618, 1, "Galar"),  // Stunfisk — PokeAPI stunfisk-galar (10180)
        new(628, 1, "Hisui"),  // Braviary — PokeAPI braviary-hisui (10240)
        new(705, 1, "Hisui"),  // Sliggoo — PokeAPI sliggoo-hisui (10241)
        new(706, 1, "Hisui"),  // Goodra — PokeAPI goodra-hisui (10242)
        new(713, 1, "Hisui"),  // Avalugg — PokeAPI avalugg-hisui (10243)
        new(724, 1, "Hisui"),  // Decidueye — PokeAPI decidueye-hisui (10244)
        // No es regional, pero es otro Pokémon (encuentro propio, no se llega a él cambiando de
        // forma): convertir un Ursaluna base dejaba "encuentro de origen no coincide".
        new(901, 1, "Bloodmoon"),  // Ursaluna — PokeAPI ursaluna-bloodmoon
    ];

    /// <summary>Formas regionales que realmente existen en el juego del save abierto
    /// (<c>save.Personal.IsPresentInGame</c>): en un save de Gen 7 salen las de Alola, no las de
    /// Galar; en uno de Gen 4 no sale ninguna.</summary>
    public static IEnumerable<RegionalForm> AvailableIn(SaveFile save) =>
        All.Where(f => f.Species <= save.MaxSpeciesID
                    && save.Personal.IsPresentInGame((ushort)f.Species, f.Form)
                    && !IsNotObtainable(f, save.Version));

    /// <summary>Formas que la tabla de personal de un juego marca como presentes pero que
    /// PKHeX no puede validar como legales en él, así que no se ofrecen en el selector (el combo
    /// de forma no las ofrece tampoco: son regionales). Hoy, solo en Legends: Z-A: el Stunfisk de
    /// Galar no tiene ningún encuentro legal (solo "huevo", con movimientos y transferencia
    /// inválidos) y el Avalugg de Hisui solo encaja como captura salvaje de Legends: Arceus, que
    /// exige alfa, IVs y "plus flags" de Z-A que BuildPokemon no genera.</summary>
    private static readonly (int Species, byte Form, GameVersion Game)[] NotObtainable =
    [
        (618, 1, GameVersion.ZA),   // Stunfisk de Galar
        (713, 1, GameVersion.ZA),   // Avalugg de Hisui
        // PKHeX 25.11.07 marca TransferBad a todo PA9 cuyo origen no es Z-A (regla de HOME aún en
        // construcción: "TODO: ZA HOME Compatibility"), y estas dos formas solo existen en Z-A
        // por transferencia (Legends: Arceus → HOME). Revisar al actualizar PKHeX.
        (705, 1, GameVersion.ZA),   // Sliggoo de Hisui
        (706, 1, GameVersion.ZA),   // Goodra de Hisui
    ];

    private static bool IsNotObtainable(RegionalForm f, GameVersion game) =>
        NotObtainable.Any(n => n.Species == f.Species && n.Form == f.Form && n.Game == game);

    /// <summary>La forma del catálogo para (especie, índice de forma), o null si no es una
    /// forma regional creable.</summary>
    public static RegionalForm? Find(int species, byte form)
    {
        foreach (var f in All)
            if (f.Species == species && f.Form == form) return f;
        return null;
    }

    /// <summary>Tipos (formato del selector: "Fire", "Ghost"...) de una forma regional, leídos de
    /// la fila propia de la forma en pokemon.db (ver <see cref="SpeciesFormResolver"/>) — la DB es
    /// la fuente de tipos del proyecto, no <c>PersonalInfo</c>.</summary>
    public static IReadOnlyList<string> GetTypeNames(RegionalForm f) =>
        SpeciesFormResolver.GetFormTypes(f.Species, f.Form);

    /// <summary>Clave estilo Showdown del set de movimientos de la forma ("Exeggutor-Alola",
    /// "Tauros-Paldea-Combat") — así están indexados los sets recomendados. Para una forma que no
    /// es regional devuelve el nombre de especie tal cual.</summary>
    public static string ShowdownName(string englishSpeciesName, int species, byte form)
    {
        var rf = Find(species, form);
        return rf is null
            ? englishSpeciesName
            : $"{englishSpeciesName}-{rf.Value.Region.Replace(' ', '-')}";
    }
}
