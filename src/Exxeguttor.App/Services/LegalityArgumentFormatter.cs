using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Traduce un <see cref="CheckResult"/> individual (ya confirmado <c>Invalid</c>/<c>Fishy</c>)
/// a un <see cref="FieldFix"/> accionable, cuando el código trae un valor que realmente sirve
/// para sugerir algo concreto — "Nivel A" en la sesión de análisis de legalidad: el dato sale
/// directo de <c>CheckResult.Value</c>/<c>.Argument</c>/<c>.Argument2</c>, sin necesidad de
/// cruzar contra <c>LegalityAnalysis.EncounterMatch</c> (eso es Nivel B, ver
/// LegalityEncounterFormatter — Etapa 3.3, todavía no escrita).
///
/// Devuelve <see langword="null"/> cuando el código no tiene un valor accionable — el caller
/// debe tratar eso como "Nivel C, mostrar solo la categoría", nunca como error.
///
/// IMPORTANTE sobre cómo se lee <see cref="CheckResult"/>: es un struct con layout explícito
/// (<c>[StructLayout(LayoutKind.Explicit)]</c>) donde <c>Value</c> (32 bits) y
/// <c>Argument</c>/<c>Argument2</c> (16+16 bits) comparten la MISMA memoria — es una unión,
/// no tres campos independientes. Para códigos de un solo argumento, <c>Value</c> y
/// <c>Argument</c> dan el mismo número (<c>Argument2</c> queda en 0). Para códigos de dos
/// argumentos empaquetados (sufijo <c>_01</c>), hay que leer <c>Argument</c> (mitad baja) y
/// <c>Argument2</c> (mitad alta) por separado — leer <c>Value</c> ahí da un número empaquetado
/// sin sentido. Confirmado leyendo CheckResult.cs y Verifier.GetInvalid en la fuente de
/// PKHeX.Core 25.11.7, no asumido por el nombre del código.
/// </summary>
public static class LegalityArgumentFormatter
{
    public static FieldFix? TryFormat(CheckResult result, PKM pkm)
    {
        var code = result.Result;

        // ── Exclusiones confirmadas por sesión de análisis — nunca representan un
        // arreglo real para ESTE diagnosticador, aunque el nombre del código sugiera lo
        // contrario. Ver notas de la sesión para el detalle de cada una. ───────────────
        if (code is LegalityCheckResultCode.HintEvolvesToSpecies_0
                 or LegalityCheckResultCode.HintEvolvesToRareForm_0)
            return null; // informativos — se disparan con GetValid(...), nunca un problema real.

        if (code is LegalityCheckResultCode.BulkAssignedMegaStoneNotFound_0)
            return null; // pertenece al análisis de TODO el save (DuplicateMegaChecker), no
                          // al LegalityAnalysis por Pokémon que usa este editor.

        if (code is LegalityCheckResultCode.MemoryArgBadSpecies_H1
                 or LegalityCheckResultCode.MemoryArgBadMove_H1
                 or LegalityCheckResultCode.MemoryArgBadItem_H1)
            return null; // el 2do argumento no trae un dato confiable en la fuente real de
                          // PKHeX (plantillas sin {1}, o raise site que nunca lo completa) —
                          // ver hallazgo de la sesión de análisis.

        if (code is LegalityCheckResultCode.IVAllEqual_0)
            return null; // siempre Fishy (ya es legal así) — no hay "arreglo" que ofrecer.

        if (code is LegalityCheckResultCode.EffortUntrainedCap_0)
            return null; // las EVs las define el jugador entrenando, no hay un valor de
                          // encuentro del que derivar una sugerencia.

        if (code is LegalityCheckResultCode.IVFlawlessCountGEQ_0)
            return null; // el caso con datos precisos (regalo/estático con IVs fijas) lo
                          // resuelve Nivel B vía IFixedIVSet (Etapa 3.3) con el valor EXACTO
                          // de cada stat — esto queda como red de respaldo genérica sin valor
                          // puntual que ofrecer (reglas generales tipo "N Ultra Bestias
                          // perfectas" sin encuentro identificable).

        if (code.IsMove())
            return FormatMoveArgument(code, result, pkm);

        if (code.IsLanguage())
            return FormatLanguageArgument(code, result, pkm);

        if (code.IsMemory())
            return FormatMemoryArgument(code, result, pkm);

        if (code.IsArgument())
            return FormatPlainArgument(code, result, pkm);

        // Nivel "Complejo" (>= FirstComplex en el enum) — cubierto caso por caso, no todos
        // los ~20 códigos de esta franja están mapeados todavía (ver comentario adentro).
        return FormatComplexArgument(code, result, pkm);
    }

    // ── IsMove(): el argumento es un ID de movimiento real — confirmado contra los raise
    // sites (MoveShopVerifier/MovePlusVerifier en HistoryVerifier.cs), específicos de
    // mecánicas de Legends Arceus/Z-A (maestría de movimientos). El editor no tiene un campo
    // de UI dedicado a "maestría de movimiento" verificado todavía (queda pendiente auditar
    // PokemonStatsView/tab Special) — por eso FieldId queda en el genérico "Moves" en vez de
    // apuntar a un control puntual; la sugerencia (nombre del movimiento) sigue siendo útil
    // en el tab Diagnóstico aunque todavía no resalte un campo específico. ───────────────
    private static FieldFix FormatMoveArgument(LegalityCheckResultCode code, CheckResult result, PKM pkm)
    {
        var moveId = result.Argument;
        var moveName = GameNames.MoveName(moveId) ?? PokemonDatabase.Instance.GetMove(moveId)?.Name ?? AppText.F("Legality_Unknown_Move", moveId);

        // PlusMoveSufficientLevelMissing_0 trae un segundo argumento (nivel requerido) —
        // el único de esta franja con dos datos en vez de uno.
        var suggestion = code is LegalityCheckResultCode.PlusMoveSufficientLevelMissing_0
            ? AppText.F("Legality_Fix_MoveReqLevel", moveName, result.Argument2)
            : moveName;

        return new FieldFix
        {
            FieldId = "Moves",
            CurrentValueDisplay = "—", // no hay un "valor actual" único que mostrar acá —
                                        // el problema es sobre un movimiento puntual, no
                                        // sobre reemplazar uno existente por otro.
            SuggestedValueDisplay = suggestion,
            SuggestedValueRaw = moveId,
            Source = FieldFixSource.DirectArgument,
        };
    }

    // ── IsLanguage(): el argumento es un LanguageID de PKHeX.Core. No usamos ningún
    // recurso de PKHeX (GameInfo.Strings no está inicializado en este proyecto todavía,
    // agregar esa inicialización sin poder compilar acá es más riesgo del que vale la
    // pena) — tabla propia, chica y estable (10 valores, no cambia entre versiones de
    // PKHeX hace años). ─────────────────────────────────────────────────────────────
    private static readonly HashSet<int> KnownLanguageIds = new() { 1, 2, 3, 4, 5, 7, 8, 9, 10 };

    // Nombre del idioma de juego en el idioma de la UI: mismas claves Trainer_Lang_<id> que el panel de Entrenador.
    private static string LanguageName(int id) =>
        KnownLanguageIds.Contains(id) ? AppText.T("Trainer_Lang_" + id) : AppText.F("Legality_Unknown_Language", id);

    private static FieldFix FormatLanguageArgument(LegalityCheckResultCode code, CheckResult result, PKM pkm)
    {
        // OTLanguageShouldBe_0or1 trae DOS idiomas posibles (Argument/Argument2) — el resto
        // de esta franja trae uno solo (Argument, Argument2 en 0).
        var suggestion = code is LegalityCheckResultCode.OTLanguageShouldBe_0or1
            ? AppText.F("Legality_Fix_Or", LanguageName(result.Argument), LanguageName(result.Argument2))
            : LanguageName(result.Argument);

        return new FieldFix
        {
            FieldId = "Language",
            CurrentValueDisplay = LanguageName(pkm.Language),
            SuggestedValueDisplay = suggestion,
            SuggestedValueRaw = (int)result.Argument,
            Source = FieldFixSource.DirectArgument,
        };
    }

    // ── IsMemory() (rango "_H", sin el "1" — los _H1 "bad X" ya se excluyeron arriba por
    // no traer datos confiables): el argumento es 0/1 indicando CUÁL entrenador (OT o
    // Handler) tiene el recuerdo con problema — nunca un valor a escribir, solo señala
    // dónde mirar. Se modela igual como FieldFix (con SuggestedValueRaw null) porque el
    // FieldId sigue siendo útil para resaltar la sección correcta en la UI, aunque no haya
    // un valor puntual que sugerir. ─────────────────────────────────────────────────────
    private static FieldFix FormatMemoryArgument(LegalityCheckResultCode code, CheckResult result, PKM pkm)
    {
        var isHandler = result.Argument == 1;
        return new FieldFix
        {
            FieldId = isHandler ? "Memory_Handler" : "Memory_OT",
            CurrentValueDisplay = "—",
            SuggestedValueDisplay = isHandler
                ? AppText.T("Legality_Fix_MemoryHandler")
                : AppText.T("Legality_Fix_MemoryOT"),
            SuggestedValueRaw = null,
            Source = FieldFixSource.DirectArgument,
        };
    }

    // ── IsArgument(): número crudo (cuenta, nivel, índice), sin traducción de ID a
    // nombre. Cada code necesita su propio FieldId + texto — no hay una regla genérica
    // única para toda la franja como sí la hay para Move/Language. Solo se cubren acá los
    // que tienen un campo Y un valor claro (ver sesión de análisis para por qué el resto
    // — IVAllEqual_0, IVFlawlessCountGEQ_0, EffortUntrainedCap_0 — ya se excluyeron
    // arriba en vez de forzarlos acá). ──────────────────────────────────────────────────
    private static FieldFix? FormatPlainArgument(LegalityCheckResultCode code, CheckResult result, PKM pkm)
    {
        switch (code)
        {
            case LegalityCheckResultCode.HyperTrainLevelGEQ_0:
                return new FieldFix
                {
                    FieldId = "Level",
                    CurrentValueDisplay = pkm.CurrentLevel.ToString(),
                    SuggestedValueDisplay = $"≥ {result.Argument}",
                    SuggestedValueRaw = (int)result.Argument,
                    Source = FieldFixSource.DirectArgument,
                };

            // El índice de marca SÍ es preciso (0-5), pero el valor válido es un RANGO
            // (0-3 en la mayoría de generaciones), no un único número — se sugiere el
            // rango en vez de inventar un valor puntual. Ver conversación: se decidió
            // sumarlo con sugerencia de rango en vez de dejarlo fuera del todo.
            case LegalityCheckResultCode.MarkValueOutOfRange_0:
                var markIndex = result.Argument;
                return new FieldFix
                {
                    FieldId = $"Marking_{markIndex}",
                    CurrentValueDisplay = AppText.T("Legality_Cur_OutOfRange"),
                    SuggestedValueDisplay = AppText.T("Legality_Fix_MarkRange"),
                    SuggestedValueRaw = null, // rango, no un valor único a escribir
                    Source = FieldFixSource.DirectArgument,
                };

            case LegalityCheckResultCode.MovePPTooHigh_0:
            case LegalityCheckResultCode.MovePPUpsTooHigh_0:
            {
                var slot = result.Argument; // 1-4, ver MovePPVerifier.cs (i + 1)
                var ppUps = code is LegalityCheckResultCode.MovePPUpsTooHigh_0;
                return new FieldFix
                {
                    FieldId = ppUps ? $"Move{slot}_PPUps" : $"Move{slot}_PP",
                    CurrentValueDisplay = AppText.T("Legality_Cur_TooHigh"),
                    SuggestedValueDisplay = ppUps ? AppText.T("Legality_Fix_LowerPpUps") : AppText.T("Legality_Fix_LowerPp"),
                    SuggestedValueRaw = null, // el tope exacto depende del move base PP, no
                                              // viene en el argumento — se resuelve mejor
                                              // recalculándolo en el momento de aplicar (v2)
                                              // que hardcodeando una fórmula acá.
                    Source = FieldFixSource.DirectArgument,
                };
            }

            case LegalityCheckResultCode.MovePPExpectHealed_0:
            {
                var slot = result.Argument;
                return new FieldFix
                {
                    FieldId = $"Move{slot}_PP",
                    CurrentValueDisplay = AppText.T("Legality_Cur_NotMaxPp"),
                    SuggestedValueDisplay = AppText.T("Legality_Fix_MaxPp"),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            // Se dispara desde NicknameVerifier (CheckIdentifier.Nickname) O
            // TrainerNameVerifier (CheckIdentifier.Trainer) según qué texto tenga
            // demasiados números — mismo código, campo distinto. result.Identifier ya
            // nos dice cuál sin tener que adivinar.
            case LegalityCheckResultCode.WordFilterTooManyNumbers_0:
            {
                var isTrainer = result.Identifier == CheckIdentifier.Trainer;
                return new FieldFix
                {
                    FieldId = isTrainer ? "OT" : "Nickname",
                    CurrentValueDisplay = isTrainer ? pkm.OriginalTrainerName : pkm.Nickname,
                    SuggestedValueDisplay = AppText.F("Legality_Fix_MaxNumbers", result.Argument),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            // Pokérus SÍ es un campo real del editor (PkrsActive, ver
            // PokemonEditorViewModel) — a diferencia de Contest Sheen, Form Argument o
            // las stats de LGPE (Fullness/Enjoyment), que se verificaron y NO existen
            // todavía en la UI (ver mensaje de la sesión: grep sin resultados en
            // PokemonEditorViewModel para esos términos). No son un "pendiente" — es una
            // ausencia confirmada, Nivel C es la respuesta correcta para esos, no una
            // que falte completar.
            case LegalityCheckResultCode.PokerusStrainUnobtainable_0:
                return new FieldFix
                {
                    FieldId = "Pokerus",
                    CurrentValueDisplay = AppText.T("Legality_Cur_PkrsStrain"),
                    SuggestedValueDisplay = AppText.T("Legality_Fix_PkrsStrain"),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };

            case LegalityCheckResultCode.PokerusDaysLEQ_0:
                return new FieldFix
                {
                    FieldId = "Pokerus",
                    CurrentValueDisplay = AppText.T("Legality_Cur_PkrsDays"),
                    SuggestedValueDisplay = AppText.F("Legality_Fix_PkrsDays", result.Argument),
                    SuggestedValueRaw = (int)result.Argument,
                    Source = FieldFixSource.DirectArgument,
                };

            default:
                // Verificado, no es un "todavía sin mirar": ContestSheenGEQ_0/LEQ_0,
                // FormArgumentLEQ_0/GEQ_0, MemoryStatFullness_0/LEQ_0/Enjoyment_0/
                // SocialLEQ_0/FriendshipOTBaseEvent_0, StatIncorrectCP_0 y
                // EggFMetLevel_0 no tienen NINGÚN campo correspondiente en el editor hoy
                // (Contest Stats, Form Argument, stats de LGPE, CP y Egg Met Level no
                // están implementados en la UI — confirmado buscando en
                // PokemonEditorViewModel, no asumido). Nivel C es la respuesta correcta
                // acá, no un hueco. EvoTradeReqOutsider_0 sí necesitaría traducir a
                // nombre de especie (result.Argument - 1u, offset +1 confirmado en
                // EvolutionVerifier) pero no corresponde a un único campo editable
                // puntual — también Nivel C. WordFilterTooManyNumbers_0 y Pokérus ya
                // están arriba.
                return null;
        }
    }

    // ── Nivel "Complejo" (>= FirstComplex): cada código con su propia lógica, mirroreando
    // LegalityLocalizationContext.GetComplex de PKHeX.Core como referencia, pero resolviendo
    // nombres contra nuestras propias DB (español) en vez de los recursos de PKHeX
    // (inglés/sin inicializar acá). Cobertura parcial — ver default. ───────────────────────
    /// <summary>Nombre de la cinta en el idioma de los datos (<c>GameInfo.Strings.Ribbons</c>, que la UI mantiene
    /// alineado); si PKHeX no lo tiene, el nombre del enum.</summary>
    private static string RibbonName(RibbonIndex index) =>
        GameInfo.Strings.Ribbons.GetNameSafe("Ribbon" + index, out var name) ? name : index.ToString();

    private static FieldFix? FormatComplexArgument(LegalityCheckResultCode code, CheckResult result, PKM pkm)
    {
        switch (code)
        {
            case LegalityCheckResultCode.EncTradeShouldHaveEvolvedToSpecies_0:
            case LegalityCheckResultCode.MoveEvoFCombination_0:
            {
                var speciesId = result.Argument;
                var speciesName = GameNames.SpeciesName(speciesId) ?? PokemonDatabase.Instance.GetSpecies(speciesId)?.Name ?? AppText.F("Legality_Unknown_Species", speciesId);
                return new FieldFix
                {
                    FieldId = "Species",
                    CurrentValueDisplay = GameNames.SpeciesName(pkm.Species) ?? PokemonDatabase.Instance.GetSpecies(pkm.Species)?.Name ?? pkm.Species.ToString(),
                    SuggestedValueDisplay = speciesName,
                    SuggestedValueRaw = (int)speciesId,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            // RibbonMarking*: el argumento es un índice dentro del enum RibbonIndex de
            // PKHeX — usamos su .ToString() (inglés, PascalCase, ej. "ChampionKalos") en
            // vez de un recurso de nombres localizados: GameInfo.Strings no está
            // inicializado en este proyecto y agregar esa inicialización sin poder
            // compilar acá es más riesgo del que vale la pena para esta primera pasada.
            // Sigue siendo información real y accionable, aunque no esté en español.
            case LegalityCheckResultCode.RibbonMarkingInvalid_0:
            case LegalityCheckResultCode.RibbonMarkingMissing_0:
            case LegalityCheckResultCode.RibbonMarkingAffixed_0:
            {
                var ribbonName = RibbonName((RibbonIndex)result.Argument);
                return new FieldFix
                {
                    FieldId = "Ribbons",
                    CurrentValueDisplay = "—",
                    SuggestedValueDisplay = code is LegalityCheckResultCode.RibbonMarkingMissing_0
                        ? AppText.F("Legality_Fix_RibbonMissing", ribbonName)
                        : AppText.F("Legality_Fix_RibbonReview", ribbonName),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            // RibbonsInvalid_0/RibbonsMissing_0: el CheckResult solo trae un CONTEO
            // (cuántas cintas faltan/sobran), no la lista de nombres — esa lista exige
            // llamar a RibbonVerifier.GetMessage(LegalityAnalysis, RibbonStrings, code),
            // que necesita el LegalityAnalysis completo (no solo este CheckResult) y un
            // RibbonStrings que tampoco está inicializado acá. Queda para una próxima
            // pasada — por ahora, descriptivo con el conteo, que sigue siendo más útil
            // que el mensaje genérico de categoría.
            case LegalityCheckResultCode.RibbonsInvalid_0:
            case LegalityCheckResultCode.RibbonsMissing_0:
                return new FieldFix
                {
                    FieldId = "Ribbons",
                    CurrentValueDisplay = "—",
                    SuggestedValueDisplay = code is LegalityCheckResultCode.RibbonsMissing_0
                        ? AppText.F("Legality_Fix_RibbonsMissingCount", result.Value)
                        : AppText.F("Legality_Fix_RibbonsInvalidCount", result.Value),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };

            // AwakenedStatGEQ_01 / GanbaruStatLEQ_01 (Legends Arceus — reemplazan IVs/EVs
            // para esa entrega, ver CategoryByIdentifier en LegalityMessageMapper: "GVs"/
            // "AVs" ya mapean a la misma categoría "IVs/EVs"). Reusamos la convención de
            // FieldId de IVs (IV_HP.._IV_SPE) asumiendo que la UI reusa el mismo control
            // de stat reinterpretado — supuesto razonable dado que comparten categoría,
            // pero no confirmado contra PokemonStatsView/tab Alpha todavía.
            case LegalityCheckResultCode.AwakenedStatGEQ_01:
            case LegalityCheckResultCode.GanbaruStatLEQ_01:
            {
                // OJO: NO es el orden estándar HP/Atk/Def/SpA/SpD/Spe. Confirmado en
                // AwakenedValueVerifier.cs — PKHeX usa acá la misma convención "Speed en
                // el índice 3" que IndividualValueSet (su propio comentario lo aclara:
                // "Speed is at index 3, not 5"). El real es 0=HP,1=Atk,2=Def,3=Speed,
                // 4=SpA,5=SpD — un primer borrador de este switch tenía 3=SpA/4=SpD/
                // 5=Spe, que hubiera sugerido el campo equivocado para 3 de las 6 stats.
                var statIndex = result.Argument2;
                var statField = statIndex switch
                {
                    0 => "IV_HP", 1 => "IV_ATK", 2 => "IV_DEF",
                    3 => "IV_SPE", 4 => "IV_SPA", 5 => "IV_SPD",
                    _ => "IV_HP",
                };
                var isMin = code is LegalityCheckResultCode.AwakenedStatGEQ_01;
                return new FieldFix
                {
                    FieldId = statField,
                    CurrentValueDisplay = AppText.T("Legality_Cur_OutOfRange"),
                    SuggestedValueDisplay = isMin ? $"≥ {result.Argument}" : $"≤ {result.Argument}",
                    SuggestedValueRaw = (int)result.Argument,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            // Etapa 4.6.4-fix — filtro de palabras (WordFilterVerifier en PKHeX.Core): el
            // texto de Apodo u OT contiene caracteres/patrones/exceso de números no
            // permitidos. Ninguno de los tres trae un "valor sugerido" real (PKHeX no
            // propone un reemplazo, solo dice "esto está mal") — Nivel C en cuanto a
            // sugerencia, pero SÍ tiene un campo puntual y responsable identificable
            // (result.Identifier distingue Apodo vs Entrenador), así que vale la pena
            // resaltar el campo aunque no podamos ofrecer un valor concreto. Antes cualquiera
            // de estos tres caía al default (Nivel C sin campo), que es probablemente el caso
            // más común de "apodo ilegal" en la práctica — un apodo con texto real (no vacío)
            // que el filtro de palabras marca, no un apodo vacío como se podría asumir.
            // Etapa post-4.6 (2) — el apodo/nombre de entrenador puede ser texto perfectamente
            // válido y aun así ser ilegal: no coincide con el nombre de especie por defecto en
            // ningún idioma válido para el origen de este Pokémon (o el flag IsNicknamed no
            // coincide con esa comparación). El fix real es tildar/destildar el checkbox
            // IsNicknamedFlag al lado del campo, no editar el texto — pero apuntamos el
            // resaltado al campo "Nickname" igual (mismo lugar donde ya funciona el badge) en
            // vez de "IsNicknamedFlag", porque el checkbox es chico y no tiene margen para un
            // badge propio; el usuario de todas formas tiene el checkbox justo al lado.
            //
            // Solo NickMatchLanguageFail está confirmado con un caso real (Hippowdon,
            // reportado en sesión). Hay otros códigos "Nick*"/"OT*" de la misma familia en
            // PKHeX que probablemente necesiten el mismo tratamiento (ver
            // NicknameVerifier.cs) pero no los agrego a ciegas — si aparece otro caso real,
            // pasame el CheckResult exacto y lo sumo acá mismo.
            case LegalityCheckResultCode.NickMatchLanguageFail:
            {
                var isTrainerNick = result.Identifier == CheckIdentifier.Trainer;
                var fieldId = isTrainerNick ? "OT" : "Nickname";
                var currentText = isTrainerNick ? pkm.OriginalTrainerName : pkm.Nickname;
                return new FieldFix
                {
                    FieldId = fieldId,
                    CurrentValueDisplay = currentText,
                    SuggestedValueDisplay = AppText.T("Legality_Fix_NickLanguage"),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            case LegalityCheckResultCode.WordFilterFlaggedPattern_01:
            case LegalityCheckResultCode.WordFilterInvalidCharacter_0:
            case LegalityCheckResultCode.WordFilterTooManyNumbers_0:
            {
                var isTrainerText = result.Identifier == CheckIdentifier.Trainer;
                var fieldId = isTrainerText ? "OT" : "Nickname";
                var currentText = isTrainerText ? pkm.OriginalTrainerName : pkm.Nickname;
                return new FieldFix
                {
                    FieldId = fieldId,
                    CurrentValueDisplay = currentText,
                    SuggestedValueDisplay = AppText.T("Legality_Fix_WordFilter"),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                };
            }

            // Etapa 4.6.4-fix (2) — mismo mecanismo de PID que "Datos internos", pero acá
            // apunta puntualmente a la Habilidad: el bit más bajo del PID selecciona entre
            // las dos habilidades normales de la especie (Gen3-5; en Gen6+ la habilidad ya
            // no se deriva del PID en la mayoría de los casos, pero PKHeX sigue corriendo
            // este chequeo para los formatos donde sí aplica). Mismo criterio que ya usa
            // PokemonService.BuildPokemon para generar un Pokémon nuevo:
            // pkm.RefreshAbility((int)(pkm.PID & 1)) — acá lo corremos sobre un clon para
            // no tocar el PKM real, solo para leer qué nombre de habilidad resultaría.
            case LegalityCheckResultCode.AbilityMismatchPID:
            {
                var strings = GameInfo.Strings;
                var currentAbilityName = pkm.Ability > 0 && pkm.Ability < strings.Ability.Count
                    ? strings.Ability[pkm.Ability] : pkm.Ability.ToString();

                string suggestedAbilityName;
                try
                {
                    var clone = pkm.Clone();
                    clone.RefreshAbility((int)(pkm.PID & 1));
                    suggestedAbilityName = clone.Ability > 0 && clone.Ability < strings.Ability.Count
                        ? strings.Ability[clone.Ability] : clone.Ability.ToString();
                }
                catch
                {
                    // Si RefreshAbility no está soportado para este tipo de PKM puntual,
                    // preferimos un mensaje genérico a tirar una excepción hacia arriba.
                    suggestedAbilityName = AppText.T("Legality_Fix_AbilityManual");
                }

                return new FieldFix
                {
                    FieldId = "Ability",
                    CurrentValueDisplay = currentAbilityName,
                    SuggestedValueDisplay = suggestedAbilityName,
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                    Action = FieldFixAction.RegeneratePid,
                };
            }

            // Post-4.6 (2) — Method 1 (Gen3/4, encuentros estáticos: Drifloon/Spiritomb
            // confirmados con casos reales). El PID no tiene la estructura matemática que
            // el LCRNG clásico produciría junto con las IVs actuales del Pokémon — a
            // diferencia de AbilityMismatchPID (donde CUALQUIER PID al azar que dé la
            // habilidad correcta sirve), acá hace falta un PID generado por el algoritmo
            // real del juego para esas IVs puntuales. Ver
            // PokemonService.TryRegeneratePidMethod1 para el algoritmo (verificado
            // matemáticamente, no adivinado) y por qué Naturaleza/Habilidad/Género pueden
            // terminar cambiando como efecto secundario.
            case LegalityCheckResultCode.PIDTypeMismatch:
                // Method 1 solo aplica a Pokémon de Gen3/4 (LCRNG clásico). Medido contra
                // PKHeX 25.11.07 real: en Gen8 el fix de Method 1 no resolvió NINGUNO de 261
                // casos (esos encuentros —estáticos de mundo abierto, raids, dens— derivan PID e
                // IVs de una semilla Xoroshiro), así que ahí NO se ofrece el botón: antes lo
                // mostraba para cualquier generación y al tocarlo el mensaje seguía igual.
                if (pkm.Format is 3 or 4)
                {
                    // Huevo eclosionado de Gen3/Gen4 (Met level 0): el PID NO puede seguir el patrón de Method 1 —
                    // PKHeX espera uno sin correlación. El botón es el mismo; el editor elige el solver de huevo
                    // al confirmar el encuentro (EggOriginService), acá solo cambia el texto.
                    var eggOrigin = EggOriginService.IsSupported(pkm) && pkm.MetLevel == 0;
                    return new FieldFix
                    {
                        FieldId = "PID",
                        CurrentValueDisplay = AppText.F("Legality_Cur_Pid", pkm.PID.ToString("X8")),
                        SuggestedValueDisplay = AppText.T(eggOrigin ? "Legality_Fix_PidEgg" : "Legality_Fix_PidMethod1"),
                        SuggestedValueRaw = null,
                        Source = FieldFixSource.DirectArgument,
                        Action = FieldFixAction.RegeneratePidMethod1,
                    };
                }

                // Gen8/9: la relación PID/IVs sale de una semilla del juego. El editor ofrece buscar una
                // semilla que produzca los IVs pedidos (IvSeedSolver); si no encontró un plan para este
                // Pokémon el botón no hace nada.
                // Solo PK8/PK9 (Espada/Escudo, Escarlata/Púrpura): el solver no cubre Legends Arceus (PA8) ni
                // Legends Z-A (PA9), y ahí el botón no haría nada.
                if (pkm is PK8 or PK9)
                {
                    return new FieldFix
                    {
                        FieldId = "PID",
                        CurrentValueDisplay = AppText.F("Legality_Cur_Pid", pkm.PID.ToString("X8")),
                        SuggestedValueDisplay = AppText.T("Legality_Fix_PidSeed"),
                        SuggestedValueRaw = null,
                        Source = FieldFixSource.DirectArgument,
                        Action = FieldFixAction.SearchIvSeed,
                    };
                }

                return new FieldFix
                {
                    FieldId = "PID",
                    CurrentValueDisplay = AppText.F("Legality_Cur_Pid", pkm.PID.ToString("X8")),
                    SuggestedValueDisplay = AppText.T("Legality_Fix_PidSeedLocked"),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DirectArgument,
                    Action = FieldFixAction.None,
                };

            // Bug real encontrado con un Magmar en un save Yellow: PokemonService.
            // BuildPokemon nunca seteaba CatchRate en su rama de fallback (blanco armado
            // campo por campo) — quedaba en 0, válido SOLO si el Pokémon visitó Gen2
            // (tradeback real), inválido para cualquier Pokémon nacido y capturado en
            // Gen1 sin más vueltas (ver MiscG1Verifier/GBRestrictions de PKHeX.Core).
            // Value/Argument/Argument2 vienen en 0 para este código (es "Misc", no trae
            // ningún dato accionable de PKHeX) — el valor sugerido lo calculamos nosotros
            // aparte (Source = DerivedFromPkm, no DirectArgument). Tampoco se calcula acá
            // mismo porque depende de qué juego puntual (Red/Blue vs Yellow) trae el save
            // cargado — PK1.Version siempre devuelve el genérico GameVersion.RBY, así que
            // hace falta el SaveFile activo (PokemonService.RequireSave), no disponible
            // en este formatter puramente de texto. El cálculo real ocurre recién al
            // tocar "Corregir automáticamente" (PokemonService.TryRecalculateCatchRate).
            case LegalityCheckResultCode.G1CatchRateItem:
            case LegalityCheckResultCode.G1CatchRateEvo:
            // Chain y None son los otros dos códigos inválidos de la misma comprobación (MiscVerifier.
            // VerifyMiscG1CatchRate): el rate no coincide con ninguna especie de la cadena evolutiva ni con un
            // objeto de Gen2. Antes no tenían botón. Medido contra PKHeX 25.11.07 (Raichu con rate 190,
            // el de Pikachu → G1CatchRateNone): CatchRateApplicator.GetSuggestedCatchRate propone un rate
            // con el que el Pokémon queda legal, igual que para Item/Evo.
            case LegalityCheckResultCode.G1CatchRateChain:
            case LegalityCheckResultCode.G1CatchRateNone:
            {
                return new FieldFix
                {
                    FieldId = "CatchRate",
                    CurrentValueDisplay = pkm is PK1 pk1 ? pk1.CatchRate.ToString() : "?",
                    SuggestedValueDisplay = AppText.T("Legality_Fix_CatchRate"),
                    SuggestedValueRaw = null,
                    Source = FieldFixSource.DerivedFromPkm,
                    Action = FieldFixAction.RecalculateCatchRate,
                };
            }

            default:
                // Resto de la franja "Complejo" (OTLanguageCannotTransferToConsoleRegion_0,
                // StoredSlotSourceInvalid_0, FormInvalidRangeLEQ_0F,
                // G6SuperTrainBagHitsInvalid_012) — no mapeados todavía, caen a Nivel C.
                return null;
        }
    }
}
