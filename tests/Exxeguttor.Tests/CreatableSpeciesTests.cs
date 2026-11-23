using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Filtro de especies no creables por juego (CreatableSpecies + Assets/uncreatable_species.json).
// Bug original: en un save de Legends: Z-A el selector ofrecía Unown y Arceus, que no existen en
// ese juego, y salían sin movimientos y con varias alertas de ilegalidad.
public class CreatableSpeciesTests
{
    [Fact]
    public void ZA_oculta_Unown_y_Arceus_pero_no_Pikachu()
    {
        var hidden = CreatableSpecies.HiddenIn(GameVersion.ZA);
        Assert.Contains(201, hidden);   // Unown
        Assert.Contains(493, hidden);   // Arceus
        Assert.DoesNotContain(25, hidden);
    }

    [Fact]
    public void ZA_sigue_ofreciendo_especies_que_llegan_por_transferencia()
    {
        // Sandshrew no está en la tabla de Z-A pero es legal (HOME): no debe ocultarse.
        Assert.DoesNotContain(27, CreatableSpecies.HiddenIn(GameVersion.ZA));
    }

    [Theory]
    [InlineData(GameVersion.SL)]
    [InlineData(GameVersion.VL)]
    [InlineData(GameVersion.SW)]
    [InlineData(GameVersion.SH)]
    [InlineData(GameVersion.PLA)]
    [InlineData(GameVersion.GP)]
    public void Juegos_con_pokedex_recortada_ocultan_algo(GameVersion v)
        => Assert.NotEmpty(CreatableSpecies.HiddenIn(v));

    [Fact]
    public void Agregados_de_PKHeX_resuelven_al_primer_juego_del_grupo()
    {
        Assert.Equal(CreatableSpecies.HiddenIn(GameVersion.SW), CreatableSpecies.HiddenIn(GameVersion.SWSH));
        Assert.Equal(CreatableSpecies.HiddenIn(GameVersion.SL), CreatableSpecies.HiddenIn(GameVersion.SV));
    }

    [Theory]
    [InlineData(GameVersion.RD)]
    [InlineData(GameVersion.C)]
    [InlineData(GameVersion.E)]
    [InlineData(GameVersion.SN)]
    [InlineData(GameVersion.US)]
    [InlineData(GameVersion.BD)]
    public void Generaciones_1_a_7_y_BDSP_no_ocultan_nada(GameVersion v)
        => Assert.Empty(CreatableSpecies.HiddenIn(v));

    [Theory]
    [InlineData(GameVersion.ZA, 12)]
    [InlineData(GameVersion.PLA, 11)]
    [InlineData(GameVersion.SWSH, 20)]
    [InlineData(GameVersion.SW, 20)]
    [InlineData(GameVersion.SV, 18)]
    [InlineData(GameVersion.VL, 18)]
    [InlineData(GameVersion.GE, 13)]
    [InlineData(GameVersion.E, 6)]
    public void TryGetGameId_mapea_al_par_de_la_tabla_Games(GameVersion v, int gameId)
        => Assert.Equal(gameId, GameVersionMappings.TryGetGameId(v));

    // Legends: Arceus: Goodra, Sliggoo, Typhlosion... solo existen en su forma de Hisui (Giratina
    // solo Origen, Basculin solo Rayas Blancas). Ofrecer la base y que al crear cambie de forma
    // y de sprite era un bug: la base se oculta y se ofrece la forma que sí existe.
    [Theory]
    [InlineData(706)]   // Goodra
    [InlineData(705)]   // Sliggoo
    [InlineData(157)]   // Typhlosion
    [InlineData(550)]   // Basculin
    [InlineData(487)]   // Giratina
    public void PLA_oculta_la_especie_base_que_solo_existe_en_otra_forma(int species)
        => Assert.Contains(species, CreatableSpecies.HiddenIn(GameVersion.PLA));

    [Fact]
    public void PLA_ofrece_Giratina_Origen_y_Basculin_Rayas_Blancas()
    {
        var forms = AlternateFormCatalog.AvailableIn(GameVersion.PLA);
        Assert.Contains((487, (byte)1), forms);
        Assert.Contains((550, (byte)2), forms);
    }

    [Fact]
    public void PLA_no_oculta_especies_con_forma_base_legal()
    {
        Assert.DoesNotContain(25, CreatableSpecies.HiddenIn(GameVersion.PLA));    // Pikachu
        Assert.DoesNotContain(899, CreatableSpecies.HiddenIn(GameVersion.PLA));   // Wyrdeer
    }
}
