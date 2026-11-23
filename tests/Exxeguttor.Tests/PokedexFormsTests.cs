using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Pokédex con formas regionales (decisiones del 11 oct): una tarjeta por especie que muestra la forma
// regional de la generación del juego cargado; la cadena evolutiva muestra todas las ramas; las condiciones
// salen en el idioma de la UI. Correr con: dotnet test --filter PokedexFormsTests
public class PokedexFormsTests
{
    private static PokedexService PokedexFor(SaveFile save)
    {
        var saveService = new SaveFileService();
        typeof(SaveFileService)
            .GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        return new PokedexService(new PokemonService(saveService), PokemonDatabase.Instance);
    }

    private static SaveFile Blank(GameVersion game) => BlankSaveFile.Get(game, "TEST", LanguageID.English);

    // ── Tarjeta: la forma de la generación ────────────────────────────────────────────────────────
    [Fact]
    public void Gen7_TarjetaDeExeggutor_EsLaDeAlola()
    {
        var save = Blank(GameVersion.SN);
        var entry = PokedexFor(save).GetAlbumEntries(save).Single(e => e.SpeciesId == 103);
        Assert.Equal(1, entry.Form);
        Assert.Equal(10114, entry.DbSpeciesId);   // exeggutor-alola
        Assert.Contains(entry.Types, t => t.Equals("dragon", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Gen4_SinFormasRegionales_LaTarjetaEsLaBase()
    {
        var save = Blank(GameVersion.Pt);
        var entry = PokedexFor(save).GetAlbumEntries(save).Single(e => e.SpeciesId == 103);
        Assert.Equal(0, entry.Form);
        Assert.Equal(103, entry.DbSpeciesId);
    }

    // ── Cadena: todas las ramas ───────────────────────────────────────────────────────────────────
    [Fact]
    public void Gen7_Exeggcute_EvolucionaAAmbasRamas()
    {
        var save = Blank(GameVersion.SN);
        var family = PokedexFor(save).GetAlbumEvolutionFamily(102, 0, save, "es");

        var baseNode = Assert.Single(family.Next, n => n.SpeciesId == 103);
        var alolaNode = Assert.Single(family.Next, n => n.SpeciesId == 10114);
        Assert.Equal(0, baseNode.Form);
        Assert.Equal(1, alolaNode.Form);
        Assert.Equal(103, alolaNode.NationalId);

        // La arista base arrastra region=alola en pokemon.db: no debe mostrarse en la rama base.
        Assert.Equal("Piedra Hoja", baseNode.ConditionText);
        Assert.Contains("Alola", alolaNode.ConditionText);
    }

    [Fact]
    public void Gen7_ExeggutorDeAlola_MuestraLaPreviaYLaOtraRama()
    {
        var save = Blank(GameVersion.SN);
        var family = PokedexFor(save).GetAlbumEvolutionFamily(103, 1, save, "es");

        Assert.NotNull(family.Prev);
        Assert.Equal(102, family.Prev!.SpeciesId);                       // Exeggcute
        Assert.Contains(family.Siblings, n => n.SpeciesId == 103);       // Exeggutor de Kanto
        Assert.DoesNotContain(family.Siblings, n => n.SpeciesId == 10114); // la propia no se repite
    }

    [Fact]
    public void Gen4_Exeggcute_SoloLaRamaBase()
    {
        var save = Blank(GameVersion.Pt);
        var family = PokedexFor(save).GetAlbumEvolutionFamily(102, 0, save, "es");
        Assert.Single(family.Next);
        Assert.Equal(103, family.Next[0].SpeciesId);
    }

    [Fact]
    public void Arceus_Quilava_OcultaLaRamaBaseDeTyphlosion()
    {
        // En Legends: Arceus Typhlosion solo existe en su forma de Hisui: la rama base (157) se oculta y
        // queda la regional. Quilava es una especie base sin forma regional, así que prueba justo ese filtro.
        var save = Blank(GameVersion.PLA);
        var family = PokedexFor(save).GetAlbumEvolutionFamily(156, 0, save, "es");
        Assert.DoesNotContain(family.Next, n => n.SpeciesId == 157);
        Assert.Contains(family.Next, n => n.NationalId == 157 && n.Form == 1);
    }

    [Fact]
    public void Arceus_GrowlitheDeHisui_EvolucionaAArcanineDeHisui()
    {
        var save = Blank(GameVersion.PLA);
        var family = PokedexFor(save).GetAlbumEvolutionFamily(58, 1, save, "es");
        Assert.Contains(family.Next, n => n.SpeciesId == 10230);          // arcanine-hisui
    }

    // ── Aristas base sin la condición de la variante regional ─────────────────────────────────────
    [Fact]
    public void Aristas_Base_NoArrastranLaCondicionDeLaVariante()
    {
        var db = PokemonDatabase.Instance;

        // Sandshrew → Sandslash: nivel 22 (base). La Piedra Hielo es de Sandslash de Alola.
        var sandslash = db.GetEvolutionsFrom(27).Single(e => e.ToSpeciesId == 28);
        Assert.Equal("22", sandslash.Conditions["min_level"]);
        Assert.False(sandslash.Conditions.ContainsKey("item"));

        // Vulpix → Ninetales: Piedra Fuego (base), no la Piedra Hielo de Alola.
        var ninetales = db.GetEvolutionsFrom(37).Single(e => e.ToSpeciesId == 38);
        Assert.Equal("fire-stone", ninetales.Conditions["item"]);
    }

    // ── Condiciones en el idioma de la UI ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData("es", "Piedra Agua")]
    [InlineData("en", "Water Stone")]
    public void Condicion_ObjetoSaleEnElIdiomaDeLaUI(string lang, string esperado)
    {
        var c = new Dictionary<string, string> { ["item"] = "water-stone", ["trigger"] = "use-item" };
        Assert.Equal(esperado, EvolutionConditionFormatter.Format(c, lang));
    }

    [Theory]
    [InlineData("es", "Piedra Hoja", "Piedra Hoja · en Alola")]
    [InlineData("en", "Leaf Stone", "Leaf Stone · in Alola")]
    public void Condicion_LaRegionSoloVaEnLaForma(string lang, string textoBase, string textoForma)
    {
        var c = new Dictionary<string, string> { ["item"] = "leaf-stone", ["region"] = "alola", ["trigger"] = "use-item" };
        Assert.Equal(textoBase, EvolutionConditionFormatter.FormatBase(c, lang));
        Assert.Equal(textoForma, EvolutionConditionFormatter.FormatForm(c, lang));
    }

    [Fact]
    public void Condicion_NivelYAmistad_EnAmbosIdiomas()
    {
        Assert.Equal("Nivel 16", EvolutionConditionFormatter.Format(new Dictionary<string, string> { ["min_level"] = "16" }, "es"));
        Assert.Equal("Level 16", EvolutionConditionFormatter.Format(new Dictionary<string, string> { ["min_level"] = "16" }, "en"));
        Assert.Equal("High friendship, at night", EvolutionConditionFormatter.Format(
            new Dictionary<string, string> { ["min_happiness"] = "220", ["time_of_day"] = "night" }, "en"));
    }
}
