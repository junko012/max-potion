using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Tabla ItemNames de pokemon.db (hotfix fix_item_names): nombres de los objetos de Z-A que PKHeX no nombra.
public class ItemNamesDbTests
{
    private const int HyperLumBerry = 2658;
    private const int DirtyScarf = 2613;
    private const int Potion = 17;         // lo nombra PKHeX: no está en ItemNames

    [Fact]
    public void Una_baya_hyper_tiene_nombre_en_el_idioma_pedido()
    {
        var es = PokemonDatabase.Instance.GetItemNames(new[] { HyperLumBerry }, "es");
        var ja = PokemonDatabase.Instance.GetItemNames(new[] { HyperLumBerry }, "ja");
        Assert.Equal("Baya Ziuela D", es[HyperLumBerry]);
        Assert.Equal("いじげんラムのみ", ja[HyperLumBerry]);
    }

    [Fact]
    public void Un_idioma_sin_nombre_cae_al_ingles_de_la_tabla()
    {
        // "xx" no existe en la tabla: debe caer al nombre en inglés.
        var xx = PokemonDatabase.Instance.GetItemNames(new[] { DirtyScarf }, "xx");
        var en = PokemonDatabase.Instance.GetItemNames(new[] { DirtyScarf }, "en");
        Assert.True(en.ContainsKey(DirtyScarf));
        Assert.Equal(en[DirtyScarf], xx[DirtyScarf]);
    }

    [Fact]
    public void Un_objeto_que_pkhex_ya_nombra_no_aparece()
    {
        var names = PokemonDatabase.Instance.GetItemNames(new[] { Potion }, "en");
        Assert.False(names.ContainsKey(Potion));
    }
}
