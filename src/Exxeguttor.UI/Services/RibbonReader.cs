using PKHeX.Core;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Lee el valor de cualquier <see cref="RibbonIndex"/> de un PKM, sin necesidad de saber de
/// antemano qué interfaz <c>IRibbonSetX</c> lo expone. Mismo mapeo que usa
/// <c>RibbonIndex.Fix()</c> en PKHeX.Core (ahí es para escribir; acá es de solo lectura,
/// consistente con que el editor todavía no persiste cambios — ver GAP CRÍTICO).
/// Devuelve null si el formato de PKM no soporta esa cinta en particular (no aplica a esta
/// generación/juego).
/// </summary>
public static class RibbonReader
{
    public static bool? TryGet(PKM pkm, RibbonIndex idx)
    {
        // Formatos modernos (PK9, PA8, WC8...) implementan IRibbonIndex directamente —
        // PKHeX ya resuelve internamente a qué interfaz específica delegar.
        if (pkm is IRibbonIndex ri)
            return ri.GetRibbonIndex(idx);

        return idx switch
        {
            RibbonIndex.ChampionG3 => (pkm as IRibbonSetCommon3)?.RibbonChampionG3,
            RibbonIndex.Effort => (pkm as IRibbonSetCommon3)?.RibbonEffort,
            RibbonIndex.Artist => (pkm as IRibbonSetCommon3)?.RibbonArtist,

            RibbonIndex.ChampionSinnoh => (pkm as IRibbonSetCommon4)?.RibbonChampionSinnoh,
            RibbonIndex.Alert => (pkm as IRibbonSetCommon4)?.RibbonAlert,
            RibbonIndex.Shock => (pkm as IRibbonSetCommon4)?.RibbonShock,
            RibbonIndex.Downcast => (pkm as IRibbonSetCommon4)?.RibbonDowncast,
            RibbonIndex.Careless => (pkm as IRibbonSetCommon4)?.RibbonCareless,
            RibbonIndex.Relax => (pkm as IRibbonSetCommon4)?.RibbonRelax,
            RibbonIndex.Snooze => (pkm as IRibbonSetCommon4)?.RibbonSnooze,
            RibbonIndex.Smile => (pkm as IRibbonSetCommon4)?.RibbonSmile,
            RibbonIndex.Gorgeous => (pkm as IRibbonSetCommon4)?.RibbonGorgeous,
            RibbonIndex.Royal => (pkm as IRibbonSetCommon4)?.RibbonRoyal,
            RibbonIndex.GorgeousRoyal => (pkm as IRibbonSetCommon4)?.RibbonGorgeousRoyal,
            RibbonIndex.Footprint => (pkm as IRibbonSetCommon4)?.RibbonFootprint,
            RibbonIndex.Record => (pkm as IRibbonSetCommon4)?.RibbonRecord,
            RibbonIndex.Legend => (pkm as IRibbonSetCommon4)?.RibbonLegend,

            RibbonIndex.Country => (pkm as IRibbonSetEvent3)?.RibbonCountry,
            RibbonIndex.National => (pkm as IRibbonSetEvent3)?.RibbonNational,
            RibbonIndex.Earth => (pkm as IRibbonSetEvent3)?.RibbonEarth,
            RibbonIndex.ChampionBattle => (pkm as IRibbonSetEvent3)?.RibbonChampionBattle,
            RibbonIndex.ChampionRegional => (pkm as IRibbonSetEvent3)?.RibbonChampionRegional,
            RibbonIndex.ChampionNational => (pkm as IRibbonSetEvent3)?.RibbonChampionNational,

            RibbonIndex.World => (pkm as IRibbonSetEvent4)?.RibbonWorld,
            RibbonIndex.Classic => (pkm as IRibbonSetEvent4)?.RibbonClassic,
            RibbonIndex.Premier => (pkm as IRibbonSetEvent4)?.RibbonPremier,
            RibbonIndex.Event => (pkm as IRibbonSetEvent4)?.RibbonEvent,
            RibbonIndex.Birthday => (pkm as IRibbonSetEvent4)?.RibbonBirthday,
            RibbonIndex.Special => (pkm as IRibbonSetEvent4)?.RibbonSpecial,
            RibbonIndex.Souvenir => (pkm as IRibbonSetEvent4)?.RibbonSouvenir,
            RibbonIndex.Wishing => (pkm as IRibbonSetEvent4)?.RibbonWishing,
            RibbonIndex.ChampionWorld => (pkm as IRibbonSetEvent4)?.RibbonChampionWorld,

            RibbonIndex.ChampionKalos => (pkm as IRibbonSetCommon6)?.RibbonChampionKalos,
            RibbonIndex.BestFriends => (pkm as IRibbonSetCommon6)?.RibbonBestFriends,
            RibbonIndex.Training => (pkm as IRibbonSetCommon6)?.RibbonTraining,
            RibbonIndex.BattlerSkillful => (pkm as IRibbonSetCommon6)?.RibbonBattlerSkillful,
            RibbonIndex.BattlerExpert => (pkm as IRibbonSetCommon6)?.RibbonBattlerExpert,
            RibbonIndex.ChampionG6Hoenn => (pkm as IRibbonSetCommon6)?.RibbonChampionG6Hoenn,
            RibbonIndex.ContestStar => (pkm as IRibbonSetCommon6)?.RibbonContestStar,
            RibbonIndex.MasterCoolness => (pkm as IRibbonSetCommon6)?.RibbonMasterCoolness,
            RibbonIndex.MasterBeauty => (pkm as IRibbonSetCommon6)?.RibbonMasterBeauty,
            RibbonIndex.MasterCuteness => (pkm as IRibbonSetCommon6)?.RibbonMasterCuteness,
            RibbonIndex.MasterCleverness => (pkm as IRibbonSetCommon6)?.RibbonMasterCleverness,
            RibbonIndex.MasterToughness => (pkm as IRibbonSetCommon6)?.RibbonMasterToughness,

            RibbonIndex.CountMemoryContest => ((pkm as IRibbonSetMemory6)?.RibbonCountMemoryContest ?? 0) > 0,
            RibbonIndex.CountMemoryBattle => ((pkm as IRibbonSetMemory6)?.RibbonCountMemoryBattle ?? 0) > 0,

            RibbonIndex.ChampionAlola => (pkm as IRibbonSetCommon7)?.RibbonChampionAlola,
            RibbonIndex.BattleRoyale => (pkm as IRibbonSetCommon7)?.RibbonBattleRoyale,
            RibbonIndex.BattleTreeGreat => (pkm as IRibbonSetCommon7)?.RibbonBattleTreeGreat,
            RibbonIndex.BattleTreeMaster => (pkm as IRibbonSetCommon7)?.RibbonBattleTreeMaster,

            RibbonIndex.ChampionGalar => (pkm as IRibbonSetCommon8)?.RibbonChampionGalar,
            RibbonIndex.TowerMaster => (pkm as IRibbonSetCommon8)?.RibbonTowerMaster,
            RibbonIndex.MasterRank => (pkm as IRibbonSetCommon8)?.RibbonMasterRank,
            RibbonIndex.Hisui => (pkm as IRibbonSetCommon8)?.RibbonHisui,
            RibbonIndex.TwinklingStar => (pkm as IRibbonSetCommon8)?.RibbonTwinklingStar,

            RibbonIndex.ChampionPaldea => (pkm as IRibbonSetCommon9)?.RibbonChampionPaldea,
            RibbonIndex.OnceInALifetime => (pkm as IRibbonSetCommon9)?.RibbonOnceInALifetime,
            RibbonIndex.Partner => (pkm as IRibbonSetCommon9)?.RibbonPartner,

            >= RibbonIndex.MarkLunchtime and <= RibbonIndex.MarkSlump => GetMark8(pkm, idx),
            RibbonIndex.MarkJumbo or RibbonIndex.MarkMini or RibbonIndex.MarkItemfinder or RibbonIndex.MarkPartner
                or RibbonIndex.MarkGourmand or RibbonIndex.MarkAlpha or RibbonIndex.MarkMightiest or RibbonIndex.MarkTitan
                => GetMark9(pkm, idx),

            _ => null,
        };
    }

    private static bool? GetMark8(PKM pkm, RibbonIndex idx)
    {
        if (pkm is not IRibbonSetMark8 m) return null;
        return idx switch
        {
            RibbonIndex.MarkLunchtime => m.RibbonMarkLunchtime,
            RibbonIndex.MarkSleepyTime => m.RibbonMarkSleepyTime,
            RibbonIndex.MarkDusk => m.RibbonMarkDusk,
            RibbonIndex.MarkDawn => m.RibbonMarkDawn,
            RibbonIndex.MarkCloudy => m.RibbonMarkCloudy,
            RibbonIndex.MarkRainy => m.RibbonMarkRainy,
            RibbonIndex.MarkStormy => m.RibbonMarkStormy,
            RibbonIndex.MarkSnowy => m.RibbonMarkSnowy,
            RibbonIndex.MarkBlizzard => m.RibbonMarkBlizzard,
            RibbonIndex.MarkDry => m.RibbonMarkDry,
            RibbonIndex.MarkSandstorm => m.RibbonMarkSandstorm,
            RibbonIndex.MarkMisty => m.RibbonMarkMisty,
            RibbonIndex.MarkDestiny => m.RibbonMarkDestiny,
            RibbonIndex.MarkFishing => m.RibbonMarkFishing,
            RibbonIndex.MarkCurry => m.RibbonMarkCurry,
            RibbonIndex.MarkUncommon => m.RibbonMarkUncommon,
            RibbonIndex.MarkRare => m.RibbonMarkRare,
            RibbonIndex.MarkRowdy => m.RibbonMarkRowdy,
            RibbonIndex.MarkAbsentMinded => m.RibbonMarkAbsentMinded,
            RibbonIndex.MarkJittery => m.RibbonMarkJittery,
            RibbonIndex.MarkExcited => m.RibbonMarkExcited,
            RibbonIndex.MarkCharismatic => m.RibbonMarkCharismatic,
            RibbonIndex.MarkCalmness => m.RibbonMarkCalmness,
            RibbonIndex.MarkIntense => m.RibbonMarkIntense,
            RibbonIndex.MarkZonedOut => m.RibbonMarkZonedOut,
            RibbonIndex.MarkJoyful => m.RibbonMarkJoyful,
            RibbonIndex.MarkAngry => m.RibbonMarkAngry,
            RibbonIndex.MarkSmiley => m.RibbonMarkSmiley,
            RibbonIndex.MarkTeary => m.RibbonMarkTeary,
            RibbonIndex.MarkUpbeat => m.RibbonMarkUpbeat,
            RibbonIndex.MarkPeeved => m.RibbonMarkPeeved,
            RibbonIndex.MarkIntellectual => m.RibbonMarkIntellectual,
            RibbonIndex.MarkFerocious => m.RibbonMarkFerocious,
            RibbonIndex.MarkCrafty => m.RibbonMarkCrafty,
            RibbonIndex.MarkScowling => m.RibbonMarkScowling,
            RibbonIndex.MarkKindly => m.RibbonMarkKindly,
            RibbonIndex.MarkFlustered => m.RibbonMarkFlustered,
            RibbonIndex.MarkPumpedUp => m.RibbonMarkPumpedUp,
            RibbonIndex.MarkZeroEnergy => m.RibbonMarkZeroEnergy,
            RibbonIndex.MarkPrideful => m.RibbonMarkPrideful,
            RibbonIndex.MarkUnsure => m.RibbonMarkUnsure,
            RibbonIndex.MarkHumble => m.RibbonMarkHumble,
            RibbonIndex.MarkThorny => m.RibbonMarkThorny,
            RibbonIndex.MarkVigor => m.RibbonMarkVigor,
            RibbonIndex.MarkSlump => m.RibbonMarkSlump,
            _ => null,
        };
    }

    private static bool? GetMark9(PKM pkm, RibbonIndex idx)
    {
        if (pkm is not IRibbonSetMark9 m) return null;
        return idx switch
        {
            RibbonIndex.MarkJumbo => m.RibbonMarkJumbo,
            RibbonIndex.MarkMini => m.RibbonMarkMini,
            RibbonIndex.MarkItemfinder => m.RibbonMarkItemfinder,
            RibbonIndex.MarkPartner => m.RibbonMarkPartner,
            RibbonIndex.MarkGourmand => m.RibbonMarkGourmand,
            RibbonIndex.MarkAlpha => m.RibbonMarkAlpha,
            RibbonIndex.MarkMightiest => m.RibbonMarkMightiest,
            RibbonIndex.MarkTitan => m.RibbonMarkTitan,
            _ => null,
        };
    }
}
