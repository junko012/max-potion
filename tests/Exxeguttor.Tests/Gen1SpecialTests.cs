using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Stat Especial único de Gen 1 (columna Species.BaseSpecialGen1, hotfix fix_gen1_special). Las columnas
// BaseSpAttack/BaseSpDefense son las de Gen 2+ y difieren (Charizard 109/85, Gen 1: Especial 85).
public class Gen1SpecialTests
{
    [Theory]
    [InlineData(1, 65)]     // Bulbasaur
    [InlineData(6, 85)]     // Charizard (Ataque Esp. de Gen 2+: 109)
    [InlineData(128, 70)]   // Tauros (Gen 2+: 40 / 70)
    [InlineData(150, 154)]  // Mewtwo
    [InlineData(151, 100)]  // Mew
    public void Especies_de_Kanto_tienen_el_Especial_de_Gen1(int species, int special)
    {
        var entry = PokemonDatabase.Instance.GetSpecies(species);
        Assert.NotNull(entry);
        Assert.Equal(special, entry!.BaseSpecialGen1);
    }

    [Fact]
    public void Charizard_Especial_de_Gen1_difiere_del_Ataque_Especial_de_Gen2()
    {
        var entry = PokemonDatabase.Instance.GetSpecies(6)!;
        Assert.Equal(109, entry.BaseSpa);
        Assert.NotEqual(entry.BaseSpa, entry.BaseSpecialGen1);
    }

    [Fact]
    public void Solo_las_151_de_Kanto_tienen_el_dato()
    {
        for (var id = 1; id <= 151; id++)
            Assert.NotNull(PokemonDatabase.Instance.GetSpecies(id)!.BaseSpecialGen1);
        Assert.Null(PokemonDatabase.Instance.GetSpecies(152)!.BaseSpecialGen1);   // Chikorita
    }
}
