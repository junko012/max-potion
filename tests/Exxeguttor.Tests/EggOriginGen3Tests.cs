using System.Diagnostics;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Huevo eclosionado de Gen3 (caso real: Ludicolo de Esmeralda con Leech Seed, que Lotad solo hereda como
// movimiento huevo). Medido contra PKHeX.Core 25.11.07 (EncounterEgg3):
//  · Met level 0 (con 5 → Encounter:EggFMetLevel_0), ubicación de eclosión fija y Poké Ball.
//  · PID sin patrón de RNG (PIDType.None): un PID Method 1 da PID:PIDTypeMismatch. Por eso Method1Solver y
//    Method1IvSearch nunca podían resolver un huevo, y la búsqueda de IVs se quedaba al 100 % de CPU.
public class EggOriginGen3Tests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("es");

    // Ludicolo "como el del usuario": origen salvaje (Route 102, nv 4, Poké Ball), PID Method 1, con Leech Seed.
    private static PK3 UserLudicolo(uint seed)
    {
        var tr = new SimpleTrainerInfo(GameVersion.E)
        { OT = "SANIA", TID16 = 0, SID16 = 0, Language = (int)LanguageID.Spanish, Gender = 0 };
        var pk = new EncounterEgg3((ushort)Species.Lotad, GameVersion.E).ConvertToPKM(tr, EncounterCriteria.Unrestricted);
        pk.Species = (ushort)Species.Ludicolo;
        pk.Nickname = "LUDICOLO";
        pk.CurrentLevel = 50; pk.Stat_Level = 50;
        pk.SetMoves(new ushort[] { 57, 92, 73, 104 });   // Surf, Toxic, Leech Seed, Double Team
        pk.SetMaximumPPCurrent(pk.Moves);
        pk.OriginalTrainerFriendship = 177;
        pk.MetLevel = 4; pk.MetLocation = 16; pk.Ball = (byte)Ball.Poke;

        var s1 = LCRNG.Next(seed); var s2 = LCRNG.Next(s1); var s3 = LCRNG.Next(s2); var s4 = LCRNG.Next(s3);
        pk.PID = (s2 & 0xFFFF0000u) | (s1 >> 16);
        pk.IV32 = ((s3 >> 16) & 0x7FFFu) | (((s4 >> 16) & 0x7FFFu) << 15);   // IVs y PID de la misma semilla (Method 1)
        pk.RefreshAbility((int)(pk.PID & 1));
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        return pk;
    }

    public static IEnumerable<object[]> Seeds() =>
        new[] { 0x12345678u, 0xCAFEBABEu, 0x0BADF00Du }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Recrear_OfreceOrigenDeHuevoLegal_ConservandoLoDelPokemon(uint seed)
    {
        var pk = UserLudicolo(seed);
        var save = BlankSaveFile.Get(GameVersion.E, "SANIA", LanguageID.Spanish);

        var analysis = EncounterRecreationService.Analyze(pk, save, Strings);

        Assert.True(analysis.HasEncounterError);
        var egg = analysis.Options.FirstOrDefault(o => o.IsEgg);
        Assert.NotNull(egg);
        Assert.True(new LegalityAnalysis(egg!.Result).Valid, "la opción de huevo debe ser legal");
        Assert.Equal(0, egg.Result.MetLevel);
        Assert.Equal((byte)Ball.Poke, egg.Result.Ball);
        Assert.Equal((pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE),
            (egg.Result.IV_HP, egg.Result.IV_ATK, egg.Result.IV_DEF, egg.Result.IV_SPA, egg.Result.IV_SPD, egg.Result.IV_SPE));
        Assert.Equal(pk.Nature, egg.Result.Nature);
        Assert.Equal(pk.Gender, egg.Result.Gender);
        Assert.Equal(pk.Ability, egg.Result.Ability);
        Assert.Equal((ushort)73, egg.Result.Move3);   // Leech Seed sigue ahí
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FromEgg_MasBotonDePid_DejaElPokemonLegal(uint seed)
    {
        var pk = UserLudicolo(seed);

        Assert.True(EggOriginService.ApplyHatchedOrigin(pk));
        // Con el origen puesto solo queda el PID con patrón de Method 1.
        var antes = new LegalityAnalysis(pk);
        Assert.Contains(antes.Results, r => !r.Valid && r.Identifier == CheckIdentifier.PID);

        Assert.True(EggOriginService.TrySolvePid(pk, out var pid));
        pk.PID = pid;
        pk.RefreshChecksum();
        Assert.True(new LegalityAnalysis(pk).Valid);
    }

    [Fact]
    public void MetLevel5_NoEsUnOrigenDeHuevoValido()
    {
        var pk = UserLudicolo(0x12345678);
        EggOriginService.ApplyHatchedOrigin(pk);
        Assert.True(EggOriginService.TrySolvePid(pk, out var pid));
        pk.PID = pid;
        pk.MetLevel = 5;   // lo que probó el usuario a mano
        pk.RefreshChecksum();

        var la = new LegalityAnalysis(pk);
        Assert.False(la.Valid);
        Assert.Contains(la.Results, r => !r.Valid && r.Identifier == CheckIdentifier.Encounter);
    }

    [Fact]
    public void BusquedaDeIvs_NoSeLanzaConErrorDeEncuentroOHuevo_Ni_SeCuelga()
    {
        var pk = UserLudicolo(0x12345678);   // error de encuentro (Met level 4 + Leech Seed) → antes: CPU al 100 %
        var sw = Stopwatch.StartNew();
        Assert.Empty(EncounterRecreationService.FindIvOptions(pk, Strings));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tardó {sw.Elapsed}");

        EggOriginService.ApplyHatchedOrigin(pk);   // ahora empareja con un huevo (PID aún con patrón)
        sw.Restart();
        Assert.Empty(EncounterRecreationService.FindIvOptions(pk, Strings));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tardó {sw.Elapsed}");
    }
}
