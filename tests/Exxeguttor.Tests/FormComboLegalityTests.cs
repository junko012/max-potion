using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Combo Forma: no debe ofrecer formas que dejan el Pokémon ilegal sin remedio (origen del encuentro
// o forma de solo batalla), en cualquier generación. Casos reales vistos en Gen 7 (Luna).
public class FormComboLegalityTests
{
    private static PKM Build(GameVersion version, int species)
    {
        var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
        var saveService = new SaveFileService();
        typeof(SaveFileService)
            .GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        return new PokemonService(saveService).BuildPokemon(species);
    }

    private static int[] Forms(PKM pk)
        => PokemonService.GetSelectableForms(pk, GameInfo.GetStrings("en"))?.Select(f => (int)f.Index).ToArray() ?? [];

    [Theory]
    [InlineData(GameVersion.SN)]
    [InlineData(GameVersion.US)]
    public void Gen7_Zygarde_NoOfrece10PorCiento(GameVersion version)
    {
        var forms = Forms(Build(version, 718));
        Assert.DoesNotContain(1, forms);   // 10% Aura Break: EncGiftNotFound
        Assert.DoesNotContain(4, forms);   // Complete: solo batalla
    }

    [Theory]
    [InlineData(GameVersion.SN)]
    [InlineData(GameVersion.US)]
    public void Gen7_Greninja_NoOfreceAshNiActive(GameVersion version)
    {
        // Ash-Greninja (forma 1: EncInvalid) y la forma de batalla (2: FormBattle) dejan el Pokémon ilegal.
        var forms = Forms(Build(version, 658));
        Assert.DoesNotContain(1, forms);
        Assert.DoesNotContain(2, forms);
    }

    [Fact]
    public void Gen3_Unown_NoOfreceLetrasQueDejenElPokemonIlegal()
    {
        // En Gen 3 la letra sale del PID: cambiarla con ApplyForm rompe el encuentro (EncInvalid), así que, por la regla
        // «el combo nunca deja un Pokémon ilegal», no se ofrece ninguna letra. Limitación conocida: no se puede
        // cambiar la letra de un Unown de Gen 3. Lo que sí debe cumplirse siempre: toda forma ofrecida es legal.
        var pk = Build(GameVersion.E, 201);
        foreach (var f in Forms(pk))
        {
            var candidate = pk.Clone();
            PokemonService.ApplyForm(candidate, (byte)f);
            candidate.RefreshChecksum();
            Assert.DoesNotContain(new LegalityAnalysis(candidate).Results,
                r => !r.Valid && r.Judgement == Severity.Invalid
                     && new LegalityAnalysis(pk).Results.All(b => b.Result != r.Result));
        }
    }
}
