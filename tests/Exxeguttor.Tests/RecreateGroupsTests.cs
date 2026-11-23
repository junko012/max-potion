using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Las ramas de reemplazo por grupos de "Recrear" Gen5–9 (identidad, bola, manejador/recuerdos, cintas) casi nunca se
// disparan con Pokémon reales (PKHeX acepta esos datos con casi todos los encuentros). Por eso se prueban aparte:
// (1) qué grupo explica cada tipo de error y (2) que copiar un grupo copia solo ese grupo, sobre tipos PK6–PK9 reales.
public class RecreateGroupsTests
{
    /// <summary>El n-ésimo Pokémon capturado en la naturaleza (encuentro de "slot"), legal, de ese juego. Se evitan los
    /// regalos y eventos: al cambiarles un dato el encuentro entero deja de coincidir y no sirven para probar grupos.</summary>
    private static PKM Wild(GameVersion v, int nth = 0)
    {
        var save = BlankSaveFile.Get(v, "SANIA", LanguageID.Spanish);
        var seen = 0;
        for (ushort species = 1; species <= Math.Min((int)save.MaxSpeciesID, 400); species++)
        {
            var probe = save.BlankPKM;
            probe.Species = species; probe.CurrentLevel = 20; probe.Stat_Level = 20; probe.Version = v;
            foreach (var e in EncounterMovesetGenerator.GenerateEncounters(probe.Clone(), ReadOnlyMemory<ushort>.Empty, v))
            {
                if (e is IEncounterEgg || e is not IEncounterConvertible c || e.IsEgg) continue;
                var pk = c.ConvertToPKM(save, EncounterCriteria.Unrestricted);
                var la = new LegalityAnalysis(pk);
                if (!la.Valid || !la.EncounterMatch.GetType().Name.Contains("Slot")) continue;
                if (seen++ == nth) return pk;
                break;
            }
        }
        throw new InvalidOperationException($"sin encuentro salvaje legal #{nth} para {v}");
    }

    public static IEnumerable<object[]> Versions() =>
        new[] { GameVersion.X, GameVersion.SN, GameVersion.SW, GameVersion.BD, GameVersion.SL }.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(Versions))]
    public void CopyGroups_Ribbons_CopiaSoloLasCintas(GameVersion v)
    {
        var from = Wild(v, 0); RibbonApplicator.SetAllValidRibbons(from); from.RefreshChecksum();
        var to = Wild(v, 3);   // otro Pokémon
        var nature = to.Nature; var ball = to.Ball; var pid = to.PID;

        EncounterRecreationService.CopyGroups(from, to, RecreateGroups.Ribbons);

        Assert.Equal(RibbonInfo.GetRibbonInfo(from).Count(r => r.HasRibbon || r.RibbonCount > 0),
                     RibbonInfo.GetRibbonInfo(to).Count(r => r.HasRibbon || r.RibbonCount > 0));
        Assert.Equal(nature, to.Nature); Assert.Equal(ball, to.Ball); Assert.Equal(pid, to.PID);   // nada más cambió
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void CopyGroups_Identity_CopiaPidEcNaturalezaGeneroYHabilidad(GameVersion v)
    {
        var from = Wild(v, 0); var to = Wild(v, 3);
        var ball = to.Ball; var level = to.CurrentLevel;

        EncounterRecreationService.CopyGroups(from, to, RecreateGroups.Identity);

        Assert.Equal(from.PID, to.PID);
        Assert.Equal(from.EncryptionConstant, to.EncryptionConstant);
        Assert.Equal(from.Nature, to.Nature);
        Assert.Equal(from.Gender, to.Gender);
        Assert.Equal(from.Ability, to.Ability);
        Assert.Equal(ball, to.Ball); Assert.Equal(level, to.CurrentLevel);   // nada más cambió
    }

    [Theory]
    [InlineData(GameVersion.X)]
    [InlineData(GameVersion.SN)]
    [InlineData(GameVersion.SW)]
    public void CopyGroups_Handler_CopiaManejadorYNoTocaLoDemas(GameVersion v)
    {
        var from = Wild(v, 0);
        from.CurrentHandler = 1; from.HandlingTrainerName = "OTRO"; from.HandlingTrainerFriendship = 50; from.HandlingTrainerGender = 1;
        var to = Wild(v, 3);
        var pid = to.PID; var ball = to.Ball;

        EncounterRecreationService.CopyGroups(from, to, RecreateGroups.Handler);

        Assert.Equal(1, to.CurrentHandler);
        Assert.Equal("OTRO", to.HandlingTrainerName);
        Assert.Equal(pid, to.PID); Assert.Equal(ball, to.Ball);
    }

    [Fact]
    public void CopyGroups_Ball_CopiaSoloLaBola()
    {
        var from = Wild(GameVersion.SW, 0); from.Ball = (byte)Ball.Great;
        var to = Wild(GameVersion.SW, 3);
        var pid = to.PID;
        EncounterRecreationService.CopyGroups(from, to, RecreateGroups.Ball);
        Assert.Equal((byte)Ball.Great, to.Ball);
        Assert.Equal(pid, to.PID);
    }

    [Fact]
    public void CopyGroups_SinGrupos_NoCambiaNada()
    {
        var from = Wild(GameVersion.SW, 0); var to = Wild(GameVersion.SW, 3);
        var before = to.Data.ToArray();
        EncounterRecreationService.CopyGroups(from, to, RecreateGroups.None);
        Assert.True(before.AsSpan().SequenceEqual(to.Data));
    }

    [Fact]
    public void GroupsFor_ElErrorDeCadaTipoPideSuGrupo()
    {
        var pk = Wild(GameVersion.SW, 0);                 // captura salvaje legal
        pk.Ball = (byte)Ball.Cherish;                      // bola imposible → Ball
        pk.Ability = 150;                                  // habilidad que la especie no tiene (Impostor) → Ability (identidad)
        if (pk is IRibbonSetCommon3 r3) r3.RibbonChampionG3 = true;   // cinta imposible → Ribbon
        pk.RefreshChecksum();
        var la = new LegalityAnalysis(pk);

        var groups = EncounterRecreationService.GroupsFor(la, new HashSet<CheckIdentifier>());
        Assert.True(groups.HasFlag(RecreateGroups.Ball), $"esperaba Ball en {groups}");
        Assert.True(groups.HasFlag(RecreateGroups.Identity), $"esperaba Identity en {groups}");
        Assert.True(groups.HasFlag(RecreateGroups.Ribbons), $"esperaba Ribbons en {groups}");

        // Los errores que el original YA tenía no cuentan como causados por el origen nuevo
        var baseline = la.Results.Where(r => !r.Valid).Select(r => r.Identifier).ToHashSet();
        Assert.Equal(RecreateGroups.None, EncounterRecreationService.GroupsFor(la, baseline));
    }

    // (En Gen6 PKHeX no marca como inválido un recuerdo de manejador raro, por eso solo Gen7+.)
    [Theory]
    [InlineData(GameVersion.SN)]
    [InlineData(GameVersion.SW)]
    public void GroupsFor_ErrorDeRecuerdosOManejador_PideManejador(GameVersion v)
    {
        var pk = Wild(v, 0);
        // Manejador con un recuerdo imposible (sin entrenador original que lo explique)
        pk.CurrentHandler = 1; pk.HandlingTrainerName = "OTRO"; pk.HandlingTrainerFriendship = 50; pk.HandlingTrainerGender = 1;
        if (pk is IMemoryHT mh) { mh.HandlingTrainerMemory = 99; mh.HandlingTrainerMemoryIntensity = 1; mh.HandlingTrainerMemoryFeeling = 0; mh.HandlingTrainerMemoryVariable = 0; }
        pk.RefreshChecksum();
        var la = new LegalityAnalysis(pk);

        var groups = EncounterRecreationService.GroupsFor(la, new HashSet<CheckIdentifier>());
        Assert.True(groups.HasFlag(RecreateGroups.Handler), $"esperaba Handler en {groups}");
    }

    // Let's Go: PKHeX exige AVs >= mínimo esperado para (nivel, nivel de captura). Al recrear el origen el mínimo sube.
    [Fact]
    public void RaiseAwakenedToMinimum_SubeLosAVsAlMinimo_YNuncaBajaUnoQueYaEstabaPorEncima()
    {
        var pb7 = (PB7)Wild(GameVersion.GE, 0);
        pb7.CurrentLevel = (byte)(pb7.CurrentLevel + 12); pb7.Stat_Level = pb7.CurrentLevel;   // más niveles desde la captura
        pb7.AwakeningClear();
        Assert.False(new LegalityAnalysis(pb7).Valid, "con AVs en 0 y 12 niveles más debe faltar el mínimo");

        EncounterRecreationService.RaiseAwakenedToMinimum(pb7);
        Assert.DoesNotContain(new LegalityAnalysis(pb7).Results, r => !r.Valid && r.Identifier == CheckIdentifier.AVs);

        // Un AV ya por encima del mínimo no se toca
        Span<byte> before = stackalloc byte[6];
        AwakeningUtil.AwakeningGetVisual(pb7, before);
        before[0] = (byte)Math.Min(before[0] + 20, 200);
        AwakeningUtil.AwakeningSetVisual(pb7, before);
        EncounterRecreationService.RaiseAwakenedToMinimum(pb7);
        Span<byte> after = stackalloc byte[6];
        AwakeningUtil.AwakeningGetVisual(pb7, after);
        Assert.Equal(before[0], after[0]);
    }
}
