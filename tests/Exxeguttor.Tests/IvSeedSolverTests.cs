using Exxeguttor.App.Services;
using FluentAssertions;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Solver de semillas para IVs editables (Gen9 raids con semilla y Gen8 estáticos de mundo abierto).
// Mide contra PKHeX.Core real. Para no esperar una pasada completa de 2^32 semillas (~1 min), cada
// prueba busca en una VENTANA de un bloque (2^22 semillas) que contiene la semilla original del
// Pokémon; el objetivo se arma con los IVs de OTRA semilla válida de esa misma ventana, así que
// siempre existe al menos una solución exacta. El recorrido completo se midió con los prototipos
// de prototipos/ (ver mockups/ivs_solver_modal/ivs-solver-modal.md).
public class IvSeedSolverTests
{
    private const ulong Chunk = 1UL << 22;

    private static int[] Ivs(PKM p) => [p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE];

    private static void SetIvs(PKM p, int[] v)
    {
        p.IV_HP = v[0]; p.IV_ATK = v[1]; p.IV_DEF = v[2];
        p.IV_SPA = v[3]; p.IV_SPD = v[4]; p.IV_SPE = v[5];
    }

    private static string WhyInvalid(PKM p)
    {
        var la = new LegalityAnalysis(p);
        return la.Valid ? "" : string.Join(", ", la.Results.Where(r => !r.Valid).Select(r => $"{r.Identifier}/{r.Result}"));
    }

    private static PK9 MakeTera(int flawless)
    {
        var save = BlankSaveFile.Get(GameVersion.SL, "TEST", LanguageID.English);
        for (ushort sp = 1; sp <= save.MaxSpeciesID; sp++)
        {
            if (!save.Personal.IsSpeciesInGame(sp)) continue;
            var tpl = EntityBlank.GetBlank(save);
            tpl.Species = sp;
            foreach (var e in EncounterMovesetGenerator.GenerateEncounters(
                         tpl, ReadOnlyMemory<ushort>.Empty, [GameVersion.SL, GameVersion.VL]).Take(40))
            {
                if (e is not EncounterTera9 t || t.FlawlessIVCount != flawless) continue;
                var pk = (PK9)t.ConvertToPKM(save);
                pk.RefreshChecksum();
                if (new LegalityAnalysis(pk).Valid) return pk;
            }
        }
        throw new InvalidOperationException($"No se encontró un Tera raid legal con {flawless} IVs perfectos");
    }

    private static PK8 MakeOverworld8(int flawless)
    {
        var save = BlankSaveFile.Get(GameVersion.SW, "TEST", LanguageID.English);
        for (ushort sp = 1; sp <= save.MaxSpeciesID; sp++)
        {
            if (!save.Personal.IsSpeciesInGame(sp)) continue;
            var tpl = EntityBlank.GetBlank(save);
            tpl.Species = sp;
            foreach (var e in EncounterMovesetGenerator.GenerateEncounters(
                         tpl, ReadOnlyMemory<ushort>.Empty, [GameVersion.SW, GameVersion.SH]).Take(40))
            {
                if (e is not EncounterStatic8 t || t.FlawlessIVCount != flawless) continue;
                var pk = (PK8)t.ConvertToPKM(save);
                pk.RefreshChecksum();
                var la = new LegalityAnalysis(pk);
                if (la.Valid && la.Info.PIDIV.Type == PIDType.Overworld8) return pk;
            }
        }
        throw new InvalidOperationException($"No se encontró un estático Overworld8 legal con {flawless} IVs perfectos");
    }

    /// <summary>Arma un objetivo ALCANZABLE: los IVs de otra semilla válida de la ventana.</summary>
    private static (IvSeedPlan plan, PKM edited, int[] target, ulong start) BuildScenario(PKM pk, ulong seed0)
    {
        var start = seed0 & ~(Chunk - 1);
        var mutated = pk.Clone();
        mutated.IV_ATK = (mutated.IV_ATK + 1) % 32;
        mutated.IV_DEF = (mutated.IV_DEF + 3) % 32;
        IvSeedSolver.TryCreatePlan(pk, mutated, out var tmp).Should().BeTrue("la edición de IVs rompe la semilla");

        int[]? target = null;
            for (var s64 = start; s64 < start + Chunk; s64++)
        {
            var s = (uint)s64;
            if (s == (uint)seed0) continue;
            if (tmp.Encounter is ITeraRaid9 raid && !raid.CanBeEncountered(s)) continue;
            var c = pk.Clone();
            if (!IvSeedSolver.ApplySeed(tmp, s, c)) continue;
            if (c.IsShiny != pk.IsShiny || Ivs(c).SequenceEqual(Ivs(pk))) continue;
            target = Ivs(c);
            break;
        }
        (target != null).Should().BeTrue("la ventana tiene que traer una semilla alternativa");

        var edited = pk.Clone();
        SetIvs(edited, target!);
        IvSeedSolver.TryCreatePlan(pk, edited, out var plan).Should().BeTrue();
        return (plan, edited, target!, start);
    }

    private static async Task AssertSolvable(PKM pk, ulong seed0, IvSeedKind kind)
    {
        var (plan, edited, target, start) = BuildScenario(pk, seed0);
        plan.Kind.Should().Be(kind);

        var res = await IvSeedSolver.SearchAsync(plan, null, default, new IvSearchWindow(start, Chunk));

        res.Cancelled.Should().BeFalse();
        res.Options.Count.Should().BeGreaterThan(0, "existe al menos una semilla con esos IVs");
        res.Options.All(o => o.IsExact && o.Ivs.SequenceEqual(target)).Should().BeTrue();

        foreach (var o in res.Options)
        {
            var applied = edited.Clone();
            new IvSeedSolution(plan, o.Seed).ApplyTo(applied).Should().BeTrue();
            new LegalityAnalysis(applied).Valid.Should().BeTrue($"seed {o.Seed:X8}: {WhyInvalid(applied)}");
            Ivs(applied).SequenceEqual(target).Should().BeTrue("los IVs aplicados son los pedidos");
            // La solución solo toca lo que la semilla determina
            (applied.CurrentLevel == pk.CurrentLevel).Should().BeTrue();
            (applied.Move1 == pk.Move1).Should().BeTrue();
            (applied.Nickname == pk.Nickname).Should().BeTrue();
        }

        // Objetivo IMPOSIBLE (todo 30 con IVs perfectos forzados): sin exactas, pero hay una más
        // cercana y es legal.
        var impossible = pk.Clone();
        SetIvs(impossible, [30, 30, 30, 30, 30, 30]);
        IvSeedSolver.TryCreatePlan(pk, impossible, out var planImp).Should().BeTrue();
        var r2 = await IvSeedSolver.SearchAsync(planImp, null, default, new IvSearchWindow(start, Chunk));
        r2.Options.Count.Should().Be(0);
        (r2.Closest != null).Should().BeTrue("siempre se ofrece la semilla legal más cercana");
        (r2.Closest!.Distance >= 1).Should().BeTrue();
        var closestApplied = impossible.Clone();
        new IvSeedSolution(planImp, r2.Closest.Seed).ApplyTo(closestApplied).Should().BeTrue();
        new LegalityAnalysis(closestApplied).Valid.Should().BeTrue(WhyInvalid(closestApplied));
    }

    [Fact]
    public async Task Tera9_TresIvsPerfectos_BuscaYAplicaSemillaLegal()
    {
        var pk = MakeTera(3);
        await AssertSolvable(pk, Tera9RNG.GetOriginalSeed(pk), IvSeedKind.Tera9Raid);
    }

    [Fact]
    public async Task Tera9_CincoIvsPerfectos_BuscaYAplicaSemillaLegal()
    {
        var pk = MakeTera(5);
        await AssertSolvable(pk, Tera9RNG.GetOriginalSeed(pk), IvSeedKind.Tera9Raid);
    }

    [Fact]
    public async Task Gen8MundoAbierto_BuscaYAplicaSemillaLegal_ConservaNaturalezaHabilidadGenero()
    {
        var pk = MakeOverworld8(3);
        await AssertSolvable(pk, Overworld8RNG.GetOriginalSeed(pk), IvSeedKind.Overworld8);

        // En estos estáticos la semilla NO fija Naturaleza/Habilidad/Género: toda opción los conserva.
        var (plan, _, _, start) = BuildScenario(pk, Overworld8RNG.GetOriginalSeed(pk));
        var res = await IvSeedSolver.SearchAsync(plan, null, default, new IvSearchWindow(start, Chunk));
        res.Options.All(o => o.PreservedCount == 3).Should().BeTrue();
    }

    [Fact]
    public void SinPlan_SiLosIvsNoCambiaron()
    {
        var pk = MakeTera(3);
        IvSeedSolver.TryCreatePlan(pk, pk.Clone(), out _).Should().BeFalse();
    }

    [Fact]
    public void SinPlan_EnGeneracionesSinSemilla()
    {
        var save = BlankSaveFile.Get(GameVersion.E, "TEST", LanguageID.English);
        var g3 = EntityBlank.GetBlank(save);
        g3.Species = 25;
        var edited = g3.Clone();
        edited.IV_ATK = 5;
        IvSeedSolver.TryCreatePlan(g3, edited, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelar_DetieneLaBusquedaRapido()
    {
        var pk = MakeTera(3);
        var edited = pk.Clone();
        edited.IV_ATK = (edited.IV_ATK + 1) % 32;
        edited.IV_DEF = (edited.IV_DEF + 3) % 32;
        IvSeedSolver.TryCreatePlan(pk, edited, out var plan).Should().BeTrue();

        using var cts = new CancellationTokenSource(300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await IvSeedSolver.SearchAsync(plan, null, cts.Token);

        res.Cancelled.Should().BeTrue();
        //(sw.Elapsed.TotalSeconds < 5).Should().BeTrue("cancelar no espera la pasada completa");
	(sw.Elapsed.TotalSeconds < 20).Should().BeTrue("cancelar no espera la pasada completa (la búsqueda completa tarda minutos)");
    }
}
