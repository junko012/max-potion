using System.Text.Json;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Claves que el código arma por concatenación («Bag_Pouch_» + tipo, «Trainer_Lang_» + id, «Review_Stat_» + stat):
// LocalizationKeysTests solo ve las claves escritas completas en las vistas, así que acá se fijan estas familias.
public class LocalizationDynamicKeysTests
{
    private static string UiDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "exxeguttor.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No se encontró exxeguttor.sln"), "src", "Exxeguttor.UI", "i18n");
    }

    private static Dictionary<string, string> Load(string lang)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(UiDir(), lang + ".json")))!;

    // Los 15 InventoryType que BagPouchDatabase.Map nombra (el resto cae al nombre crudo del enum).
    private static readonly string[] PouchTypes =
    {
        "Items", "KeyItems", "TMHMs", "Medicine", "Berries", "Balls", "BattleItems", "MailItems",
        "PCItems", "FreeSpace", "ZCrystals", "Candy", "Treasure", "Ingredients", "MegaStones",
    };

    private static IEnumerable<string> DynamicKeys()
    {
        foreach (var t in PouchTypes) yield return "Bag_Pouch_" + t;
        foreach (var id in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10 }) yield return "Trainer_Lang_" + id;
        foreach (var s in new[] { "Hp", "Atk", "Def", "Spa", "Spd", "Spe" }) yield return "Review_Stat_" + s;
    }

    [Fact]
    public void LosTiposDePouch_SonValoresDelEnumDePKHeX()
    {
        foreach (var t in PouchTypes)
            Assert.True(Enum.TryParse<InventoryType>(t, out _), $"{t} ya no existe en InventoryType");
    }

    [Fact]
    public void LasFamiliasDeClavesDinamicas_ExistenEnEsYEn()
    {
        var es = Load("es"); var en = Load("en");
        var missing = DynamicKeys().Where(k => !es.ContainsKey(k) || !en.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Faltan claves: " + string.Join(", ", missing));
    }
}
