using System.Linq;
using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// ARCHIVO TEMPORAL DE DIAGNÓSTICO — vuelca lo que devuelve GetLearnsets(species, gameId)
// tal cual sale de la DB, para ver si LearnItem llega poblado o si se pierde en el camino.
// Correr con: dotnet test --filter DiagnosticarLearnsets
public class LearnsetDiagnosticTests
{
    [Theory]
    [InlineData(1, 6)]   // Bulbasaur, Emerald (gameId 6 según MapVersionToGameId)
    [InlineData(152, 6)] // Chikorita, por si el 1 no tiene datos en esta DB
    public void DiagnosticarLearnsets(int speciesId, int gameId)
    {
        var db = PokemonDatabase.Instance;
        var entries = db.GetLearnsets(speciesId, gameId);

        var lines = entries
            .Where(e => e.LearnMethod is "tm" or "tr" or "egg" or "tutor" or "levelup")
            .OrderBy(e => e.LearnMethod)
            .Select(e => $"Method='{e.LearnMethod}' Level={e.LearnLevel} LearnItem={(e.LearnItem == null ? "<null>" : $"'{e.LearnItem}'")} Move={e.MoveName}")
            .Take(20)
            .ToList();

        Assert.Fail($"speciesId={speciesId} gameId={gameId} totalEntries={entries.Count}\n" + string.Join("\n", lines));
    }
}
