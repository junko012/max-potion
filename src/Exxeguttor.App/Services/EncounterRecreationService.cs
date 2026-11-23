using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>Gen5+: grupos de datos que la opción tuvo que REEMPLAZAR por los del encuentro generado porque PKHeX no
/// los aceptaba junto al origen nuevo. Lo que no aparece acá se conserva tal cual lo tenía el Pokémon.</summary>
[Flags]
public enum RecreateGroups
{
    None = 0,
    /// <summary>PID, EC, Naturaleza, Género, Habilidad y tamaños (lo que define Brillante y estadísticas).</summary>
    Identity = 1,
    Ball = 2,
    /// <summary>Manejador (HT) y recuerdos, tanto los del entrenador original como los del manejador.</summary>
    Handler = 4,
    Ribbons = 8,
}

/// <summary>Una forma concreta de "recrear" el origen de un Pokémon con error de encuentro.</summary>
public sealed class RecreateOption
{
    /// <summary>Gen5+: qué grupos se reemplazaron (ver <see cref="RecreateGroups"/>); None = se conservó todo.</summary>
    public RecreateGroups Groups { get; init; }
    public int OriginalRibbons { get; init; }
    public int Ribbons { get; init; }
    public string OriginalHandler { get; init; } = "—";
    public string Handler { get; init; } = "—";
    /// <summary>Let's Go: suma de AVs (valores de despertar) antes y después; sube si el origen nuevo exige un mínimo mayor.</summary>
    public int OriginalAwakening { get; init; }
    public int Awakening { get; init; }
    public string OriginalShiny { get; init; } = "—";
    public string Shiny { get; init; } = "—";
    /// <summary>El Pokémon ya recreado y VERIFICADO (sin error de encuentro y sin errores nuevos).</summary>
    public required PKM Result { get; init; }
    public required string Place { get; init; }
    public required int CaptureLevel { get; init; }
    public required string Ball { get; init; }
    public required string Nature { get; init; }
    public required string Gender { get; init; }
    public required bool KeepsPlace { get; init; }
    public required bool IsEgg { get; init; }
    public required bool NatureChanged { get; init; }
    public required bool GenderChanged { get; init; }
    public required bool BallChanged { get; init; }
    public required bool LevelChanged { get; init; }
    public required string OriginalPlace { get; init; }
    public required string OriginalBall { get; init; }
    public required string OriginalNature { get; init; }
    public required string OriginalGender { get; init; }
    public required int OriginalCaptureLevel { get; init; }
    /// <summary>True cuando la opción cambia IVs (modo "conservar la Naturaleza").</summary>
    public bool IvsChanged { get; init; }
    public string OriginalIvText { get; init; } = string.Empty;
    public string NewIvText { get; init; } = string.Empty;
    /// <summary>Clave de deduplicación (lugar, nivel de captura, bola, naturaleza, género, huevo, IVs).</summary>
    public string Key => $"{Place}|{CaptureLevel}|{Ball}|{Nature}|{Gender}|{IsEgg}|{NewIvText}|{(int)Groups}";
}

/// <summary>Una combinación de IVs con un PID válido (ya verificada), con lo que implica para el Pokémon.</summary>
public sealed record IvSearchOption(IvPidCandidate Cand, string Nature, string Gender, string OriginalNature,
    string OriginalIvText, bool NatureChanged);

/// <summary>Resultado de analizar un Pokémon con error de encuentro.</summary>
public sealed class RecreateAnalysis
{
    public bool HasEncounterError { get; init; }
    public IReadOnlyList<RecreateOption> Options { get; init; } = [];
    /// <summary>Movimientos que PKHeX no acepta (caso "sin candidatos").</summary>
    public IReadOnlyList<string> IllegalMoves { get; init; } = [];
    /// <summary>Slots (1-4) de esos movimientos, para el atajo "Ir a Moves".</summary>
    public IReadOnlyList<int> IllegalSlots { get; init; } = [];
}

/// <summary>
/// Edición compuesta de la sesión: reescribe el ORIGEN del Pokémon (lugar, nivel y fecha de captura,
/// bola, juego de origen y, en Gen3/4, el PID con lo que deriva de él) copiándolo del Pokémon ya
/// verificado. Se aplica igual en el preview de legalidad y al exportar; lo demás (apodo, moves,
/// objeto...) sigue viniendo de las ediciones normales.
/// </summary>
public sealed record EncounterRecreation(PKM Result, bool CopyIvs = false, RecreateGroups Groups = RecreateGroups.None)
{
    public bool ApplyTo(PKM target)
    {
        if (target.GetType() != Result.GetType()) return false;
        EncounterRecreationService.CopyOrigin(Result, target, Groups);
        if (CopyIvs) EncounterRecreationService.CopyIvs(Result, target);
        return true;
    }
}

/// <summary>
/// "Recrear con un encuentro válido" (Gen2–Gen4). Mismo criterio generate-and-verify que
/// <see cref="Method1Solver"/>: se generan los encuentros que PKHeX considera compatibles con la
/// especie y los movimientos actuales, se trasplanta al Pokémon generado todo lo que se debe conservar
/// y se acepta solo si LegalityAnalysis ya no marca error de encuentro NI suma errores nuevos.
/// El trasplante de PP y PP Ups es obligatorio: sin él casi todos los candidatos fallaban por PP.
/// Gen1 no guarda datos de captura: no hay origen que recrear (ver <see cref="TryRaiseLevel"/>).
/// </summary>
public static class EncounterRecreationService
{
    private const int MaxCandidates = 300;
    private const int MaxIvTries = 10;
    private const int BudgetSeconds = 25;

    /// <summary>Gen2–4 (PID de Method 1 / DVs), Gen5–9 principales, Let's Go (PB7), Leyendas: Arceus (PA8) y Z-A (PA9):
    /// medidos con 10 de 10 casos resueltos cada uno. Let's Go solo funcionaba en 3 de cada 10 hasta que se subieron los
    /// AVs al mínimo del origen nuevo (ver <see cref="RaiseAwakenedToMinimum"/>).</summary>
    public static bool IsSupported(PKM pkm) =>
        pkm.Format is >= 2 and <= 4 || pkm is PK5 or PK6 or PK7 or PK8 or PB8 or PK9 or PB7 or PA8 or PA9;

    private static bool IsModern(PKM pkm) => pkm.Format >= 5;

    /// <summary>Error "Origen del encuentro" (Encounter/SlotType) o "Encuentro especial" (Fateful).</summary>
    public static bool HasEncounterError(LegalityAnalysis la) =>
        la.Results.Any(r => !r.Valid && r.Judgement == Severity.Invalid && IsEncounterId(r.Identifier));

    private static bool IsEncounterId(CheckIdentifier id) =>
        id is CheckIdentifier.Encounter or CheckIdentifier.SlotType or CheckIdentifier.Fateful;

    private static int OtherErrors(LegalityAnalysis la)
    {
        var n = la.Results.Count(r => !r.Valid && r.Judgement == Severity.Invalid && !IsEncounterId(r.Identifier));
        for (var i = 0; i < la.Info.Moves.Length; i++)
            if (!la.Info.Moves[i].Valid && la.Info.Moves[i].Info.Method != 0) n++;
        return n;
    }

    /// <summary>Busca opciones. <paramref name="actual"/> debe traer ya las ediciones pendientes.</summary>
    public static RecreateAnalysis Analyze(PKM actual, SaveFile save, GameStrings strings, bool keepNature = false,
        int maxOptions = 60, CancellationToken ct = default)
    {
        LegalityAnalysis baseLa;
        try { baseLa = new LegalityAnalysis(actual); }
        catch { return new RecreateAnalysis(); }

        if (!IsSupported(actual) || !HasEncounterError(baseLa))
            return new RecreateAnalysis { HasEncounterError = HasEncounterError(baseLa) };

        var illegal = new List<string>();
        var illegalSlots = new List<int>();
        for (var i = 0; i < baseLa.Info.Moves.Length && i < 4; i++)
        {
            if (baseLa.Info.Moves[i].Valid) continue;
            var id = actual.GetMove(i);
            if (id != 0 && id < strings.movelist.Length) { illegal.Add(strings.movelist[id]); illegalSlots.Add(i + 1); }
        }

        var baseline = OtherErrors(baseLa);
        // Identificadores que YA fallaban en el Pokémon original: no cuentan como "causados" por el origen nuevo.
        var baselineIds = baseLa.Results.Where(r => !r.Valid && r.Judgement == Severity.Invalid)
            .Select(r => r.Identifier).ToHashSet();
        var moves = new ushort[] { actual.Move1, actual.Move2, actual.Move3, actual.Move4 }
            .Where(m => m != 0).ToArray();

        // Modo "conservar la Naturaleza" (Gen3/4): la Naturaleza sale del PID y el PID del LCRNG junto con los
        // IVs, así que con IVs fijos casi nunca coincide. Se buscan (una sola vez) PIDs con la misma Naturaleza
        // y los IVs lo más parecidos posible; cada encuentro prueba esos candidatos en orden.
        List<IvPidCandidate>? ivCandidates = null;
        if (keepNature && actual.Format is 3 or 4)
            ivCandidates = Method1IvSearch.FindProgressive(actual, keepNature: true, enough: 20, maxLevel: 2, max: 60, ct: ct);

        // Tope de tiempo: si la verificación se alarga se devuelve lo que ya se juntó (nunca congela ni queda
        // trabajando sin fin). El token del llamador sí cancela de verdad (lanza OperationCanceledException).
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(BudgetSeconds * 1000);

        var found = new System.Collections.Concurrent.ConcurrentBag<(int Index, RecreateOption Option)>();
        // En paralelo solo el modo "conservar Naturaleza" (el lento). El modo normal tarda ~0,2 s y en paralelo
        // se vio que ConvertToPKM de Gen3 (comparte el azar interno de PKHeX) dejaba opciones sin encontrar.
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = ivCandidates != null ? Method1IvSearch.Workers : 1 };
        var index = 0;
        foreach (var versions in VersionSets(actual, save))
        {
            List<IEncounterConvertible> candidates;
            try
            {
                candidates = EncounterMovesetGenerator.GenerateEncounters(actual, moves, versions)
                    .Take(MaxCandidates).OfType<IEncounterConvertible>().ToList();
            }
            catch { continue; }

            var startIndex = index;
            index += candidates.Count;
            Parallel.For(0, candidates.Count, parallel, (i, state) =>
            {
                if (budget.IsCancellationRequested || ct.IsCancellationRequested) { state.Stop(); return; }
                if (found.Count >= maxOptions) { state.Stop(); return; }
                var conv = candidates[i];
                try
                {
                    var option = IsModern(actual)
                        ? TryBuildModern(actual, save, strings, (IEncounterable)conv, conv, baseline, baselineIds, budget.Token)
                        : TryBuild(actual, save, strings, (IEncounterable)conv, conv, baseline, ivCandidates, budget.Token);
                    if (option != null) found.Add((startIndex + i, option));
                }
                catch { /* un candidato que no se deja convertir no es una opción */ }
            });
            ct.ThrowIfCancellationRequested();
            if (!found.IsEmpty) break;   // el primer juego con opciones basta (conserva el juego de origen)
        }

        // Orden estable (el del generador de PKHeX) y sin repetidos, igual que cuando era secuencial.
        var options = new Dictionary<string, RecreateOption>();
        foreach (var (_, option) in found.OrderBy(f => f.Index))
            options.TryAdd(option.Key, option);

        var ordered = options.Values
            .OrderBy(o => o.IsEgg ? 2 : o.KeepsPlace ? 0 : 1)
            .ThenBy(o => System.Numerics.BitOperations.PopCount((uint)o.Groups))   // Gen5+: las que conservan más, primero
            .ThenBy(o => o.Place, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(o => o.CaptureLevel)
            .ToList();

        return new RecreateAnalysis { HasEncounterError = true, Options = ordered, IllegalMoves = illegal, IllegalSlots = illegalSlots };
    }

    private static IEnumerable<GameVersion[]> VersionSets(PKM actual, SaveFile save)
    {
        if (actual.Format <= 2) { yield return []; yield break; }   // Gen2 no guarda el juego de origen
        var seen = new HashSet<GameVersion>();
        if (actual.Version != 0 && seen.Add(actual.Version)) yield return [actual.Version];
        if (seen.Add(save.Version)) yield return [save.Version];
    }

    private static RecreateOption? TryBuild(PKM actual, SaveFile save, GameStrings strings,
        IEncounterable enc, IEncounterConvertible conv, int baseline, List<IvPidCandidate>? ivCandidates,
        CancellationToken budget)
    {
        var gen = conv.ConvertToPKM(save);
        if (gen.GetType() != actual.GetType()) return null;
        if (gen.Species != actual.Species)
        {
            if (enc.Species != actual.Species && !CanEvolve(gen, actual)) return null;
        }

        // Fechas: se intenta conservar la de captura original (Gen4); si con ella queda un error nuevo,
        // se usa la que trae el encuentro generado.
        var keepDates = actual.Format >= 4 && actual.MetDate.HasValue;
        for (var attempt = keepDates ? 0 : 1; attempt < 2; attempt++)
        {
            var trial = gen.Clone();
            Transplant(actual, trial);
            if (attempt == 0) KeepDates(actual, trial);

            if (ivCandidates != null && !enc.IsEgg)
            {
                // Conservar Naturaleza: se prueban los PIDs candidatos (con sus IVs) hasta que uno verifica.
                var tried = 0;
                foreach (var cand in ivCandidates)
                {
                    if (++tried > MaxIvTries || budget.IsCancellationRequested) break;
                    var t = trial.Clone();
                    ApplyIvCandidate(actual, t, cand);
                    t.RefreshChecksum();
                    LegalityAnalysis lc;
                    try { lc = new LegalityAnalysis(t); } catch { continue; }
                    if (HasEncounterError(lc) || OtherErrors(lc) > baseline) continue;
                    return Describe(actual, t, enc, strings);
                }
                continue;
            }

            if (!SolveGen34(actual, trial, enc.IsEgg)) return null;
            trial.RefreshChecksum();

            LegalityAnalysis la;
            try { la = new LegalityAnalysis(trial); } catch { return null; }
            if (HasEncounterError(la)) continue;
            if (OtherErrors(la) > baseline) continue;
            return Describe(actual, trial, enc, strings);
        }
        return null;
    }

    // ── Gen5–9 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gen5+: se parte de una COPIA DEL POKÉMON ORIGINAL y solo se le pone el origen del encuentro generado (lugar,
    /// nivel, fechas, juego, encuentro especial). Así se conservan por construcción PID, EC, Naturaleza, Brillante,
    /// Habilidad, tamaños, bola, cintas y recuerdos. Si PKHeX rechaza alguno de esos datos junto al origen nuevo, se
    /// reemplaza SOLO ese grupo por el del encuentro generado (<see cref="RecreateGroups"/>) y se vuelve a verificar:
    /// "conservar si queda legal". Cada vuelta suma como mucho un grupo nuevo, así que termina siempre.
    /// </summary>
    private static RecreateOption? TryBuildModern(PKM actual, SaveFile save, GameStrings strings,
        IEncounterable enc, IEncounterConvertible conv, int baseline, HashSet<CheckIdentifier> baselineIds,
        CancellationToken budget)
    {
        var gen = conv.ConvertToPKM(save);
        if (gen.GetType() != actual.GetType()) return null;
        if (gen.Species != actual.Species && enc.Species != actual.Species && !CanEvolve(gen, actual)) return null;

        // Fecha de captura: primero la original; si con ella queda un error nuevo, la del encuentro generado.
        var keepDates = actual.MetDate.HasValue;
        for (var attempt = keepDates ? 0 : 1; attempt < 2; attempt++)
        {
            var groups = RecreateGroups.None;
            for (var step = 0; step < 5; step++)
            {
                if (budget.IsCancellationRequested) return null;
                var trial = BuildModernTrial(actual, gen, groups, attempt == 0);

                LegalityAnalysis la;
                try { la = new LegalityAnalysis(trial); } catch { return null; }
                if (!HasEncounterError(la) && OtherErrors(la) <= baseline)
                    return Describe(actual, trial, enc, strings, groups);

                var more = GroupsFor(la, baselineIds) & ~groups;
                if (more == RecreateGroups.None) break;
                groups |= more;
            }
        }
        return null;
    }

    /// <summary>Qué grupos explican los errores NUEVOS (los que el original no tenía).</summary>
    internal static RecreateGroups GroupsFor(LegalityAnalysis la, HashSet<CheckIdentifier> baselineIds)
    {
        var g = RecreateGroups.None;
        foreach (var r in la.Results)
        {
            if (r.Valid || r.Judgement != Severity.Invalid || baselineIds.Contains(r.Identifier)) continue;
            g |= r.Identifier switch
            {
                CheckIdentifier.Ball => RecreateGroups.Ball,
                CheckIdentifier.Memory or CheckIdentifier.Handler or CheckIdentifier.Geography => RecreateGroups.Handler,
                CheckIdentifier.Ribbon or CheckIdentifier.RibbonMark => RecreateGroups.Ribbons,
                CheckIdentifier.Shiny or CheckIdentifier.EC or CheckIdentifier.PID or CheckIdentifier.Gender
                    or CheckIdentifier.Nature or CheckIdentifier.Ability or CheckIdentifier.Misc => RecreateGroups.Identity,
                _ => RecreateGroups.None,
            };
        }
        return g;
    }

    /// <summary>Copia del original + origen del generado + los grupos indicados tomados del generado.</summary>
    private static PKM BuildModernTrial(PKM actual, PKM gen, RecreateGroups groups, bool keepOriginalDate)
    {
        var trial = actual.Clone();
        CopyModernOrigin(gen, trial);
        if (keepOriginalDate)
        {
            if (actual.MetDate is { } d) trial.MetDate = d;
            if (trial.EggLocation != 0 && trial.EggMetDate is { } e && trial.MetDate is { } m && e > m) trial.EggMetDate = m;
        }
        CopyGroups(gen, trial, groups);
        if (trial is PB7 lgpe) RaiseAwakenedToMinimum(lgpe);   // Let's Go: AVs mínimos y CP dependen del origen
        trial.RefreshChecksum();
        return trial;
    }

    private static readonly string[] OriginProps =
    [
        nameof(PKM.Version), nameof(PKM.MetLocation), nameof(PKM.MetLevel), nameof(PKM.EggLocation),
        nameof(PKM.FatefulEncounter), nameof(PKM.MetDate), nameof(PKM.EggMetDate), "ObedienceLevel",
    ];

    private static readonly string[] IdentityProps =
    [
        nameof(PKM.PID), nameof(PKM.EncryptionConstant), nameof(PKM.Nature), "StatNature", nameof(PKM.Gender),
        nameof(PKM.Ability), "AbilityNumber", "HeightScalar", "WeightScalar", "Scale",
    ];

    private static bool IsHandlerProp(string n) =>
        n.StartsWith("HandlingTrainer", StringComparison.Ordinal) || n.StartsWith("OriginalTrainerMemory", StringComparison.Ordinal)
        || n.StartsWith("Geo", StringComparison.Ordinal) || n is nameof(PKM.CurrentHandler) or "Country" or "Region" or "ConsoleRegion";

    private static bool IsRibbonProp(string n) =>
        n.StartsWith("Ribbon", StringComparison.Ordinal) || n.StartsWith("AffixedRibbon", StringComparison.Ordinal);

    private static void CopyModernOrigin(PKM from, PKM to) => CopyProps(from, to, n => OriginProps.Contains(n));

    /// <summary>Copia los grupos indicados del Pokémon verificado (por nombre de propiedad, como hace PKHeX con las cintas).</summary>
    internal static void CopyGroups(PKM from, PKM to, RecreateGroups groups)
    {
        if (groups == RecreateGroups.None) return;
        if (groups.HasFlag(RecreateGroups.Ball)) to.Ball = from.Ball;
        if (groups.HasFlag(RecreateGroups.Identity)) CopyProps(from, to, n => IdentityProps.Contains(n));
        if (groups.HasFlag(RecreateGroups.Handler)) CopyProps(from, to, IsHandlerProp);
        if (groups.HasFlag(RecreateGroups.Ribbons)) CopyProps(from, to, IsRibbonProp);
        if (groups.HasFlag(RecreateGroups.Identity)) to.ResetPartyStats();   // la Naturaleza/tamaños cambian las estadísticas
    }

    private static void CopyProps(PKM from, PKM to, Func<string, bool> pick)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        foreach (var p in from.GetType().GetProperties(flags))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || !pick(p.Name)) continue;
            var q = to.GetType().GetProperty(p.Name, flags);
            if (q is not { CanWrite: true } || q.PropertyType != p.PropertyType || q.SetMethod?.IsPublic != true) continue;
            try { q.SetValue(to, p.GetValue(from)); }
            catch { /* una propiedad que no admite ese valor no es parte del grupo */ }
        }
    }

    /// <summary>
    /// Let's Go: PKHeX exige AVs (valores de despertar) iguales o mayores al mínimo esperado para el nivel actual y el
    /// de captura ("al subir de nivel se reparten, así que un Pokémon debe estar en o sobre ese mínimo"). Al recrear
    /// el origen cambia el nivel de captura y el mínimo sube, y los AVs que traía el Pokémon quedaban por debajo.
    /// Se SUBE cada AV hasta el mínimo y nunca se baja uno que ya estuviera por encima; después se recalcula el CP.
    /// </summary>
    internal static void RaiseAwakenedToMinimum(PB7 pb7)
    {
        Span<byte> required = stackalloc byte[6];
        AwakeningUtil.SetExpectedMinimumAVs(required, pb7);
        Span<byte> current = stackalloc byte[6];
        AwakeningUtil.AwakeningGetVisual(pb7, current);
        for (var i = 0; i < current.Length; i++)
            if (current[i] < required[i]) current[i] = required[i];
        AwakeningUtil.AwakeningSetVisual(pb7, current);
        pb7.ResetCalculatedValues();   // el CP depende de los AVs
    }

    private static int AwakeningVisualSum(PB7 pb7)
    {
        Span<byte> v = stackalloc byte[6];
        AwakeningUtil.AwakeningGetVisual(pb7, v);
        var sum = 0;
        foreach (var x in v) sum += x;
        return sum;
    }

    private static int RibbonCount(PKM p)
    {
        try { return RibbonInfo.GetRibbonInfo(p).Count(r => r.HasRibbon || r.RibbonCount > 0); }
        catch { return 0; }
    }

    private static string HandlerText(PKM p) =>
        p.CurrentHandler == 1 && !string.IsNullOrWhiteSpace(p.HandlingTrainerName) ? p.HandlingTrainerName : "—";

    private static bool CanEvolve(PKM gen, PKM actual) => gen.Species != actual.Species && gen.Species < actual.Species;

    private static void KeepDates(PKM actual, PKM trial)
    {
        if (actual.MetDate is { } d) trial.MetDate = d;
        // Un huevo eclosionado: la fecha del huevo no puede ser posterior a la de captura.
        if (trial.EggLocation != 0 && trial.EggMetDate is { } e && trial.MetDate is { } m && e > m)
            trial.EggMetDate = m;
    }

    /// <summary>Copia del Pokémon actual al generado todo lo que NO es origen.</summary>
    private static void Transplant(PKM actual, PKM gen)
    {
        gen.Species = actual.Species;
        gen.Form = actual.Form;
        gen.OriginalTrainerName = actual.OriginalTrainerName;
        gen.TID16 = actual.TID16;
        gen.SID16 = actual.SID16;
        gen.OriginalTrainerGender = actual.OriginalTrainerGender;
        gen.Language = actual.Language;
        gen.IsNicknamed = actual.IsNicknamed;
        gen.Nickname = actual.Nickname;
        gen.HeldItem = actual.HeldItem;
        gen.EXP = actual.EXP;
        gen.CurrentLevel = actual.CurrentLevel;
        gen.OriginalTrainerFriendship = actual.OriginalTrainerFriendship;
        gen.CurrentFriendship = actual.CurrentFriendship;

        // Movimientos con PP y PP Ups (obligatorio: sin esto casi todos los candidatos fallaban por PP).
        gen.Move1 = actual.Move1; gen.Move2 = actual.Move2; gen.Move3 = actual.Move3; gen.Move4 = actual.Move4;
        gen.Move1_PPUps = actual.Move1_PPUps; gen.Move2_PPUps = actual.Move2_PPUps;
        gen.Move3_PPUps = actual.Move3_PPUps; gen.Move4_PPUps = actual.Move4_PPUps;
        gen.Move1_PP = actual.Move1_PP; gen.Move2_PP = actual.Move2_PP;
        gen.Move3_PP = actual.Move3_PP; gen.Move4_PP = actual.Move4_PP;

        gen.EV_HP = actual.EV_HP; gen.EV_ATK = actual.EV_ATK; gen.EV_DEF = actual.EV_DEF;
        gen.EV_SPE = actual.EV_SPE; gen.EV_SPA = actual.EV_SPA; gen.EV_SPD = actual.EV_SPD;
        gen.IV_HP = actual.IV_HP; gen.IV_ATK = actual.IV_ATK; gen.IV_DEF = actual.IV_DEF;
        gen.IV_SPE = actual.IV_SPE; gen.IV_SPA = actual.IV_SPA; gen.IV_SPD = actual.IV_SPD;

        gen.PokerusStrain = actual.PokerusStrain;
        gen.PokerusDays = actual.PokerusDays;
        if (actual.Format >= 3) gen.Ability = actual.Ability;
        if (actual.Format >= 3) gen.Gender = actual.Gender;

        gen.Stat_Level = actual.Stat_Level;
        gen.ResetPartyStats();
    }

    /// <summary>Gen3/4: con los IVs ya trasplantados se busca un PID que el LCRNG pueda haber generado
    /// junto con ellos (Method1Solver, que además VERIFICA contra LegalityAnalysis). Gen2: los DVs
    /// ya definen todo (brillante incluido), no hay PID.</summary>
    private static bool SolveGen34(PKM actual, PKM trial, bool isEgg)
    {
        if (actual.Format is not (3 or 4)) return true;

        // Huevo de Gen3: el PID NO puede seguir el patrón de Method 1 (PKHeX espera PIDType.None). Con el
        // solver de Method 1 todos los candidatos de huevo fallaban, y por eso "no había origen válido"
        // aunque el movimiento sí fuera un movimiento huevo. Ver EggOriginService.
        if (isEgg && EggOriginService.IsSupported(trial))
        {
            if (!EggOriginService.TrySolvePid(trial, out var eggPid, reference: actual)) return false;
            trial.PID = eggPid;
            trial.RefreshAbility((int)(eggPid & 1));   // el slot de habilidad sigue al bit del PID
            if (trial.Format == 4) trial.Nature = (Nature)(eggPid % 25);   // Gen4 guarda la Naturaleza aparte
            return true;
        }

        if (!Method1Solver.TrySolve(trial, out var r)) return false;

        trial.PID = r.Pid;
        var g = EntityGender.GetFromPID(trial.Species, r.Pid);
        if (trial.Gender != g) trial.Gender = g;
        if (r.AbilityChanged) trial.RefreshAbility((int)(r.Pid & 1));
        if (actual.Format == 4) trial.Nature = (Nature)(r.Pid % 25);
        return true;
    }

    private static void ApplyIvCandidate(PKM actual, PKM t, IvPidCandidate c)
    {
        t.IV_HP = c.Ivs[0]; t.IV_ATK = c.Ivs[1]; t.IV_DEF = c.Ivs[2];
        t.IV_SPA = c.Ivs[3]; t.IV_SPD = c.Ivs[4]; t.IV_SPE = c.Ivs[5];
        t.PID = c.Pid;
        var g = EntityGender.GetFromPID(t.Species, c.Pid);
        if (t.Gender != g) t.Gender = g;
        if (t.Ability != actual.Ability) t.RefreshAbility((int)(c.Pid & 1));
        if (actual.Format == 4) t.Nature = (Nature)(c.Pid % 25);
    }

    /// <summary>Copia los IVs del Pokémon verificado (solo cuando la opción los cambió).</summary>
    public static void CopyIvs(PKM from, PKM to)
    {
        to.IV_HP = from.IV_HP; to.IV_ATK = from.IV_ATK; to.IV_DEF = from.IV_DEF;
        to.IV_SPA = from.IV_SPA; to.IV_SPD = from.IV_SPD; to.IV_SPE = from.IV_SPE;
        to.RefreshChecksum();
    }

    private static string IvText(PKM p) => $"{p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE}";

    private static RecreateOption Describe(PKM actual, PKM result, IEncounterable enc, GameStrings strings,
        RecreateGroups groups = RecreateGroups.None)
    {
        string Place(PKM p)
        {
            try
            {
                var list = GameInfo.GetLocationList(p.Version == 0 ? GameVersion.C : p.Version, p.Context, egg: false);
                var item = list.FirstOrDefault(l => l.Value == p.MetLocation);
                if (item != null) return item.Text;
            }
            catch { }
            return p.MetLocation.ToString();
        }
        string Ball(PKM p) => p.Ball > 0 && p.Ball < strings.itemlist.Length ? strings.itemlist[p.Ball] : "—";
        string Nat(PKM p) => p.Format >= 3 && (int)p.Nature < strings.natures.Length ? strings.natures[(int)p.Nature] : "—";
        string Gen(PKM p) => p.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };

        var place = Place(result);
        var isEgg = enc.IsEgg;
        var hasBall = actual.Format >= 3;
        return new RecreateOption
        {
            Result = result,
            Place = place,
            CaptureLevel = result.MetLevel,
            Ball = hasBall ? Ball(result) : "—",
            Nature = Nat(result),
            Gender = Gen(result),
            KeepsPlace = !isEgg && result.MetLocation == actual.MetLocation,
            IsEgg = isEgg,
            NatureChanged = actual.Format >= 3 && result.Nature != actual.Nature,
            GenderChanged = result.Gender != actual.Gender,
            BallChanged = hasBall && result.Ball != actual.Ball,
            LevelChanged = result.MetLevel != actual.MetLevel,
            OriginalPlace = Place(actual),
            OriginalBall = hasBall ? Ball(actual) : "—",
            OriginalNature = Nat(actual),
            OriginalGender = Gen(actual),
            OriginalCaptureLevel = actual.MetLevel,
            IvsChanged = IvText(actual) != IvText(result),
            OriginalIvText = IvText(actual),
            NewIvText = IvText(result),
            Groups = groups,
            OriginalRibbons = actual.Format >= 5 ? RibbonCount(actual) : 0,
            Ribbons = actual.Format >= 5 ? RibbonCount(result) : 0,
            OriginalHandler = actual.Format >= 6 ? HandlerText(actual) : "—",
            Handler = actual.Format >= 6 ? HandlerText(result) : "—",
            OriginalAwakening = actual is PB7 a7 ? AwakeningVisualSum(a7) : 0,
            Awakening = result is PB7 r7 ? AwakeningVisualSum(r7) : 0,
            OriginalShiny = actual.IsShiny ? "★" : "—",
            Shiny = result.IsShiny ? "★" : "—",
        };
    }

    /// <summary>Copia SOLO los datos de origen del Pokémon verificado al destino.</summary>
    public static void CopyOrigin(PKM from, PKM to, RecreateGroups groups = RecreateGroups.None)
    {
        if (IsModern(from))
        {
            // Gen5+: el origen siempre; Bola, identidad, manejador/recuerdos y cintas solo si la opción los reemplazó
            // (si no, el destino ya los tiene y coinciden con lo verificado).
            CopyModernOrigin(from, to);
            CopyGroups(from, to, groups);
            if (to is PB7 lgpe) RaiseAwakenedToMinimum(lgpe);
            to.RefreshChecksum();
            return;
        }

        to.MetLocation = from.MetLocation;
        to.MetLevel = from.MetLevel;
        to.EggLocation = from.EggLocation;
        to.Ball = from.Ball;
        to.FatefulEncounter = from.FatefulEncounter;
        if (from.Format >= 3)
        {
            to.Version = from.Version;
            to.PID = from.PID;
            to.Gender = from.Gender;
            to.Ability = from.Ability;
            if (from.Format == 4) to.Nature = from.Nature;
        }
        if (from.Format >= 4)
        {
            if (from.MetDate is { } md) to.MetDate = md;
            if (from.EggMetDate is { } ed) to.EggMetDate = ed;
        }
        if (from is G4PKM g4 && to is G4PKM t4) t4.GroundTile = g4.GroundTile;
        if (from is PK2 p2 && to is PK2 t2) t2.MetTimeOfDay = p2.MetTimeOfDay;
        to.OriginalTrainerGender = from.OriginalTrainerGender;
        to.RefreshChecksum();
    }

    // ── PID inválido sin PID para los IVs actuales (Gen3/4) ─────────────────────────────────

    /// <summary>Cuando <see cref="Method1Solver"/> no encuentra ningún PID para los IVs actuales (esa
    /// combinación no puede existir en el encuentro), busca IVs lo más parecidos que SÍ tengan un PID
    /// válido, conservando brillante, habilidad y género. Primero intenta conservar también la Naturaleza.
    /// Cada candidato se verifica con LegalityAnalysis; devuelve hasta <paramref name="max"/> opciones.</summary>
    public static List<IvSearchOption> FindIvOptions(PKM current, GameStrings strings, int max = 12,
        CancellationToken ct = default)
    {
        var result = new List<IvSearchOption>();
        if (current.Format is not (3 or 4)) return result;

        var baseLa = new LegalityAnalysis(current);
        // Esta búsqueda solo sirve cuando lo único roto es la relación PID/IVs de un encuentro Method 1.
        // Con error de encuentro, o con origen de huevo (PIDType.None, nunca Method 1), no puede tener éxito
        // y recorría millones de semillas sin fin (CPU al 100 %): no se lanza. Ver EggOriginService.
        if (HasEncounterError(baseLa) || EggOriginService.IsEggEncounter(baseLa)) return result;

        // Tope de tiempo: si se alarga se devuelve lo que haya (nunca queda trabajando sin fin). El token del
        // llamador sí cancela de verdad.
        var callerCt = ct;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(BudgetSeconds * 1000);
        ct = budget.Token;

        int NonPid(LegalityAnalysis la) => la.Results.Count(r => !r.Valid && r.Judgement == Severity.Invalid && r.Identifier != CheckIdentifier.PID);
        var baseline = NonPid(baseLa);
        var seen = new HashSet<string>();

        string Nat(PKM p) => (int)p.Nature < strings.natures.Length ? strings.natures[(int)p.Nature] : "—";
        string Gen(PKM p) => p.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };

        // Por niveles: primero con 1 IV distinto, después 2 y recién al final 3 (el último es ~10 veces más
        // lento). En cada nivel se prefieren los PIDs que conservan la Naturaleza.
        try
        {
        for (var level = 1; level <= 3 && result.Count == 0; level++)
        {
            foreach (var keepNature in new[] { true, false })
            {
                var cands = Method1IvSearch.Find(current, keepNature, maxChanged: level, max: 80, ct: ct);
                foreach (var c in cands)
                {
                    ct.ThrowIfCancellationRequested();
                    if (result.Count >= max) break;
                    if (!seen.Add(c.IvText)) continue;
                    var t = current.Clone();
                    ApplyIvCandidate(current, t, c);
                    t.RefreshChecksum();
                    LegalityAnalysis la;
                    try { la = new LegalityAnalysis(t); } catch { continue; }
                    if (la.Results.Any(r => !r.Valid && r.Identifier == CheckIdentifier.PID)) continue;
                    if (NonPid(la) > baseline) continue;
                    result.Add(new IvSearchOption(c, Nat(t), Gen(t), Nat(current), IvText(current), t.Nature != current.Nature));
                }
                if (result.Count > 0) break;   // si alguna conserva la Naturaleza, no se mezclan las que no
            }
        }
        }
        catch (OperationCanceledException) when (!callerCt.IsCancellationRequested)
        {
            // Se agotó el tope de tiempo: se devuelve lo que se haya juntado.
        }
        return result;
    }

    // ── Gen1 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Gen1 no guarda datos de captura: PKHeX decide el encuentro solo con especie, nivel,
    /// movimientos y catch rate. Si el error de encuentro se debe a un nivel demasiado bajo, devuelve el
    /// nivel MÍNIMO con el que existe un encuentro válido (probando niveles y verificando cada uno).</summary>
    public static bool TryRaiseLevel(PK1 pk, out int level, out uint exp)
    {
        level = pk.CurrentLevel; exp = pk.EXP;
        var baseLa = new LegalityAnalysis(pk);
        if (!HasEncounterError(baseLa)) return false;
        var baseline = OtherErrors(baseLa);

        for (var lv = pk.CurrentLevel + 1; lv <= 100; lv++)
        {
            var trial = (PK1)pk.Clone();
            trial.CurrentLevel = (byte)lv;
            trial.Stat_Level = (byte)lv;
            trial.EXP = Experience.GetEXP((byte)lv, PersonalTable.RB[pk.Species].EXPGrowth);
            trial.ResetPartyStats();
            var la = new LegalityAnalysis(trial);
            if (HasEncounterError(la) || OtherErrors(la) > baseline) continue;
            level = lv; exp = trial.EXP;
            return true;
        }
        return false;
    }
}
