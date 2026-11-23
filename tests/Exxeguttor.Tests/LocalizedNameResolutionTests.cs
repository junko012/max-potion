using System.Collections.Generic;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// DIAGNÓSTICO DE HIPÓTESIS — resolución de nombres de objetos/movimientos por idioma.
//
// Hipótesis: HeldItem y Move1..4 del PokemonEditorViewModel guardan el nombre de PKHeX en el idioma
// de la UI (strings.Item[i] / strings.Move[i]), pero al exportar y al armar el preview de legalidad se
// resuelven con PokemonDatabase.GetItemByName / GetMoveByName, que buscan en Items.Name / Moves.Name
// de pokemon.db — solo inglés. Con la UI en otro idioma el lookup devuelve null y la edición se omite
// sin avisar (EditApplyService: "if (entry != null) ...").
//
// Estos tests documentan el comportamiento ACTUAL. Si todos pasan, la hipótesis está confirmada.
// Cuando se implemente el arreglo por id (las ediciones llevan el id, no el nombre), el test
// Hipotesis_* debe borrarse o invertirse.
//
// Correr con: dotnet test --filter LocalizedNameResolutionTests
public class LocalizedNameResolutionTests
{
    private const int MaxItemId = 920; // tope de objetos de Gen 7 (USUM); alcanza para la medición
    private const int MaxMoveId = 919;
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public LocalizedNameResolutionTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    // ── Control: con nombres en inglés la DB resuelve bien ────────────────────────────────────────
    [Theory]
    [InlineData(234, "Leftovers")]
    [InlineData(656, "Gengarite")]
    [InlineData(804, "Snorlium Z")]
    [InlineData(17,  "Potion")]
    public void Control_NombreEnIngles_ObjetoResuelve(int id, string nombreEn)
    {
        var strings = GameInfo.GetStrings("en");
        Assert.Equal(nombreEn, strings.Item[id]);
        var entry = PokemonDatabase.Instance.GetItemByName(strings.Item[id]);
        Assert.NotNull(entry);
        Assert.Equal(id, entry!.Id);
    }

    [Theory]
    [InlineData(53,  "Flamethrower")]
    [InlineData(126, "Fire Blast")]
    [InlineData(38,  "Double-Edge")]
    [InlineData(63,  "Hyper Beam")]
    public void Control_NombreEnIngles_MovimientoResuelve(int id, string nombreEn)
    {
        var strings = GameInfo.GetStrings("en");
        Assert.Equal(nombreEn, strings.Move[id]);
        var entry = PokemonDatabase.Instance.GetMoveByName(strings.Move[id]);
        Assert.NotNull(entry);
        Assert.Equal(id, entry!.Id);
    }

    // ── Hipótesis: los mismos objetos/movimientos con el nombre de PKHeX en español NO resuelven ──
    [Theory]
    [InlineData(234)] // Restos
    [InlineData(656)] // Gengarita
    [InlineData(804)] // Snorlastal Z
    [InlineData(17)]  // Poción
    public void Hipotesis_NombreEnEspanol_ObjetoNoResuelve(int id)
    {
        var nombreEs = GameInfo.GetStrings("es").Item[id];
        _out.WriteLine($"Objeto {id}: PKHeX es='{nombreEs}' -> GetItemByName = " +
                       (PokemonDatabase.Instance.GetItemByName(nombreEs)?.Id.ToString() ?? "null"));
        Assert.Null(PokemonDatabase.Instance.GetItemByName(nombreEs));
    }

    [Theory]
    [InlineData(53)]  // Lanzallamas
    [InlineData(126)] // Llamarada
    [InlineData(38)]  // Doble Filo
    [InlineData(63)]  // Hiperrayo
    public void Hipotesis_NombreEnEspanol_MovimientoNoResuelve(int id)
    {
        var nombreEs = GameInfo.GetStrings("es").Move[id];
        _out.WriteLine($"Movimiento {id}: PKHeX es='{nombreEs}' -> GetMoveByName = " +
                       (PokemonDatabase.Instance.GetMoveByName(nombreEs)?.Id.ToString() ?? "null"));
        Assert.Null(PokemonDatabase.Instance.GetMoveByName(nombreEs));
    }

    // ── Medición: porcentaje de nombres de PKHeX que resuelven, por idioma ────────────────────────
    [Theory]
    [InlineData("en", true)]
    [InlineData("es", false)]
    [InlineData("fr", false)]
    [InlineData("de", false)]
    [InlineData("it", false)]
    public void Medicion_TasaDeResolucionPorIdioma(string lang, bool debeResolverCasiTodo)
    {
        var strings = GameInfo.GetStrings(lang);
        var db = PokemonDatabase.Instance;

        var (itemTotal, itemOk) = (0, 0);
        for (var i = 1; i <= MaxItemId && i < strings.Item.Count; i++)
        {
            var name = strings.Item[i];
            if (string.IsNullOrWhiteSpace(name) || name == "???") continue;
            itemTotal++;
            if (db.GetItemByName(name) != null) itemOk++;
        }

        var (moveTotal, moveOk) = (0, 0);
        for (var i = 1; i <= MaxMoveId && i < strings.Move.Count; i++)
        {
            var name = strings.Move[i];
            if (string.IsNullOrWhiteSpace(name) || name == "-") continue;
            moveTotal++;
            if (db.GetMoveByName(name) != null) moveOk++;
        }

        var itemPct = 100.0 * itemOk / itemTotal;
        var movePct = 100.0 * moveOk / moveTotal;
        _out.WriteLine($"[{lang}] objetos: {itemOk}/{itemTotal} ({itemPct:F0}%)  movimientos: {moveOk}/{moveTotal} ({movePct:F0}%)");

        if (debeResolverCasiTodo)
        {
            Assert.True(itemPct >= 90, $"[{lang}] objetos {itemPct:F0}% (se esperaba >= 90%)");
            Assert.True(movePct >= 90, $"[{lang}] movimientos {movePct:F0}% (se esperaba >= 90%)");
        }
        else
        {
            // Cualquier idioma que no sea inglés: la gran mayoría de los nombres no resuelve.
            Assert.True(itemPct < 25, $"[{lang}] objetos {itemPct:F0}% (se esperaba < 25% si la DB es solo inglés)");
            Assert.True(movePct < 25, $"[{lang}] movimientos {movePct:F0}% (se esperaba < 25% si la DB es solo inglés)");
        }
    }
}
