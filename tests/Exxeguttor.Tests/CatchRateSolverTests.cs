using Exxeguttor.App.Services;
using FluentAssertions;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Corrección automática de catch rate de Gen1. Caso real: un Magmar Nv50 de un save Yellow con
// Flamethrower y Confuse Ray (solo los aprende por nivel en Gen2) → PKHeX exige un catch rate de objeto
// de Gen2 (G1CatchRateItem). PKHeX sugería 45 (el de la especie), justo el valor inválido.
public class CatchRateSolverTests
{
    private static PK1 Make(SaveFile save, ushort species, int level, byte rate, string nick, ushort[] moves)
    {
        var pk = (PK1)EntityBlank.GetBlank(save);
        pk.Species = species;
        pk.CurrentLevel = (byte)level;
        pk.CatchRate = rate;
        var pi = PersonalTable.RB[species];
        pk.Type1 = (byte)pi.Type1;
        pk.Type2 = (byte)pi.Type2;
        pk.Move1 = moves[0];
        pk.Move2 = moves.Length > 1 ? moves[1] : (ushort)0;
        pk.Move3 = moves.Length > 2 ? moves[2] : (ushort)0;
        pk.Move4 = moves.Length > 3 ? moves[3] : (ushort)0;
        pk.Move1_PP = 20;
        pk.Move2_PP = moves.Length > 1 ? (byte)20 : (byte)0;
        pk.Move3_PP = moves.Length > 2 ? (byte)20 : (byte)0;
        pk.Move4_PP = moves.Length > 3 ? (byte)20 : (byte)0;
        pk.OriginalTrainerName = "TESTOT";
        pk.Nickname = nick;
        pk.TID16 = 12345;
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void Magmar_ConMovimientosSoloDeGen2_QuedaConCatchRateValido()
    {
        var save = BlankSaveFile.Get(GameVersion.YW, "TEST", LanguageID.English);
        // Psychic, Seismic Toss, Flamethrower, Confuse Ray; catch rate 45 = el de la especie
        var pk = Make(save, 126, 50, 45, "MAGMAR", [94, 69, 53, 109]);

        CatchRateSolver.HasInvalidCatchRate(new LegalityAnalysis(pk)).Should().BeTrue("45 es inválido con movimientos de Gen2");
        CatchRateApplicator.GetSuggestedCatchRate(pk, save).Should().Be(45, "PKHeX sugiere el mismo valor inválido");

        CatchRateSolver.TrySolve(pk, save, out var rate).Should().BeTrue();
        rate.Should().Be(0);

        pk.CatchRate = rate;
        pk.RefreshChecksum();
        CatchRateSolver.HasInvalidCatchRate(new LegalityAnalysis(pk)).Should().BeFalse();
    }

    [Fact]
    public void Raichu_ConCatchRateDePikachu_SeCorrigeConElDelEncuentro()
    {
        var save = BlankSaveFile.Get(GameVersion.YW, "TEST", LanguageID.English);
        var pk = Make(save, 26, 40, 190, "RAICHU", [84, 98]);        // G1CatchRateNone

        CatchRateSolver.HasInvalidCatchRate(new LegalityAnalysis(pk)).Should().BeTrue();
        CatchRateSolver.TrySolve(pk, save, out var rate).Should().BeTrue();

        pk.CatchRate = rate;
        pk.RefreshChecksum();
        CatchRateSolver.HasInvalidCatchRate(new LegalityAnalysis(pk)).Should().BeFalse();
    }
}
