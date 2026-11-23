using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fase 3 tanda 5: los nombres de la Mochila salen de
// GameStrings.GetItemStrings(contexto, versión) (lista de objetos de PKHeX PARA ESE JUEGO) indexada
// por el RawItemId, en el idioma de los datos. Sondeo de que la API existe para cada idioma y de que
// los ids bajos (Master Ball = 1 en todas las generaciones) tienen nombre en cada uno.
public class BagItemNamesTests
{
    [Theory]
    [InlineData("en", EntityContext.Gen1, GameVersion.RD)]
    [InlineData("es", EntityContext.Gen3, GameVersion.E)]
    [InlineData("fr", EntityContext.Gen9, GameVersion.SL)]
    [InlineData("de", EntityContext.Gen3, GameVersion.E)]
    [InlineData("ja", EntityContext.Gen9, GameVersion.SL)]
    [InlineData("ko", EntityContext.Gen1, GameVersion.RD)]
    [InlineData("zh-Hans", EntityContext.Gen9, GameVersion.SL)]
    [InlineData("zh-Hant", EntityContext.Gen3, GameVersion.E)]
    public void GetItemStrings_DaNombres_PorJuegoEIdioma(string code, EntityContext context, GameVersion version)
    {
        var items = GameInfo.GetStrings(code).GetItemStrings(context, version);
        Assert.NotEmpty(items);
        Assert.False(string.IsNullOrWhiteSpace(items[1]), $"{code}/{context}: id 1 sin nombre");
    }
}
