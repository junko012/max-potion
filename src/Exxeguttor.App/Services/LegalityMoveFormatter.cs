using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Traduce los resultados de <see cref="LegalityAnalysis.Info"/> (<c>Moves</c>/
/// <c>Relearn</c>, arrays de <see cref="MoveResult"/> de tamaño fijo — 1 por slot 1-4) a
/// <see cref="FieldFix"/> accionables.
///
/// Este es un sistema TOTALMENTE APARTE del <c>CheckResult</c>/<c>Results</c> que cubre
/// <see cref="LegalityArgumentFormatter"/> — los movimientos no se verifican como el resto
/// de los campos, tienen su propia estructura paralela indexada por slot. Por eso vive en
/// su propio archivo con su propia entrada pública, en vez de ser un caso más del switch de
/// <see cref="LegalityArgumentFormatter"/>.
///
/// La ventaja real de este sistema frente al resto: el índice del array da el slot exacto
/// sin ambigüedad (a diferencia de, por ejemplo, la suma de EVs), y
/// <see cref="MoveResult.Expect"/> a veces ya trae el ID del movimiento que debería ir en
/// ese slot en vez del actual — cuando está disponible, es la sugerencia más precisa de
/// todo el diagnosticador.
/// </summary>
public static class LegalityMoveFormatter
{
    /// <summary>Un <see cref="FieldFix"/> por cada slot inválido, de Moves y de Relearn —
    /// puede devolver una lista vacía si los 8 slots (4+4) son válidos.
    /// <paramref name="pkm"/> se pasa explícito porque <c>LegalityAnalysis.Entity</c> es
    /// <c>internal</c> en PKHeX.Core — no accesible desde este ensamblado (confirmado al
    /// intentar compilar mentalmente esto, no asumido).</summary>
    public static List<FieldFix> BuildMoveFixes(LegalityAnalysis analysis, PKM pkm)
    {
        var fixes = new List<FieldFix>();
        CollectSlots(analysis.Info.Moves, isRelearn: false, pkm, fixes);
        CollectSlots(analysis.Info.Relearn, isRelearn: true, pkm, fixes);
        return fixes;
    }

    private static void CollectSlots(MoveResult[] slots, bool isRelearn, PKM pkm, List<FieldFix> fixes)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.Valid)
                continue;

            var slotNumber = i + 1; // 1-4, coincide con la convención Move1.._4 ya usada
                                     // para los FieldId de PP en LegalityArgumentFormatter.
            var fieldId = isRelearn ? $"RelearnMove{slotNumber}" : $"Move{slotNumber}";

            var currentMoveId = isRelearn ? pkm.GetRelearnMove(i) : pkm.GetMove(i);
            var currentName = currentMoveId == 0
                ? AppText.T("Legality_Cur_EmptyMove")
                : GameNames.MoveName(currentMoveId) ?? PokemonDatabase.Instance.GetMove(currentMoveId)?.Name ?? AppText.F("Legality_Unknown_Move", currentMoveId);

            string suggestion;
            object? raw;

            // HasExpectedMove() es la única condición donde PKHeX garantiza que Expect
            // trae un ID real (LearnMethod.UnobtainableExpect) — confirmado en
            // LearnMethod.cs. En cualquier otro método inválido, Expect queda en su
            // default (0) y no hay que intentar resolverlo a nombre.
            if (slot.Info.Method.HasExpectedMove() && slot.Expect != 0)
            {
                suggestion = GameNames.MoveName(slot.Expect) ?? PokemonDatabase.Instance.GetMove(slot.Expect)?.Name ?? AppText.F("Legality_Unknown_Move", slot.Expect);
                raw = (int)slot.Expect;
            }
            else
            {
                // Sin un reemplazo puntual conocible — igual describe el motivo real en
                // vez de un genérico "movimiento inválido", usando LearnMethod (ver
                // LearnMethod.cs de PKHeX.Core para la lista completa de métodos
                // "Invalid").
                suggestion = slot.Info.Method switch
                {
                    LearnMethod.Duplicate => AppText.T("Legality_Move_Duplicate"),
                    LearnMethod.EmptyInvalid => AppText.T("Legality_Move_EmptyInvalid"),
                    LearnMethod.Unobtainable => AppText.T("Legality_Move_Unobtainable"),
                    _ => AppText.T("Legality_Move_Invalid"),
                };
                raw = null;
            }

            fixes.Add(new FieldFix
            {
                FieldId = fieldId,
                CurrentValueDisplay = currentName,
                SuggestedValueDisplay = suggestion,
                SuggestedValueRaw = raw,
                Source = FieldFixSource.MoveResultExpect,
            });
        }
    }
}
