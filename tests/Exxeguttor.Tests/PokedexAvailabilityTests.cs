using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// El álbum de la Pokédex usa la misma fuente de disponibilidad que el selector de crear Pokémon
// (SpeciesGameAvailability, Status = 'uncreatable'). Correr con: dotnet test --filter PokedexAvailabilityTests
public class PokedexAvailabilityTests
{
    private static PokedexService PokedexFor(SaveFile save)
    {
        var saveService = new SaveFileService();
        typeof(SaveFileService)
            .GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        return new PokedexService(new PokemonService(saveService), PokemonDatabase.Instance);
    }

    private static SaveFile Blank(GameVersion game) => BlankSaveFile.Get(game, "TEST", LanguageID.English);

    [Theory]
    [InlineData(GameVersion.GP)]   // Let's Go
    [InlineData(GameVersion.PLA)]
    [InlineData(GameVersion.SW)]
    [InlineData(GameVersion.SL)]
    [InlineData(GameVersion.SN)]
    public void Album_NoMuestraLasEspeciesNoCreables(GameVersion game)
    {
        var save = Blank(game);
        var hidden = CreatableSpecies.HiddenIn(save.Version);
        var ids = PokedexFor(save).GetAlbumEntries(save).Select(e => e.SpeciesId).ToList();

        // Una especie oculta sigue en el álbum solo si el juego tiene una forma suya (Goodra de Hisui en
        // Legends: Arceus, Zygarde forma 2 en Z-A): ver PokedexService.GetAlbumEntries.
        var withForm = RegionalFormCatalog.AvailableIn(save).Select(f => f.Species)
            .Concat(AlternateFormCatalog.AvailableIn(save.Version).Select(a => a.Species))
            .ToHashSet();
        Assert.DoesNotContain(ids, id => hidden.Contains(id) && !withForm.Contains(id));
        Assert.Equal(save.MaxSpeciesID - hidden.Count(id => !withForm.Contains(id)), ids.Count);
    }

    [Fact]
    public void Legends_Arceus_MantieneLasEspeciesQueSoloExistenComoFormaDeHisui()
    {
        // Goodra base no se puede crear en Legends: Arceus (solo existe la de Hisui), pero sigue siendo
        // una entrada del álbum — con su forma de Hisui.
        var save = Blank(GameVersion.PLA);
        var entries = PokedexFor(save).GetAlbumEntries(save);
        var goodra = Assert.Single(entries, e => e.SpeciesId == 706);
        Assert.Equal(1, goodra.Form);
        Assert.Contains(entries, e => e.SpeciesId == 487);   // Giratina (Origen)
    }

    [Fact]
    public void LetsGo_SoloLasEspeciesDelJuego()
    {
        // Let's Go: 153 nativas (la DB marca 656 de 809 como uncreatable). Antes el álbum mostraba las 809.
        var save = Blank(GameVersion.GP);
        Assert.Equal(153, PokedexFor(save).GetAlbumEntries(save).Count);
    }

    [Fact]
    public void ZA_NoMuestraLasEspeciesAusentesDeSuTabla()
    {
        // Z-A: además de lo 'uncreatable' de la DB, el álbum oculta toda especie que la tabla personal del juego no tiene
        // (CreatableSpecies.HiddenForCreation), igual que el selector de crear Pokémon.
        var save = Blank(GameVersion.ZA);
        var hidden = CreatableSpecies.HiddenForCreation(save);
        var ids = PokedexFor(save).GetAlbumEntries(save).Select(e => e.SpeciesId).ToList();
        var withForm = RegionalFormCatalog.AvailableIn(save).Select(f => f.Species)
            .Concat(AlternateFormCatalog.AvailableIn(save.Version).Select(a => a.Species))
            .ToHashSet();
        Assert.DoesNotContain(ids, id => hidden.Contains(id) && !withForm.Contains(id));
        Assert.Equal(save.MaxSpeciesID - hidden.Count(id => !withForm.Contains(id)), ids.Count);
    }

    [Fact]
    public void ZA_NoMuestraLasEspeciesQueSoloLleganPorTransferencia()
    {
        // Sandshrew (27) no está en la tabla de Z-A (transfer_only): ese save no puede guardarlo, así que el álbum
        // ya no lo muestra (ver PokedexService.GetAlbumEntries).
        var save = Blank(GameVersion.ZA);
        Assert.DoesNotContain(PokedexFor(save).GetAlbumEntries(save), e => e.SpeciesId == 27);
    }

    [Fact]
    public void SwSh_IncluyeMewtwoQueLaListaEmbebidaOmitia()
    {
        var save = Blank(GameVersion.SW);
        Assert.Contains(PokedexFor(save).GetAlbumEntries(save), e => e.SpeciesId == 150);
    }

    [Fact]
    public void Gen7_SinRestricciones_MuestraTodasHastaElMaximo()
    {
        var save = Blank(GameVersion.SN);
        Assert.Equal(save.MaxSpeciesID, PokedexFor(save).GetAlbumEntries(save).Count);
    }
}
