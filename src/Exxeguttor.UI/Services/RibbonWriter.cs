using PKHeX.Core;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Escribe el valor de cualquier <see cref="RibbonIndex"/> a un PKM — simétrico a
/// <see cref="RibbonReader"/> (mismos nombres de interfaz/propiedad, generado a partir de ese
/// mismo mapeo para no duplicar investigación), pero escribiendo en vez de solo leyendo.
/// Usado por el pipeline de escritura real al aplicar ediciones pendientes con prefijo
/// "Ribbon:" de <see cref="EditSessionService"/> — antes de esto, las cintas nunca se
/// escribían al PKM real (ver GAP CRÍTICO en CLAUDE.md/context.md).
///
/// Devuelve false si el formato de PKM no soporta esa cinta en particular (no aplica a esta
/// generación/juego) — el llamador debe ignorar silenciosamente esos casos, igual que
/// RibbonReader.TryGet devuelve null en la misma situación.
/// </summary>
public static class RibbonWriter
{
    public static bool TrySet(PKM pkm, RibbonIndex idx, bool value)
    {
        // Formatos modernos (PK9, PA8, WC8...) implementan IRibbonIndex directamente — un
        // único método SetRibbon(int, bool) para todas las cintas, confirmado por reflection
        // y por WritePipelineRoundTripTests.MutarCintaFormatoModerno_SeReflejaInmediatamente.
        if (pkm is IRibbonIndex ri)
        {
            ri.SetRibbon((int)idx, value);
            return true;
        }

        switch (idx)
        {
            case RibbonIndex.ChampionG3:
                if (pkm is IRibbonSetCommon3 c_ChampionG3) { c_ChampionG3.RibbonChampionG3 = value; return true; }
                return false;
            case RibbonIndex.Effort:
                if (pkm is IRibbonSetCommon3 c_Effort) { c_Effort.RibbonEffort = value; return true; }
                return false;
            case RibbonIndex.Artist:
                if (pkm is IRibbonSetCommon3 c_Artist) { c_Artist.RibbonArtist = value; return true; }
                return false;
            case RibbonIndex.ChampionSinnoh:
                if (pkm is IRibbonSetCommon4 c_ChampionSinnoh) { c_ChampionSinnoh.RibbonChampionSinnoh = value; return true; }
                return false;
            case RibbonIndex.Alert:
                if (pkm is IRibbonSetCommon4 c_Alert) { c_Alert.RibbonAlert = value; return true; }
                return false;
            case RibbonIndex.Shock:
                if (pkm is IRibbonSetCommon4 c_Shock) { c_Shock.RibbonShock = value; return true; }
                return false;
            case RibbonIndex.Downcast:
                if (pkm is IRibbonSetCommon4 c_Downcast) { c_Downcast.RibbonDowncast = value; return true; }
                return false;
            case RibbonIndex.Careless:
                if (pkm is IRibbonSetCommon4 c_Careless) { c_Careless.RibbonCareless = value; return true; }
                return false;
            case RibbonIndex.Relax:
                if (pkm is IRibbonSetCommon4 c_Relax) { c_Relax.RibbonRelax = value; return true; }
                return false;
            case RibbonIndex.Snooze:
                if (pkm is IRibbonSetCommon4 c_Snooze) { c_Snooze.RibbonSnooze = value; return true; }
                return false;
            case RibbonIndex.Smile:
                if (pkm is IRibbonSetCommon4 c_Smile) { c_Smile.RibbonSmile = value; return true; }
                return false;
            case RibbonIndex.Gorgeous:
                if (pkm is IRibbonSetCommon4 c_Gorgeous) { c_Gorgeous.RibbonGorgeous = value; return true; }
                return false;
            case RibbonIndex.Royal:
                if (pkm is IRibbonSetCommon4 c_Royal) { c_Royal.RibbonRoyal = value; return true; }
                return false;
            case RibbonIndex.GorgeousRoyal:
                if (pkm is IRibbonSetCommon4 c_GorgeousRoyal) { c_GorgeousRoyal.RibbonGorgeousRoyal = value; return true; }
                return false;
            case RibbonIndex.Footprint:
                if (pkm is IRibbonSetCommon4 c_Footprint) { c_Footprint.RibbonFootprint = value; return true; }
                return false;
            case RibbonIndex.Record:
                if (pkm is IRibbonSetCommon4 c_Record) { c_Record.RibbonRecord = value; return true; }
                return false;
            case RibbonIndex.Legend:
                if (pkm is IRibbonSetCommon4 c_Legend) { c_Legend.RibbonLegend = value; return true; }
                return false;
            case RibbonIndex.Country:
                if (pkm is IRibbonSetEvent3 c_Country) { c_Country.RibbonCountry = value; return true; }
                return false;
            case RibbonIndex.National:
                if (pkm is IRibbonSetEvent3 c_National) { c_National.RibbonNational = value; return true; }
                return false;
            case RibbonIndex.Earth:
                if (pkm is IRibbonSetEvent3 c_Earth) { c_Earth.RibbonEarth = value; return true; }
                return false;
            case RibbonIndex.ChampionBattle:
                if (pkm is IRibbonSetEvent3 c_ChampionBattle) { c_ChampionBattle.RibbonChampionBattle = value; return true; }
                return false;
            case RibbonIndex.ChampionRegional:
                if (pkm is IRibbonSetEvent3 c_ChampionRegional) { c_ChampionRegional.RibbonChampionRegional = value; return true; }
                return false;
            case RibbonIndex.ChampionNational:
                if (pkm is IRibbonSetEvent3 c_ChampionNational) { c_ChampionNational.RibbonChampionNational = value; return true; }
                return false;
            case RibbonIndex.World:
                if (pkm is IRibbonSetEvent4 c_World) { c_World.RibbonWorld = value; return true; }
                return false;
            case RibbonIndex.Classic:
                if (pkm is IRibbonSetEvent4 c_Classic) { c_Classic.RibbonClassic = value; return true; }
                return false;
            case RibbonIndex.Premier:
                if (pkm is IRibbonSetEvent4 c_Premier) { c_Premier.RibbonPremier = value; return true; }
                return false;
            case RibbonIndex.Event:
                if (pkm is IRibbonSetEvent4 c_Event) { c_Event.RibbonEvent = value; return true; }
                return false;
            case RibbonIndex.Birthday:
                if (pkm is IRibbonSetEvent4 c_Birthday) { c_Birthday.RibbonBirthday = value; return true; }
                return false;
            case RibbonIndex.Special:
                if (pkm is IRibbonSetEvent4 c_Special) { c_Special.RibbonSpecial = value; return true; }
                return false;
            case RibbonIndex.Souvenir:
                if (pkm is IRibbonSetEvent4 c_Souvenir) { c_Souvenir.RibbonSouvenir = value; return true; }
                return false;
            case RibbonIndex.Wishing:
                if (pkm is IRibbonSetEvent4 c_Wishing) { c_Wishing.RibbonWishing = value; return true; }
                return false;
            case RibbonIndex.ChampionWorld:
                if (pkm is IRibbonSetEvent4 c_ChampionWorld) { c_ChampionWorld.RibbonChampionWorld = value; return true; }
                return false;
            case RibbonIndex.ChampionKalos:
                if (pkm is IRibbonSetCommon6 c_ChampionKalos) { c_ChampionKalos.RibbonChampionKalos = value; return true; }
                return false;
            case RibbonIndex.BestFriends:
                if (pkm is IRibbonSetCommon6 c_BestFriends) { c_BestFriends.RibbonBestFriends = value; return true; }
                return false;
            case RibbonIndex.Training:
                if (pkm is IRibbonSetCommon6 c_Training) { c_Training.RibbonTraining = value; return true; }
                return false;
            case RibbonIndex.BattlerSkillful:
                if (pkm is IRibbonSetCommon6 c_BattlerSkillful) { c_BattlerSkillful.RibbonBattlerSkillful = value; return true; }
                return false;
            case RibbonIndex.BattlerExpert:
                if (pkm is IRibbonSetCommon6 c_BattlerExpert) { c_BattlerExpert.RibbonBattlerExpert = value; return true; }
                return false;
            case RibbonIndex.ChampionG6Hoenn:
                if (pkm is IRibbonSetCommon6 c_ChampionG6Hoenn) { c_ChampionG6Hoenn.RibbonChampionG6Hoenn = value; return true; }
                return false;
            case RibbonIndex.ContestStar:
                if (pkm is IRibbonSetCommon6 c_ContestStar) { c_ContestStar.RibbonContestStar = value; return true; }
                return false;
            case RibbonIndex.MasterCoolness:
                if (pkm is IRibbonSetCommon6 c_MasterCoolness) { c_MasterCoolness.RibbonMasterCoolness = value; return true; }
                return false;
            case RibbonIndex.MasterBeauty:
                if (pkm is IRibbonSetCommon6 c_MasterBeauty) { c_MasterBeauty.RibbonMasterBeauty = value; return true; }
                return false;
            case RibbonIndex.MasterCuteness:
                if (pkm is IRibbonSetCommon6 c_MasterCuteness) { c_MasterCuteness.RibbonMasterCuteness = value; return true; }
                return false;
            case RibbonIndex.MasterCleverness:
                if (pkm is IRibbonSetCommon6 c_MasterCleverness) { c_MasterCleverness.RibbonMasterCleverness = value; return true; }
                return false;
            case RibbonIndex.MasterToughness:
                if (pkm is IRibbonSetCommon6 c_MasterToughness) { c_MasterToughness.RibbonMasterToughness = value; return true; }
                return false;
            case RibbonIndex.ChampionAlola:
                if (pkm is IRibbonSetCommon7 c_ChampionAlola) { c_ChampionAlola.RibbonChampionAlola = value; return true; }
                return false;
            case RibbonIndex.BattleRoyale:
                if (pkm is IRibbonSetCommon7 c_BattleRoyale) { c_BattleRoyale.RibbonBattleRoyale = value; return true; }
                return false;
            case RibbonIndex.BattleTreeGreat:
                if (pkm is IRibbonSetCommon7 c_BattleTreeGreat) { c_BattleTreeGreat.RibbonBattleTreeGreat = value; return true; }
                return false;
            case RibbonIndex.BattleTreeMaster:
                if (pkm is IRibbonSetCommon7 c_BattleTreeMaster) { c_BattleTreeMaster.RibbonBattleTreeMaster = value; return true; }
                return false;
            case RibbonIndex.ChampionGalar:
                if (pkm is IRibbonSetCommon8 c_ChampionGalar) { c_ChampionGalar.RibbonChampionGalar = value; return true; }
                return false;
            case RibbonIndex.TowerMaster:
                if (pkm is IRibbonSetCommon8 c_TowerMaster) { c_TowerMaster.RibbonTowerMaster = value; return true; }
                return false;
            case RibbonIndex.MasterRank:
                if (pkm is IRibbonSetCommon8 c_MasterRank) { c_MasterRank.RibbonMasterRank = value; return true; }
                return false;
            case RibbonIndex.Hisui:
                if (pkm is IRibbonSetCommon8 c_Hisui) { c_Hisui.RibbonHisui = value; return true; }
                return false;
            case RibbonIndex.TwinklingStar:
                if (pkm is IRibbonSetCommon8 c_TwinklingStar) { c_TwinklingStar.RibbonTwinklingStar = value; return true; }
                return false;
            case RibbonIndex.ChampionPaldea:
                if (pkm is IRibbonSetCommon9 c_ChampionPaldea) { c_ChampionPaldea.RibbonChampionPaldea = value; return true; }
                return false;
            case RibbonIndex.OnceInALifetime:
                if (pkm is IRibbonSetCommon9 c_OnceInALifetime) { c_OnceInALifetime.RibbonOnceInALifetime = value; return true; }
                return false;
            case RibbonIndex.Partner:
                if (pkm is IRibbonSetCommon9 c_Partner) { c_Partner.RibbonPartner = value; return true; }
                return false;

            case RibbonIndex.CountMemoryContest:
                if (pkm is IRibbonSetMemory6 m_CountMemoryContest) { m_CountMemoryContest.RibbonCountMemoryContest = (byte)(value ? 1 : 0); return true; }
                return false;
            case RibbonIndex.CountMemoryBattle:
                if (pkm is IRibbonSetMemory6 m_CountMemoryBattle) { m_CountMemoryBattle.RibbonCountMemoryBattle = (byte)(value ? 1 : 0); return true; }
                return false;

            case >= RibbonIndex.MarkLunchtime and <= RibbonIndex.MarkSlump:
                return SetMark8(pkm, idx, value);
            case RibbonIndex.MarkJumbo or RibbonIndex.MarkMini or RibbonIndex.MarkItemfinder or RibbonIndex.MarkPartner
                or RibbonIndex.MarkGourmand or RibbonIndex.MarkAlpha or RibbonIndex.MarkMightiest or RibbonIndex.MarkTitan:
                return SetMark9(pkm, idx, value);

            default:
                return false;
        }
    }

    private static bool SetMark8(PKM pkm, RibbonIndex idx, bool value)
    {
        if (pkm is not IRibbonSetMark8 m) return false;
        switch (idx)
        {
            case RibbonIndex.MarkLunchtime: m.RibbonMarkLunchtime = value; return true;
            case RibbonIndex.MarkSleepyTime: m.RibbonMarkSleepyTime = value; return true;
            case RibbonIndex.MarkDusk: m.RibbonMarkDusk = value; return true;
            case RibbonIndex.MarkDawn: m.RibbonMarkDawn = value; return true;
            case RibbonIndex.MarkCloudy: m.RibbonMarkCloudy = value; return true;
            case RibbonIndex.MarkRainy: m.RibbonMarkRainy = value; return true;
            case RibbonIndex.MarkStormy: m.RibbonMarkStormy = value; return true;
            case RibbonIndex.MarkSnowy: m.RibbonMarkSnowy = value; return true;
            case RibbonIndex.MarkBlizzard: m.RibbonMarkBlizzard = value; return true;
            case RibbonIndex.MarkDry: m.RibbonMarkDry = value; return true;
            case RibbonIndex.MarkSandstorm: m.RibbonMarkSandstorm = value; return true;
            case RibbonIndex.MarkMisty: m.RibbonMarkMisty = value; return true;
            case RibbonIndex.MarkDestiny: m.RibbonMarkDestiny = value; return true;
            case RibbonIndex.MarkFishing: m.RibbonMarkFishing = value; return true;
            case RibbonIndex.MarkCurry: m.RibbonMarkCurry = value; return true;
            case RibbonIndex.MarkUncommon: m.RibbonMarkUncommon = value; return true;
            case RibbonIndex.MarkRare: m.RibbonMarkRare = value; return true;
            case RibbonIndex.MarkRowdy: m.RibbonMarkRowdy = value; return true;
            case RibbonIndex.MarkAbsentMinded: m.RibbonMarkAbsentMinded = value; return true;
            case RibbonIndex.MarkJittery: m.RibbonMarkJittery = value; return true;
            case RibbonIndex.MarkExcited: m.RibbonMarkExcited = value; return true;
            case RibbonIndex.MarkCharismatic: m.RibbonMarkCharismatic = value; return true;
            case RibbonIndex.MarkCalmness: m.RibbonMarkCalmness = value; return true;
            case RibbonIndex.MarkIntense: m.RibbonMarkIntense = value; return true;
            case RibbonIndex.MarkZonedOut: m.RibbonMarkZonedOut = value; return true;
            case RibbonIndex.MarkJoyful: m.RibbonMarkJoyful = value; return true;
            case RibbonIndex.MarkAngry: m.RibbonMarkAngry = value; return true;
            case RibbonIndex.MarkSmiley: m.RibbonMarkSmiley = value; return true;
            case RibbonIndex.MarkTeary: m.RibbonMarkTeary = value; return true;
            case RibbonIndex.MarkUpbeat: m.RibbonMarkUpbeat = value; return true;
            case RibbonIndex.MarkPeeved: m.RibbonMarkPeeved = value; return true;
            case RibbonIndex.MarkIntellectual: m.RibbonMarkIntellectual = value; return true;
            case RibbonIndex.MarkFerocious: m.RibbonMarkFerocious = value; return true;
            case RibbonIndex.MarkCrafty: m.RibbonMarkCrafty = value; return true;
            case RibbonIndex.MarkScowling: m.RibbonMarkScowling = value; return true;
            case RibbonIndex.MarkKindly: m.RibbonMarkKindly = value; return true;
            case RibbonIndex.MarkFlustered: m.RibbonMarkFlustered = value; return true;
            case RibbonIndex.MarkPumpedUp: m.RibbonMarkPumpedUp = value; return true;
            case RibbonIndex.MarkZeroEnergy: m.RibbonMarkZeroEnergy = value; return true;
            case RibbonIndex.MarkPrideful: m.RibbonMarkPrideful = value; return true;
            case RibbonIndex.MarkUnsure: m.RibbonMarkUnsure = value; return true;
            case RibbonIndex.MarkHumble: m.RibbonMarkHumble = value; return true;
            case RibbonIndex.MarkThorny: m.RibbonMarkThorny = value; return true;
            case RibbonIndex.MarkVigor: m.RibbonMarkVigor = value; return true;
            case RibbonIndex.MarkSlump: m.RibbonMarkSlump = value; return true;
            default: return false;
        }
    }

    private static bool SetMark9(PKM pkm, RibbonIndex idx, bool value)
    {
        if (pkm is not IRibbonSetMark9 m) return false;
        switch (idx)
        {
            case RibbonIndex.MarkJumbo: m.RibbonMarkJumbo = value; return true;
            case RibbonIndex.MarkMini: m.RibbonMarkMini = value; return true;
            case RibbonIndex.MarkItemfinder: m.RibbonMarkItemfinder = value; return true;
            case RibbonIndex.MarkPartner: m.RibbonMarkPartner = value; return true;
            case RibbonIndex.MarkGourmand: m.RibbonMarkGourmand = value; return true;
            case RibbonIndex.MarkAlpha: m.RibbonMarkAlpha = value; return true;
            case RibbonIndex.MarkMightiest: m.RibbonMarkMightiest = value; return true;
            case RibbonIndex.MarkTitan: m.RibbonMarkTitan = value; return true;
            default: return false;
        }
    }
}
