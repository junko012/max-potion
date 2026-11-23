using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fase 3 (Sets recomendados): habilidades/objetos/movimientos escritos en inglés por
// Showdown/Smogon se resuelven por id y se muestran en el idioma de los datos.
public class GameNamesTests
{
    [Theory]
    [InlineData("Intimidate")] [InlineData("Levitate")] [InlineData("Static")]
    [InlineData("Huge Power")] [InlineData("Protean")] [InlineData("Sheer Force")] [InlineData("intimidate")]
    public void Habilidades_ResuelvenAUnId(string name)
        => Assert.True(GameNames.AbilityId(name) > 0, $"sin id para '{name}'");

    [Theory]
    [InlineData("Leftovers")] [InlineData("Choice Scarf")] [InlineData("Life Orb")] [InlineData("Focus Sash")]
    [InlineData("Heavy-Duty Boots")] [InlineData("Poke Ball")] [InlineData("Poké Ball")] [InlineData("Black Sludge")]
    public void Objetos_ResuelvenAUnId_IncluidosAcentosYGuiones(string name)
        => Assert.True(GameNames.ItemId(name) > 0, $"sin id para '{name}'");

    [Fact]
    public void LaNormalizacion_IgnoraAcentosMayusculasYSignos()
    {
        Assert.Equal(GameNames.ItemId("Poké Ball"), GameNames.ItemId("POKE BALL"));
        Assert.Equal(MoveNames.IdFromEnglish("Will-O-Wisp"), MoveNames.IdFromEnglish("will o wisp"));
        Assert.True(MoveNames.IdFromEnglish("Will-O-Wisp") > 0);
    }

    [Fact]
    public void LoQueNoSeResuelve_PasaSinCambios()
    {
        Assert.Equal("Hidden Power Fire", MoveNames.Localize("Hidden Power Fire"));
        Assert.Equal("", GameNames.Ability(null, GameInfo.GetStrings("es").Ability));
        Assert.Equal("Nada de nada", GameNames.Item("Nada de nada", GameInfo.GetStrings("es").Item));
    }

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("ja")] [InlineData("ko")]
    [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void Localiza_ConElNombreDelIdioma(string code)
    {
        var s = GameInfo.GetStrings(code);
        Assert.Equal(s.Ability[GameNames.AbilityId("Intimidate")], GameNames.Ability("Intimidate", s.Ability));
        Assert.Equal(s.Item[GameNames.ItemId("Leftovers")], GameNames.Item("Leftovers", s.Item));
        Assert.Equal(s.Move[MoveNames.IdFromEnglish("Will-O-Wisp")], MoveNames.Localize("Will-O-Wisp", s.Move));
        Assert.False(string.IsNullOrEmpty(GameNames.Ability("Intimidate", s.Ability)));
    }
}

public class GameNamesByIdTests
{
    [Fact]
    public void IdFueraDeRangoOCero_DaNull()
    {
        Assert.Null(GameNames.MoveName(0));
        Assert.Null(GameNames.MoveName(-5));
        Assert.Null(GameNames.MoveName(100000));
        Assert.Null(GameNames.SpeciesName(0));
        Assert.Null(GameNames.SpeciesName(100000));
    }

    [Fact]
    public void IdValido_DaNombreNoVacio()
    {
        Assert.False(string.IsNullOrEmpty(GameNames.MoveName(1)));
        Assert.False(string.IsNullOrEmpty(GameNames.SpeciesName(25)));
    }
}
