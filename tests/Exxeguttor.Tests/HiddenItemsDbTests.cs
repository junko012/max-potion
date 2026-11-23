using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Tabla HiddenItems de pokemon.db (hotfix fix_hidden_items): MT sin movimiento conocido.
public class HiddenItemsDbTests
{
    private const int GameZA = 12;
    private const int GameSV = 18;

    [Fact]
    public void TM161_de_ZA_y_TM00_de_SV_estan_ocultas()
    {
        Assert.Contains(2221, PokemonDatabase.Instance.GetHiddenItemIds(GameZA));
        Assert.Contains(1230, PokemonDatabase.Instance.GetHiddenItemIds(GameSV));
    }

    [Fact]
    public void Una_MT_con_movimiento_no_esta_oculta()
    {
        // Z-A TM108 (item 2168) tiene movimiento en MachineMoves.
        Assert.DoesNotContain(2168, PokemonDatabase.Instance.GetHiddenItemIds(GameZA));
    }
}
