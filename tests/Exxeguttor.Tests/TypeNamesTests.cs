using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fase 3 tanda 3: la llave del tipo (inglés, para colores/efectividad) se separa
// del texto mostrado (idioma de los datos). Usa los overloads internos con lista explícita para no
// tocar las preferencias reales del usuario (DataLanguageService.Instance).
public class TypeNamesTests
{
    private static IReadOnlyList<string> Types(string code) => GameInfo.GetStrings(code).Types;

    [Theory]
    [InlineData("Normal", 0)] [InlineData("fighting", 1)] [InlineData("FIRE", 9)]
    [InlineData("Water", 10)] [InlineData("psychic", 13)] [InlineData("Fairy", 17)]
    public void IndexOf_AceptaIngles_EnCualquierCapitalizacion(string name, int esperado)
        => Assert.Equal(esperado, TypeNames.IndexOf(name, Types("en")));

    [Theory]
    [InlineData("Todos")] [InlineData("Stellar")] [InlineData("")] [InlineData("???")]
    public void LoQueNoEsUnTipo_PasaSinCambios(string name)
    {
        Assert.Equal(-1, TypeNames.IndexOf(name, Types("es")));
        Assert.Equal(name, TypeNames.Localize(name, Types("es"), Types("fr")));
        Assert.Equal(name, TypeNames.ToKey(name, Types("es")));
    }

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("ja")] [InlineData("ko")]
    [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void LosDieciochoTipos_RoundTrip_PorIdioma(string code)
    {
        var loc = Types(code);
        var en = Types("en");
        for (var i = 0; i < 18; i++)
        {
            Assert.False(string.IsNullOrEmpty(loc[i]), $"{code}: falta el tipo {i}");
            // el nombre localizado resuelve al mismo índice y a la llave inglesa
            Assert.Equal(i, TypeNames.IndexOf(loc[i], loc));
            Assert.Equal(en[i].ToLowerInvariant(), TypeNames.ToKey(loc[i], loc));
            // y desde la llave inglesa se llega al texto del idioma
            Assert.Equal(loc[i], TypeNames.Localize(en[i], loc, loc));
            Assert.Equal(loc[i], TypeNames.Localize(en[i].ToLowerInvariant(), loc, loc));
        }
    }
}
