using System.Collections.Concurrent;
using System.Diagnostics;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

// ═════════════════════════════════════════════════════════════════════════════════════════
// Paso 2 de "IVs editables en encuentros ligados a una semilla" (ver mockups/ivs_solver_modal/
// y los prototipos en prototipos/). Cubre las dos familias de semilla de 32 bits donde la
// búsqueda es exhaustiva y barata:
//   · Gen9 raids con semilla (Tera raids, Might, Distribución) — ITeraRaid9.
//   · Gen8 estáticos de mundo abierto (PIDType Overworld8) — EncounterStatic8.
// Los raids de 64 bits de Gen8, Legends Arceus y los itinerantes de BDSP quedan para una
// etapa posterior (mismo servicio, otro IvSeedKind): ya están prototipados y medidos.
//
// Idea: en estos encuentros EC, PID, IVs (y Naturaleza/Habilidad/Género/Tamaño según el caso)
// salen de UNA semilla. Editar un IV rompe esa relación. Acá se recorre el espacio de semillas
// buscando las que producen EXACTAMENTE los IVs pedidos, se ordenan por cuántos atributos del
// Pokémon conservan, y se verifican contra LegalityAnalysis antes de ofrecerlas.
// ═════════════════════════════════════════════════════════════════════════════════════════

/// <summary>Familia de encuentro soportada por la búsqueda de semillas.</summary>
public enum IvSeedKind
{
    /// <summary>Gen9: Tera raids, Might y Distribución (semilla de 32 bits + <c>CanBeEncountered</c>).</summary>
    Tera9Raid,
    /// <summary>Gen8: estáticos de mundo abierto con PIDType Overworld8 (semilla de 32 bits).</summary>
    Overworld8,
}

/// <summary>Lo que hace falta para buscar: el encuentro del Pokémon original, el Pokémon con
/// sus ediciones pendientes y los IVs que el usuario quiere.</summary>
public sealed class IvSeedPlan
{
    internal IvSeedPlan(IvSeedKind kind, IEncounterTemplate encounter, PKM edited, int baselineInvalid)
    {
        Kind = kind;
        Encounter = encounter;
        Edited = edited;
        BaselineInvalid = baselineInvalid;
        TargetIvs = [edited.IV_HP, edited.IV_ATK, edited.IV_DEF, edited.IV_SPA, edited.IV_SPD, edited.IV_SPE];
    }

    public IvSeedKind Kind { get; }
    public IEncounterTemplate Encounter { get; }

    /// <summary>Copia del Pokémon con las ediciones pendientes (IVs nuevos incluidos).</summary>
    public PKM Edited { get; }

    /// <summary>IVs pedidos, en el orden interno de PKHeX: PS, Atq, Def, AtE, DeE, Vel.</summary>
    public int[] TargetIvs { get; }

    /// <summary>Cantidad de chequeos inválidos que el Pokémon tiene por motivos AJENOS a los
    /// IVs (se mide con los IVs originales restaurados). Un candidato es aceptable si no
    /// supera este número: así un Pokémon con otro problema a la vez no descarta todo.</summary>
    internal int BaselineInvalid { get; }

    public int FlawlessIVCount => Encounter is IFlawlessIVCount f ? f.FlawlessIVCount : 0;
}

/// <summary>Una semilla candidata ya evaluada (y verificada con LegalityAnalysis).</summary>
public sealed record IvSeedOption(
    ulong Seed,
    int[] Ivs,
    int Distance,
    Nature Nature,
    int Ability,
    byte Gender,
    bool NatureChanged,
    bool AbilityChanged,
    bool GenderChanged,
    bool SizeChanged)
{
    /// <summary>Cuántos de Naturaleza/Habilidad/Género se conservan (0..3). Ordena las opciones.</summary>
    public int PreservedCount => (NatureChanged ? 0 : 1) + (AbilityChanged ? 0 : 1) + (GenderChanged ? 0 : 1);

    public bool IsExact => Distance == 0;
}

/// <summary>Resultado a guardar en la sesión de edición: el plan + la semilla elegida. Se
/// reaplica igual en el preview de legalidad y al exportar (ver EditApplyService).</summary>
public sealed record IvSeedSolution(IvSeedPlan Plan, ulong Seed)
{
    public bool ApplyTo(PKM pkm) => IvSeedSolver.ApplySeed(Plan, Seed, pkm);
}

public readonly record struct IvSearchProgress(
    double Fraction, ulong Checked, ulong Total, int ExactFound, TimeSpan Elapsed);

/// <summary>Ventana de semillas a recorrer. Por defecto es todo el espacio de 32 bits; existe
/// para poder probar el servicio sin esperar una pasada completa.</summary>
public readonly record struct IvSearchWindow(ulong Start, ulong Count);

public sealed class IvSearchResult
{
    /// <summary>Opciones exactas (los IVs pedidos, tal cual), ordenadas: primero las que
    /// conservan más atributos. Vacía si no existe ninguna semilla exacta legal.</summary>
    public IReadOnlyList<IvSeedOption> Options { get; init; } = [];

    /// <summary>La semilla legal más cercana a los IVs pedidos. Solo se calcula cuando no hay
    /// opciones exactas.</summary>
    public IvSeedOption? Closest { get; init; }

    /// <summary>True si se recorrió TODO el espacio: entonces "no existe una semilla exacta"
    /// es una afirmación segura. False si se cortó antes (cancelación o tope de coincidencias).</summary>
    public bool Exhaustive { get; init; }

    public bool Cancelled { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public static class IvSeedSolver
{
    private const int ChunkBits = 22;                 // 4 194 304 semillas por bloque (1024 bloques en total)
    private const int ExactCap = 400;                 // tope de coincidencias exactas a reunir antes de cortar
    private const int MaxOptions = 8;
    private const int MaxVerifications = 40;

    // ── Plan ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Decide si este Pokémon editado se puede resolver por búsqueda de semilla.
    /// <paramref name="pristine"/> es el Pokémon legal original (de ahí sale el encuentro);
    /// <paramref name="edited"/> trae los IVs nuevos. Solo devuelve un plan cuando los IVs
    /// editados son la causa de la ilegalidad: restaurar los IVs originales tiene que mejorar
    /// el análisis.</summary>
    public static bool TryCreatePlan(PKM pristine, PKM edited, out IvSeedPlan plan)
    {
        plan = null!;
        if (pristine.GetType() != edited.GetType()) return false;
        if (pristine is not (PK8 or PK9)) return false;
        if (SameIvs(pristine, edited)) return false;

        var original = new LegalityAnalysis(pristine);
        if (!original.Valid) return false;

        IvSeedKind kind;
        var enc = original.EncounterMatch;
        if (enc is IFixedIVSet { IVs.IsSpecified: true }) return false;   // IVs fijos: no hay nada que buscar

        if (enc is ITeraRaid9 && pristine is PK9)
            kind = IvSeedKind.Tera9Raid;
        else if (enc is EncounterStatic8 s8 && pristine is PK8
                 && original.Info.PIDIV.Type == PIDType.Overworld8
                 && s8.Shiny is Shiny.Never or Shiny.Random)
            kind = IvSeedKind.Overworld8;
        else
            return false;

        var current = new LegalityAnalysis(edited);
        if (current.Valid) return false;

        var restored = edited.Clone();
        CopyIvs(pristine, restored);
        var restoredInvalid = CountInvalid(new LegalityAnalysis(restored));
        if (restoredInvalid >= CountInvalid(current)) return false;     // los IVs no son la causa

        plan = new IvSeedPlan(kind, enc, edited.Clone(), restoredInvalid);
        return true;
    }

    // ── Aplicar una semilla ─────────────────────────────────────────────────────────────

    /// <summary>Escribe en <paramref name="pkm"/> todo lo que la semilla determina (EC, PID, IVs
    /// y, según la familia, Naturaleza/Habilidad/Género/Tamaño/Tera tipo). El resto del
    /// Pokémon (nivel, movimientos, apodo, objeto…) no se toca.</summary>
    public static bool ApplySeed(IvSeedPlan plan, ulong seed, PKM pkm)
    {
        var s = (uint)seed;
        switch (plan.Kind)
        {
            case IvSeedKind.Tera9Raid when pkm is PK9 pk9 && plan.Encounter is ITeraRaid9 raid:
                raid.GenerateSeed32(pk9, s);
                // GenerateSeed32 NO asigna el Tera tipo original (medido: sin esto todos los
                // candidatos salen ilegales).
                if (plan.Encounter is IGemType gem)
                    pk9.TeraTypeOriginal = (MoveType)Tera9RNG.GetTeraType(s, gem.TeraType, raid.Species, raid.Form);
                pk9.RefreshChecksum();
                return true;

            case IvSeedKind.Overworld8 when pkm is PK8 pk8 && plan.Encounter is EncounterStatic8 st:
                Span<int> iv = stackalloc int[6];
                if (!GenOverworld8(s, st.FlawlessIVCount, iv, out var ec, out var pid, out var h, out var w))
                    return false;
                pk8.EncryptionConstant = ec;
                pk8.PID = pid;
                pk8.IV_HP = iv[0]; pk8.IV_ATK = iv[1]; pk8.IV_DEF = iv[2];
                pk8.IV_SPA = iv[3]; pk8.IV_SPD = iv[4]; pk8.IV_SPE = iv[5];
                pk8.HeightScalar = (byte)h;
                pk8.WeightScalar = (byte)w;
                pk8.RefreshChecksum();
                return true;
        }
        return false;
    }

    // ── Búsqueda ────────────────────────────────────────────────────────────────────────

    public static async Task<IvSearchResult> SearchAsync(
        IvSeedPlan plan,
        IProgress<IvSearchProgress>? progress = null,
        CancellationToken ct = default,
        IvSearchWindow? window = null)
    {
        var sw = Stopwatch.StartNew();
        var total = window?.Count ?? (1UL << 32);
        var start = window?.Start ?? 0UL;
        var chunkSize = 1UL << ChunkBits;
        var chunks = (int)((total + chunkSize - 1) / chunkSize);

        var exact = new ConcurrentBag<uint>();
        var exactCount = 0;
        ulong done = 0;
        var bestLock = new object();
        var bestDist = int.MaxValue;
        uint bestSeed = 0;
        var stoppedEarly = false;

        var cx = new ScanContext(plan);

        try
        {
            await Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                };
                Parallel.For(0, chunks, options, (chunk, loop) =>
                {
                    var from = start + (ulong)chunk * chunkSize;
                    var count = (int)Math.Min(chunkSize, start + total - from);
                    var local = ScanChunk(cx, from, count, ct);

                    foreach (var s in local.Exact) exact.Add(s);
                    if (Interlocked.Add(ref exactCount, local.Exact.Count) >= ExactCap)
                    {
                        stoppedEarly = true;
                        loop.Stop();
                    }
                    if (local.BestDist < int.MaxValue)
                    {
                        lock (bestLock)
                        {
                            if (local.BestDist < bestDist || (local.BestDist == bestDist && local.BestSeed < bestSeed))
                            {
                                bestDist = local.BestDist;
                                bestSeed = local.BestSeed;
                            }
                        }
                    }

                    var checkedNow = Interlocked.Add(ref done, (ulong)count);
                    progress?.Report(new IvSearchProgress(
                        Math.Min(1.0, (double)checkedNow / total), checkedNow, total,
                        Volatile.Read(ref exactCount), sw.Elapsed));
                });
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return new IvSearchResult { Cancelled = true, Elapsed = sw.Elapsed };
        }

        // ── Armado de opciones (acá sí se usa LegalityAnalysis, solo sobre pocas semillas) ──
        var candidates = new List<(IvSeedOption opt, ulong seed)>();
        foreach (var seed in exact.OrderBy(x => x).Take(ExactCap))
        {
            var built = Evaluate(plan, seed);
            if (built is not null) candidates.Add((built.Value.opt, seed));
        }

        // Una opción por combinación distinta de Naturaleza/Habilidad/Género: ocho semillas con
        // lo mismo no le dan nada que elegir al usuario.
        var diverse = candidates
            .GroupBy(c => (c.opt.Nature, c.opt.Ability, c.opt.Gender))
            .Select(g => g.OrderBy(c => c.seed).First())
            .OrderByDescending(c => c.opt.PreservedCount)
            .ThenBy(c => c.seed)
            .ToList();

        var options = new List<IvSeedOption>();
        var verified = 0;
        foreach (var (opt, seed) in diverse)
        {
            if (options.Count >= MaxOptions || verified >= MaxVerifications) break;
            verified++;
            if (Verify(plan, seed)) options.Add(opt);
        }

        IvSeedOption? closest = null;
        if (options.Count == 0 && bestDist < int.MaxValue)
        {
            var built = Evaluate(plan, bestSeed);
            if (built is not null && Verify(plan, bestSeed)) closest = built.Value.opt;
        }

        sw.Stop();
        return new IvSearchResult
        {
            Options = options,
            Closest = closest,
            Exhaustive = !stoppedEarly && done >= total,
            Elapsed = sw.Elapsed,
        };
    }

    // ── Evaluación de una semilla ───────────────────────────────────────────────────────

    /// <summary>Aplica la semilla a una copia y arma la opción (qué cambia respecto del Pokémon
    /// editado). Devuelve null si la semilla cambiaría el brillante: eso nunca se ofrece.</summary>
    private static (IvSeedOption opt, PKM pk)? Evaluate(IvSeedPlan plan, ulong seed)
    {
        var pk = plan.Edited.Clone();
        if (!ApplySeed(plan, seed, pk)) return null;
        if (pk.IsShiny != plan.Edited.IsShiny) return null;

        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        var dist = 0;
        for (var i = 0; i < 6; i++) dist += Math.Abs(ivs[i] - plan.TargetIvs[i]);

        var sizeChanged = false;
        if (pk is IScaledSize a && plan.Edited is IScaledSize b)
            sizeChanged = a.HeightScalar != b.HeightScalar || a.WeightScalar != b.WeightScalar;
        if (!sizeChanged && pk is IScaledSize3 a3 && plan.Edited is IScaledSize3 b3)
            sizeChanged = a3.Scale != b3.Scale;

        var opt = new IvSeedOption(
            seed, ivs, dist, pk.Nature, pk.Ability, pk.Gender,
            NatureChanged: pk.Nature != plan.Edited.Nature,
            AbilityChanged: pk.AbilityNumber != plan.Edited.AbilityNumber,
            GenderChanged: pk.Gender != plan.Edited.Gender,
            SizeChanged: sizeChanged);
        return (opt, pk);
    }

    private static bool Verify(IvSeedPlan plan, ulong seed)
    {
        var built = Evaluate(plan, seed);
        if (built is null) return false;
        return CountInvalid(new LegalityAnalysis(built.Value.pk)) <= plan.BaselineInvalid;
    }

    // ── Pasada rápida sobre un bloque de semillas ───────────────────────────────────────

    private sealed class ScanContext
    {
        public readonly IvSeedKind Kind;
        public readonly int Flawless;
        public readonly int[] Target;
        public readonly ITeraRaid9? Raid;
        public readonly bool WantShiny;
        public readonly uint Id32;

        public ScanContext(IvSeedPlan plan)
        {
            Kind = plan.Kind;
            Flawless = plan.FlawlessIVCount;
            Target = plan.TargetIvs;
            Raid = plan.Encounter as ITeraRaid9;
            WantShiny = plan.Edited.IsShiny;
            Id32 = plan.Edited.ID32;
        }
    }

    private readonly record struct ChunkResult(List<uint> Exact, int BestDist, uint BestSeed);

    private static ChunkResult ScanChunk(ScanContext cx, ulong from, int count, CancellationToken ct)
    {
        var exact = new List<uint>();
        var best = int.MaxValue;
        uint bestSeed = 0;

        for (var i = 0; i < count; i++)
        {
            //if ((i & 0xFFFF) == 0 && ct.IsCancellationRequested) break;
			if ((i & 0x3FF) == 0 && ct.IsCancellationRequested) break;

            var seed = (uint)(from + (ulong)i);
            var d = cx.Kind == IvSeedKind.Tera9Raid
                ? DistTera(cx, seed, best)
                : DistOverworld8(cx, seed, best);
            if (d == int.MaxValue) continue;

            if (d == 0) exact.Add(seed);
            if (d < best) { best = d; bestSeed = seed; }
            if (exact.Count >= ExactCap) break;
        }
        return new ChunkResult(exact, best, bestSeed);
    }

    /// <summary>Distancia (suma de diferencias de IVs) entre los IVs de la semilla y el objetivo,
    /// con corte temprano cuando ya supera <paramref name="bound"/>. int.MaxValue = descartada.
    /// Réplica de Encounter9RNG.GenerateData para raids (rollCount 1: sin tiradas de PID extra).</summary>
    private static int DistTera(ScanContext cx, uint seed, int bound)
    {
        if (!cx.Raid!.CanBeEncountered(seed)) return int.MaxValue;   // primero lo barato: ¿esta semilla origina este raid?

        var r = new Xoroshiro128Plus(seed);
        r.NextInt(uint.MaxValue);       // EC
        r.NextInt(); r.NextInt();       // TID falso, PID

        Span<int> iv = stackalloc int[6];
        iv.Fill(-1);
        var d = 0;
        for (var i = 0; i < cx.Flawless; i++)
        {
            int idx;
            do { idx = (int)r.NextInt(6); } while (iv[idx] != -1);
            iv[idx] = 31;
            d += Math.Abs(31 - cx.Target[idx]);
        }
        if (d > bound) return int.MaxValue;
        for (var i = 0; i < 6; i++)
        {
            if (iv[i] != -1) continue;
            d += Math.Abs((int)r.NextInt(32) - cx.Target[i]);
            if (d > bound) return int.MaxValue;
        }
        return d;
    }

    /// <summary>Réplica del prefijo de Overworld8RNG.TryApplyFromSeed (EC, PID, IVs) con la regla
    /// de brillante: el estado brillante del Pokémon editado no cambia.</summary>
    private static int DistOverworld8(ScanContext cx, uint seed, int bound)
    {
        var r = new Xoroshiro128Plus(seed);
        r.NextInt(uint.MaxValue);
        var pid = (uint)r.NextInt(uint.MaxValue);
        if ((ShinyUtil.GetShinyXor(cx.Id32, pid) < 16) != cx.WantShiny) return int.MaxValue;

        Span<int> iv = stackalloc int[6];
        iv.Fill(-1);
        var d = 0;
        for (var i = 0; i < cx.Flawless; i++)
        {
            int idx;
            do { idx = (int)r.NextInt(6); } while (iv[idx] != -1);
            iv[idx] = 31;
            d += Math.Abs(31 - cx.Target[idx]);
        }
        if (d > bound) return int.MaxValue;
        for (var i = 0; i < 6; i++)
        {
            if (iv[i] != -1) continue;
            d += Math.Abs((int)r.NextInt(32) - cx.Target[i]);
            if (d > bound) return int.MaxValue;
        }
        return d;
    }

    /// <summary>Generación completa de un estático de mundo abierto de Gen8 desde su semilla
    /// (incluye altura y peso, que PKHeX también valida).</summary>
    private static bool GenOverworld8(uint seed, int flawless, Span<int> iv,
        out uint ec, out uint pid, out int height, out int weight)
    {
        var r = new Xoroshiro128Plus(seed);
        ec = (uint)r.NextInt(uint.MaxValue);
        pid = (uint)r.NextInt(uint.MaxValue);
        iv.Fill(-1);
        for (var i = 0; i < flawless; i++)
        {
            int idx;
            do { idx = (int)r.NextInt(6); } while (iv[idx] != -1);
            iv[idx] = 31;
        }
        for (var i = 0; i < 6; i++)
            if (iv[i] == -1) iv[i] = (int)r.NextInt(32);
        height = (int)(r.NextInt(0x81) + r.NextInt(0x80));
        weight = (int)(r.NextInt(0x81) + r.NextInt(0x80));
        return true;
    }

    // ── Utilidades ──────────────────────────────────────────────────────────────────────

    private static int CountInvalid(LegalityAnalysis la) => la.Results.Count(r => !r.Valid);

    private static bool SameIvs(PKM a, PKM b) =>
        a.IV_HP == b.IV_HP && a.IV_ATK == b.IV_ATK && a.IV_DEF == b.IV_DEF &&
        a.IV_SPA == b.IV_SPA && a.IV_SPD == b.IV_SPD && a.IV_SPE == b.IV_SPE;

    private static void CopyIvs(PKM from, PKM to)
    {
        to.IV_HP = from.IV_HP; to.IV_ATK = from.IV_ATK; to.IV_DEF = from.IV_DEF;
        to.IV_SPA = from.IV_SPA; to.IV_SPD = from.IV_SPD; to.IV_SPE = from.IV_SPE;
    }
}
