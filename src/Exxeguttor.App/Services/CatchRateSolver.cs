using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Corrección automática de los errores de catch rate de Gen1 (G1CatchRateItem / Evo / Chain / None).
/// Antes se usaba solo <see cref="CatchRateApplicator.GetSuggestedCatchRate"/>, y no alcanza: PKHeX
/// devuelve el catch rate de la especie (ej. Magmar → 45) incluso cuando ESE es el valor inválido.
/// Caso real (save Yellow): un Magmar Nv50 con Flamethrower y Confuse Ray, movimientos que Magmar solo
/// aprende por nivel en Gen2 → PKHeX lo trata como un Pokémon que pasó por Gen2 y exige que el catch
/// rate sea un objeto de Gen2; con 45 queda G1CatchRateItem y el botón "no hacía nada" porque sugería
/// otra vez 45. Con 0 (sin objeto) queda legal: 193 de los 256 valores lo son.
///
/// Por eso acá se prueban candidatos y se verifica cada uno contra LegalityAnalysis (mismo criterio
/// generate-and-verify que Method1Solver): el sugerido por PKHeX, 0 (se puede quitar el objeto antes
/// del tradeback) y el catch rate de la especie en Red/Blue y en Yellow; gana el primero con el que
/// ya no queda ningún error G1CatchRate*.
/// </summary>
public static class CatchRateSolver
{
    public static bool TrySolve(PK1 pk, SaveFile save, out byte rate)
    {
        rate = pk.CatchRate;

        var candidates = new List<byte>();
        void Add(int value)
        {
            var b = (byte)value;
            if (!candidates.Contains(b)) candidates.Add(b);
        }

        Add(CatchRateApplicator.GetSuggestedCatchRate(pk, save));
        Add(0);
        Add(PersonalTable.RB[pk.Species].CatchRate);
        Add(PersonalTable.Y[pk.Species].CatchRate);

        foreach (var candidate in candidates)
        {
            var trial = (PK1)pk.Clone();
            trial.CatchRate = candidate;
            if (HasInvalidCatchRate(new LegalityAnalysis(trial))) continue;
            rate = candidate;
            return true;
        }
        return false;
    }

    /// <summary>TODOS los catch rates candidatos con los que el Pokémon deja de tener error de catch rate,
    /// para ofrecerlos en el modal (el propuesto, 0 si sirve, va primero).</summary>
    public static List<byte> GetValidCandidates(PK1 pk, SaveFile save)
    {
        var all = new List<byte>();
        void Add(int v) { var b = (byte)v; if (!all.Contains(b)) all.Add(b); }
        Add(0);
        Add(CatchRateApplicator.GetSuggestedCatchRate(pk, save));
        Add(PersonalTable.RB[pk.Species].CatchRate);
        Add(PersonalTable.Y[pk.Species].CatchRate);
        var valid = new List<byte>();
        foreach (var c in all)
        {
            var trial = (PK1)pk.Clone();
            trial.CatchRate = c;
            if (!HasInvalidCatchRate(new LegalityAnalysis(trial))) valid.Add(c);
        }
        return valid;
    }

    public static bool HasInvalidCatchRate(LegalityAnalysis la) =>
        la.Results.Any(r => !r.Valid && r.Result is
            LegalityCheckResultCode.G1CatchRateItem or LegalityCheckResultCode.G1CatchRateEvo
            or LegalityCheckResultCode.G1CatchRateChain or LegalityCheckResultCode.G1CatchRateNone);
}
