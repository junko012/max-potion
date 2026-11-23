using PKHeX.Core;

namespace Exxeguttor.UI.Services;

public enum RibbonCategory
{
    Contest,
    Battle,
    Champion,
    Memory,
    Event,
    Common,
    Mark,
}

/// <summary>
/// Categoriza y da nombre de UI a cada valor de <see cref="RibbonIndex"/> (enum propio de
/// PKHeX.Core que ya cubre TODAS las cintas de todas las generaciones — no reinventamos la
/// lista, solo la organizamos para mostrarla). MinGeneration es aproximado por familia de
/// interfaz (Common3→3, Common4→4, Common6→6, Common7→7, Common8/Mark8→8, Common9/Mark9→9);
/// no es 100% exacto juego-por-juego pero alcanza para decidir qué grupo mostrar según el save.
/// </summary>
public static class RibbonDatabase
{
    public sealed record RibbonDefinition(RibbonIndex Index, string DisplayName, RibbonCategory Category, int MinGeneration);

    private static readonly RibbonDefinition[] Entries =
    {
        // ── Champion ──────────────────────────────────────────────
        new(RibbonIndex.ChampionG3,        "Champion (Hoenn)",        RibbonCategory.Champion, 3),
        new(RibbonIndex.ChampionSinnoh,    "Champion (Sinnoh)",       RibbonCategory.Champion, 4),
        new(RibbonIndex.ChampionKalos,     "Champion (Kalos)",        RibbonCategory.Champion, 6),
        new(RibbonIndex.ChampionG6Hoenn,   "Champion (Hoenn, ORAS)",  RibbonCategory.Champion, 6),
        new(RibbonIndex.ChampionAlola,     "Champion (Alola)",        RibbonCategory.Champion, 7),
        new(RibbonIndex.ChampionGalar,     "Champion (Galar)",        RibbonCategory.Champion, 8),
        new(RibbonIndex.ChampionPaldea,    "Champion (Paldea)",       RibbonCategory.Champion, 9),
        new(RibbonIndex.ChampionBattle,    "Champion Battle",         RibbonCategory.Champion, 3),
        new(RibbonIndex.ChampionRegional,  "Champion Regional",       RibbonCategory.Champion, 3),
        new(RibbonIndex.ChampionNational,  "Champion Nacional",       RibbonCategory.Champion, 3),
        new(RibbonIndex.ChampionWorld,     "Champion Mundial",        RibbonCategory.Champion, 4),

        // ── Battle ────────────────────────────────────────────────
        new(RibbonIndex.BattlerSkillful,   "Battler Hábil",           RibbonCategory.Battle, 6),
        new(RibbonIndex.BattlerExpert,     "Battler Experto",         RibbonCategory.Battle, 6),
        new(RibbonIndex.BattleRoyale,      "Battle Royale",           RibbonCategory.Battle, 7),
        new(RibbonIndex.BattleTreeGreat,   "Battle Tree (Great)",     RibbonCategory.Battle, 7),
        new(RibbonIndex.BattleTreeMaster,  "Battle Tree (Master)",    RibbonCategory.Battle, 7),
        new(RibbonIndex.TowerMaster,       "Tower Master",            RibbonCategory.Battle, 8),
        new(RibbonIndex.MasterRank,        "Master Rank",             RibbonCategory.Battle, 8),
        new(RibbonIndex.CountMemoryBattle, "Memoria de Batallas",     RibbonCategory.Battle, 6),

        // ── Contest ───────────────────────────────────────────────
        new(RibbonIndex.ContestStar,       "Contest Star",            RibbonCategory.Contest, 6),
        new(RibbonIndex.MasterCoolness,    "Master de Genialidad",    RibbonCategory.Contest, 6),
        new(RibbonIndex.MasterBeauty,      "Master de Belleza",       RibbonCategory.Contest, 6),
        new(RibbonIndex.MasterCuteness,    "Master de Ternura",       RibbonCategory.Contest, 6),
        new(RibbonIndex.MasterCleverness,  "Master de Inteligencia",  RibbonCategory.Contest, 6),
        new(RibbonIndex.MasterToughness,   "Master de Resistencia",   RibbonCategory.Contest, 6),
        new(RibbonIndex.CountMemoryContest,"Memoria de Concursos",    RibbonCategory.Contest, 6),

        // ── Memory / personalidad (Gen 4) ────────────────────────
        new(RibbonIndex.Effort,            "Esfuerzo",                RibbonCategory.Memory, 3),
        new(RibbonIndex.Artist,            "Artista",                 RibbonCategory.Memory, 3),
        new(RibbonIndex.Alert,             "Alerta",                  RibbonCategory.Memory, 4),
        new(RibbonIndex.Shock,             "Impacto",                 RibbonCategory.Memory, 4),
        new(RibbonIndex.Downcast,          "Decaimiento",             RibbonCategory.Memory, 4),
        new(RibbonIndex.Careless,          "Descuido",                RibbonCategory.Memory, 4),
        new(RibbonIndex.Relax,             "Relajación",              RibbonCategory.Memory, 4),
        new(RibbonIndex.Snooze,            "Somnolencia",             RibbonCategory.Memory, 4),
        new(RibbonIndex.Smile,             "Sonrisa",                 RibbonCategory.Memory, 4),
        new(RibbonIndex.Gorgeous,          "Espléndida",              RibbonCategory.Memory, 4),
        new(RibbonIndex.Royal,             "Real",                    RibbonCategory.Memory, 4),
        new(RibbonIndex.GorgeousRoyal,     "Espléndida Real",         RibbonCategory.Memory, 4),
        new(RibbonIndex.Footprint,         "Huella",                  RibbonCategory.Memory, 4),
        new(RibbonIndex.Record,            "Récord",                  RibbonCategory.Memory, 4),
        new(RibbonIndex.Legend,            "Leyenda",                 RibbonCategory.Memory, 4),

        // ── Event ─────────────────────────────────────────────────
        new(RibbonIndex.Country,           "País",                    RibbonCategory.Event, 3),
        new(RibbonIndex.National,          "Nacional",                RibbonCategory.Event, 3),
        new(RibbonIndex.Earth,             "Tierra",                  RibbonCategory.Event, 3),
        new(RibbonIndex.World,             "Mundo",                   RibbonCategory.Event, 4),
        new(RibbonIndex.Classic,           "Clásica",                 RibbonCategory.Event, 4),
        new(RibbonIndex.Premier,           "Premier",                 RibbonCategory.Event, 4),
        new(RibbonIndex.Event,             "Evento",                  RibbonCategory.Event, 4),
        new(RibbonIndex.Birthday,          "Cumpleaños",              RibbonCategory.Event, 4),
        new(RibbonIndex.Special,           "Especial",                RibbonCategory.Event, 4),
        new(RibbonIndex.Souvenir,          "Recuerdo",                RibbonCategory.Event, 4),
        new(RibbonIndex.Wishing,           "Deseo",                   RibbonCategory.Event, 4),
        new(RibbonIndex.Hisui,             "Hisui",                   RibbonCategory.Event, 8),
        new(RibbonIndex.TwinklingStar,     "Estrella Titilante",      RibbonCategory.Event, 8),
        new(RibbonIndex.OnceInALifetime,   "Única en la Vida",        RibbonCategory.Event, 9),
        new(RibbonIndex.Partner,           "Compañero",               RibbonCategory.Event, 9),

        // ── Common / misc ─────────────────────────────────────────
        new(RibbonIndex.BestFriends,       "Mejores Amigos",          RibbonCategory.Common, 6),
        new(RibbonIndex.Training,          "Entrenamiento",           RibbonCategory.Common, 6),

        // ── Marks (Gen 8: encuentro salvaje) ──────────────────────
        new(RibbonIndex.MarkLunchtime,     "Marca: Hora de Comer",    RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkSleepyTime,    "Marca: Hora de Dormir",   RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkDusk,          "Marca: Anochecer",        RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkDawn,          "Marca: Amanecer",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkCloudy,        "Marca: Nublado",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkRainy,         "Marca: Lluvioso",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkStormy,        "Marca: Tormenta",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkSnowy,         "Marca: Nevado",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkBlizzard,      "Marca: Ventisca",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkDry,           "Marca: Seco",             RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkSandstorm,     "Marca: Tormenta de Arena",RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkMisty,         "Marca: Niebla",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkDestiny,       "Marca: Destino",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkFishing,       "Marca: Pesca",            RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkCurry,         "Marca: Curry",            RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkUncommon,      "Marca: Poco Común",       RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkRare,          "Marca: Rara",             RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkRowdy,         "Marca: Alborotador",      RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkAbsentMinded,  "Marca: Distraído",        RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkJittery,       "Marca: Nervioso",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkExcited,       "Marca: Emocionado",       RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkCharismatic,   "Marca: Carismático",      RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkCalmness,      "Marca: Calmado",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkIntense,       "Marca: Intenso",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkZonedOut,      "Marca: Distante",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkJoyful,        "Marca: Alegre",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkAngry,         "Marca: Enojado",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkSmiley,        "Marca: Sonriente",        RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkTeary,         "Marca: Llorón",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkUpbeat,        "Marca: Optimista",        RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkPeeved,        "Marca: Irritado",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkIntellectual,  "Marca: Intelectual",      RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkFerocious,     "Marca: Feroz",            RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkCrafty,        "Marca: Astuto",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkScowling,      "Marca: Ceñudo",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkKindly,        "Marca: Amable",           RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkFlustered,     "Marca: Aturdido",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkPumpedUp,      "Marca: Animado",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkZeroEnergy,    "Marca: Sin Energía",      RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkPrideful,      "Marca: Orgulloso",        RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkUnsure,        "Marca: Inseguro",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkHumble,        "Marca: Humilde",          RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkThorny,        "Marca: Espinoso",         RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkVigor,         "Marca: Vigor",            RibbonCategory.Mark, 8),
        new(RibbonIndex.MarkSlump,         "Marca: Bajón",            RibbonCategory.Mark, 8),

        // ── Marks (Gen 9) ─────────────────────────────────────────
        new(RibbonIndex.MarkJumbo,         "Marca: Jumbo",            RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkMini,          "Marca: Mini",             RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkItemfinder,    "Marca: Buscador de Ítems",RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkPartner,       "Marca: Compañero",        RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkGourmand,      "Marca: Gourmet",          RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkAlpha,         "Marca: Alpha",            RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkMightiest,     "Marca: El Más Poderoso",  RibbonCategory.Mark, 9),
        new(RibbonIndex.MarkTitan,         "Marca: Titán",            RibbonCategory.Mark, 9),
    };

    public static IReadOnlyList<RibbonDefinition> AllRibbons => Entries;

    /// <summary>Llave estable de una cinta para ediciones pendientes y exportación: el nombre del
    /// valor de <see cref="RibbonIndex"/> (ej. <c>"ChampionG3"</c>). Nunca el nombre visible, que
    /// cambia con el idioma de los datos.</summary>
    public static string KeyOf(RibbonIndex index) => index.ToString();

    /// <summary>Nombre de la cinta en el idioma de los datos activo (<c>GameInfo.Strings</c>, que
    /// <c>DataLanguageService.ApplyToPkhexGlobals</c> mantiene alineado; cae a inglés si PKHeX no
    /// tiene el idioma). El nombre escrito a mano de la definición es solo el último respaldo.</summary>
    public static string GetDisplayName(RibbonDefinition def)
    {
        try
        {
            if (GameInfo.Strings.Ribbons.GetNameSafe(def.Index.GetPropertyName(), out var name)
                && !string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (ArgumentOutOfRangeException) { /* índice sin propiedad conocida: respaldo */ }
        return def.DisplayName;
    }

    /// <summary>Nombre localizado a partir de la llave guardada en una edición (ver <see cref="KeyOf"/>).
    /// Si la llave no corresponde a ninguna cinta conocida, devuelve la llave tal cual.</summary>
    public static string GetDisplayNameByKey(string key)
    {
        foreach (var def in Entries)
            if (def.Index.ToString() == key) return GetDisplayName(def);
        return key;
    }
}
