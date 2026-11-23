using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// DIAGNÓSTICO — seguimiento de BagItemCategoryDiagnosticTests. Resultado de esa corrida:
// Gen3 quedó limpio (cada ítem en un solo pouch, salvo el overlap legítimo Items/PCItems),
// pero Gen1/Gen2 mostraron solapamiento MASIVO e ítems que ni siquiera existen en esa
// generación (ej. "old-gateau", ítem de Sinnoh/Gen4, apareciendo en un save de Gen2). Hipótesis:
// InventoryPouch.CanContain no hace un chequeo estricto de categoría en los pouches
// "genéricos" (clase base InventoryPouch, sin subclase propia — Gen1/2/4/5/6), a diferencia
// de las subclases dedicadas (InventoryPouch3/7/8/9) que sí parecen filtrar bien.
// InventoryPouch.Info (IItemStorage) expone IsLegal(InventoryType, int, ...) — capaz es el
// chequeo real y estricto que hace falta, en vez de CanContain a nivel de instancia de pouch.
public class ItemStorageDiagnosticTests
{
    [Fact]
    public void DiagnosticarIsLegalVsCanContain()
    {
        var sb = new System.Text.StringBuilder();

        foreach (var (label, version) in new (string, GameVersion)[]
                 {
                     ("Gen2 — Oro", GameVersion.GD),
                 })
        {
            var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
            sb.AppendLine($"── {label} ──");

            foreach (var pouch in save.Inventory)
            {
                sb.AppendLine($"  Pouch {pouch.Type}: Info tipo real = {pouch.Info.GetType().Name}");
            }
            sb.AppendLine();

            // Comparar CanContain vs Info.IsLegal para un puñado de itemIds representativos
            // (algunos que sabemos que deberían ser Balls, algunos que deberían ser Items
            // comunes, y el caso que salió mal: itemId=54 "old-gateau" en el pouch Items).
            int[] sample = { 1, 3, 4, 7, 17, 54, 191, 305 };
            foreach (var pouch in save.Inventory)
            {
                foreach (var itemId in sample)
                {
                    bool canContain;
                    try { canContain = pouch.CanContain((ushort)itemId); } catch { canContain = false; }

                    bool isLegal;
                    string isLegalErr = "";
                    try { isLegal = pouch.Info.IsLegal(pouch.Type, itemId, 0); }
                    catch (Exception ex) { isLegal = false; isLegalErr = $" [IsLegal tiró: {ex.GetType().Name}: {ex.Message}]"; }

                    var dbName = PokemonDatabase.Instance.GetItem(itemId)?.Name ?? "?";
                    if (canContain || isLegal)
                        sb.AppendLine($"  {pouch.Type,-10} item={itemId,-4} \"{dbName}\"  CanContain={canContain}  IsLegal={isLegal}{isLegalErr}");
                }
            }
            sb.AppendLine();
        }

        Assert.Fail(sb.ToString());
    }
}
