using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Continuación de EggOriginGen3Tests para el resto de generaciones. Medido contra PKHeX.Core 25.11.07:
//  · Gen4 también espera un PID sin correlación (PIDType.None) en un huevo; "Recrear" forzaba Method 1 y no ofrecía
//    ninguna opción de huevo en DP/Pt/HGSS (Gen2 y Gen3 sí/ya arreglado).
//  · Gen4–9: un huevo eclosionado exige Met level 0 (Gen4) o 1 (Gen5+), Poké Ball, ubicación de huevo y fecha de
//    huevo; en ORAS además una ubicación de eclosión válida (EggLocationInvalid).
public class EggOriginGen4PlusTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("es");
    private static readonly ushort[] Candidates = { 172, 175, 1, 270, 447, 133, 4, 7, 152, 252, 387 };

    /// <summary>Un huevo ya eclosionado, legal, generado por el propio PKHeX para ese juego.</summary>
    private static (SaveFile Save, PKM Egg) HatchedEgg(GameVersion v)
    {
        var save = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish);
        foreach (var sp in Candidates)
        {
            if (sp > save.MaxSpeciesID) continue;
            var probe = save.BlankPKM;
            probe.Species = sp; probe.CurrentLevel = 5; probe.Stat_Level = 5; probe.Version = v;
            foreach (var e in EncounterMovesetGenerator.GenerateEncounters(probe, ReadOnlyMemory<ushort>.Empty, v))
                if (e is IEncounterEgg && e is IEncounterConvertible c)
                    return (save, c.ConvertToPKM(save, EncounterCriteria.Unrestricted));
        }
        throw new InvalidOperationException($"sin encuentro de huevo para {v}");
    }

    public static IEnumerable<object[]> Gen4Versions() =>
        new[] { GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS }.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(Gen4Versions))]
    public void Gen4_Recrear_OfreceOrigenDeHuevoLegal_ConservandoLoDelPokemon(GameVersion v)
    {
        var (save, egg) = HatchedEgg(v);
        for (var i = 0; i < 4; i++)
        {
            var bad = egg.Clone();
            bad.IV_HP = (i * 7 + 3) % 32; bad.IV_ATK = (i * 5 + 11) % 32; bad.IV_SPE = (i * 3 + 20) % 32;
            bad.MetLevel = 50;   // origen imposible para un huevo recién nacido → error de encuentro
            bad.RefreshChecksum();

            var option = EncounterRecreationService.Analyze(bad, save, Strings).Options.FirstOrDefault(o => o.IsEgg);

            Assert.NotNull(option);
            var r = option!.Result;
            Assert.True(new LegalityAnalysis(r).Valid, $"{v} #{i}: la opción de huevo debe ser legal");
            Assert.Equal((bad.IV_HP, bad.IV_ATK, bad.IV_DEF, bad.IV_SPA, bad.IV_SPD, bad.IV_SPE),
                (r.IV_HP, r.IV_ATK, r.IV_DEF, r.IV_SPA, r.IV_SPD, r.IV_SPE));
            Assert.Equal(bad.Nature, r.Nature);
            Assert.Equal(bad.Gender, r.Gender);
            Assert.Equal(bad.Ability, r.Ability);
        }
    }

    public static IEnumerable<object[]> Gen4PlusVersions() =>
        new[] { GameVersion.D, GameVersion.Pt, GameVersion.HG, GameVersion.B, GameVersion.W2, GameVersion.X, GameVersion.Y,
                GameVersion.OR, GameVersion.AS, GameVersion.SN, GameVersion.US, GameVersion.UM, GameVersion.SW, GameVersion.SH,
                GameVersion.BD, GameVersion.SP, GameVersion.SL, GameVersion.VL }.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(Gen4PlusVersions))]
    public void Gen4Plus_PlantillaDeHuevo_DejaElPokemonSinErroresDeEncuentro(GameVersion v)
    {
        var save = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish);
        // Pokémon "capturado" evolucionado (Raichu/Ambipom): lo que tendría el usuario antes de marcar From egg
        var pk = save.BlankPKM;
        pk.Species = v is GameVersion.HG or GameVersion.SS ? (ushort)424 : (ushort)26;
        pk.Version = v; pk.CurrentLevel = 30; pk.Stat_Level = 30; pk.MetLevel = 17; pk.Ball = 1; pk.MetLocation = 16;
        pk.OriginalTrainerName = "SANIA"; pk.ResetPartyStats(); pk.RefreshChecksum();

        Assert.True(EggOriginService.TryGetEggTemplate(pk, save, out var t));
        // Lo mismo que hace el editor al tildar el check
        pk.MetLevel = t.MetLevel; pk.Ball = t.Ball; pk.EggLocation = t.EggLocation;
        pk.EggMetDate = pk.MetDate ?? t.EggDate;
        if (t.NeedsMetLocation) pk.MetLocation = t.MetLocation;
        pk.RefreshChecksum();

        var la = new LegalityAnalysis(pk);
        Assert.True(la.EncounterMatch is IEncounterEgg, $"{v}: debe emparejar con un huevo");
        Assert.DoesNotContain(la.Results, r => !r.Valid && r.Identifier is CheckIdentifier.Encounter or CheckIdentifier.Level);
    }
}
