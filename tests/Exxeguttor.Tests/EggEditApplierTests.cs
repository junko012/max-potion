using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// La vista previa de legalidad y la escritura real aplicaban las ediciones de huevo con bloques distintos: la previa no
// aplicaba la fecha de huevo ni MetDate, así que el panel decía "Legal" sobre algo distinto de lo exportado, y al recargar
// el save aparecía Memory:DateCalendarInvalidEgg (fecha escrita, ubicación no). Medido con PKHeX 25.11.07 sobre capturas
// salvajes reales: un huevo eclosionado de Gen4 exige además el terreno de captura (GroundTile) en "ninguno".
public class EggEditApplierTests
{
    /// <summary>Captura salvaje legal (encuentro de "slot") de una especie que puede salir de un huevo.</summary>
    private static (SaveFile Save, PKM Wild, EggOriginService.EggTemplate T) Wild(GameVersion v)
    {
        var save = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish);
        for (ushort sp = 1; sp <= Math.Min((int)save.MaxSpeciesID, 400); sp++)
        {
            var probe = save.BlankPKM;
            probe.Species = sp; probe.CurrentLevel = 20; probe.Stat_Level = 20; probe.Version = v;
            foreach (var e in EncounterMovesetGenerator.GenerateEncounters(probe.Clone(), ReadOnlyMemory<ushort>.Empty, v))
            {
                if (e is IEncounterEgg || e is not IEncounterConvertible c || e.IsEgg) continue;
                var pk = c.ConvertToPKM(save, EncounterCriteria.Unrestricted);
                var la = new LegalityAnalysis(pk);
                if (!la.Valid || !la.EncounterMatch.GetType().Name.Contains("Slot")) continue;
                if (!EggOriginService.TryGetEggTemplate(pk, save, out var t)) continue;
                pk.MetDate ??= new DateOnly(2023, 7, 13);
                pk.RefreshChecksum();
                return (save, pk, t);
            }
        }
        throw new InvalidOperationException($"sin captura salvaje legal que pueda salir de un huevo para {v}");
    }

    private static List<string> EncounterOrMemoryErrors(PKM p) =>
        new LegalityAnalysis(p).Results
            .Where(r => !r.Valid && r.Identifier is CheckIdentifier.Encounter or CheckIdentifier.Memory)
            .Select(r => $"{r.Identifier}:{r.Result}").ToList();

    public static IEnumerable<object[]> Gen4Versions() =>
        new[] { GameVersion.D, GameVersion.Pt, GameVersion.HG }.Select(v => new object[] { v });

    public static IEnumerable<object[]> Gen4PlusVersions() =>
        new[] { GameVersion.D, GameVersion.Pt, GameVersion.HG, GameVersion.B2, GameVersion.X, GameVersion.OR,
                GameVersion.SN, GameVersion.SW, GameVersion.BD, GameVersion.SL }.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(Gen4PlusVersions))]
    public void TildarFromEgg_ConTodasLasEdiciones_DejaUnHuevoSinErroresDeEncuentroNiDeFecha(GameVersion v)
    {
        var (save, wild, t) = Wild(v);
        var edits = new Dictionary<string, object?>
        {
            ["HatchedFromEgg"] = true,
            ["SelectedEggLocation"] = new ComboItem("lugar", t.EggLocation),
            ["EggDate"] = (wild.MetDate ?? new DateOnly(2023, 7, 13)).ToString("yyyy-MM-dd"),
        };
        wild.MetLevel = t.MetLevel; wild.Ball = t.Ball;   // lo que el editor ya escribió por su cuenta (MetLevel y bola)
        if (t.NeedsMetLocation) wild.MetLocation = t.MetLocation;

        EggEditApplier.Apply(wild, edits);

        Assert.Empty(EncounterOrMemoryErrors(wild));
        if (wild is IGroundTile g) Assert.Equal(default, g.GroundTile);
    }

    [Theory]
    [MemberData(nameof(Gen4PlusVersions))]
    public void SoloLaFecha_NoDejaUnaFechaDeHuevoHuerfana(GameVersion v)
    {
        var (_, wild, _) = Wild(v);
        var edits = new Dictionary<string, object?> { ["EggDate"] = "2023-07-13" };   // lo observado tras exportar: sin ubicación

        EggEditApplier.Apply(wild, edits);

        Assert.Null(wild.EggMetDate);
        Assert.DoesNotContain(new LegalityAnalysis(wild).Results, r => !r.Valid && r.Result.ToString() == "DateCalendarInvalidEgg");
    }

    [Theory]
    [MemberData(nameof(Gen4PlusVersions))]
    public void TildadoSinUbicacion_CompletaConLaPlantillaEnVezDePerderLaIntencion(GameVersion v)
    {
        var (_, wild, t) = Wild(v);
        var edits = new Dictionary<string, object?> { ["HatchedFromEgg"] = true };   // ninguna otra edición llegó

        EggEditApplier.Apply(wild, edits);

        Assert.NotEqual((ushort)0, wild.EggLocation == Locations.Default8bNone && wild is PB8 ? (ushort)0 : wild.EggLocation);
        Assert.Equal(t.MetLevel, wild.MetLevel);
        Assert.NotNull(wild.EggMetDate);
        Assert.Empty(EncounterOrMemoryErrors(wild));
    }

    [Theory]
    [MemberData(nameof(Gen4Versions))]
    public void DestildadoExplicito_LimpiaUbicacionYFecha(GameVersion v)
    {
        var (_, wild, t) = Wild(v);
        wild.EggLocation = t.EggLocation; wild.EggMetDate = wild.MetDate; wild.RefreshChecksum();

        EggEditApplier.Apply(wild, new Dictionary<string, object?> { ["HatchedFromEgg"] = false });

        Assert.Equal((ushort)0, wild.EggLocation);
        Assert.Null(wild.EggMetDate);
    }

    [Theory]
    [MemberData(nameof(Gen4Versions))]
    public void SinEdicionesDeHuevo_NoTocaElPokemon(GameVersion v)
    {
        var (_, wild, _) = Wild(v);
        var before = wild.Data.ToArray();

        EggEditApplier.Apply(wild, new Dictionary<string, object?> { ["Level"] = 30 });

        Assert.True(before.AsSpan().SequenceEqual(wild.Data));
    }

    [Fact]
    public void Gen3_TildarFromEgg_NoCambiaNada_ElAtajoDeGen3EsOtro()
    {
        var pk = (PK3)BlankSaveFile.Get(GameVersion.E, "SANIA", LanguageID.Spanish).BlankPKM;
        pk.Species = (ushort)Species.Lotad; pk.CurrentLevel = 20; pk.Stat_Level = 20; pk.RefreshChecksum();
        var before = pk.Data.ToArray();

        EggEditApplier.Apply(pk, new Dictionary<string, object?> { ["HatchedFromEgg"] = true });

        Assert.True(before.AsSpan().SequenceEqual(pk.Data));
    }
}
