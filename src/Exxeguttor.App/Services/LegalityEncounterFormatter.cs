using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// "Nivel B" de la sesión de análisis de legalidad — cuando el <see cref="CheckResult"/>
/// no trae el valor correcto (ej. <c>IVNotCorrect</c>, sin argumento), pero
/// <see cref="LegalityAnalysis.EncounterMatch"/> sí lo sabe, porque es la plantilla de
/// encuentro real contra la que PKHeX comparó al Pokémon. Cubre los dos casos confirmados
/// en la sesión de análisis — ubicación (<see cref="ILocation"/>) e IVs fijas de un
/// regalo/estático (<see cref="IFixedIVSet"/>) — ambos con el mismo patrón: una interfaz
/// chica que algunas plantillas de encuentro implementan y otras no, con su propio
/// <c>IsSpecified</c>/valor-0 para no asumir que el dato siempre está.
/// </summary>
public static class LegalityEncounterFormatter
{
    /// <summary><paramref name="pkm"/> se pasa explícito por el mismo motivo que en
    /// <see cref="LegalityMoveFormatter"/>: <c>LegalityAnalysis.Entity</c> es
    /// <c>internal</c> en PKHeX.Core, no accesible desde este ensamblado.
    /// <see cref="LegalityAnalysis.EncounterMatch"/> sí es público, por eso este método
    /// puede leerlo directo de <paramref name="analysis"/>.</summary>
    public static List<FieldFix> BuildEncounterFixes(LegalityAnalysis analysis, PKM pkm)
    {
        var fixes = new List<FieldFix>();
        var enc = analysis.EncounterMatch;

        // Nivel B: solo se dispara con CheckIdentifier.Geography, la categoría "Ubicación"
        // específica. Se probó ampliar esto también a CheckIdentifier.Encounter (para casos
        // como "Origen del encuentro" en Spiritomb — ver sesión post-4.6), pero PKHeX
        // reempareja a una plantilla de encuentro distinta según qué campos se van editando,
        // y en encuentros complejos (ritual/trade) terminaba mostrando "sugerido: Route 209"
        // siendo Route 209 el valor YA puesto — una ficha roja "bloqueante" sugiriendo un
        // valor que ya está bien, más confuso que útil. Revertido: para esos casos más
        // enredados es más honesto dejar la categoría sin campo resaltado (Nivel C) que una
        // sugerencia que puede estar pisándose con el propio re-análisis en vivo.
        if (HasIssue(analysis, CheckIdentifier.Geography, out _))
            TryAddLocationFix(enc, pkm, fixes);

        if (HasIssue(analysis, CheckIdentifier.IVs, out _))
            TryAddFixedIvFixes(enc, pkm, fixes);

        return fixes;
    }

    /// <summary>Para que LegalityDiagnosticBuilder sepa qué Severity ponerle a una categoría
    /// que recién está creando en este paso (Ubicación/IVs vía EncounterMatch) — sin
    /// inventarla, leyéndola del Judgement real de PKHeX para ese identifier. Null si no hay
    /// ningún problema para ese identifier (no debería llamarse en ese caso, pero evita un
    /// valor por default engañoso si se llama de todos modos).</summary>
    public static Severity? GetGeographySeverity(LegalityAnalysis analysis) =>
        HasIssue(analysis, CheckIdentifier.Geography, out var judgement) ? judgement : null;

    public static Severity? GetFixedIvSeverity(LegalityAnalysis analysis) =>
        HasIssue(analysis, CheckIdentifier.IVs, out var judgement) ? judgement : null;

    private static bool HasIssue(LegalityAnalysis analysis, CheckIdentifier identifier, out Severity judgement)
    {
        // Devuelve también el Judgement real de PKHeX para ese identifier — necesario para
        // que el caller (LegalityDiagnosticBuilder) sepa qué severidad ponerle a una
        // categoría nueva SIN inventarla (bug real encontrado: antes se forzaba Invalid a
        // ciegas "porque la categoría es nueva", mezclando dos ejes que son independientes—
        // Nivel A/B/C es qué tan seguros estamos del valor a sugerir, Severity es lo que
        // PKHeX dictaminó de verdad). Si hay más de un CheckResult para el mismo identifier,
        // Invalid gana sobre Fishy (mismo criterio de escalada que ya usa
        // CategoryAccumulator.NoteSeverity).
        judgement = Severity.Fishy;
        var found = false;
        foreach (var r in analysis.Results)
        {
            if (r.Valid || r.Identifier != identifier) continue;
            found = true;
            if (r.Judgement == Severity.Invalid) judgement = Severity.Invalid;
        }
        return found;
    }

    private static void TryAddLocationFix(IEncounterable enc, PKM pkm, List<FieldFix> fixes)
    {
        // El lado "actual" siempre se puede mostrar, tenga o no la plantilla un ILocation
        // — GameInfo.GetLocationList (mismo mecanismo que ya usa PokemonEditorViewModel para
        // el combobox, Etapa 1) porque acá partimos de un ID crudo (pkm.MetLocation).
        var currentName = GameInfo.GetLocationList(pkm.Version, pkm.Context, egg: false)
            .FirstOrDefault(l => l.Value == pkm.MetLocation)?.Text ?? pkm.MetLocation.ToString();

        if (enc is ILocation loc)
        {
            var suggestedId = loc.GetLocation();
            if (suggestedId != 0)
            {
                // GetEncounterLocation ya devuelve el nombre resuelto (no un ID crudo) —
                // evita tener que pasar por GameInfo.GetLocationList para el lado "sugerido".
                var suggestedName = loc.GetEncounterLocation(pkm.Format, pkm.Version) ?? AppText.F("Legality_Unknown_Location", suggestedId);
                fixes.Add(new FieldFix
                {
                    FieldId = "MetLocation",
                    CurrentValueDisplay = currentName,
                    SuggestedValueDisplay = suggestedName,
                    SuggestedValueRaw = suggestedId,
                    Source = FieldFixSource.EncounterMatch,
                });
                return;
            }
        }

        // Etapa 4.6.4-fix — la plantilla de encuentro NO trae una ubicación única fija
        // (típico de un encuentro salvaje: cualquier ubicación válida de esa ruta/zona
        // sirve, PKHeX no propone UNA respuesta correcta). Antes esto simplemente no
        // generaba ningún FieldFix — la categoría "Ubicación" aparecía como ficha pero el
        // campo nunca se resaltaba, que es probablemente el caso más común al editar a
        // mano el combo de Location. Sin un valor puntual que sugerir, pero el campo
        // responsable del problema sigue siendo identificable, así que vale la pena
        // resaltarlo igual.
        fixes.Add(new FieldFix
        {
            FieldId = "MetLocation",
            CurrentValueDisplay = currentName,
            SuggestedValueDisplay = AppText.T("Legality_Fix_NoEncounter"),
            SuggestedValueRaw = null,
            Source = FieldFixSource.EncounterMatch,
        });
    }

    private static void TryAddFixedIvFixes(IEncounterable enc, PKM pkm, List<FieldFix> fixes)
    {
        if (enc is not IFixedIVSet ivSet)
            return;
        if (!ivSet.IVs.IsSpecified)
            return; // el conjunto entero no tiene IVs fijas — nada que sugerir.

        // Se usan las propiedades CON NOMBRE (HP/ATK/DEF/SPA/SPD/SPE) en vez de un
        // indexador o un Span posicional a propósito: tanto IndividualValueSet[int] como
        // PKM.GetIVs(Span<int>) usan la convención "Speed en el índice 3" de PKHeX (no
        // el orden estándar) — confirmado leyendo la fuente línea por línea después de
        // encontrar el mismo problema ya escrito en LegalityEvFormatter. Usar las
        // propiedades con nombre en los dos lados (plantilla y PKM) elimina esa fuente
        // de error por completo, no hay ningún índice que se pueda confundir.
        AddIfMismatch("IV_HP",  ivSet.IVs.HP,  pkm.IV_HP,  fixes);
        AddIfMismatch("IV_ATK", ivSet.IVs.ATK, pkm.IV_ATK, fixes);
        AddIfMismatch("IV_DEF", ivSet.IVs.DEF, pkm.IV_DEF, fixes);
        AddIfMismatch("IV_SPA", ivSet.IVs.SPA, pkm.IV_SPA, fixes);
        AddIfMismatch("IV_SPD", ivSet.IVs.SPD, pkm.IV_SPD, fixes);
        AddIfMismatch("IV_SPE", ivSet.IVs.SPE, pkm.IV_SPE, fixes);
    }

    private static void AddIfMismatch(string fieldId, sbyte fixedValue, int currentValue, List<FieldFix> fixes)
    {
        if (fixedValue < 0)
            return; // -1 en ESTA stat puntual = "random" — el set entero puede estar
                     // IsSpecified con solo algunas stats fijas y el resto libres.
        if (fixedValue == currentValue)
            return; // ya coincide, no hace falta sugerir nada para esta stat.

        fixes.Add(new FieldFix
        {
            FieldId = fieldId,
            CurrentValueDisplay = currentValue.ToString(),
            SuggestedValueDisplay = fixedValue.ToString(),
            SuggestedValueRaw = (int)fixedValue,
            Source = FieldFixSource.EncounterMatch,
        });
    }
}
