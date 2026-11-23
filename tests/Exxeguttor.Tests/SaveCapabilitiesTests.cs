using Exxeguttor.App.Capabilities;
using FluentAssertions;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// PKHeX.Core 25.11.7: SaveUtil.GetBlankSAV(...) desapareció de SaveUtil por completo.
// El generador de saves en blanco se movió a la clase BlankSaveFile, con una firma nueva
// que ahora pide LanguageID explícito (antes lo inferia). Confirmado por reflection contra
// el ensamblado real (SaveUtil.GetBlankSave NO existe — fue un nombre inventado en una
// sesión anterior, nunca compiló; quede como registro de qué NO es).
// BlankSaveFile.Get(GameVersion version, string trainerName, LanguageID language) -> SaveFile
public class SaveCapabilitiesTests
{
    [Fact]
    public void Gen1_NoDeberiaTenerNatures()
    {
        var save = BlankSaveFile.Get(GameVersion.RD, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasNatures.Should().BeFalse();
        caps.HasAbilities.Should().BeFalse();
        caps.HasShiny.Should().BeFalse();
        caps.HasHeldItems.Should().BeFalse();
        // Bug real: PK1.IsEgg está hardcodeado a "get => false; set { }" en PKHeX.Core —
        // Gen1 no tiene mecánica de cría en absoluto (llegó recién en Gen2, Day Care).
        // Antes de este fix, el checkbox "Egg"/"From egg" no tenía este gate y el cambio
        // se perdía en silencio al exportar (reportado con un Ditto de un save Gen1).
        caps.HasBreeding.Should().BeFalse();
    }

    [Fact]
    public void Gen2_DeberiaTenerBreedingPeroNoEggLocationNiEggDate()
    {
        var save = BlankSaveFile.Get(GameVersion.SI, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        // A diferencia de Gen1: PK2.IsEgg SÍ es un get/set real en PKHeX.Core (Gen2
        // introdujo el Day Care). Pero GBPKM.EggLocation sigue hardcodeado a "get => 0;
        // set { }" para PK1 *y* PK2 — Gen2 puede saber SI es un huevo, pero no DÓNDE o
        // CUÁNDO se originó (eso llega recién con el sistema de Met Location de Gen3+).
        caps.HasBreeding.Should().BeTrue();
        caps.HasEggLocation.Should().BeFalse();
        caps.HasEggDate.Should().BeFalse();
    }

    [Fact]
    public void Gen3_DeberiaTenerNaturesYAbilities()
    {
        var save = BlankSaveFile.Get(GameVersion.E, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasNatures.Should().BeTrue();
        caps.HasAbilities.Should().BeTrue();
        caps.HasShiny.Should().BeTrue();
        caps.HasHeldItems.Should().BeTrue();
        caps.HasTerastal.Should().BeFalse();
        caps.HasDynamax.Should().BeFalse();
    }

    [Fact]
    public void Gen6X_DeberiaTenerMega()
    {
        var save = BlankSaveFile.Get(GameVersion.X, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasMega.Should().BeTrue();
        caps.HasNatures.Should().BeTrue();
        caps.HasTerastal.Should().BeFalse();
        caps.HasDynamax.Should().BeFalse();
    }

    [Fact]
    public void Gen8SW_DeberiaTenerDynamaxYGigamax()
    {
        var save = BlankSaveFile.Get(GameVersion.SW, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasDynamax.Should().BeTrue();
        caps.HasGigamax.Should().BeTrue();
        caps.HasTerastal.Should().BeFalse();
        caps.HasAlpha.Should().BeFalse();
    }

    [Fact]
    public void Gen8PLA_DeberiaTenerAlpha()
    {
        var save = BlankSaveFile.Get(GameVersion.PLA, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasAlpha.Should().BeTrue();
        caps.HasNobleLevel.Should().BeTrue();
        caps.HasDynamax.Should().BeFalse();
        caps.HasTerastal.Should().BeFalse();
    }

    [Fact]
    public void Gen9SL_DeberiaTenerTerastal()
    {
        var save = BlankSaveFile.Get(GameVersion.SL, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasTerastal.Should().BeTrue();
        caps.HasNatures.Should().BeTrue();
        caps.HasAbilities.Should().BeTrue();
        caps.HasDynamax.Should().BeFalse();
        caps.HasAlpha.Should().BeFalse();
    }

    [Fact]
    public void DeberiaExponerGeneracionYGameVersion()
    {
        var save = BlankSaveFile.Get(GameVersion.SL, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.Generation.Should().Be(9);
        caps.GameVersion.Should().Be(GameVersion.SL);
        caps.GameName.Should().NotBeNullOrEmpty();
    }

    // Let's Go, Leyendas: Arceus y Z-A no tienen huevos (PKHeX no trae plantillas de huevo Gen7b/Gen8a/Gen9a). Reportado con un
    // save de Z-A: tildar "From egg" no hacía nada, seguía "legal" y tras exportar y recargar el check volvía a estar sin marcar.
    [Theory]
    [InlineData(GameVersion.GP)]
    [InlineData(GameVersion.GE)]
    [InlineData(GameVersion.PLA)]
    [InlineData(GameVersion.ZA)]
    public void JuegosSinHuevos_NoDeberianTenerBreedingNiUbicacionNiFechaDeHuevo(GameVersion version)
    {
        var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasBreeding.Should().BeFalse();
        caps.HasEggLocation.Should().BeFalse();
        caps.HasEggDate.Should().BeFalse();
    }

    // Los juegos con huevos conservan el check (incluye BDSP y Escarlata/Púrpura, donde PKHeX sí tiene plantilla de huevo).
    [Theory]
    [InlineData(GameVersion.D)]
    [InlineData(GameVersion.Pt)]
    [InlineData(GameVersion.HG)]
    [InlineData(GameVersion.B2)]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.SN)]
    [InlineData(GameVersion.SW)]
    [InlineData(GameVersion.BD)]
    [InlineData(GameVersion.SL)]
    public void JuegosConHuevos_ConservanBreedingUbicacionYFecha(GameVersion version)
    {
        var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
        var caps = new SaveCapabilities(save);
        caps.HasBreeding.Should().BeTrue();
        caps.HasEggLocation.Should().BeTrue();
        caps.HasEggDate.Should().BeTrue();
    }
}
