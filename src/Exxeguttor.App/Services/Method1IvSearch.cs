using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>Un PID que el LCRNG clásico (Method 1) genera junto con unos IVs concretos.</summary>
public sealed record IvPidCandidate(uint Pid, int[] Ivs, int Distance, int Changed)
{
    /// <summary>"PS/Atq/Def/AtE/DeE/Vel" como "31/31/30/0/31/31".</summary>
    public string IvText => string.Join("/", Ivs);
}

/// <summary>
/// Búsqueda de IVs cercanos con un PID válido (Gen3/4, Method 1). Para unos IVs fijos solo existen ≈4 PIDs
/// posibles (<see cref="Method1Solver"/>), y casi nunca conservan la Naturaleza; y hay combinaciones de IVs
/// que no tienen ningún PID válido para el encuentro. Esta búsqueda recorre TODO el espacio de semillas
/// (2^32, en paralelo) y se queda con los PIDs que conservan lo que se pidió (brillante, habilidad,
/// género y, opcionalmente, Naturaleza) ordenados por cuánto se parecen sus IVs a los originales.
/// El llamador verifica cada candidato contra LegalityAnalysis (generate-and-verify).
/// </summary>
public static class Method1IvSearch
{
    private const uint Mult = 0x41C64E6D;
    private const uint Add = 0x00006073;
    private const uint Inv = 0xEEB9EB65; // inverso modular de Mult (mod 2^32)

    private static uint Next(uint s) => unchecked(s * Mult + Add);
    private static uint Prev(uint s) => unchecked((s - Add) * Inv);

    /// <summary>Deja un núcleo libre para que la ventana siga respondiendo mientras se busca.</summary>
    internal static int Workers => Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Busca de a poco: primero con a lo sumo 1 IV distinto, después 2 y recién al final 3 (el
    /// último nivel es unas 10 veces más lento). Devuelve el primer nivel que junta al menos
    /// <paramref name="enough"/> candidatos (o el último intentado).</summary>
    public static List<IvPidCandidate> FindProgressive(PKM pk, bool keepNature, int enough, int maxLevel = 3,
        int max = 200, CancellationToken ct = default)
    {
        var last = new List<IvPidCandidate>();
        for (var level = 1; level <= maxLevel; level++)
        {
            last = Find(pk, keepNature, maxChanged: level, max: max, ct: ct);
            if (last.Count >= enough) break;
        }
        return last;
    }

    /// <summary>Candidatos ordenados por cercanía de IVs (menos IVs cambiados primero, luego menor suma de
    /// diferencias), con a lo sumo <paramref name="maxChanged"/> IVs distintos de los actuales.
    /// En vez de recorrer las 2^32 semillas se recorren solo los IVs cercanos a los originales (PS/Atq/Def
    /// primero) y, para cada uno, las 2^17 semillas que los producen: con ≤2 cambios son ~400 millones de
    /// pasos (menos de un segundo) en vez de 4 300 millones.</summary>
    public static List<IvPidCandidate> Find(PKM pk, bool keepNature, bool keepGender = true, bool keepAbility = true,
        int maxChanged = 3, int max = 200, CancellationToken ct = default)
    {
        // Slot de habilidad actual (0/1), -1 si no es ninguna de las dos normales (mismo criterio que Method1Solver).
        var slot = -1;
        if (keepAbility)
        {
            var probe = pk.Clone();
            probe.RefreshAbility(0);
            if (probe.Ability == pk.Ability) slot = 0;
            else { probe.RefreshAbility(1); if (probe.Ability == pk.Ability) slot = 1; }
        }

        var genderRatio = pk.PersonalInfo.Gender;
        var wantGender = pk.Gender;
        var wantNature = (int)pk.Nature;
        var wantShiny = pk.IsShiny;
        var tsv = (uint)(pk.TID16 ^ pk.SID16);
        int oHp = pk.IV_HP, oAtk = pk.IV_ATK, oDef = pk.IV_DEF, oSpa = pk.IV_SPA, oSpd = pk.IV_SPD, oSpe = pk.IV_SPE;

        // Todas las combinaciones (PS, Atq, Def) con a lo sumo maxChanged valores distintos.
        var triples = new List<(int Hp, int Atk, int Def, int Changed)>();
        for (var hp = 0; hp < 32; hp++)
        {
            var c1 = hp != oHp ? 1 : 0;
            if (c1 > maxChanged) continue;
            for (var atk = 0; atk < 32; atk++)
            {
                var c2 = c1 + (atk != oAtk ? 1 : 0);
                if (c2 > maxChanged) continue;
                for (var def = 0; def < 32; def++)
                {
                    var c3 = c2 + (def != oDef ? 1 : 0);
                    if (c3 <= maxChanged) triples.Add((hp, atk, def, c3));
                }
            }
        }

        var bag = new System.Collections.Concurrent.ConcurrentBag<List<IvPidCandidate>>();
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Workers };

        Parallel.For(0, triples.Count, options, ti =>
        {
            var (hp, atk, def, changed1) = triples[ti];
            var iv1 = (uint)(hp | (atk << 5) | (def << 10));
            List<IvPidCandidate>? local = null;

            for (var top = 0u; top <= 1u; top++)
            {
                var baseS3 = (top << 31) | (iv1 << 16);
                for (var low = 0u; low <= 0xFFFFu; low++)
                {
                    var s3 = baseS3 | low;
                    var iv2 = (Next(s3) >> 16) & 0x7FFF;
                    var spe = (int)(iv2 & 31); var spa = (int)((iv2 >> 5) & 31); var spd = (int)((iv2 >> 10) & 31);
                    var changed = changed1 + (spe != oSpe ? 1 : 0) + (spa != oSpa ? 1 : 0) + (spd != oSpd ? 1 : 0);
                    if (changed > maxChanged) continue;

                    var s2 = Prev(s3);
                    var s1 = Prev(s2);
                    var pidLow = s1 >> 16;
                    var pidHigh = s2 >> 16;
                    var pid = (pidHigh << 16) | pidLow;

                    if (slot != -1 && (int)(pid & 1) != slot) continue;
                    if ((pidHigh ^ pidLow ^ tsv) < 8 != wantShiny) continue;
                    if (keepNature && (int)(pid % 25) != wantNature) continue;
                    if (keepGender && GenderOf(pid, genderRatio) != wantGender) continue;

                    var dist = Math.Abs(hp - oHp) + Math.Abs(atk - oAtk) + Math.Abs(def - oDef)
                             + Math.Abs(spa - oSpa) + Math.Abs(spd - oSpd) + Math.Abs(spe - oSpe);
                    (local ??= []).Add(new IvPidCandidate(pid, [hp, atk, def, spa, spd, spe], dist, changed));
                }
            }
            if (local != null)
            {
                local.Sort(Compare);
                if (local.Count > max) local.RemoveRange(max, local.Count - max);
                bag.Add(local);
            }
        });

        var all = bag.SelectMany(l => l).ToList();
        all.Sort(Compare);
        // Mismos IVs con PIDs distintos: se conservan hasta 3 por combinación (el llamador prueba los demás
        // solo si hace falta).
        var perIv = new Dictionary<string, int>();
        var result = new List<IvPidCandidate>();
        foreach (var c in all)
        {
            perIv.TryGetValue(c.IvText, out var n);
            if (n >= 3) continue;
            perIv[c.IvText] = n + 1;
            result.Add(c);
            if (result.Count >= max) break;
        }
        return result;
    }

    private static int Compare(IvPidCandidate a, IvPidCandidate b)
    {
        var c = a.Changed.CompareTo(b.Changed);
        if (c != 0) return c;
        c = a.Distance.CompareTo(b.Distance);
        return c != 0 ? c : a.Pid.CompareTo(b.Pid);
    }

    private static int GenderOf(uint pid, byte ratio) => ratio switch
    {
        255 => 2,
        254 => 1,
        0 => 0,
        _ => (pid & 0xFF) < ratio ? 1 : 0,
    };
}
