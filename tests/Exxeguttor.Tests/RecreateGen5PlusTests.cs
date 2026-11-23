using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// "Recrear con un encuentro válido" en Gen5–9. Diseño: se parte de una copia del Pokémon ORIGINAL y solo se le pone el
// origen del encuentro generado; si PKHeX rechaza algo (bola, cintas, manejador/recuerdos, identidad) se reemplaza SOLO
// ese grupo (RecreateGroups). Antes de esto "Recrear" no existía fuera de Gen2–4 y, levantando solo el filtro, se
// perdían PID/EC/Naturaleza/Brillante y la bola en todos los casos.
public class RecreateGen5PlusTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("es");

    public static IEnumerable<object[]> Versions() =>
        new[] { GameVersion.B, GameVersion.W2, GameVersion.X, GameVersion.OR, GameVersion.SN, GameVersion.US,
                GameVersion.SW, GameVersion.BD, GameVersion.SL, GameVersion.PLA, GameVersion.ZA, GameVersion.GP, GameVersion.GE }.Select(v => new object[] { v });

    private static int Ribbons(PKM p) => RibbonInfo.GetRibbonInfo(p).Count(r => r.HasRibbon || r.RibbonCount > 0);

    /// <summary>Capturado legal con datos "de usuario": bola, apodo, objeto y todas las cintas válidas.</summary>
    private static PKM? RichWild(GameVersion v, SaveFile save, ref ushort species)
    {
        for (; species <= Math.Min((int)save.MaxSpeciesID, 300); species++)
        {
            var probe = save.BlankPKM;
            probe.Species = species; probe.CurrentLevel = 20; probe.Stat_Level = 20; probe.Version = v;
            try
            {
                foreach (var e in EncounterMovesetGenerator.GenerateEncounters(probe.Clone(), ReadOnlyMemory<ushort>.Empty, v))
                {
                    if (e is IEncounterEgg || e is not IEncounterConvertible c || e.IsEgg) continue;
                    var pk = c.ConvertToPKM(save, EncounterCriteria.Unrestricted);
                    if (!new LegalityAnalysis(pk).Valid) continue;

                    var rich = pk.Clone();
                    rich.IsNicknamed = true; rich.Nickname = "APODO"; rich.HeldItem = 4;
                    RibbonApplicator.SetAllValidRibbons(rich);
                    rich.RefreshChecksum();
                    if (new LegalityAnalysis(rich).Valid) return rich;
                    if (new LegalityAnalysis(pk).Valid) return pk;
                }
            }
            catch { }
        }
        return null;
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Recrear_Gen5Plus_ConservaLoQueSePueda_YLoAplicadoEsLoVerificado(GameVersion v)
    {
        var save = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish);
        ushort sp = 1;
        var checkedCases = 0;
        while (checkedCases < 2)
        {
            var wild = RichWild(v, save, ref sp);
            if (wild == null) break;
            sp++;

            // Origen imposible para ese encuentro (Met level) pero con nivel actual coherente
            var bad = wild.Clone();
            bad.MetLevel = (byte)(wild.MetLevel + 9); bad.CurrentLevel = (byte)(wild.CurrentLevel + 12); bad.Stat_Level = bad.CurrentLevel;
            bad.RefreshChecksum();

            var analysis = EncounterRecreationService.Analyze(bad, save, Strings);
            if (!analysis.HasEncounterError) continue;           // este caso solo rompía el nivel, no el encuentro
            // Sin error de encuentro y sin errores NUEVOS respecto al original (el original de prueba puede traer los suyos,
            // p. ej. movimientos "Plus" de Z-A tras cambiarle el nivel; eso no lo causa la recreación).
            var option = analysis.Options.FirstOrDefault(o =>
                !EncounterRecreationService.HasEncounterError(new LegalityAnalysis(o.Result))
                && new LegalityAnalysis(o.Result).Results.Count(x => !x.Valid) <= new LegalityAnalysis(bad).Results.Count(x => !x.Valid));
            Assert.NotNull(option);
            checkedCases++;

            var r = option!.Result;
            // Se conserva lo que no se reemplazó
            if (!option.Groups.HasFlag(RecreateGroups.Identity))
            {
                Assert.Equal(bad.PID, r.PID);
                Assert.Equal(bad.EncryptionConstant, r.EncryptionConstant);
                Assert.Equal(bad.Nature, r.Nature);
                Assert.Equal(bad.IsShiny, r.IsShiny);
                Assert.Equal(bad.Ability, r.Ability);
            }
            if (!option.Groups.HasFlag(RecreateGroups.Ball)) Assert.Equal(bad.Ball, r.Ball);
            if (!option.Groups.HasFlag(RecreateGroups.Ribbons)) Assert.Equal(Ribbons(bad), Ribbons(r));
            Assert.Equal((bad.IV_HP, bad.IV_ATK, bad.IV_DEF, bad.IV_SPA, bad.IV_SPD, bad.IV_SPE),
                (r.IV_HP, r.IV_ATK, r.IV_DEF, r.IV_SPA, r.IV_SPD, r.IV_SPE));
            Assert.Equal(bad.Nickname, r.Nickname);
            Assert.Equal(bad.HeldItem, r.HeldItem);

            // Lo que se aplica al exportar (EncounterRecreation) debe dar un Pokémon legal, igual al verificado
            var target = bad.Clone();
            Assert.True(new EncounterRecreation(r, false, option.Groups).ApplyTo(target));
            var la = new LegalityAnalysis(target);
            Assert.False(EncounterRecreationService.HasEncounterError(la), $"{v}: el Pokémon exportado sigue con error de encuentro");
            Assert.Equal(r.MetLocation, target.MetLocation);
            Assert.Equal(r.MetLevel, target.MetLevel);
            Assert.Equal(r.PID, target.PID);
            Assert.Equal(r.Nature, target.Nature);
            Assert.Equal(r.Ball, target.Ball);
            Assert.Equal(Ribbons(r), Ribbons(target));
        }
        Assert.True(checkedCases > 0, $"{v}: no se encontró ningún caso con error de encuentro para probar");
    }

    [Fact]
    public void IsSupported_CubreGen2a9_LetsGo_Arceus_YZA()
    {
        foreach (var v in new[] { GameVersion.C, GameVersion.D, GameVersion.E, GameVersion.B, GameVersion.X, GameVersion.SN, GameVersion.SW, GameVersion.BD, GameVersion.SL, GameVersion.PLA, GameVersion.ZA, GameVersion.GP, GameVersion.GE })
        {
            var pk = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish).BlankPKM;
            Assert.True(EncounterRecreationService.IsSupported(pk), $"{v}");
        }
    }
}
