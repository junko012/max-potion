using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fase 3 tanda 4: los nombres en inglés escritos en ZCrystalDatabase (UI; el
// proyecto de tests solo referencia App, por eso la lista se repite acá) se resuelven por id a
// cualquier idioma de datos, y los ids de Mega Stones / Z-Crystals tienen nombre en cada idioma.
public class MoveNamesTests
{
    // Copia de ZCrystalDatabase.TypeCrystals.ZMoveName + SignatureCrystals.RequiredMoveName/ZMoveName.
    // Si se agrega un cristal nuevo allá, agregarlo acá.
    private static readonly string[] EnglishNames =
    {
        "Breakneck Blitz", "Inferno Overdrive", "Hydro Vortex", "Gigavolt Havoc", "Bloom Doom",
        "Subzero Slammer", "All-Out Pummeling", "Acid Downpour", "Tectonic Rage", "Supersonic Skystrike",
        "Shattered Psyche", "Savage Spin-Out", "Continental Crush", "Never-Ending Nightmare",
        "Devastating Drake", "Black Hole Eclipse", "Corkscrew Crash", "Twinkle Tackle",
        "Volt Tackle", "Catastropika", "Spirit Shackle", "Sinister Arrow Raid", "Darkest Lariat",
        "Malicious Moonsault", "Sparkling Aria", "Oceanic Operetta", "Nature's Madness",
        "Guardian of Alola", "Spectral Thief", "Soul-Stealing 7-Star Strike", "Thunderbolt",
        "Stoked Sparksurfer", "Giga Impact", "Pulverizing Pancake", "Last Resort", "Extreme Evoboost",
        "Psychic", "Genesis Supernova", "10,000,000 Volt Thunderbolt", "Sunsteel Strike",
        "Searing Sunraze Smash", "Moongeist Beam", "Menacing Moonraze Maelstrom", "Photon Geyser",
        "Light That Burns the Sky", "Play Rough", "Let's Snuggle Forever", "Stone Edge",
        "Splintered Stormshards", "Clanging Scales", "Clangorous Soulblaze",
    };

    private static readonly int[] MegaStoneIds =
        Enumerable.Range(656, 30).Concat(Enumerable.Range(752, 13)).Concat(Enumerable.Range(767, 4)).ToArray();

    private static readonly int[] ZCrystalHeldIds =
        Enumerable.Range(776, 19).Concat(Enumerable.Range(798, 9)).Append(836).Concat(Enumerable.Range(921, 6)).ToArray();

    [Fact]
    public void TodoNombreInglesDeZCrystalDatabase_ResuelveAUnId()
        => Assert.All(EnglishNames, n => Assert.True(MoveNames.IdFromEnglish(n) > 0, $"sin id para '{n}'"));

    [Fact]
    public void IdFromEnglish_IgnoraMayusculasApostrofoYGuiones()
    {
        Assert.Equal(MoveNames.IdFromEnglish("Nature's Madness"), MoveNames.IdFromEnglish("natures madness"));
        Assert.Equal(MoveNames.IdFromEnglish("All-Out Pummeling"), MoveNames.IdFromEnglish("ALL OUT PUMMELING"));
        Assert.Equal(0, MoveNames.IdFromEnglish("no existe este movimiento"));
        Assert.Equal(0, MoveNames.IdFromEnglish(null));
    }

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("ja")] [InlineData("ko")]
    [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void Localize_DaElNombreDelIdioma_YNuncaVacio(string code)
    {
        var moves = GameInfo.GetStrings(code).Move;
        Assert.All(EnglishNames, n =>
        {
            var loc = MoveNames.Localize(n, moves);
            Assert.False(string.IsNullOrEmpty(loc), $"{code}: '{n}' sin nombre");
            var id = MoveNames.IdFromEnglish(n);
            // PKHeX agrega " (P)"/" (S)" a los Z-Moves genéricos (622-657); MoveNames lo quita al mostrar.
            Assert.Equal(MoveNames.DisplayName(id, moves[id]), loc);
            Assert.DoesNotContain(" (P)", loc);
            Assert.DoesNotContain(" (S)", loc);
        });
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("de")] [InlineData("ja")]
    [InlineData("ko")] [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void MegaStonesYZCrystals_TienenNombreDeObjetoEnCadaIdioma(string code)
    {
        var items = GameInfo.GetStrings(code).Item;
        Assert.All(MegaStoneIds.Concat(ZCrystalHeldIds), id =>
            Assert.False(string.IsNullOrWhiteSpace(items[id]), $"{code}: objeto {id} sin nombre"));
    }
}
