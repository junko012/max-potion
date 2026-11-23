using System.Text.RegularExpressions;
using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Tablas ItemDescriptions / MachineMoves de pokemon.db (hotfix fix_item_descriptions) y su lectura.
public class ItemDescriptionsDbTests
{
    private const int GameZA = 12;
    private const int GameSV = 18;

    [Fact]
    public void Un_objeto_comun_tiene_descripcion_en_ingles_y_en_otro_idioma()
    {
        const int potion = 17;
        var en = PokemonDatabase.Instance.GetItemDescriptions(new[] { potion }, "en");
        var es = PokemonDatabase.Instance.GetItemDescriptions(new[] { potion }, "es");
        Assert.True(en.TryGetValue(potion, out var enText) && enText.Length > 0);
        Assert.True(es.TryGetValue(potion, out var esText) && esText.Length > 0);
        Assert.NotEqual(enText, esText);
    }

    [Fact]
    public void Un_idioma_sin_texto_cae_al_ingles_de_la_misma_tabla()
    {
        // Mayonnaise (Escarlata/Púrpura): la investigación trae solo inglés → en cualquier idioma debe salir ese texto.
        const int mayonnaise = 1889;
        var ja = PokemonDatabase.Instance.GetItemDescriptions(new[] { mayonnaise }, "ja");
        var en = PokemonDatabase.Instance.GetItemDescriptions(new[] { mayonnaise }, "en");
        Assert.True(ja.ContainsKey(mayonnaise));
        Assert.Equal(en[mayonnaise], ja[mayonnaise]);
    }

    [Theory]
    [InlineData(GameZA, 2168, 4)]    // Z-A TM108 (Mega Dimension) → Comet Punch
    [InlineData(GameSV, 2289, 918)]  // Escarlata/Púrpura TM229
    public void Las_MT_nuevas_tienen_movimiento_por_juego(int gameId, int itemId, int moveId)
    {
        var moves = PokemonDatabase.Instance.GetMachineMovesByItem(gameId);
        Assert.True(moves.TryGetValue(itemId, out var actual));
        Assert.Equal(moveId, actual);
    }

    [Fact]
    public void Casi_todos_los_objetos_de_mochila_tienen_descripcion()
    {
        // 74 sin texto al cargar la investigación (65 sin fuente + 9 sin verificar + Data ROM...). Si sube, algo se rompió.
        var machine = new Regex(@"^(TM|TR|HM)\d+$");
        var ids = new HashSet<int>();
        for (var game = 1; game <= 24; game++)
            foreach (var row in PokemonDatabase.Instance.GetItemPouchRows(game)) ids.Add(row.ItemId);
        var items = PokemonDatabase.Instance.GetItems(ids);
        var described = PokemonDatabase.Instance.GetItemDescriptions(ids, "en");
        var missing = ids.Where(id => items.TryGetValue(id, out var it)
                                      && !machine.IsMatch(it.Name)
                                      && !described.ContainsKey(id)
                                      && string.IsNullOrWhiteSpace(it.Description))
                         .Select(id => items[id].Name).ToList();
        Assert.True(missing.Count <= 80, $"{missing.Count} objetos sin descripción: {string.Join(", ", missing.Take(30))}");
    }
}
