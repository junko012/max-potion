using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Crear Pokémon en Legends: Z-A: especies del Pokédex de Kalos que antes salían con alertas de
// ilegalidad (ver CHANGELOG / SpeciesGameAvailability) y el caso Zygarde, que solo es legal en la
// forma 2 (jefe de misión) y por eso BuildPokemon prueba otras formas si la forma 0 no tiene origen.
public class ZaCreationLegalityTests
{
    private static PokemonService ServiceFor(SaveFile save)
    {
        var saveService = new SaveFileService();
        typeof(SaveFileService)
            .GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        return new PokemonService(saveService);
    }

    private static PKM Build(int species)
    {
        var save = BlankSaveFile.Get(GameVersion.ZA, "TEST", LanguageID.English);
        return ServiceFor(save).BuildPokemon(species);
    }

    [Theory]
    [InlineData(13)]    // Weedle
    [InlineData(15)]    // Beedrill
    [InlineData(65)]    // Alakazam
    [InlineData(68)]    // Machamp
    [InlineData(681)]   // Aegislash
    [InlineData(716)]   // Xerneas
    [InlineData(780)]   // Drampa
    public void EspeciesDeKalos_SalenLegales(int species)
    {
        // El constructor tiene azar (PID, naturaleza): hasta 3 intentos, como el resto de tests.
        var legal = Enumerable.Range(0, 3).Any(_ => new LegalityAnalysis(Build(species)).Valid);
        Assert.True(legal, $"{species} no sale legal en Z-A");
    }

    [Fact]
    public void Zygarde_ForzaLaFormaConOrigenLegal()
    {
        var pk = Build(718);
        Assert.Equal(2, pk.Form);
        Assert.True(new LegalityAnalysis(pk).Valid);
    }

    [Theory]
    [InlineData(718, 5)]   // Mega Zygarde
    [InlineData(658, 3)]   // Mega Greninja
    public void ComboForma_NoOfreceLasMegas(int species, int megaForm)
    {
        var save = BlankSaveFile.Get(GameVersion.ZA, "TEST", LanguageID.English);
        var pk = save.BlankPKM;
        pk.Species = (ushort)species;
        pk.Version = save.Version;
        var forms = PokemonService.GetSelectableForms(pk, GameInfo.GetStrings("en"));
        Assert.True(forms == null || forms.All(f => f.Index != megaForm), $"{species}: el combo ofrece la forma Mega {megaForm}");
    }

    [Fact]
    public void ComboForma_Zygarde_SoloOfreceFormasLegalesParaZA()
    {
        // Creado en Z-A es la forma 2 (jefe de misión); las formas Aura Break (0 y 1) dejarían un
        // Pokémon con EncInvalid sin remedio, así que no se ofrecen. La 3 (Poder Construcción 50%) sí.
        var pk = Build(718);
        var forms = PokemonService.GetSelectableForms(pk, GameInfo.GetStrings("en"));
        Assert.NotNull(forms);
        var idx = forms!.Select(f => (int)f.Index).ToList();
        Assert.True(!idx.Contains(0) && !idx.Contains(1), "Zygarde: el combo ofrece formas Aura Break en Z-A");
        Assert.True(idx.Contains(2) && idx.Contains(3), "Zygarde: faltan las formas Poder Construcción");
    }

    [Fact]
    public void ComboForma_Greninja_NoOfreceAshEnZA()
    {
        var forms = PokemonService.GetSelectableForms(Build(658), GameInfo.GetStrings("en"));
        Assert.True(forms == null, "Greninja: el combo ofrece Ash en Z-A");
    }
}
