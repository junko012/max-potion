using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// RibbonDatabase (UI) guarda las ediciones de cintas con la llave RibbonIndex.ToString() y muestra el nombre
// de GameInfo.Strings.Ribbons (idioma de los datos). El proyecto de tests no referencia UI: acá se fija el
// contrato de PKHeX.Core que esa tabla asume (verificado contra el código fuente de 25.11.07).
public class RibbonNamesTests
{
    private static IEnumerable<RibbonIndex> AllIndexes() =>
        Enumerable.Range(0, (int)RibbonIndex.MAX_COUNT).Select(i => (RibbonIndex)i);

    [Fact]
    public void LaLlaveDeEdicion_SeRecuperaConEnumTryParse()
    {
        // EditApplyService hace Enum.TryParse<RibbonIndex>(llave): antes la llave era el nombre visible
        // y solo 2 de 111 cintas (Premier, Hisui) se aplicaban al exportar.
        foreach (var idx in AllIndexes())
        {
            Assert.True(Enum.TryParse<RibbonIndex>(idx.ToString(), out var back), $"{idx}: la llave no se parsea");
            Assert.Equal(idx, back);
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("es-419")] [InlineData("fr")] [InlineData("it")]
    [InlineData("de")] [InlineData("ja")] [InlineData("ko")] [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void TodasLasCintas_TienenNombreEnCadaIdioma(string code)
    {
        var ribbons = GameInfo.GetStrings(code).Ribbons;
        foreach (var idx in AllIndexes())
        {
            var prop = idx.GetPropertyName();
            Assert.True(ribbons.GetNameSafe(prop, out var name), $"{code}: {idx} ({prop}) sin nombre");
            Assert.False(string.IsNullOrWhiteSpace(name), $"{code}: {idx} con nombre vacío");
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("ja")]
    public void LosNombres_NoSeRepitenDentroDeUnIdioma(string code)
    {
        var ribbons = GameInfo.GetStrings(code).Ribbons;
        var names = AllIndexes().Select(i => { ribbons.GetNameSafe(i.GetPropertyName(), out var n); return n; }).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
