using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Punto de entrada único del diagnosticador de legalidad — junta todo lo construido en
/// la sesión de análisis (Etapas 3.1 a 3.3) en la lista final de <see cref="DiagnosticStep"/>
/// que consume la UI:
///
/// <list type="bullet">
/// <item><see cref="LegalityArgumentFormatter"/> — Nivel A, un <see cref="FieldFix"/> por
/// <see cref="CheckResult"/> individual cuando el argumento sirve.</item>
/// <item><see cref="LegalityEncounterFormatter"/> — Nivel B, ubicación e IVs fijas vía
/// <see cref="LegalityAnalysis.EncounterMatch"/>.</item>
/// <item><see cref="LegalityMoveFormatter"/> — sistema aparte, lee
/// <see cref="LegalInfo.Moves"/>/<c>.Relearn</c> en vez de <c>Results</c> (ver notas de la
/// sesión: los movimientos no generan <see cref="CheckResult"/> propio para "este
/// movimiento no es legal", solo para PP — por eso la categoría "Movimientos" a veces hay
/// que crearla desde cero acá, no alcanza con agrupar <c>Results</c>).</item>
/// <item><see cref="LegalityEvFormatter"/> — la suma de EVs, calculada por nosotros
/// mismos porque PKHeX no da ningún argumento para esos dos códigos.</item>
/// </list>
/// </summary>
public static class LegalityDiagnosticBuilder
{
    /// <summary>Lista vacía si <paramref name="analysis"/> ya es legal — nunca null,
    /// para que el caller no tenga que chequear dos formas distintas de "no hay nada que
    /// mostrar".</summary>
    public static List<DiagnosticStep> BuildDiagnosticSteps(LegalityAnalysis analysis, PKM pkm)
    {
        if (analysis.Valid)
            return [];

        var byCategory = new Dictionary<string, CategoryAccumulator>();

        // 1) Agrupar todo lo que venga de Results (CheckResult) — misma fuente que ya usa
        // LegalityMessageMapper.Summarize, pero acumulando por categoría en vez de una
        // lista plana + un blob técnico único, y sumando el intento de FieldFix por
        // cada resultado individual (Nivel A).
        foreach (var result in analysis.Results)
        {
            if (result.Valid)
                continue; // Valid de verdad — Invalid y Fishy los dos entran acá.

            var category = LegalityMessageMapper.GetCategory(result.Identifier);
            if (category is null)
                continue; // no debería pasar — identifier nuevo que la tabla no conoce.

            var acc = GetOrCreate(byCategory, category);
            acc.NoteSeverity(result.Judgement);

            var technical = LegalityHumanizer.Humanize(analysis, result);
            if (!string.IsNullOrWhiteSpace(technical))
                acc.TechnicalLines.Add(technical);

            var fix = LegalityArgumentFormatter.TryFormat(result, pkm);
            if (fix is not null)
                acc.Fixes.Add(fix);
        }

        // 2) Nivel B — ubicación e IVs fijas. Se ATTACHean a la categoría que ya debería
        // existir del paso 1 (los códigos IVNotCorrect / de ubicación SÍ generan
        // CheckResult real — EncounterMatch solo aporta precisión, nunca la categoría en
        // sí). Si por algún motivo no existiera (no debería pasar), se crea igual en vez
        // de perder el dato.
        //
        // Historia de este bloque (sesión post-4.6, caso Spiritomb — feedback correcto del
        // usuario): cuando la categoría "Ubicación"/"IVs/EVs" es nueva en este paso (no
        // venía ya del paso 1 con su propia severidad real, ej. porque el CheckResult real
        // cayó bajo "Origen del encuentro" y no bajo "Ubicación" específicamente),
        // CategoryAccumulator arranca en Fishy por default — dejando un FieldFix con
        // FieldId correcto en una ficha amarilla que HighlightedFieldIds ignora (solo mira
        // Invalid), campo nunca resaltado pese a tener sugerencia válida. Un primer intento
        // de arreglar esto forzaba Invalid a ciegas "porque la categoría es nueva" — mezcla
        // incorrecta de dos ejes independientes (Nivel A/B/C = certeza del VALOR a sugerir;
        // Severity = lo que PKHeX dictaminó de verdad). La versión de abajo lee el Judgement
        // real del CheckResult correspondiente en vez de inventarlo.
        foreach (var fix in LegalityEncounterFormatter.BuildEncounterFixes(analysis, pkm))
        {
            var category = fix.FieldId == "MetLocation" ? "Ubicación" : "IVs/EVs";
            var isNewCategory = !byCategory.ContainsKey(category);
            var acc = GetOrCreate(byCategory, category);
            acc.Fixes.Add(fix);

            if (isNewCategory)
            {
                // Corrección sobre el fix anterior (sesión post-4.6, feedback correcto del
                // usuario): antes esto forzaba Invalid a ciegas "porque la categoría es
                // nueva" — mezclando dos ejes independientes (Nivel A/B/C = qué tan seguros
                // estamos del VALOR a sugerir; Severity = lo que PKHeX dictaminó de
                // verdad). Ahora se lee el Judgement real del CheckResult correspondiente
                // en vez de inventarlo — puede ser Fishy legítimamente, no siempre Invalid.
                var severity = fix.FieldId == "MetLocation"
                    ? LegalityEncounterFormatter.GetGeographySeverity(analysis)
                    : LegalityEncounterFormatter.GetFixedIvSeverity(analysis);
                if (severity is { } s) acc.NoteSeverity(s);
            }
        }

        // 3) La suma de EVs (DerivedFromPkm) — misma categoría "IVs/EVs" que el resto.
        foreach (var fix in LegalityEvFormatter.BuildEvFixes(analysis, pkm))
            GetOrCreate(byCategory, "IVs/EVs").Fixes.Add(fix);

        // 4) Movimientos — el único caso donde la categoría puede no existir todavía
        // (MoveResult no pasa por Results, ver comentario de la clase). Si hay fixes acá
        // y la categoría "Movimientos" no se creó en el paso 1 (porque no hubo ningún
        // CheckResult de PP), se crea desde cero con severidad Invalid — un movimiento
        // ilegal siempre es Invalid, MoveResult no tiene un concepto de "Fishy".
        var moveFixes = LegalityMoveFormatter.BuildMoveFixes(analysis, pkm);
        if (moveFixes.Count > 0)
        {
            var acc = GetOrCreate(byCategory, "Movimientos");
            acc.NoteSeverity(Severity.Invalid);
            acc.Fixes.AddRange(moveFixes);
        }

        // 5) Convertir a DiagnosticStep, ordenados por LegalityMessageMapper.GetOrder.
        var steps = new List<DiagnosticStep>(byCategory.Count);
        foreach (var (category, acc) in byCategory)
        {
            steps.Add(new DiagnosticStep
            {
                Order = LegalityMessageMapper.GetOrder(category),
                Category = category,
                Severity = acc.WorstSeverity,
                Summary = LegalityMessageMapper.GetFriendlyText(category),
                TechnicalDetail = string.Join("\n", acc.TechnicalLines),
                Fixes = acc.Fixes,
            });
        }

        steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        return steps;
    }

    private static CategoryAccumulator GetOrCreate(Dictionary<string, CategoryAccumulator> dict, string category)
    {
        if (!dict.TryGetValue(category, out var acc))
            dict[category] = acc = new CategoryAccumulator();
        return acc;
    }

    /// <summary>Estado mutable mientras se arma un <see cref="DiagnosticStep"/> — se
    /// descarta apenas se convierte a records inmutables en el paso 5. No se expone
    /// fuera de este archivo, es puramente un scratch de armado.</summary>
    private sealed class CategoryAccumulator
    {
        // Invalid=-1, Fishy=0, Valid=1 en el enum de PKHeX — el número más CHICO es el
        // más grave, por eso arranca en Fishy (0) y solo baja a Invalid si aparece algo
        // realmente inválido. Nunca sube a Valid: si este acumulador existe es porque
        // por lo menos un resultado no fue válido.
        public Severity WorstSeverity { get; private set; } = Severity.Fishy;
        public List<string> TechnicalLines { get; } = [];
        public List<FieldFix> Fixes { get; } = [];

        public void NoteSeverity(Severity judgement)
        {
            if (judgement < WorstSeverity)
                WorstSeverity = judgement;
        }
    }
}
