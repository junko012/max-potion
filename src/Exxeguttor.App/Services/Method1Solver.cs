using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Paso 1 de "IVs editables en encuentros con PID ligado a los IVs" (Gen3/4, PIDType
/// Method_1): dado un Pokémon con los IVs YA editados, busca un PID que el LCRNG clásico
/// pueda haber generado junto con esos IVs exactos, y lo VERIFICA contra
/// <see cref="LegalityAnalysis"/> real antes de ofrecerlo (generate-and-verify) — en vez de
/// quedarse con el primer candidato matemático, que a veces cae en un frame inválido
/// (<c>EncConditionBadRNGFrame</c>, medido: 26 de 95 salvajes de Platino fallaban así).
///
/// Para un par de IVs hay solo unos pocos candidatos (≈4 de las 2^17 semillas posibles), así
/// que el costo es trivial: no hace falta modal ni cancelar. El orden de preferencia de los
/// candidatos intenta tocar lo menos posible del Pokémon:
///   1) Naturaleza + Género + Habilidad + Brillante intactos.
///   2) Género + Habilidad + Brillante intactos (la Naturaleza cambia: en Gen3/4 se deriva
///      del PID, así que no hay nada que registrar aparte).
///   3) Habilidad + Brillante intactos (el Género cambia: en Gen4 es un campo guardado, hay
///      que registrarlo como edición — ver <see cref="Result.GenderChanged"/>).
///   4) Solo Brillante intacto (la Habilidad pasa a la que dicta el PID — ver
///      <see cref="Result.NewAbility"/>; hay que registrarla como edición).
/// El Brillante nunca se cambia. Sin el paso 4 el éxito medido contra PKHeX real era de ≈65 %
/// en salvajes de Gen3/4 (la mitad de los candidatos tiene el bit de habilidad equivocado);
/// con él, ≈98 % en Gen3 y ≈87 % en Gen4 — el resto son IVs sin ningún frame válido.
/// Arreglar la Habilidad después con el otro botón (AbilityMismatchPID → RegeneratePid) NO
/// sirve: cambiaría el PID y volvería a romper la correlación con los IVs.
/// </summary>
public static class Method1Solver
{
    /// <param name="NewAbility">Id de habilidad que dicta el PID elegido; solo distinto de la
    /// actual cuando <see cref="AbilityChanged"/>.</param>
    public readonly record struct Result(
        uint Pid, bool NatureChanged, bool GenderChanged, bool AbilityChanged, int NewAbility);

    private const uint Mult = 0x41C64E6D;
    private const uint Add = 0x00006073;
    private const uint Inv = 0xEEB9EB65; // inverso modular de Mult (mod 2^32)

    private static uint Next(uint s) => unchecked(s * Mult + Add);
    private static uint Prev(uint s) => unchecked((s - Add) * Inv);

    /// <summary>PIDs que el LCRNG clásico (Method 1) generaría junto con los IVs actuales de
    /// <paramref name="pkm"/>. Pocos (≈4 en promedio).</summary>
    public static List<uint> Candidates(PKM pkm)
    {
        var iv1 = (uint)(pkm.IV_HP | (pkm.IV_ATK << 5) | (pkm.IV_DEF << 10));
        var iv2 = (uint)(pkm.IV_SPE | (pkm.IV_SPA << 5) | (pkm.IV_SPD << 10));
        var list = new List<uint>();
        for (var top = 0u; top <= 1u; top++)
        {
            var baseS3 = (top << 31) | (iv1 << 16);
            for (var low16 = 0u; low16 <= 0xFFFFu; low16++)
            {
                var s3 = baseS3 | low16;
                if (((Next(s3) >> 16) & 0x7FFF) != iv2) continue;
                var s2 = Prev(s3);
                var s1 = Prev(s2);
                list.Add(((s2 >> 16) << 16) | (s1 >> 16));
            }
        }
        return list;
    }

    private static int InvalidOutsidePid(LegalityAnalysis la) =>
        la.Results.Count(r => !r.Valid && r.Identifier != CheckIdentifier.PID);

    private static bool HasInvalidPid(LegalityAnalysis la) =>
        la.Results.Any(r => !r.Valid && r.Identifier == CheckIdentifier.PID);

    /// <summary><paramref name="edited"/> debe traer los IVs ya editados (el Pokémon con las
    /// ediciones pendientes aplicadas, NO el original del save).</summary>
    public static bool TrySolve(PKM edited, out Result result)
    {
        result = default;
        if (edited.Format is not (3 or 4)) return false;

        var baseline = InvalidOutsidePid(new LegalityAnalysis(edited));
        var wantGender = edited.Gender;
        var wantNature = edited.Nature;
        var wantShiny = edited.IsShiny;

        // Slot de habilidad actual (0/1) — -1 si no es ninguna de las dos normales.
        var currentAbility = edited.Ability;
        var slot = -1;
        var probe = edited.Clone();
        probe.RefreshAbility(0);
        if (probe.Ability == currentAbility) slot = 0;
        else
        {
            probe.RefreshAbility(1);
            if (probe.Ability == currentAbility) slot = 1;
        }

        var candidates = Candidates(edited);
        for (var pass = 1; pass <= 4; pass++)
        {
            foreach (var pid in candidates)
            {
                var abilityOk = slot == -1 || (int)(pid & 1) == slot;
                var genderOk = EntityGender.GetFromPID(edited.Species, pid) == wantGender;
                var natureOk = (Nature)(pid % 25) == wantNature;

                var accept = pass switch
                {
                    1 => abilityOk && genderOk && natureOk,
                    2 => abilityOk && genderOk,
                    3 => abilityOk,
                    _ => true,
                };
                if (!accept) continue;

                var trial = edited.Clone();
                trial.PID = pid;
                if (!genderOk) trial.Gender = EntityGender.GetFromPID(edited.Species, pid);
                if (!abilityOk) trial.RefreshAbility((int)(pid & 1));
                if (trial.IsShiny != wantShiny) continue;
                trial.RefreshChecksum();

                var la = new LegalityAnalysis(trial);
                if (HasInvalidPid(la)) continue;
                if (InvalidOutsidePid(la) > baseline) continue;

                result = new Result(pid, NatureChanged: !natureOk, GenderChanged: !genderOk,
                    AbilityChanged: !abilityOk, NewAbility: trial.Ability);
                return true;
            }
        }
        return false;
    }
}
