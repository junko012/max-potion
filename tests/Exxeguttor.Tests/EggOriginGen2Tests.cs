using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Gen2. Medido contra PKHeX.Core 25.11.07 (EncounterEgg2):
//  · "Recrear" ya ofrecía un origen de huevo legal en Cristal, Oro y Plata (los DVs no dependen de un PID).
//  · Solo Cristal guarda datos de captura (PK2.HasOriginalMetLocation). Un huevo eclosionado de Cristal exige
//    Met level 1 y una hora del día 1–3 (con 0 → Encounter:MetDetailTimeOfDay; con Met level 0 PKHeX lo empareja
//    con otro encuentro). El check "From egg" del editor no escribía nada y la hora del día no se aplicaba al exportar.
public class EggOriginGen2Tests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("es");

    private static PK2 CrystalCaught(int timeOfDay)
    {
        var pk = (PK2)BlankSaveFile.Get(GameVersion.C, "SANIA", LanguageID.Spanish).BlankPKM;
        pk.Species = (ushort)Species.Raichu; pk.CurrentLevel = 30; pk.Stat_Level = 30;
        pk.MetLevel = 17; pk.MetLocation = 16; pk.MetTimeOfDay = timeOfDay;
        pk.OriginalTrainerName = "SANIA"; pk.ResetPartyStats(); pk.RefreshChecksum();
        return pk;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Cristal_FromEgg_FijaMetLevel1YUnaHoraValida(int horaInicial)
    {
        var pk = CrystalCaught(horaInicial);
        Assert.True(EggOriginService.TryGetDirectHatchedMeta(pk, out var meta));
        Assert.Equal((byte)1, meta.MetLevel);

        // Lo mismo que hace el editor al tildar el check
        pk.MetLevel = meta.MetLevel;
        pk.MetTimeOfDay = meta.TimeOfDay!.Value;
        pk.RefreshChecksum();

        var la = new LegalityAnalysis(pk);
        Assert.True(la.EncounterMatch is IEncounterEgg);
        Assert.DoesNotContain(la.Results, r => !r.Valid && r.Identifier == CheckIdentifier.Encounter);
        Assert.True(EggOriginService.IsHatchedMeta(pk, pk.MetLevel));
    }

    [Fact]
    public void OroPlata_SinDatosDeCaptura_NoTieneAtajoDeHuevo()
    {
        var pk = (PK2)BlankSaveFile.Get(GameVersion.GD, "SANIA", LanguageID.Spanish).BlankPKM;
        pk.Species = (ushort)Species.Raichu; pk.CurrentLevel = 30; pk.Stat_Level = 30; pk.RefreshChecksum();
        Assert.False(pk.HasOriginalMetLocation);
        Assert.False(EggOriginService.TryGetDirectHatchedMeta(pk, out _));
    }

    public static IEnumerable<object[]> Gen2Versions() =>
        new[] { GameVersion.C, GameVersion.GD, GameVersion.SI }.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(Gen2Versions))]
    public void Gen2_Recrear_OfreceOrigenDeHuevoLegal_ConservandoLosDVs(GameVersion v)
    {
        var save = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish);
        PK2? egg = null;
        foreach (var sp in new ushort[] { 172, 175, 1, 152, 133, 4, 7, 179 })
        {
            var probe = save.BlankPKM; probe.Species = sp; probe.CurrentLevel = 5; probe.Stat_Level = 5; probe.Version = v;
            foreach (var e in EncounterMovesetGenerator.GenerateEncounters(probe, ReadOnlyMemory<ushort>.Empty, v))
                if (e is IEncounterEgg && e is IEncounterConvertible c) { egg = (PK2)c.ConvertToPKM(save, EncounterCriteria.Unrestricted); break; }
            if (egg != null) break;
        }
        Assert.NotNull(egg);

        for (var i = 0; i < 3; i++)
        {
            var bad = egg!.Clone();
            bad.IV_ATK = (i * 5 + 3) % 16; bad.IV_DEF = (i * 3 + 7) % 16; bad.IV_SPE = (i * 7 + 1) % 16; bad.IV_SPA = (i * 2 + 9) % 16;
            bad.MetLevel = 30; bad.MetLocation = 16; bad.RefreshChecksum();

            var option = EncounterRecreationService.Analyze(bad, save, Strings).Options.FirstOrDefault(o => o.IsEgg);

            Assert.NotNull(option);
            var r = option!.Result;
            Assert.True(new LegalityAnalysis(r).Valid, $"{v} #{i}");
            Assert.Equal((bad.IV_ATK, bad.IV_DEF, bad.IV_SPE, bad.IV_SPA), (r.IV_ATK, r.IV_DEF, r.IV_SPE, r.IV_SPA));
        }
    }
}
