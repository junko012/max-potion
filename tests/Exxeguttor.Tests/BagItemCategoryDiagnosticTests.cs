using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// DIAGNÓSTICO — reportado: en Gen1 no aparecen TMs/HMs (esperable, ver abajo); en Gen2/Gen3
// los ítems de distintas categorías (TMs, Poké Balls) aparecen "mezclados" con Objetos.
// Dos hipótesis a distinguir, sin asumir cuál es (ya me equivoqué dos veces esta sesión
// confiando en una firma/comportamiento de PKHeX.Core sin verificar):
//   A) InventoryPouch.CanContain no es exclusivo por pouch — el mismo itemId da true para
//      MÁS DE UN pouch del mismo save (ej. una Poké Ball "puede estar" tanto en el pouch
//      Items como en el pouch Balls).
//   B) El ItemId de pokemon.db no coincide 1:1 con el índice de PKHeX.Core para TODOS los
//      ítems como se asumió en ItemInventoryService — el nombre mostrado no corresponde al
//      ítem real de esa posición, dando la apariencia de "mezcla" aunque CanContain esté
//      filtrando bien.
public class BagItemCategoryDiagnosticTests
{
    [Fact]
    public void DiagnosticarCategorizacionDeItems()
    {
        var sb = new System.Text.StringBuilder();

        foreach (var (label, version) in new (string, GameVersion)[]
                 {
                     ("Gen1 — Rojo", GameVersion.RD),
                     ("Gen2 — Oro", GameVersion.GD),
                     ("Gen3 — Esmeralda", GameVersion.E),
                 })
        {
            var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
            sb.AppendLine($"── {label} ──");
            sb.AppendLine($"  Pouches: {string.Join(", ", save.Inventory.Select(p => p.Type))}");

            // Para cada itemId de 1 a 400 (cubre bolas, objetos comunes, MTs y bastante más),
            // listar en qué pouch(es) CanContain da true, y el nombre que le pone pokemon.db
            // a ese índice — así vemos de un vistazo si hay solapamiento entre pouches
            // (hipótesis A) y si el nombre tiene sentido para la categoría marcada
            // (hipótesis B). Solo se imprimen los que dan match en al menos un pouch, así que
            // el volumen real depende de cuántos ítems soporte cada juego.
            for (int itemId = 1; itemId <= 400; itemId++)
            {
                var matchingPouches = save.Inventory
                    .Where(p => SafeCanContain(p, itemId))
                    .Select(p => p.Type.ToString())
                    .ToList();

                if (matchingPouches.Count == 0) continue;

                var dbName = Exxeguttor.App.Services.PokemonDatabase.Instance.GetItem(itemId)?.Name ?? "(sin nombre en DB)";
                var flag = matchingPouches.Count > 1 ? "  ⚠️ EN MÁS DE UN POUCH" : "";
                sb.AppendLine($"  itemId={itemId,-3} DB=\"{dbName}\"  pouches=[{string.Join(", ", matchingPouches)}]{flag}");
            }
            sb.AppendLine();
        }

        Assert.Fail(sb.ToString());
    }

    private static bool SafeCanContain(InventoryPouch pouch, int itemId)
    {
        try { return pouch.CanContain((ushort)itemId); }
        catch { return false; }
    }
}
