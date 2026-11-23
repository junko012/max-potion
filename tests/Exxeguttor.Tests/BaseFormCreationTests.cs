using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Al pedir la forma base, BuildPokemon prefiere candidatos que dan la forma 0 (antes devolvía el
// encuentro "más fácil" aunque fuera de otra forma: Deoxys Ataque en HeartGold, Giratina Origen...).
public class BaseFormCreationTests
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

    [Theory]
    [InlineData(GameVersion.HG, 386)]   // Deoxys
    [InlineData(GameVersion.HG, 487)]   // Giratina
    [InlineData(GameVersion.X, 487)]    // Giratina
    [InlineData(GameVersion.B2, 645)]   // Landorus
    public void FormaBase_sale_en_forma_base_y_legal(GameVersion v, int species)
    {
        var pkm = Build(v, species);
        Assert.Equal(0, pkm.Form);
        Assert.True(new LegalityAnalysis(pkm).Valid, $"{species} en {v} no sale legal");
    }

    [Fact]
    public void ZA_oculta_Zygarde_base_y_ofrece_su_unica_forma()
    {
        Assert.Contains(718, CreatableSpecies.HiddenIn(GameVersion.ZA));
        Assert.Contains((718, (byte)2), AlternateFormCatalog.AvailableIn(GameVersion.ZA));
    }
}
