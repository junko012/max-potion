using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// NatureDatabase (UI) guarda la llave inglesa = nombre del enum PKHeX.Core.Nature, en el mismo orden que
// el valor numérico (Hardy = 0 … Quirky = 24), y muestra el texto de GameStrings.Natures[id] en el idioma
// de los datos. El proyecto de tests no referencia UI: acá se fija el contrato que esa tabla asume.
public class NatureNamesTests
{
    private static readonly string[] Keys =
    {
        "Hardy", "Lonely", "Brave", "Adamant", "Naughty", "Bold", "Docile", "Relaxed", "Impish", "Lax",
        "Timid", "Hasty", "Serious", "Jolly", "Naive", "Modest", "Mild", "Quiet", "Bashful", "Rash",
        "Calm", "Gentle", "Sassy", "Careful", "Quirky",
    };

    [Fact]
    public void LaTablaFijaCoincideConElEnumDePKHeX()
    {
        for (var i = 0; i < Keys.Length; i++)
            Assert.Equal(Keys[i], ((Nature)i).ToString());
        Assert.True(Enum.TryParse<Nature>("Adamant", out var n) && (int)n == 3);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("ja")]
    [InlineData("ko")] [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void LasVeinticincoNaturalezas_TienenNombreEnCadaIdioma(string code)
    {
        var names = GameInfo.GetStrings(code).Natures;
        for (var i = 0; i < 25; i++)
            Assert.False(string.IsNullOrWhiteSpace(names[i]), $"{code}: naturaleza {i} sin nombre");
    }

    [Fact]
    public void EnIngles_ElNombreDePKHeXEsLaLlave()
    {
        var en = GameInfo.GetStrings("en").Natures;
        for (var i = 0; i < Keys.Length; i++) Assert.Equal(Keys[i], en[i]);
    }
}
