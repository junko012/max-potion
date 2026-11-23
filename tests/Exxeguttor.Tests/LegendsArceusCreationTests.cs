using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Crear Pokémon en Legends: Arceus: 37 especies salían con alertas (movimientos vacíos o inválidos,
// FormArgument de Basculegion/Wyrdeer). Ver FallbackToSuggestedMoveset y TryEvolveForward.
public class LegendsArceusCreationTests
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
    [InlineData(42)]    // Golbat
    [InlineData(64)]    // Kadabra
    [InlineData(134)]   // Vaporeon
    [InlineData(156)]   // Quilava
    [InlineData(185)]   // Sudowoodo
    [InlineData(400)]   // Bibarel
    [InlineData(463)]   // Lickilicky
    [InlineData(473)]   // Mamoswine
    [InlineData(899)]   // Wyrdeer (FormArgument + Nv.31)
    [InlineData(902)]   // Basculegion (FormArgument 294)
    public void LegendsArceus_EspeciesAntesIlegales_SalenLegales(int species)
    {
        // El constructor tiene azar: hasta 3 intentos, como el resto de tests.
        var legal = Enumerable.Range(0, 3).Any(_ => new LegalityAnalysis(Build(GameVersion.PLA, species)).Valid);
        Assert.True(legal, $"{species} no sale legal en Legends: Arceus");
    }

    [Theory]
    [InlineData(463)]   // Lickilicky (huevo evolucionado en Espada)
    [InlineData(473)]   // Mamoswine
    [InlineData(465)]   // Tangrowth
    [InlineData(763)]   // Tsareena
    [InlineData(853)]   // Grapploct
    public void EspadaEscudo_HuevosEvolucionados_SalenLegales(int species)
    {
        // Sin reintentos: el constructor debe dar un Pokémon legal SIEMPRE, en el primer intento.
        for (var i = 0; i < 5; i++)
            Assert.True(new LegalityAnalysis(Build(GameVersion.SW, species)).Valid, $"{species} no sale legal en Espada (intento {i})");
    }

    [Theory]
    [InlineData(469)]   // Yanmega (evoluciona sabiendo Ancient Power)
    [InlineData(982)]   // Dudunsparce (evoluciona sabiendo Hyper Drill)
    public void EscarlataPurpura_EvolucionPorMovimiento_SaleLegal(int species)
    {
        var legal = Enumerable.Range(0, 4).Any(_ => new LegalityAnalysis(Build(GameVersion.SL, species)).Valid);
        Assert.True(legal, $"{species} no sale legal en Escarlata");
    }
}
