using System;
using System.Collections.Generic;
using System.Linq;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Valida los supuestos del arreglo de idioma (ediciones por ID en vez de por nombre):
//  1. El id de un objeto / habilidad se deriva del índice de strings.Item / strings.Ability de PKHeX:
//     nombre → índice debe volver al mismo índice en todos los idiomas.
//  2. Las listas de PKHeX están alineadas entre idiomas (mismo índice = mismo objeto), que es lo que permite
//     filtrar las balls con el nombre inglés del mismo índice y mostrar el nombre localizado.
// Correr con: dotnet test --filter NameToIdIndexTests
public class NameToIdIndexTests
{
    private const int MaxItemId = 920;
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public NameToIdIndexTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("it")]
    public void Habilidades_NombreVuelveAlMismoId(string lang)
    {
        var abilities = GameInfo.GetStrings(lang).Ability;
        var firstWins = new Dictionary<string, int>(StringComparer.Ordinal);
        int total = 0, ok = 0;
        for (var i = 1; i < abilities.Count; i++)
        {
            var name = abilities[i];
            if (string.IsNullOrWhiteSpace(name) || name == "-") continue;
            total++;
            firstWins.TryAdd(name, i);
            if (firstWins[name] == i) ok++;
        }
        var pct = 100.0 * ok / total;
        _out.WriteLine($"[{lang}] habilidades que vuelven a su id: {ok}/{total} ({pct:F1}%)");
        Assert.True(pct >= 97, $"[{lang}] {pct:F1}% (se esperaba >= 97%)");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("it")]
    public void Objetos_NombreVuelveAlMismoId(string lang)
    {
        var items = GameInfo.GetStrings(lang).Item;
        var lastWins = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 1; i <= MaxItemId && i < items.Count; i++)
            if (!string.IsNullOrWhiteSpace(items[i])) lastWins[items[i]] = i;
        int total = 0, ok = 0;
        for (var i = 1; i <= MaxItemId && i < items.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(items[i])) continue;
            total++;
            if (lastWins[items[i]] == i) ok++;
        }
        var pct = 100.0 * ok / total;
        _out.WriteLine($"[{lang}] objetos que vuelven a su id: {ok}/{total} ({pct:F1}%)");
        Assert.True(pct >= 93, $"[{lang}] {pct:F1}% (se esperaba >= 93%)");
    }

    // Los Z-Crystals equipados y los de la mochila deben tener nombres distintos (PKHeX agrega " [Z]" al de
    // la mochila): si no, derivar el id por nombre podría devolver el id de mochila, que es ilegal equipado.
    [Theory]
    [InlineData("en")] [InlineData("es")]
    public void ZCrystalsEquipados_TienenNombreDistintoDelDeLaMochila(string lang)
    {
        var items = GameInfo.GetStrings(lang).Item;
        Assert.NotEqual(items[776], items[807]);  // Normalium Z equipado vs de la mochila
        Assert.NotEqual(items[804], items[832]);  // Snorlium Z equipado vs de la mochila
        Assert.EndsWith("[Z]", items[807]);
    }

    // La lista de balls nueva (nombre inglés del mismo índice para filtrar, nombre localizado para mostrar)
    // debe dar la misma cantidad en todos los idiomas. Con el filtro anterior (nombre localizado contra la
    // DB en inglés) en español quedaban 9 de 36 y en alemán 0.
    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("it")]
    public void ListaDeBalls_TieneLaMismaCantidadQueEnIngles(string lang)
    {
        var ballCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "standard-balls", "special-balls", "apricorn-balls" };
        var ballSlugs = new HashSet<string>(
            PokemonDatabase.Instance.GetItemsByCategory(ballCategories)
                .Select(b => b.Name.ToLowerInvariant().Replace(" ", "-")),
            StringComparer.OrdinalIgnoreCase);

        int Count(string language)
        {
            var strings = GameInfo.GetStrings(language);
            var en = GameInfo.GetStrings("en").Item;
            var n = 0;
            for (var i = 1; i <= MaxItemId && i < strings.Item.Count; i++)
            {
                var name = strings.Item[i];
                if (string.IsNullOrWhiteSpace(name)) continue;
                var enName = i < en.Count && !string.IsNullOrWhiteSpace(en[i]) ? en[i] : name;
                if (ballSlugs.Contains(enName.ToLowerInvariant().Replace(" ", "-"))) n++;
            }
            return n;
        }

        var english = Count("en");
        var localized = Count(lang);
        _out.WriteLine($"balls: en={english}, {lang}={localized}");
        Assert.True(english >= 20);
        Assert.Equal(english, localized);
    }
}
