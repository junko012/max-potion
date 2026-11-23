using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fase 3 tanda 2: los learnsets traen el id del movimiento (mismo espacio que
// pkm.MoveN y GameStrings.Move) para mostrar el nombre en el idioma de los datos y editar por id.
public class LearnsetMoveIdTests
{
    [Theory]
    [InlineData(1, 6)]   // Bulbasaur, Emerald (gameId 6 según MapVersionToGameId)
    [InlineData(6, 6)]   // Charizard, Emerald
    public void GetLearnsets_TraeMoveId_QueCoincideConElNombreDeLaDB(int speciesId, int gameId)
    {
        var entries = PokemonDatabase.Instance.GetLearnsets(speciesId, gameId);
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.True(e.MoveId > 0, $"MoveId faltante para {e.MoveName}"));

        // El nombre inglés de PKHeX para ese id debe ser el mismo movimiento que el slug de la DB
        // ("flare-blitz" == "Flare Blitz"): confirma que ambos usan el mismo espacio de ids.
        var en = GameInfo.GetStrings("en").Move;
        static string Norm(string v) => new string(v.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        Assert.All(entries.Take(40), e => Assert.Equal(Norm(e.MoveName), Norm(en[e.MoveId])));
    }

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("ja")] [InlineData("ko")]
    [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void CadaMoveIdDeLearnset_TieneNombreEnElIdioma(string code)
    {
        var names = GameInfo.GetStrings(code).Move;
        var entries = PokemonDatabase.Instance.GetLearnsets(6, 6);
        Assert.All(entries, e => Assert.False(string.IsNullOrEmpty(names[e.MoveId]), $"{code}: sin nombre para id {e.MoveId}"));
    }
}
