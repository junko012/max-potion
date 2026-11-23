using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Cubre los dos códigos de EVs donde PKHeX no da absolutamente ningún dato en el
/// <see cref="CheckResult"/> — confirmado en EffortValueVerifier.cs:
///
/// <code>
/// if (sum > EffortValues.Max510)
///     data.AddLine(GetInvalid(EffortAbove510));   // sin argumento
/// </code>
///
/// Ni el total, ni cuánto te pasaste, ni qué stat es la culpable del tope de 252 por
/// separado — nada. No hace falta que PKHeX nos lo dé: tenemos acceso directo al propio
/// PKM, así que lo calculamos nosotros mismos leyendo <c>pkm.EVs</c>. Es la única fuente
/// de <see cref="FieldFix"/> en todo el diagnosticador que no depende de ningún dato que
/// exponga PKHeX (ver <see cref="FieldFixSource.DerivedFromPkm"/>).
/// </summary>
public static class LegalityEvFormatter
{
    // OJO con el orden: NO es el estándar HP/Atk/Def/SpA/SpD/Spe. PKM.GetEVs (y también
    // GetIVs) usan acá la misma convención "Speed en el índice 3" que
    // IndividualValueSet — confirmado leyendo PKM.cs línea por línea, no asumido. Un
    // primer borrador de este archivo tenía el orden estándar acá, que hubiera señalado
    // Ataque Especial cuando el problema real era de Velocidad (y viceversa con SpD).
    private static readonly string[] StatSuffixes = ["HP", "ATK", "DEF", "SPE", "SPA", "SPD"];

    /// <summary>Devuelve 0, 1, o varios <see cref="FieldFix"/> según si el Pokémon tiene
    /// el total de EVs pasado (<c>EffortAbove510</c>), alguna EV individual pasada de 252
    /// (<c>EffortAbove252</c>), o ninguno de los dos — se chequea contra
    /// <paramref name="analysis"/> antes de calcular nada, para no hacer el trabajo de
    /// leer las 6 EVs si no hace falta.</summary>
    public static List<FieldFix> BuildEvFixes(LegalityAnalysis analysis, PKM pkm)
    {
        var fixes = new List<FieldFix>();

        var hasTotalIssue = false;
        var hasPerStatIssue = false;
        foreach (var r in analysis.Results)
        {
            if (r.Valid) continue;
            if (r.Result == LegalityCheckResultCode.EffortAbove510) hasTotalIssue = true;
            else if (r.Result == LegalityCheckResultCode.EffortAbove252) hasPerStatIssue = true;
        }

        if (!hasTotalIssue && !hasPerStatIssue)
            return fixes;

        Span<int> evs = stackalloc int[6];
        pkm.GetEVs(evs);

        if (hasTotalIssue)
        {
            var total = 0;
            foreach (var v in evs) total += v;

            fixes.Add(new FieldFix
            {
                FieldId = "EV_TOTAL",
                CurrentValueDisplay = $"{total} / {EffortValues.Max510}",
                // No hay UN valor único que sugerir acá — el jugador decide de qué stat
                // sacar los puntos de sobra. SuggestedValueRaw queda null a propósito:
                // no hay nada auto-aplicable en la v2 para este caso puntual.
                SuggestedValueDisplay = AppText.F("Legality_Fix_EvTotal", total - EffortValues.Max510),
                SuggestedValueRaw = null,
                Source = FieldFixSource.DerivedFromPkm,
            });
        }

        if (hasPerStatIssue)
        {
            for (int i = 0; i < evs.Length; i++)
            {
                if (evs[i] <= EffortValues.Max252) continue;

                fixes.Add(new FieldFix
                {
                    FieldId = $"EV_{StatSuffixes[i]}",
                    CurrentValueDisplay = evs[i].ToString(),
                    SuggestedValueDisplay = $"≤ {EffortValues.Max252}",
                    SuggestedValueRaw = (int)EffortValues.Max252,
                    Source = FieldFixSource.DerivedFromPkm,
                });
            }
        }

        return fixes;
    }
}
