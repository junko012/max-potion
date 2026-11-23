using PKHeX.Core;
using System.Linq;

namespace Exxeguttor.App.Services;

/// <summary>
/// Servicio para leer y editar Pokémon individuales dentro de un save.
/// </summary>
public class PokemonService
{
    private readonly SaveFileService _saveService;

    public PokemonService(SaveFileService saveService)
    {
        _saveService = saveService;
    }

    /// <summary>
    /// Obtiene todos los Pokémon del equipo activo (máximo 6).
    /// </summary>
    public IReadOnlyList<PKM> GetParty()
    {
        var save = RequireSave();
        return save.PartyData.ToList();
    }

    /// <summary>
    /// Obtiene todos los Pokémon de una caja específica.
    /// </summary>
    public IReadOnlyList<PKM> GetBox(int boxIndex)
    {
        var save = RequireSave();

        if (boxIndex < 0 || boxIndex >= save.BoxCount)
            throw new ArgumentOutOfRangeException(nameof(boxIndex),
                $"Box index {boxIndex} is out of range (0–{save.BoxCount - 1}).");

        var box = new List<PKM>();
        for (int slot = 0; slot < save.BoxSlotCount; slot++)
            box.Add(save.GetBoxSlotAtIndex(boxIndex, slot));

        return box;
    }

    /// <summary>
    /// Obtiene el nombre de cada caja.
    /// Usa nombre genérico "Box N" hasta conectar la API correcta en la UI.
    /// </summary>
    public IReadOnlyList<string> GetBoxNames()
    {
        var save = RequireSave();
        var names = new List<string>();
        for (int i = 0; i < save.BoxCount; i++)
            names.Add($"Box {i + 1}");
        return names;
    }

    /// <summary>
    /// Verifica la legalidad de un Pokémon.
    /// Retorna true si es legal según las reglas de PKHeX.Core.
    /// </summary>
    public (bool IsLegal, string[] Issues) CheckLegality(PKM pkm)
    {
        var analysis = new LegalityAnalysis(pkm);
        // CheckResult.Comment (string) ya no existe en 25.11.7, pero CheckResult sigue
        // sobrecargando ToString() para dar el mensaje legible — confirmado contra una
        // referencia de trabajo ya compilada en 25.11.7, no hace falta tocar ninguna
        // propiedad puntual (.Result/.Code) para esto.
        var issues = analysis.Results
            .Where(r => !r.Valid)
            .Select(r => r.ToString())
            .ToArray();
        return (analysis.Valid, issues);
    }

    /// <summary>
    /// Igual que <see cref="CheckLegality"/> pero categorizado y con mensajes en español
    /// entendibles para el usuario (ver <see cref="LegalityMessageMapper"/>) — el detalle
    /// técnico crudo de PKHeX.Core sigue disponible en <c>LegalitySummary.TechnicalDetail</c>,
    /// solo que no es lo primero que se muestra. Usado por el badge de legalidad del panel
    /// principal (bajo el sprite) y por el modal de revisión antes de exportar.
    /// </summary>
    public LegalitySummary GetLegalitySummary(PKM pkm)
    {
        var analysis = new LegalityAnalysis(pkm);
        return LegalityMessageMapper.Summarize(analysis);
    }

    /// <summary>Etapa 4.6.3 — mismo análisis que GetLegalitySummary de arriba, pero como
    /// DiagnosticStep completos (fichas de categoría + FieldFix accionables) en vez del
    /// resumen de una línea. Usado en la carga inicial del editor (LoadLegalityAsync) para
    /// que las fichas/el resaltado de campo ya aparezcan al abrir un Pokémon ilegal, sin
    /// esperar a la primera edición — el recálculo en vivo (EditSessionService.
    /// BuildLiveDiagnosticSteps) toma la posta después, sobre el preview con ediciones.</summary>
    public List<DiagnosticStep> GetLegalityDiagnosticSteps(PKM pkm)
    {
        var analysis = new LegalityAnalysis(pkm);
        return LegalityDiagnosticBuilder.BuildDiagnosticSteps(analysis, pkm);
    }

    /// <summary>Post-4.6 — primer botón de "corregir automáticamente" del tab Diagnóstico
    /// (FieldFixAction.RegeneratePid). Busca un PID nuevo que satisfaga el Género/
    /// Naturaleza(Gen3-5)/slot de Habilidad/Brillante que el Pokémon YA tiene — NUNCA al
    /// revés (no tocamos esos campos para acomodar un PID al azar; el usuario ya los
    /// eligió a propósito). Mismo criterio que BuildPokemon usa para crear un Pokémon
    /// nuevo (RefreshAbility, EntityGender.GetFromPID), aplicado "hacia atrás" acá.
    ///
    /// El slot de habilidad actual (0 o 1) no se lee de ninguna tabla — se DEDUCE
    /// probando sobre un clon: si RefreshAbility(0) reproduce la Ability actual, el slot
    /// es 0; si no, se prueba con 1. Evita depender de acceder a PersonalInfo para esto
    /// puntual. Si ninguna de las dos reproduce la Ability actual (no debería pasar para
    /// el caso AbilityMismatchPID, que por definición es una de las dos), se aborta sin
    /// tocar nada en vez de adivinar.</summary>
    public bool TryRegeneratePid(PKM pkm, int generation, out uint newPid)
        => TryFindPidFor(pkm, generation, pkm.IsShiny, out newPid);

    /// <summary>Etapa "campos huérfanos del pipeline de escritura" — mismo mecanismo que
    /// TryRegeneratePid (sorteo de PID conservando Género/Naturaleza(Gen3-5)/slot de
    /// Habilidad), pero con el Brillante como parámetro en vez de leerlo del PKM actual.
    /// Hace falta este camino separado porque <see cref="PKM.IsShiny"/> es get-only
    /// (confirmado en PKHeX.Core/PKM/PKM.cs: "public virtual bool IsShiny => TSV == PSV")
    /// — no existe ningún setter directo, así que tildar/destildar el checkbox Shiny de la
    /// UI solo puede aplicarse de verdad buscando un PID nuevo que produzca el estado
    /// deseado. Usado por EditApplyService al aplicar un edit de "IsShiny" pendiente.</summary>
    public bool TrySetShiny(PKM pkm, int generation, bool wantShiny, out uint newPid)
        => TryFindPidFor(pkm, generation, wantShiny, out newPid);

    /// <summary>Botón "Corregir automáticamente" para G1CatchRateItem/G1CatchRateEvo (tab
    /// Diagnóstico). Reusa <see cref="CatchRateApplicator.GetSuggestedCatchRate"/> — el
    /// mismo helper que ya trae PKHeX.Core y que usa su propio editor oficial — en vez de
    /// reimplementar la lógica de tradeback/pre-evolución a mano. Necesita el SaveFile
    /// activo (RequireSave) porque el rate correcto depende de si el juego es Red/Blue o
    /// Yellow — PK1.Version siempre devuelve el genérico GameVersion.RBY, no distingue
    /// entre las tres, así que no alcanza con mirar el propio PKM.</summary>
    public bool TryRecalculateCatchRate(PKM pkm, out byte newCatchRate)
    {
        newCatchRate = 0;
        if (pkm is not PK1 pk1) return false;

        var save = RequireSave();
        // Prueba candidatos y verifica cada uno (ver CatchRateSolver): el rate que sugiere PKHeX no
        // siempre sirve (a veces es el mismo valor inválido que el Pokémon ya tiene).
        return CatchRateSolver.TrySolve(pk1, save, out newCatchRate);
    }

    /// <summary>Bug real encontrado con un save Gen4: la primera versión de este método
    /// sorteaba los 32 bits del PID de una sola vez y exigía las 4 restricciones juntas
    /// (slot de habilidad ~1/2, naturaleza ~1/25, género ~1/2, brillante ~1/8192) en el
    /// MISMO intento — probabilidad conjunta ≈ 1/819.200 por sorteo. Con el tope de
    /// 200.000 intentos que tenía antes, la chance de NO encontrar nada en Gen3-5 rondaba
    /// el 78% (200.000/819.200 ≈ 0,24 éxitos esperados) — el checkbox Shiny se aplicaba
    /// "en silencio" la mayoría de las veces, sin ningún error visible, exactamente el
    /// síntoma reportado. En Gen6+ (sin restricción de habilidad/naturaleza/género, esas
    /// tres se determinan por otro mecanismo) la única restricción es brillante
    /// (~1/8192), así que ahí 200.000 intentos casi siempre alcanzaban — por eso el bug
    /// solo se notó al probar en un save de Gen3-5.
    ///
    /// Fix: separar el sorteo en dos mitades de 16 bits. La mitad baja (`lo`) se sortea al
    /// azar por intento externo; para ESA mitad baja fija, se recorren de forma
    /// SISTEMÁTICA (no al azar) las 65.536 combinaciones posibles de la mitad alta
    /// (`hi`) — evaluando ability/nature/gender sobre el PID de 32 bits completo en cada
    /// una (el % 25 de la naturaleza mezcla ambas mitades, así que no se puede fijar
    /// nature de antemano solo con `lo`). Recorrer las 65.536 combinaciones de `hi` para
    /// un mismo `lo` cubre ~1-e^-8 ≈ 99,97% de probabilidad de toparse con brillante al
    /// menos una vez, siempre que ese `lo` en particular deje suficientes candidatos que
    /// cumplan ability/nature/gender (statísticamente, con las probabilidades de arriba,
    /// alcanza con unas pocas decenas de mitades bajas distintas en el peor caso). Reusa
    /// un único clon (mutando .PID en cada intento) en vez de clonar por intento — antes
    /// clonaba hasta 200.000 veces, ahora clona 1 vez y muta el mismo objeto.</summary>
    private static bool TryFindPidFor(PKM pkm, int generation, bool wantShiny, out uint newPid)
    {
        newPid = pkm.PID;

        var currentAbility = pkm.Ability;

        int wantAbilitySlot = -1;
        if (generation is >= 3 and <= 5)
        {
            var abilityProbe = pkm.Clone();
            abilityProbe.RefreshAbility(0);
            if (abilityProbe.Ability == currentAbility) wantAbilitySlot = 0;
            else
            {
                abilityProbe.RefreshAbility(1);
                if (abilityProbe.Ability == currentAbility) wantAbilitySlot = 1;
            }
            if (wantAbilitySlot == -1) return false; // no se pudo determinar, no adivinamos
        }

        var wantGender = pkm.Gender;
        var wantNature = pkm.Nature;
        var rng = Random.Shared;
        var clone = pkm.Clone();

        // Tope de "mitades bajas" distintas a probar — cada una ya recorre las 65.536
        // combinaciones de mitad alta por dentro, así que esto es 4000*65536 ≈ 262M
        // iteraciones en el peor caso absoluto (nunca alcanzado en la práctica: el éxito
        // llega, en promedio, dentro de las primeras ~10-30 mitades bajas para Gen3-5, y
        // en la primera para Gen6+/Gen1-2 sin restricciones extra).
        for (var outer = 0; outer < 4000; outer++)
        {
            var lo = (uint)rng.Next(0, 65536);
            for (var hi = 0u; hi < 65536; hi++)
            {
                var candidate = (hi << 16) | lo;

                if (generation is >= 3 and <= 5)
                {
                    if ((int)(candidate & 1) != wantAbilitySlot) continue;
                    if ((Nature)(candidate % 25) != wantNature) continue;
                    if (EntityGender.GetFromPID(pkm.Species, candidate) != wantGender) continue;
                }

                clone.PID = candidate;
                if (clone.IsShiny != wantShiny) continue;

                newPid = candidate;
                return true;
            }
        }

        return false; // combinación estadísticamente casi imposible incluso con la
                       // búsqueda sistemática (no debería pasar en la práctica) — no nos
                       // colgamos buscando para siempre.
    }

    // ── LCRNG clásica (Gen3/4, "PokeRNG") — constantes verificadas contra el código fuente
    // real de PKHeX.Core (ClassicEraRNG), y el inverso modular calculado y comprobado acá
    // mismo (mult*inv mod 2^32 = 1), no de memoria. ──────────────────────────────────────
    private const uint LcrngMult = 0x41C64E6D;
    private const uint LcrngAdd  = 0x00006073;
    private const uint LcrngInv  = 0xEEB9EB65; // inverso modular de LcrngMult

    private static uint LcrngNext(uint seed) => unchecked(seed * LcrngMult + LcrngAdd);
    private static uint LcrngPrev(uint seed) => unchecked((seed - LcrngAdd) * LcrngInv);

    /// <summary>Post-4.6 (2) — corrige PIDTypeMismatch (Method 1, Gen3/4: encuentros
    /// estáticos donde el PID y las IVs salen de la MISMA semilla del LCRNG clásico, en
    /// cadena — Drifloon/Spiritomb confirmados con casos reales). A diferencia de
    /// TryFindPidFor/TryRegeneratePid (que buscan un PID libre manteniendo Género/
    /// Naturaleza/Habilidad/Brillante), acá la relación es al revés: la semilla determina
    /// el PID Y las IVs juntos, así que NO se pueden fijar las IVs Y esos otros cuatro
    /// campos a la vez — hay que elegir. Esta función mantiene las IVs actuales (las que
    /// el usuario ya tiene, generalmente más valiosas de preservar) y el PID resultante
    /// puede traer una Naturaleza/Habilidad/Género distintos — se intenta primero
    /// encontrar una semilla candidata que TAMBIÉN coincida con el estado actual (mejor
    /// caso), pero si ninguna de las candidatas lo logra, se usa la primera válida igual,
    /// avisando el cambio.
    ///
    /// Algoritmo verificado matemáticamente antes de portarlo acá (generé un caso de
    /// prueba con semilla conocida, "la olvidé", y la reconstruí solo a partir de sus IVs
    /// — dio exactamente la semilla original entre los candidatos encontrados; repetido
    /// con 20 semillas al azar más, 20/20 reconstruyeron el PID original entre los
    /// candidatos).</summary>
    public bool TryRegeneratePidMethod1(PKM pkm, out uint newPid, out bool changedNatureAbilityGender)
    {
        newPid = pkm.PID;
        changedNatureAbilityGender = false;

        var hp = pkm.IV_HP; var atk = pkm.IV_ATK; var def = pkm.IV_DEF;
        var spa = pkm.IV_SPA; var spd = pkm.IV_SPD; var spe = pkm.IV_SPE;
        var iv1Target = (uint)(hp | (atk << 5) | (def << 10));
        var iv2Target = (uint)(spe | (spa << 5) | (spd << 10));

        var wantGender = pkm.Gender;
        var wantNature = pkm.Nature;
        var currentAbility = pkm.Ability;
        int wantAbilitySlot = -1;
        var probe = pkm.Clone();
        probe.RefreshAbility(0);
        if (probe.Ability == currentAbility) wantAbilitySlot = 0;
        else
        {
            probe.RefreshAbility(1);
            if (probe.Ability == currentAbility) wantAbilitySlot = 1;
        }
        var wantShiny = pkm.IsShiny;

        uint? fallbackPid = null;

        // Hasta 131072 candidatos (17 bits libres: el bit más alto de la semilla S3 más
        // sus 16 bits bajos — los únicos 15 bits del medio quedan fijados por iv1Target).
        // Rápido de sobra para un click de UI (decenas de miles de multiplicaciones).
        for (var topBit = 0u; topBit <= 1u; topBit++)
        {
            var baseS3 = (topBit << 31) | (iv1Target << 16);
            for (var low16 = 0u; low16 <= 0xFFFFu; low16++)
            {
                var s3 = baseS3 | low16;
                var s4 = LcrngNext(s3);
                if (((s4 >> 16) & 0x7FFF) != iv2Target) continue;

                var s2 = LcrngPrev(s3);
                var s1 = LcrngPrev(s2);
                var candidatePid = ((s2 >> 16) << 16) | (s1 >> 16);

                fallbackPid ??= candidatePid;

                var probe2 = pkm.Clone();
                probe2.PID = candidatePid;
                var natureOk = wantAbilitySlot == -1 || (Nature)(candidatePid % 25) == wantNature;
                var abilityOk = wantAbilitySlot == -1 || (int)(candidatePid & 1) == wantAbilitySlot;
                var genderOk = EntityGender.GetFromPID(pkm.Species, candidatePid) == wantGender;
                var shinyOk = probe2.IsShiny == wantShiny;

                if (natureOk && abilityOk && genderOk && shinyOk)
                {
                    newPid = candidatePid;
                    changedNatureAbilityGender = false;
                    return true;
                }
            }
        }

        if (fallbackPid is { } fb)
        {
            newPid = fb;
            changedNatureAbilityGender = true;
            return true;
        }

        return false; // no debería pasar nunca en la práctica (siempre hay al menos un
                       // candidato válido para un par de IVs cualquiera), pero preferimos
                       // no tocar nada antes que aplicar un PID a medio calcular.
    }

    /// <summary>Nivel con el que se crea cualquier Pokémon nuevo — acordado con el usuario
    /// (no hay "default" de PKHeX para esto, es una decisión de producto).</summary>
    private const int CreationLevel = 5;

    /// <summary>
    /// Construye un Pokémon nuevo (solo se elige especie) — EN MEMORIA, sin tocar el
    /// SaveFile todavía. Antes esta operación insertaba directo con
    /// SetBoxSlotAtIndex/SetPartySlotAtIndex, pero eso rompía el flujo ya definido: todas las
    /// ediciones del usuario (incluida crear un Pokémon) quedan pendientes hasta que se
    /// confirman una por una con el check del modal de revisión al exportar — recién ahí se
    /// escribe sobre el SaveFile real. Ver EditSessionService (RecordPendingCreation) para
    /// cómo se trackea esto mientras tanto, y CLAUDE.md/context.md para el resto del patrón
    /// de escritura pendiente (todavía no implementado para el resto del editor).
    ///
    /// Decisiones de producto ya acordadas (no hay "default" de PKHeX para ninguna de estas):
    /// Nivel 5 · IVs 31 en las 6 · EVs 0 · Habilidad slot 0 (primera no oculta) ·
    /// Moveset = últimos 4 movimientos de levelup hasta el nivel de creación ·
    /// Gen 3-5: PID al azar, Naturaleza/Género se derivan solos del PID al leerlos (formatos
    /// donde son campos calculados), Habilidad se resincroniza con el bit del PID vía
    /// RefreshAbility · Gen 6+: PID al azar sin restricción, Naturaleza/Habilidad/Género
    /// random e independientes entre sí (Género vía GenderRate de nuestra propia DB, sin
    /// depender de ninguna clase de género de PKHeX.Core para esto puntual).
    /// Sin PKHeX.Core.AutoMod (paquete aparte, decisión de no sumar esa dependencia).
    ///
    /// <paramref name="form"/> (Fase 2 de formas, default 0 = comportamiento de siempre): forma
    /// a crear, pensada para las formas regionales de <see cref="RegionalFormCatalog"/> (Marowak
    /// de Alola, Meowth de Galar...). Se propaga a la búsqueda de encuentros (plantilla) y a
    /// TryEvolveForward; con form != 0 se descartan los candidatos que no terminen en esa
    /// forma. Con form == 0 no se agrega ningún filtro nuevo (no cambia nada para las
    /// especies con formas cosméticas al azar, tipo Unown/Vivillon).
    /// </summary>
    public PKM BuildPokemon(int speciesId, byte form = 0)
    {
        var save = RequireSave();
        var db   = PokemonDatabase.Instance;

        // Intento 1: usar un encuentro legítimo real (huevo, estático/regalo o salvaje) de
        // este juego puntual, vía los generadores que ya trae PKHeX.Core — evita que el
        // Pokémon creado salga siempre ilegal en "Origen del encuentro"
        // (CheckIdentifier.Encounter/Fateful/Language/Gender/etc., ver sesión de legalidad
        // amigable), que es lo que pasa si se arma todo a mano sin que matchee ninguna
        // plantilla real. Ver TryBuildFromLegitEncounter — sin AutoMod, esto vive en el
        // propio PKHeX.Core, confirmado por reflection antes de escribir esto.
        var pkm = TryBuildFromLegitEncounter(save, speciesId, form);

        // Forma 0 sin ningún origen legal pero con otra forma que sí lo tiene: se crea esa forma
        // en vez de un Pokémon de forma 0 ilegal. Caso real en Legends: Z-A: Zygarde solo existe
        // como el jefe de la misión (forma 2, Poder Construcción 10%-C); pedir la forma 0 caía a un
        // regalo de Gen7 que PKHeX marca "TransferBad" para Z-A. La forma queda editable en el
        // combo Forma, así que el usuario puede cambiarla igual. Solo se activa cuando el
        // resultado de forma 0 NO es legal, o sea que no cambia nada para las especies que ya
        // salían bien.
        if (form == 0 && (pkm == null || !IsFreeOfInvalidChecks(pkm)))
        {
            var alt = TryBuildOtherLegalForm(save, speciesId);
            if (alt != null) pkm = alt;
        }
        // Red de seguridad de formato: dentro de la misma generación el encuentro puede venir
        // en OTRO formato que el que guarda el save (caso real: Leyendas Z-A guarda PA9, pero un
        // encuentro de Escarlata/Púrpura sale como PK9 — ambos Format 9, así que el chequeo
        // "built.Format < save.Generation" de más arriba no los convierte, y el save lanza
        // "PKM Format needs to be PA9" al exportar). Se convierte; si no se puede, se descarta y
        // cae al PKM en blanco del formato correcto.
        if (pkm != null && pkm.GetType() != save.PKMType)
        {
            pkm = EntityConverter.TryMakePKMCompatible(pkm, save.BlankPKM, out var fmtResult, out var fmtConverted)
                  && fmtResult.IsSuccess() && fmtConverted != null && fmtConverted.GetType() == save.PKMType
                ? fmtConverted
                : null;
        }
        var isFallback = pkm == null; // se pierde el "pkm == null" original al reasignar
                                       // pkm abajo — este flag es lo que distingue, más
                                       // adelante, qué post-procesos son "solo para blanco
                                       // armado a mano" vs. cosas que ya trae bien un
                                       // encuentro real (moveset, apodo — ver bugs de
                                       // CatchRate/Nivel/Apodo más abajo, todos con el mismo
                                       // patrón de raíz).

        if (pkm == null)
        {
            // Fallback: comportamiento de siempre, PKM en blanco armado campo por campo. Pasa
            // cuando no hay ninguna plantilla real para esta especie en este juego puntual
            // (ej. legendarios/míticos exclusivos de otra versión, o cualquier caso que
            // TryBuildFromLegitEncounter no supo resolver) — sigue quedando ilegal en esa
            // categoría, pero ahora es la excepción, no la regla.
            pkm = EntityBlank.GetBlank(save);
            pkm.Species  = (ushort)speciesId;
            pkm.Form     = form;   // antes de RefreshAbility/Gender: ambos dependen de la forma
            pkm.MetLevel = (byte)CreationLevel;
            pkm.Version  = save.Version;

            // Entrenador — copiado del save actual. Sin esto el Pokémon queda "de otro
            // entrenador" (mismo problema que dispararía una advertencia de legalidad al tiro).
            pkm.Language              = save.Language;
            pkm.OriginalTrainerName   = save.OT;
            pkm.TID16                 = save.TID16;
            pkm.SID16                 = save.SID16;
            pkm.OriginalTrainerGender = save.Gender;

            // PID al azar — en Gen 3-5 Naturaleza se computa sola a partir de este PID al
            // leerla (campo calculado en esos formatos); en Gen 6+ es independiente y se
            // randomiza por separado abajo.
            var rng = Random.Shared;
            pkm.PID = (uint)rng.NextInt64(uint.MinValue, (long)uint.MaxValue + 1);

            if (save.Generation <= 5)
            {
                // El slot de habilidad en estos formatos lo determina el bit bajo del PID —
                // RefreshAbility resincroniza el campo Ability real con ese bit.
                pkm.RefreshAbility((int)(pkm.PID & 1));

                // Gender no se deriva solo — hay que setearlo con la misma fórmula que usa
                // PKHeX internamente (EntityGender.GetFromPID), confirmada por reflection.
                pkm.Gender = EntityGender.GetFromPID((ushort)speciesId, pkm.PID);
            }
            else
            {
                pkm.Nature = (Nature)rng.Next(25);

                var dbSpeciesFallback = db.GetSpecies(speciesId);
                var abilityCount = dbSpeciesFallback?.Abilities.Count(a => !a.IsHidden) ?? 1;
                pkm.RefreshAbility(rng.Next(Math.Max(1, abilityCount)));

                var genderRate = db.GetSpeciesGenderRate(speciesId);
                pkm.Gender = ResolveGender(genderRate, rng);
            }

            // Gen1: sin esto, CatchRate queda en 0 (el default de EntityBlank) — válido
            // SOLO si el Pokémon visitó Gen2 (tradeback), inválido para cualquier
            // Pokémon "nacido" en Gen1 sin más vueltas como este (bug real encontrado con
            // un Magmar armado a mano en un save Yellow, ver G1CatchRateItem/
            // G1CatchRateEvo — el mismo botón "Corregir automáticamente" del tab
            // Diagnóstico que arregla esto para Pokémon YA existentes con el problema
            // usa este mismo helper, TryRecalculateCatchRate). Nunca se llega acá cuando
            // vino de TryBuildFromLegitEncounter — esas plantillas de PKHeX.Core ya traen
            // el CatchRate correcto resuelto por ConvertToPKM.
            if (pkm is PK1 pk1Fallback && TryRecalculateCatchRate(pk1Fallback, out var catchRate))
                pk1Fallback.CatchRate = catchRate;

            // Apodo: PKHeX no lo completa solo para un blanco armado a mano. NicknameVerifier
            // exige que el campo nunca esté vacío (código NickLengthShort si lo está) y, con
            // IsNicknamed=false, que coincida EXACTO con el nombre de especie por idioma/
            // generación (VerifyUnNicknamed) — sin esto, todo Pokémon armado a mano nace
            // ilegal por "Apodo" desde el primer instante. Confirmado leyendo
            // NicknameVerifier.cs en la fuente de PKHeX.Core. GetSpeciesNameGeneration no es
            // un simple lookup: maneja mayúsculas Gen1-4, el apóstrofo de Farfetch'd,
            // revisiones de nombres en chino, etc. — por eso se usa el helper de PKHeX en vez
            // de armar el nombre a mano.
            //
            // Bug real encontrado con Jynx en un save Yellow: este bloque ANTES corría
            // incondicionalmente para CUALQUIER Pokémon, no solo los de acá (fallback). Jynx
            // es intercambio-en-juego en Gen1 (EncounterTrade1) — ese tipo de encuentro trae
            // un apodo FIJO propio (IFixedNickname.IsFixedNickname=true en PKHeX.Core,
            // confirmado leyendo EncounterTrade1.cs) que ConvertToPKM ya deja bien puesto.
            // Pisarlo acá con el nombre de especie genérico da EncTradeChangedNickname
            // ("el apodo no coincide con el intercambio") — el mismo síntoma que tuvimos con
            // CatchRate y el nivel: una regla que tiene sentido SOLO para el blanco armado a
            // mano se aplicaba también a encuentros reales que ya traían su propio dato
            // correcto. Los encuentros reales (salvaje, estático, huevo) también resuelven su
            // propio apodo solos en su ConvertToPKM (ej. EncounterSlot1 llama
            // pk.SetNotNicknamed(language) — confirmado en la fuente) — no hace falta, y no
            // hay que, tocar nada de esto fuera del fallback.
            pkm.IsNicknamed = false;
            pkm.Nickname = SpeciesName.GetSpeciesNameGeneration(
                (ushort)speciesId, pkm.Language, (byte)save.Generation);
        }

        // Nivel de creación: al menos CreationLevel (5), pero nunca por debajo del nivel que
        // ya trae el PKM en este punto — subir de nivel después de recibir/capturar un
        // Pokémon SIEMPRE es legal (CurrentLevel >= nivel de origen), así que esto respeta la
        // decisión de producto original ("Nivel 5") sin romper la legalidad del encuentro
        // elegido en el intento 1. Si ese encuentro real requiere un nivel más alto que 5
        // (ej. un salvaje de zona avanzada), el Pokémon creado queda en ese nivel más alto en
        // vez de 5 — trade-off aceptado a cambio de no salir ilegal.
        //
        // Bug real encontrado: esto usaba pkm.MetLevel como piso en vez de pkm.CurrentLevel.
        // Funciona bien en Gen2+ (MetLevel es un campo real), pero en Gen1 PK1.MetLevel está
        // hardcodeado a "get => 0; set { }" en PKHeX.Core — Gen1 no guarda nivel de captura
        // en el save en absoluto. Con MetLevel como piso, CUALQUIER Pokémon Gen1 armado desde
        // un encuentro real (ej. el Magmar salvaje de Azul, Nv.34) volvía a Nivel 5 acá mismo,
        // pisando el nivel correcto que ConvertToPKM ya le había puesto un momento antes —
        // exactamente lo que se veía en la UI. CurrentLevel, a diferencia de MetLevel, es un
        // campo real en todos los formatos (nunca hardcodeado), así que sirve de piso
        // universal sin este problema — y en el camino de fallback no cambia nada: ahí
        // CurrentLevel sigue en 0 (EntityBlank.GetBlank no lo toca), así que el resultado
        // sigue siendo CreationLevel (5) como siempre.
        ApplyMinimumCreationLevel(pkm);

        // IVs perfectos — SOLO en el blanco armado a mano (ver bug real abajo). EVs nunca se
        // tocan acá (ni en este bloque ni en ningún otro lado de BuildPokemon): quedan en lo
        // que haya dejado ConvertToPKM (o el default de EntityBlank en el fallback, 0), no
        // hay ningún código que los pise.
        //
        // Bug real (mismo patrón que CatchRate/Nivel/Apodo/Moveset): esto corría
        // incondicionalmente para cualquier Pokémon. La mayoría de los encuentros Gen1
        // (salvaje/estático/intercambio) tienen DVs libres — cualquier valor es legal — así
        // que pisarlos con 31 (Gen1 los clampea sólo a 0-15, PKHeX.Core lo hace solo:
        // GBPKM.IV_ATK/IV_DEF "value > 0xF ? 0xF : value", sin corrupción) no rompía nada
        // ahí. Pero HAY excepciones reales: los regalos VC de Mew (EncounterGift1, Trainer=
        // EuropeTour/JapanTour) traen DVs FIJOS hardcodeados — (5,10,1,12,5,5) para las giras
        // Europa/Japón '99 — y el verificador exige que coincidan exacto
        // (Legal.GetIsFixedIVSequenceValidNoRand, confirmado en EncounterGift1.cs de
        // PKHeX.Core). El caso "VirtualConsoleMew" (15,15,15,15,15,15) coincidía de
        // casualidad con lo que forzábamos, pero el de las giras no — mismo síntoma que
        // CatchRate/Nivel/Apodo: un Mew de gira armado acá salía ilegal por DVs.
        if (isFallback)
        {
            Span<int> ivs = stackalloc int[6] { 31, 31, 31, 31, 31, 31 };
            pkm.SetIVs(ivs);
        }

        // Moveset: últimos 4 movimientos de levelup hasta el nivel final de creación (ver
        // arriba — ya no es siempre 5 si vino de un encuentro legítimo con nivel más alto).
        // Bug real (mismo patrón que CatchRate/Nivel/Apodo): esto corría incondicionalmente
        // para cualquier Pokémon, pisando el moveset que un encuentro real ya trae correcto
        // (EncounterUtil.SetEncounterMoves dentro de ConvertToPKM, confirmado en
        // EncounterTrade1.cs de PKHeX.Core) — solo hace falta cuando armamos todo a mano.
        if (isFallback)
        {
            var gameId = MapVersionToGameId(save.Version);
            var topMoveNames = db.GetLearnsets(speciesId, gameId, LearnsetFormFor(speciesId, form))
                .Where(l => l.LearnMethod == "levelup" && l.LearnLevel <= pkm.CurrentLevel)
                .OrderByDescending(l => l.LearnLevel)
                .Select(l => l.MoveName)
                .Distinct()
                .Take(4)
                .Reverse()
                .ToArray();

            for (var i = 0; i < 4; i++)
            {
                var moveEntry = i < topMoveNames.Length ? db.GetMoveByName(topMoveNames[i]) : null;
                var moveId    = (ushort)(moveEntry?.Id ?? 0);
                var pp        = moveEntry?.PP ?? 0;
                switch (i)
                {
                    case 0: pkm.Move1 = moveId; pkm.Move1_PP = pp; break;
                    case 1: pkm.Move2 = moveId; pkm.Move2_PP = pp; break;
                    case 2: pkm.Move3 = moveId; pkm.Move3_PP = pp; break;
                    case 3: pkm.Move4 = moveId; pkm.Move4_PP = pp; break;
                }
            }
        }

        // RefreshChecksum es interno al PKM (recalcula su propio campo de checksum) — no
        // toca el SaveFile, así que es seguro dejarlo acá aunque todavía no se inserte en
        // ningún lado. Si el usuario edita algo más tarde antes de exportar, va a hacer
        // falta recalcularlo de nuevo en el momento de aplicar la creación real.
        // Furfrou con corte (forma != 0): en Gen 6/7 el "argumento de forma" son los días de corte que
        // le quedan (máx. 5) y PKHeX marca FormArgumentInvalid si queda en 0. Se pone el valor sugerido
        // por PKHeX (el máximo). Solo Furfrou: el resto de argumentos de forma ya los resuelve el flujo
        // de evolución (neededFormArgument).
        if (pkm is IFormArgument && pkm.Species == (ushort)Species.Furfrou && pkm.Form != 0)
            pkm.SetSuggestedFormArgument();

        // Gen 6/7: el patrón de Scatterbug/Spewpa/Vivillon sale de la región de la consola 3DS y del
        // país/subregión del entrenador de origen (PKHeX: FormVivillonInvalid / GeoHardwareInvalid si no
        // coinciden). Se elige una combinación coherente para el patrón pedido.
        if (pkm.Species is (ushort)Species.Scatterbug or (ushort)Species.Spewpa or (ushort)Species.Vivillon
            && pkm.Form != 0 && save.Generation is 6 or 7)
            TrySetVivillonRegion(pkm);

        pkm.RefreshChecksum();

        return pkm;
    }

    /// <summary>Pone en un PK6/PK7 una región de consola + país + subregión de origen con los que el patrón
    /// de Vivillon de <paramref name="pkm"/> es legal (<c>Vivillon3DS</c>): el patrón debe ser válido para la
    /// consola, el país debe pertenecer a esa consola y el patrón debe ser el nativo de ese país/subregión.
    /// Prefiere la consola/idioma que ya trae el Pokémon. False si el patrón no es obtenible de forma
    /// salvaje (Fancy y Poké Ball son de evento) o si el formato no guarda esos datos.</summary>
    private static bool TrySetVivillonRegion(PKM pkm)
    {
        if (pkm is not IRegionOrigin region) return false;
        var form = pkm.Form;
        if (form > Vivillon3DS.MaxWildFormID) return false;

        // Consolas posibles: primero la actual, luego el resto.
        var consoles = new List<byte> { region.ConsoleRegion };
        consoles.AddRange(Locale3DS.DefinedLocales.ToArray().Where(c => c != region.ConsoleRegion));
        // Primera pasada respetando el idioma del Pokémon con la región de la consola; segunda pasada sin
        // ese requisito (patrones solo de Japón, como Elegante, con un Pokémon que no es japonés).
        foreach (var requireLanguage in new[] { true, false })
        foreach (var console in consoles)
        {
            if (!Vivillon3DS.IsPatternValid(form, console)) continue;
            if (requireLanguage && !Locale3DS.IsRegionLockedLanguageValid(console, pkm.Language)) continue;
            for (var country = 1; country <= 186; country++)
            {
                if (!Locale3DS.IsConsoleRegionCountryValid(console, (byte)country)) continue;
                for (var sub = 0; sub <= 255; sub++)
                {
                    if (!Vivillon3DS.IsPatternNative(form, (byte)country, (byte)sub)) continue;
                    region.ConsoleRegion = console;
                    region.Country = (byte)country;
                    region.Region = (byte)sub;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Intenta armar un PKM a partir de un encuentro legítimo real de este juego (huevo,
    /// estático/regalo o salvaje) usando <c>EncounterMovesetGenerator.GenerateEncounters</c> —
    /// el único método público de esa clase con esta forma (confirmado por reflection sobre
    /// PKHeX.Core.dll: <c>GetEggs</c>/<c>GetStatic</c>/<c>GetSlots</c>/<c>GetGifts</c>, que
    /// arman cada tipo de encuentro por separado, son privados — por eso se llama al genérico
    /// en vez de encadenarlos a mano). Ese método busca, para la especie indicada y la versión
    /// del juego cargado, todas las plantillas de encuentro reales que existen en PKHeX.Core
    /// (las mismas contra las que valida <see cref="LegalityAnalysis"/>) y las devuelve en su
    /// propio orden de prioridad interno (típicamente huevo primero si la especie puede
    /// criarse, si no estático/regalo, si no salvaje).
    ///
    /// Cada plantilla candidata sabe convertirse a un PKM ya autoconsistente
    /// (Species/Form/Nivel/Bola/Ubicación de captura/Idioma/Fateful/Entrenador/PID/Naturaleza/
    /// Género/Habilidad ya resueltos correctamente para ESE encuentro puntual) vía
    /// <c>IEncounterConvertible.ConvertToPKM</c> — no hace falta reimplementar nada de esa
    /// lógica a mano, ni sumar el paquete PKHeX.Core.AutoMod (esto vive en el PKHeX.Core base,
    /// que ya es una dependencia existente del proyecto).
    ///
    /// No se le pasa ningún moveset deseado (se busca sin restricción de movimientos) — el
    /// moveset final que ve el usuario lo decide igual <see cref="BuildPokemon"/> después,
    /// sobrescribiendo Move1-4 con los últimos 4 de levelup hasta el nivel final. Los
    /// "Relearn Moves" que trae la plantilla (relevantes solo para huevos) quedan tal cual los
    /// resolvió PKHeX — son una dimensión de legalidad separada de los movimientos actuales,
    /// así que no hace falta que coincidan.
    ///
    /// Devuelve null si no se encuentra ninguna plantilla real para esta especie en este juego
    /// puntual (ej. legendarios/míticos exclusivos de otra versión) — en ese caso
    /// <see cref="BuildPokemon"/> cae al PKM en blanco de siempre. Cualquier excepción en el
    /// camino (versión sin datos de encuentro cargados, plantilla que no convierte bien, etc.)
    /// se traga y también devuelve null — mismo criterio defensivo que
    /// <c>EditSessionService.AnalyzeLegality</c>: preferimos degradar a un comportamiento ya
    /// conocido antes que tirarle una excepción al usuario por un caso puntual raro.
    /// </summary>
    private static PKM? TryBuildFromLegitEncounter(SaveFile save, int speciesId, byte form = 0)
    {
        if (form != 0) return TryBuildFromLegitEncounterCore(save, speciesId, form, false);

        // Forma base pedida: primero solo candidatos que de verdad dan la forma 0. Sin esto, en juegos
        // donde el encuentro más fácil de la especie es de otra forma (Deoxys en HeartGold, Goodra en
        // Legends: Arceus...) se devolvía esa otra forma aunque la base fuera creable o no existiera, y
        // el sprite cambiaba al crear. Si ningún candidato da la base, se conserva el comportamiento
        // anterior (segunda pasada, cualquier forma).
        var exact = TryBuildFromLegitEncounterCore(save, speciesId, 0, true);
        if (exact != null && exact.Form == 0 && IsFreeOfInvalidChecks(exact)) return exact;
        return TryBuildFromLegitEncounterCore(save, speciesId, 0, false) ?? exact;
    }

    private static PKM? TryBuildFromLegitEncounterCore(SaveFile save, int speciesId, byte form, bool preferExactBase)
    {
        try
        {
            var template = EntityBlank.GetBlank(save);
            template.Species = (ushort)speciesId;
            // Fase 2 de formas: GenerateEncounters filtra los encuentros por la forma de la
            // plantilla (sin esto, Marowak/Exeggutor de Alola nunca salían: siempre forma 0).
            template.Form = form;

            // Gen4 — especies que SOLO existen como hembra (Vespiquen, Froslass, Wormadam...).
            // GenerateEncounters devuelve encuentros validando la cadena evolutiva contra el
            // PKM plantilla, y EntityBlank trae Gender=0 (macho): con eso Combee→Vespiquen
            // (LevelUpFemale), Snorunt→Froslass (UseItemFemale) y Burmy→Wormadam fallan por
            // BadGender y la búsqueda devuelve CERO encuentros — el Pokémon caía siempre al
            // fallback armado a mano (ilegal por Encuentro). Verificado corriendo el
            // algoritmo completo contra PKHeX.Core 25.11.07 compilado desde su fuente.
            if (save.Generation is >= 4 and <= 9 && save.Personal[speciesId].OnlyFemale)
                template.Gender = 1;

            // Gen1/Gen2/Gen3: intercambio por cable link entre las versiones hermanas era
            // una mecánica central del juego (de hecho, imprescindible para completar la
            // Pokédex — hay especies exclusivas de cada versión). Bug real encontrado con
            // Magmar: es encuentro salvaje EXCLUSIVO de Azul (Mansión Pokémon, Nv.34/38 —
            // confirmado extrayendo a mano el binario real de encuentros de PKHeX.Core, no
            // estaba en ninguna tabla por nombre) — buscar únicamente en la versión del
            // save activo (Amarillo) nunca lo iba a encontrar, así que siempre caía al
            // fallback armado a mano (de ahí el problema de CatchRate en 0 que arreglamos
            // antes). Mismo problema aplica a CUALQUIER especie exclusiva-de-intercambio en
            // Gen1 (Mr. Mime, Jynx, Farfetch'd, Lickitung, etc.) y, por la misma razón, a
            // cualquier exclusiva de versión en Gen2 (Oro/Plata/Cristal) y Gen3
            // (Rubí/Zafiro/Esmeralda/RojoFuego/VerdeHoja — los cinco se intercambiaban
            // libremente entre sí por cable link de GBA).
            // Se excluye GameVersion.BU (Azul JAPONÉS, versión y juego distintos de la
            // Azul internacional) porque el link trade exige mismo idioma de cartucho — no
            // tendría sentido ofrecerlo como origen para un save en español/inglés. Gen2 no
            // tiene este problema: a diferencia de Gen1, Oro/Plata/Cristal no tuvieron una
            // versión japonesa separada con datos distintos (confirmado en el enum
            // GameVersion de PKHeX.Core — GD/SI/C no tienen ningún equivalente a BU).
            // Gen3 SÍ tiene una versión que hay que excluir a propósito, pero por otro
            // motivo: GameVersion.CXD (Pokémon Colosseum/XD, GameCube) — mecánicamente muy
            // distintos a los juegos de GBA (Pokémon Sombra, cámara de purificación, sin
            // intercambio directo por cable link con RSE/FRLG de la misma forma simple),
            // así que no tiene sentido meterlo en un broadening de "intercambio simple
            // entre cartuchos GBA hermanos".
            var versions = save.Generation switch
            {
                1 => new[] { GameVersion.RD, GameVersion.GN, GameVersion.YW },
                2 => new[] { GameVersion.GD, GameVersion.SI, GameVersion.C },
                3 => new[] { GameVersion.R, GameVersion.S, GameVersion.E, GameVersion.FR, GameVersion.LG },

                // Gen4 — DOS problemas distintos, ambos confirmados con PKHeX.Core real:
                // (1) BUG GRAVE: SAV4DP.Version devuelve el valor AGREGADO GameVersion.DP
                //     (no D ni P — un save de Diamante/Perla no distingue cuál de los dos
                //     es) y SAV4HGSS.Version devuelve GameVersion.HGSS. Buscar encuentros
                //     con `new[] { save.Version }` (lo que hacía el `_ =>` de abajo) con un
                //     valor agregado no encuentra NADA — 492 de 493 especies caían al
                //     fallback ilegal en cualquier save de Diamante/Perla/HeartGold/
                //     SoulSilver. Solo Platino (Version=Pt, específico) funcionaba. Mismo
                //     patrón de trampa #22 (GameVersion agregado vs específico).
                // (2) Igual que en Gen1-3: las cinco versiones de Gen4 se intercambian entre
                //     sí (incluso DPPt↔HGSS), así que las exclusivas de otra versión y las
                //     que solo se consiguen evolucionando son un origen legítimo. Se pone
                //     primero la familia del propio save para que el orden de prioridad
                //     prefiera un encuentro "nativo" cuando existe.
                4 => save.Version switch
                {
                    GameVersion.HGSS or GameVersion.HG or GameVersion.SS
                        => new[] { GameVersion.HG, GameVersion.SS, GameVersion.D, GameVersion.P, GameVersion.Pt },
                    GameVersion.Pt
                        => new[] { GameVersion.Pt, GameVersion.D, GameVersion.P, GameVersion.HG, GameVersion.SS },
                    _   => new[] { GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS },
                },

                // Gen5 — mismo patrón que Gen4: Negro/Blanco y Negro2/Blanco2 se intercambian
                // entre sí por cable/infrarrojos (y con Gen4 por Poké Transfer, ver más abajo
                // la nota sobre eso), así que las exclusivas de la otra versión y las que solo
                // se consiguen evolucionando son un origen legítimo. A diferencia de Gen4,
                // acá SAV5.Version SÍ es específico (lo lee del byte Game del save: B, W, B2
                // o W2, nunca el agregado BW/B2W2), así que el `_` de abajo hubiera
                // funcionado — pero solo con las plantillas de UNA versión. Familia propia
                // primero para preferir un encuentro nativo cuando existe.
                5 => save.Version switch
                {
                    GameVersion.B2 or GameVersion.W2 or GameVersion.B2W2
                        => new[] { GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W },
                    _   => new[] { GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2 },
                },

                // Gen6 — mismo patrón que Gen4/Gen5. SAV6.Version es específico (lee el byte
                // Game del save: X, Y, OR o AS, nunca el agregado XY/ORAS). Las cuatro
                // versiones se intercambian entre sí (X/Y ↔ OR/AS incluido) — familia propia
                // primero para preferir un encuentro nativo cuando existe.
                6 => save.Version switch
                {
                    GameVersion.OR or GameVersion.AS or GameVersion.ORAS
                        => new[] { GameVersion.OR, GameVersion.AS, GameVersion.X, GameVersion.Y },
                    _   => new[] { GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS },
                },

                // Gen7 — SM/USUM (Alola) y Let's Go (Kanto, otra rama). SAV7.Version es
                // específico (SN/MN/US/UM), nunca el agregado. SM y USUM se intercambian
                // entre sí; Let's Go Pikachu/Eevee NO intercambia con ellos (solo entre GP y
                // GE, más Pokémon GO y Meltan) — por eso su familia queda aparte.
                7 => save.Version switch
                {
                    GameVersion.GP or GameVersion.GE or GameVersion.GG
                        => new[] { GameVersion.GP, GameVersion.GE },
                    GameVersion.US or GameVersion.UM or GameVersion.USUM
                        => new[] { GameVersion.US, GameVersion.UM, GameVersion.SN, GameVersion.MN },
                    _   => new[] { GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM },
                },

                // Gen8 — tres ramas distintas que NO se intercambian entre sí: Espada/Escudo
                // (Galar), Diamante Brillante/Perla Reluciente (Sinnoh remake) y Leyendas:
                // Arceus (Hisui). SAV8SWSH.Version es SW o SH, SAV8BS es BD o SP y SAV8LA es
                // PLA — todos específicos (nunca el agregado).
                8 => save.Version switch
                {
                    GameVersion.BD or GameVersion.SP or GameVersion.BDSP
                        => new[] { GameVersion.BD, GameVersion.SP },
                    GameVersion.PLA
                        => new[] { GameVersion.PLA },
                    _   => new[] { GameVersion.SW, GameVersion.SH },
                },

                // Gen9 — Escarlata/Púrpura. SAV9SV.Version es SL o VL (específico). Recibe de
                // TODAS las ramas anteriores por HOME (Galar, Sinnoh-remake, Hisui, Alola,
                // Let's Go, Kalos, Teselia, Sinnoh/Johto, Hoenn, GO), así que la búsqueda
                // propia va primero y después la misma cadena que en Gen8, ver abajo.
                9 => save.Version is GameVersion.ZA
                    ? new[] { GameVersion.ZA }
                    : new[] { GameVersion.SL, GameVersion.VL },
                _ => new[] { save.Version },
            };

            // Gen2: Articuno, Zapdos, Moltres, Mewtwo y Mew NO tienen ningún encuentro en
            // Oro/Plata/Cristal (confirmado con PKHeX.Core: 0 candidatos con GD+SI+C) — en Gen2
            // solo existen por Time Capsule, es decir, un Pokémon de ORIGEN Gen1 (aves y
            // Mewtwo: EncounterStatic1, Nv.50/50/50/70; Mew: EncounterGift1, Nv.5) intercambiado
            // hacia Gen2. Antes caían siempre al fallback armado a mano (ilegal por Encuentro).
            // Se agrega un SEGUNDO conjunto de versiones (RD/GN/YW — sin BU, mismo criterio
            // de arriba) que solo se consulta si el primero no dio ningún candidato limpio,
            // para NO cambiar el origen de las 246 especies que ya salían bien con encuentro
            // nativo de Gen2 (si se mezclaran en una sola búsqueda, un Gen1 podría ordenarse
            // antes que el nativo). El candidato Gen1 sale como PK1: hay que pasarlo a PK2
            // (PK1.ConvertToPK2, el mismo camino de la Time Capsule) antes de analizarlo o
            // devolverlo — verificado que el PK2 resultante queda sin ninguna alerta.
            //
            // Gen5: mismo mecanismo, un salto más largo. Sin conjunto extra, 11 especies caían
            // al fallback en cualquier save de Gen5 (verificado con PKHeX.Core, B/W/B2/W2 por
            // igual): Articuno/Zapdos/Moltres/Mew/Raikou/Entei/Suicune/Celebi/Manaphy/Unown,
            // que solo se consiguen en Gen4 (HGSS: aves y bestias, eventos PCD/PGT, Ruinas
            // Alfa) o Gen3, y llegan a Gen5 por Poké Transfer (Gen3 pasa antes por Pal Park);
            // más Dustox: en Gen5 puro no tiene candidato propio y TryEvolveForward, en la
            // primera bifurcación de Wurmple, toma la primera rama (Silcoon) y nunca llega a
            // Cascoon→Dustox (limitación conocida de las cadenas con rama no terminal); sí
            // tiene encuentro directo en Gen4/Gen3, y de ahí sale ahora.
            // Conjunto 2 = las cinco de Gen4, conjunto 3 = las cinco de Gen3 (GBA, sin CXD).
            // El PKM sale en formato Gen4/Gen3 y se convierte al formato del save más abajo.
            var versionSets = save.Generation switch
            {
                2 => new[] { versions, new[] { GameVersion.RD, GameVersion.GN, GameVersion.YW } },
                5 => new[]
                {
                    versions,
                    new[] { GameVersion.Pt, GameVersion.HG, GameVersion.SS, GameVersion.D, GameVersion.P },
                    new[] { GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S },
                },
                // Gen7 (SM/USUM; Let's Go no — es otra rama que no recibe de Gen1-6 por Banco):
                // 7 especies sin encuentro propio en Alola — Unown, Deoxys, Keldeo, Genesect,
                // Diancie, Volcanion (eventos/formas de Gen3-6 que llegan por Poké Banco) y
                // Dustox (ver la nota de Gen5). Se consultan los juegos de Gen6, Gen5, Gen4 y
                // Gen3 en ese orden — el más reciente primero, para que el Pokémon salga con
                // el menor número de saltos de conversión posible.
                7 when save.Version is not (GameVersion.GP or GameVersion.GE or GameVersion.GG) => new[]
                {
                    versions,
                    new[] { GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS },
                    new[] { GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W },
                    new[] { GameVersion.Pt, GameVersion.HG, GameVersion.SS, GameVersion.D, GameVersion.P },
                    new[] { GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S },
                },
                // Gen8: Espada/Escudo — Diancie, Magearna, Zeraora y Melmetal (eventos WC6/WC7/
                // WC8) llegan por Banco/HOME desde Gen6/Gen7. Meltan es SOLO de Pokémon GO — no
                // tiene NINGÚN encuentro dentro de PKHeX.Core para SWSH ni para ningún juego de
                // consola; se busca con el pseudo-"juego" GameVersion.GO, que sí devuelve
                // candidatos (EncounterSlot7GO — aterrizan en formato 7, un salto más antes de
                // llegar a PK8, igual que cualquier transferencia por HOME). BDSP — Deoxys y uno
                // de los dos orígenes de Manaphy solo tienen encuentro en Gen6
                // (EncounterStatic6, confirmado con PKHeX.Core: la cadena de Gen7 no encontró
                // nada para Deoxys). Meltan no está en el dex de BDSP, así que esa rama no
                // necesita el conjunto de GO. Leyendas Arceus no recibe nada por Banco (no tiene
                // conexión), así que no suma conjunto de respaldo. Se agrega la cadena completa
                // hasta Gen3 en ambas ramas, más reciente primero, mismo criterio que en Gen7.
                8 when save.Version is GameVersion.SW or GameVersion.SH or GameVersion.SWSH => new[]
                {
                    versions,
                    new[] { GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM },
                    new[] { GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS },
                    new[] { GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W },
                    new[] { GameVersion.Pt, GameVersion.HG, GameVersion.SS, GameVersion.D, GameVersion.P },
                    new[] { GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S },
                    new[] { GameVersion.GO },
                },
                8 when save.Version is GameVersion.BD or GameVersion.SP or GameVersion.BDSP => new[]
                {
                    versions,
                    new[] { GameVersion.SW, GameVersion.SH },
                    new[] { GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM },
                    new[] { GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS },
                    new[] { GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W },
                    new[] { GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S },
                },
                // Gen9: 68 especies sin encuentro propio en Paldea/Kitakami/Arándano (los
                // iniciales evolucionados de otras regiones, los legendarios de Sinnoh, etc.):
                // llegan por HOME desde cualquier generación anterior. Se consulta la propia
                // primero y después de más reciente a más antigua, mismo criterio que Gen7/8.
                9 => new[]
                {
                    versions,
                    // Legends: Z-A: sus propios encuentros (versions = ZA) van primero; Escarlata/
                    // Púrpura queda como respaldo para lo que PKHeX no tiene como encuentro de Z-A.
                    // Para un save de SV esta rama es la propia (versions ya es SL/VL): no se repite.
                    save.Version is GameVersion.ZA ? new[] { GameVersion.SL, GameVersion.VL } : Array.Empty<GameVersion>(),
                    new[] { GameVersion.SW, GameVersion.SH },
                    new[] { GameVersion.BD, GameVersion.SP },
                    new[] { GameVersion.PLA },
                    new[] { GameVersion.GP, GameVersion.GE },
                    new[] { GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM },
                    new[] { GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS },
                    new[] { GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W },
                    new[] { GameVersion.Pt, GameVersion.HG, GameVersion.SS, GameVersion.D, GameVersion.P },
                    new[] { GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S },
                    new[] { GameVersion.GO },
                },
                _ => new[] { versions },
            };
            versionSets = versionSets.Where(v => v.Length > 0).ToArray();   // (el conjunto vacío de arriba es solo de relleno)

            var attemptsPerCandidate = save.Generation is >= 3 and <= 9 ? 12 : 1;
            const int AnalysisBudgetPerSet = 80;
            var analysisBudget = AnalysisBudgetPerSet;
            PKM? firstBuilt = null;
            // Presupuesto TOTAL (no por candidato) de la sonda de nivel + moveset de Gen8+: acota el
            // costo cuando ninguna combinación sale legal (especies que no existen en el juego).
            var moveProbeBudget = 300;

            foreach (var versionSet in versionSets)
            {
                // Presupuesto POR conjunto de versiones, no global: en Legends: Z-A el conjunto
                // propio (ZA) tiene decenas de candidatos y con 12 intentos cada uno agotaba los 80
                // análisis antes de llegar al respaldo de Escarlata/Púrpura, y Gyarados, los Eevee-
                // lución, Tyranitar, Talonflame... se devolvían con el primer candidato de Z-A,
                // ilegal (EncInvalid), aunque había uno limpio en el siguiente conjunto.
                analysisBudget = AnalysisBudgetPerSet;
                IEnumerable<IEncounterable> candidates = EncounterMovesetGenerator.GenerateEncounters(
                    template, ReadOnlyMemory<ushort>.Empty, versionSet);

                // Legends: Z-A: hay decenas de candidatos por especie (Weedle: 26, Beedrill: 46, con
                // el Weedle Alfa de nivel 45 primero) y el presupuesto de análisis (12 intentos × 80)
                // se agotaba antes de llegar al encuentro propio de la especie pedida (Beedrill nv.10
                // en la Zona 31), así que se devolvía el Weedle Alfa evolucionado, ilegal por
                // "StatAlphaInvalid". Se prueban primero los encuentros de la propia especie (sin
                // evolución que pueda fallar), no Alfa antes que Alfa, y después el resto en su orden.
                // OrderBy es estable. Solo para Z-A: el resto de juegos mantiene el orden de PKHeX.
                if (save.Version is GameVersion.ZA)
                    candidates = candidates
                        .OrderBy(e => e.Species == speciesId ? 0 : 1)
                        .ThenBy(e => e is IAlphaReadOnly { IsAlpha: true } ? 1 : 0);

                // Bug real encontrado con Machamp: hay un intercambio real en Amarillo
                // (Machoke con EvolveOnTrade=true → sale Machamp, confirmado en Encounters1.cs
                // de PKHeX.Core) que la búsqueda SÍ debería encontrar — pero antes había un
                // tope de 20 candidatos acá, y hay bastantes Machop/Machoke salvajes en
                // RD+GN+YW combinados que se enumeran antes que ese único intercambio,
                // agotando el tope sin llegar nunca a probarlo. Subido a un tope mucho más
                // alto (no eliminado del todo por las dudas de que algún generador futuro
                // devuelva algo patológicamente grande) — convertir un candidato de más es
                // barato, no vale la pena arriesgar volver a cortar la búsqueda temprano.
                // Preferir un candidato LIMPIO en vez de devolver ciegamente el primero. Dos casos
                // reales distintos, ambos confirmados corriendo todas las especies contra
                // PKHeX.Core:
                // - Gen1, Nidoran♂: el primer candidato es el intercambio EncounterTrade1 de
                //   Rojo/Azul que existe SOLO en la versión japonesa (Nidoran♂ ↔ Nidoran♀); su
                //   apodo fijo no tiene traducción a otros idiomas, así que en un save no japonés
                //   sale con el apodo vacío (NickLengthShort). Sí existe un Nidoran♂ salvaje
                //   legítimo más abajo en la lista de candidatos — solo había que seguir buscando.
                // - Gen3/Gen4, Wurmple→Silcoon/Cascoon: la rama la decide el EC/PID (WurmpleUtil),
                //   y el PID del huevo sale al azar en cada conversión, así que a veces cae en la
                //   rama equivocada (se veía intermitente como Silcoon/Beautifly ilegales en Gen3).
                //   Ahí sí sirve reintentar la MISMA plantilla: hasta 12 conversiones por candidato
                //   en Gen3/Gen4 (el resto de generaciones no tiene azar que aprovechar: 1 intento).
                // Se acepta el primer PKM sin ningún chequeo Invalid; si ninguno cierra, se devuelve
                // el primero que se pudo armar (mismo comportamiento de siempre). Tope total de 80
                // análisis de legalidad por creación, para que una especie sin ningún origen limpio
                // no recorra 500 candidatos × 12 intentos.

                foreach (var enc in candidates.Take(500))
                {
                    if (enc is not IEncounterConvertible convertible) continue;
                    for (var attempt = 0; attempt < attemptsPerCandidate; attempt++)
                    {
                        try
                        {
                            // Unown en Gen 4: la letra sale del PID; PKHeX la respeta si se le pide en el criterio.
                            var built = form != 0 && speciesId == (int)Species.Unown && save.Generation == 4
                                ? convertible.ConvertToPKM(save, new EncounterCriteria { Form = (sbyte)form })
                                : convertible.ConvertToPKM(save);

                            // Bug real encontrado con Glastrier y Spectrier (encuentros
                            // estáticos de Corona Yerma): la propia plantilla trae definido un
                            // "recuerdo de movimientos" (Relearn) — Glastrier con Carámbano
                            // Feroz, por ejemplo — pero EncounterStatic8.ConvertToPKM (código de
                            // PKHeX.Core, no nuestro) solo aplica el moveset ACTUAL de la
                            // plantilla; el de recuerdo se lo deja vacío. Se completa acá si el
                            // candidato lo trae y el PKM construido no tiene ninguno propio
                            // todavía (para no pisar el de un huevo, que sí lo arma bien por su
                            // cuenta).
                            if (built.RelearnMove1 == 0 && enc is IRelearn { Relearn.HasMoves: true } withRelearn)
                                built.SetRelearnMoves(withRelearn.Relearn);

                            if (built.Species != speciesId)
                            {
                                // Bug real encontrado con Alakazam/Omastar/Dragonite: ninguno de los
                                // tres tiene encuentro PROPIO en Gen1 (solo sus pre-evoluciones sí:
                                // Kadabra salvaje, Omanyte de fósil, Dragonair salvaje) — así que todo
                                // candidato que llegaba acá tenía otra especie y se descartaba siempre,
                                // aunque fuera un origen legítimo real. Evolucionar "hacia adelante" en
                                // vez de exigir coincidencia exacta cubre esto — ver TryEvolveForward.
                                // Mismo problema y misma solución en Gen2 (ej. Crobat, Espeon/Umbreon,
                                // Politoed/Slowking/Steelix/Scizor/Kingdra por intercambio+objeto —
                                // ninguno tiene encuentro propio, solo su pre-evolución), en Gen3 (ej.
                                // Milotic por Belleza, Ninjask/Shedinja por nivel desde Nincada,
                                // Huntail/Gorebyss por intercambio+objeto desde Clamperl) y en Gen4
                                // (misma mecánica; ver los fixups post-evolución en TryEvolveForward).
                                if (!(save.Generation is >= 1 and <= 9 && TryEvolveForward(built, speciesId, enc, form)))
                                    break; // este candidato no llega a la especie pedida — pasar al siguiente
                            }
                            else
                            {
                                // Sin evolución: ConvertToPKM ya deja bien casi todo, salvo la
                                // habilidad oculta forzada de los regalos (ver
                                // FixupAbilityToMatchEncounter) — TryEvolveForward llama a este
                                // mismo helper dentro de FixupAfterEvolution, pero cuando NO hace
                                // falta evolucionar nadie más lo llamaba.
                                FixupAbilityToMatchEncounter(built, enc);
                            }

                            // Fase 2 de formas: con una forma pedida solo sirve un candidato que
                            // termine EXACTAMENTE en esa forma (ej. un Exeggutor de Kanto no vale
                            // si se pidió el de Alola). Con form == 0 no se filtra nada: así no se
                            // descartan encuentros de especies con forma cosmética al azar.
                            // Gen 6/7, Scatterbug/Spewpa/Vivillon: el patrón del encuentro salvaje lo decide la
                            // región del entrenador del save, no el encuentro. Se pide el patrón y se
                            // fija una región de consola/país coherente con él (TrySetVivillonRegion);
                            // si no existe ninguna (Fancy, Poké Ball) el candidato se descarta abajo.
                            if (form != 0 && built.Form != form && save.Generation is 6 or 7
                                && built.Species is (ushort)Species.Scatterbug or (ushort)Species.Spewpa or (ushort)Species.Vivillon
                                && form <= Vivillon3DS.MaxWildFormID)
                            {
                                built.Form = form;
                                TrySetVivillonRegion(built);
                            }
                            if (form != 0 && built.Form != form)
                                break;
                            if (preferExactBase && built.Form != 0)
                                break;   // primera pasada: solo candidatos de forma base

                            if (save.Generation == 2 && built is PK1 gen1Origin)
                                built = gen1Origin.ConvertToPK2(); // ver versionSets más arriba

                        // Gen5 con origen Gen3/Gen4: Poké Transfer (Gen3 → Gen4 por Pal Park → Gen5).
                        // EntityConverter.TryMakePKMCompatible ya encadena los saltos que haga
                        // falta; verificado que el PK5 resultante queda sin alertas en las 11
                        // especies de arriba. Si por algún motivo no convierte, se descarta este
                        // candidato en vez de devolver un PKM del formato equivocado (el save no
                        // lo aceptaría).
                        // También cuenta el cruce de FORMATO dentro de la misma generación (regalo WC8 de Espada/Escudo
                        // en BDSP = PK8 → PB8; encuentro de Escarlata/Púrpura en Z-A = PK9 → PA9): es un pase
                        // por HOME y necesita Tracker, igual que un cruce de generación.
                        var crossedGenerationBoundary = save.Generation is 5 or 6 or 7 or 8 or 9
                            && (built.Format < save.Generation || built.GetType() != save.PKMType);
                        if (crossedGenerationBoundary)
                        {
                            if (!EntityConverter.TryMakePKMCompatible(built, save.BlankPKM, out var transferResult, out var transferred)
                                || !transferResult.IsSuccess() || transferred is null)
                                break;
                            built = transferred;

                            // Gen6/Gen7: un Pokémon que cruza de generación por Poké Banco debe
                            // salir con el Entrenador Actual (HT) = quien lo transfirió, no
                            // vacío. Con los eventos (OT de Game Freak, etc.) el conversor de
                            // PKHeX ya lo deja así, pero con un Pokémon capturado por el propio
                            // entrenador del save (OT = el save, como pasa con cualquier
                            // candidato salvaje/huevo de Gen3-6 que ConvertToPKM le arma) el HT
                            // queda vacío y PKHeX marca "TransferHandlerFlagRequired": Unown de
                            // Gen7 salía ilegal por esto (era el único de 802).
                            if (built.CurrentHandler == 0)
                                MarkAsTransferred(built, save);
                        }

                            // Un Pokémon que pasa por HOME (Gen6/7 → Gen8+, o Gen8 → Gen9, o un
                            // regalo misterioso de una generación ANTERIOR a la del save, como
                            // Diancie/WC6 llegando a Espada/Escudo) recibe ahí un Tracker
                            // (identificador único de HOME) — PKHeX solo puede comprobar que NO
                            // sea 0 (no somos el servidor de HOME, así que no puede validar el
                            // valor real), pero exige que exista. Confirmado con PKHeX.Core real:
                            // Diancie (WC6) y Magearna (WC7) por el salto de formato (arriba), y
                            // Zeraora y Melmetal (ambos YA en formato PK8, WC8, sin cruzar
                            // formato) por esto — los cuatro en Espada/Escudo salían con
                            // "TransferTrackerMissing". SOLO se asigna en estos dos casos — un
                            // huevo, salvaje o regalo NATIVO de esta generación (nunca pasó por
                            // HOME) debe conservar Tracker=0, y asignárselo también es ilegal:
                            // confirmado con PKHeX.Core real que un Spinda de huevo en
                            // Diamante/Perla-remake, Y un Mew de evento WC9 en Escarlata/Púrpura
                            // (evento NATIVO de Gen9, no llegado por HOME), salían con
                            // "TransferTrackerShouldBeZero" al ponérselo sin que corresponda —
                            // por eso la condición de regalo exige generación estrictamente
                            // menor a la del save, no cualquier regalo.
                            if ((crossedGenerationBoundary || enc is MysteryGift { Generation: var giftGen } && giftGen < save.Generation)
                                && built is IHomeTrack { HasTracker: false } tracked)
                                tracked.Tracker = (ulong)Random.Shared.NextInt64(1, long.MaxValue);

                            firstBuilt ??= built;
                            if (analysisBudget-- <= 0) goto NextVersionSet;   // sin presupuesto en este conjunto: probar el siguiente
                            ApplyMinimumCreationLevel(built);
                            // Intercambios del juego de Let's Go (EncounterTrade7b, p. ej. el Golem de
                            // Alola): PKHeX nunca admite al OT como manejador actual en ellos y exige
                            // CurrentHandler = 1 con el HT del entrenador del save; sin esto salía
                            // "TransferHandlerFlagRequired". Confirmado con PKHeX.Core real en GP/GE.
                            if (enc is EncounterTrade7b && built.CurrentHandler == 0)
                                MarkAsTransferred(built, save);
                            // Leyendas: Arceus (Gen8a) exige el moveset COMPLETO para el nivel
                            // final, incluso en encuentros salvajes/estáticos directos (no solo
                            // tras evolucionar) — confirmado con PKHeX.Core real: Vaporeon y
                            // Typhlosion (encuentro propio, sin pasar por TryEvolveForward)
                            // también salían con "UnobtainableExpect" en slots vacíos. Por eso
                            // se llama acá, para TODO candidato en Gen8a, no solo dentro de
                            // FixupAfterEvolution (ver el docblock largo de
                            // RefreshMovesetForRigidLevelUpGames).
                            //
                            // EXCEPCIÓN: si el propio candidato ya trae un moveset fijo (un
                            // encuentro estático puntual — un Alfa con nombre, por ejemplo — que
                            // especifica sus 4 movimientos de antemano, vía IMoveset) y no
                            // evolucionó, ese moveset YA es el correcto y no hay que tocarlo.
                            // Confirmado con PKHeX.Core real: un Kricketune Alfa de Leyendas:
                            // Arceus (encuentro fijo, no una captura salvaje común) trae
                            // "Corte Falso, Absorción, Placaje, Golpe Aéreo" como moveset propio
                            // del guion — nuestro recálculo genérico lo pisaba con solo 3
                            // movimientos (los que da el aprendizaje normal por nivel, que para
                            // esta especie a ese nivel no llegan a 4), perdiendo Corte Falso y
                            // dejando un slot vacío que entonces sí quedaba inválido.
                            //
                            // Además, CUALQUIER regalo misterioso (WC9, etc.) se deja siempre
                            // afuera, aunque su IMoveset.Moves.HasMoves marque false: sigue
                            // siendo un evento con datos propios (nivel fijo, altura/peso
                            // fijos, cintas...) que nuestro recálculo genérico no conoce.
                            // Confirmado con PKHeX.Core real: un Mew de evento en
                            // Escarlata/Púrpura (WC9, Nv.5 fijo) era legal tal cual salía de
                            // ConvertToPKM, y nuestro recálculo lo rompía poniéndole un
                            // moveset que ya no correspondía al del regalo.
                            var hasFixedMoveset = enc is MysteryGift
                                || (built.Species == enc.Species && enc is IMoveset { Moves.HasMoves: true });
                            if (!hasFixedMoveset)
                                RefreshMovesetForRigidLevelUpGames(built);
                            RepairRelearnMoves(built);
                            RepairPlusFlags(built);
                            built.RefreshChecksum();
                            // Gen9, especie evolucionada: IsFreeOfInvalidChecks no mira los movimientos;
                            // un huevo evolucionado con slots vacíos (Yanmega, Dudunsparce) no es
                            // válido aunque los chequeos de evolución pasen — va a la sonda de nivel.
                            var movesNeedFix = save.Generation >= 8 && built.Species != enc.Species
                                               && CountInvalidMoves(built) > 0;
                            // Gen4-7: lo mismo — IsFreeOfInvalidChecks no mira los movimientos, y un huevo
                            // evolucionado a Nv.5 con el movimiento que exige la evolución (Ambipom,
                            // Lickilicky, Tangrowth, Yanmega en Gen6) devolvía ese candidato con "Invalid
                            // Move". Se descarta para probar el siguiente candidato (uno salvaje a un nivel
                            // real); si ninguno es limpio, sigue valiendo el primero (firstBuilt).
                            var movesBadGen4To7 = save.Generation is >= 4 and < 8 && built.Species != enc.Species
                                                  && CountInvalidMoves(built) > 0;
                            // Forma pedida: encuentros que llegan por HOME (regalos de HOME, capturas de Pokémon GO)
                            // exigen Tracker y Entrenador Actual = HT; si PKHeX lo marca, se completa.
                            if (form != 0) RepairHomeDeliveryFlags(built, save);
                            if (IsFreeOfInvalidChecks(built) && !movesNeedFix && !movesBadGen4To7)
                                return built;

                            // Gen4+: unas 16 evoluciones (Ambipom, Lickilicky, Tangrowth, Yanmega,
                            // Mamoswine, Tsareena, Grapploct, Wyrdeer, Overqwil, Annihilape,
                            // Farigiraf, Dudunsparce, Hydrapple, Sr. Mime, Cofagrigus/Sudowoodo...
                            // — ver EvolutionRestrictions.GetSpeciesEvolutionMove en PKHeX.Core)
                            // exigen además CONOCER un movimiento puntual — pero la evolución en
                            // sí no impone ningún nivel mínimo propio (next.Level=0: solo pide
                            // "sabe Golpe Doble", no "Nv.X"), así que el Pokémon creado quedaba
                            // muy por debajo del nivel real en que se puede aprender ese
                            // movimiento y evolucionar. Confirmado con PKHeX.Core real: Ambipom y
                            // Yanmega salían con "MoveEvoFCombination" a Nv.5 — a Nv.32 (el nivel
                            // real en que Aipom aprende Golpe Doble) ya no hace falta tener el
                            // movimiento puesto: a PKHeX le alcanza con que el Pokémon PUDO haber
                            // pasado por ese nivel (VerifyEvolution acepta que después lo haya
                            // "olvidado" al subir más, si algún slot no es de huevo).
                            //
                            // No hay una API pública en PKHeX.Core para pedirle directamente ese
                            // nivel sin reimplementar su tabla interna especie→movimiento (que es
                            // `internal`, no accesible desde este proyecto) — así que se prueba
                            // contra su propio motor de legalidad: si el ÚNICO problema es
                            // justamente ese ("Evolution" inválido), se sube el nivel de a uno
                            // hasta que deje de estarlo, sin pasar de Nv.100 ni del presupuesto
                            // total de análisis de este intento de creación.
                            // Gen4-7: además del caso "solo MoveEvoFCombination", las evoluciones por movimiento
                            // que se aprende por nivel (Ambipom en Gen6, desde un Aipom salvaje Nv.30) salen
                            // con EncInvalid por quedar por debajo del nivel de evolución: también se sube el nivel.
                            var knowMoveByLevelGen4To7 = save.Generation is >= 4 and < 8 && _lastEvoKnowMoveByLevel
                                                         && (!IsFreeOfInvalidChecks(built) || CountInvalidMoves(built) > 0);
                            if (save.Generation >= 4 && built.Species != enc.Species
                                && (movesNeedFix || HasOnlyEvolutionMoveIssue(built) || knowMoveByLevelGen4To7))
                            {
                                // Gen9: el moveset debe regenerarse en cada nivel probado (Yanmega,
                                // Dudunsparce: los slots vacíos/ilegales dependen del nivel) y se
                                // exige análisis COMPLETO válido, movimientos incluidos (que
                                // IsFreeOfInvalidChecks no mira).
                                var moveAware = save.Generation >= 8;
                                var snapshot = moveAware ? built.Clone() : null;
                                // En Gen8 SetMoveset elige al azar entre movimientos aprendibles y no
                                // siempre cae en un conjunto legal (huevos evolucionados de Espada/
                                // Escudo: Tangrowth, Tsareena, Grapploct) — se reintenta por nivel.
                                // Presupuesto propio: no consume el de candidatos.
                                var movesPerLevel = moveAware ? (save.Generation >= 9 ? 1 : 6) : 1;
                                for (var probeLevel = built.CurrentLevel + 1;
                                     probeLevel <= 100 && (moveAware ? moveProbeBudget > 0 : analysisBudget-- > 0);
                                     probeLevel++)
                                {
                                    built.CurrentLevel = (byte)probeLevel;
                                    if (!moveAware)
                                    {
                                        built.RefreshChecksum();
                                        if (IsFreeOfInvalidChecks(built) && CountInvalidMoves(built) == 0) return built;
                                        continue;
                                    }
                                    built.Stat_Level = (byte)probeLevel;
                                    for (var m = 0; m < movesPerLevel && moveProbeBudget-- > 0; m++)
                                    {
                                        built.SetMoveset();
                                        built.HealPP();
                                        built.RefreshChecksum();
                                        if (new LegalityAnalysis(built).Valid)
                                            return built;
                                    }
                                }
                                if (snapshot != null) built = snapshot;
                            }
                        }
                        catch
                        {
                            // Esta plantilla puntual no convirtió bien — probar la siguiente en vez
                            // de abortar todo el intento.
                            break;
                        }
                    }
                }

            NextVersionSet: ;
            } // foreach versionSet

            if (firstBuilt != null) return firstBuilt;
        }
        catch
        {
            // GenerateEncounters en sí falló (versión de juego sin datos de encuentro
            // cargados, u otra causa inesperada) — no bloqueamos la creación por esto.
        }
        return null;
    }

    /// <summary>Prueba las formas 1..N de la especie que existen en el juego del save y devuelve
    /// el primer PKM legal cuya forma sea exactamente la pedida; null si ninguna lo logra.
    /// Ver el uso en <see cref="BuildPokemon"/>.</summary>
    private static PKM? TryBuildOtherLegalForm(SaveFile save, int speciesId)
    {
        try
        {
            var formCount = save.Personal[speciesId].FormCount;
            for (byte f = 1; f < formCount; f++)
            {
                if (!save.Personal.IsPresentInGame((ushort)speciesId, f)) continue;
                var candidate = TryBuildFromLegitEncounter(save, speciesId, f);
                if (candidate != null && candidate.Form == f && IsFreeOfInvalidChecks(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Caso raro (tabla sin esa especie, etc.): se sigue con el resultado de forma 0.
        }
        return null;
    }

    /// <summary>True si el PKM no tiene ningún chequeo de legalidad en estado Invalid. Solo se
    /// usa para elegir entre candidatos de creación en Gen4 (ver TryBuildFromLegitEncounter);
    /// los movimientos no cuentan acá (no salen en <c>Results</c>, ver trampa #29) porque un
    /// PKM armado desde un encuentro real ya trae movimientos coherentes con ese encuentro.</summary>
    /// <summary>Completa el Tracker de HOME y el manejador actual cuando PKHeX los marca como faltantes
    /// (TransferTrackerMissing / TransferHandlerFlagRequired): regalos entregados vía HOME (Magearna de
    /// color original) y capturas de Pokémon GO (Gimmighoul Errante). Solo actúa si PKHeX lo pide, así que
    /// nunca asigna Tracker a un Pokémon que debe tenerlo en 0.</summary>
    private static void RepairHomeDeliveryFlags(PKM pkm, SaveFile save)
    {
        var codes = new LegalityAnalysis(pkm).Results
            .Where(r => !r.Valid && r.Judgement == Severity.Invalid).Select(r => r.Result).ToHashSet();
        if (codes.Contains(LegalityCheckResultCode.TransferTrackerMissing) && pkm is IHomeTrack { HasTracker: false } tracked)
            tracked.Tracker = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        if (codes.Contains(LegalityCheckResultCode.TransferHandlerFlagRequired) && pkm.CurrentHandler == 0)
            MarkAsTransferred(pkm, save);
    }

    private static bool IsFreeOfInvalidChecks(PKM pkm)
    {
        var la = new LegalityAnalysis(pkm);
        return !la.Results.Any(r => !r.Valid && r.Judgement == Severity.Invalid);
    }

    /// <summary>True si el ÚNICO chequeo Invalid es EXACTAMENTE "sabe el movimiento
    /// equivocado para evolucionar" (MoveEvoFCombination_0) — el caso que interesa para la
    /// sonda de nivel de más arriba. Cualquier OTRO problema de categoría Evolución (por
    /// ejemplo EvoInvalid — la cadena tomó una rama que no corresponde, como pasaba con
    /// Raichu/Marowak/Exeggutor de Alola antes de este ajuste) NO se arregla subiendo el
    /// nivel — antes se comprobaba solo la categoría (CheckIdentifier.Evolution), y para esos
    /// tres la sonda subía el nivel hasta Nv.100 sin arreglar nada, gastando TODO el
    /// presupuesto de análisis antes de llegar a un candidato distinto que sí funcionaba —
    /// confirmado con PKHeX.Core real: Sol/Luna pasó de 802/802 a 799/802 con el chequeo
    /// amplio, y volvió a 802/802 al acotarlo a este código específico.</summary>
    private static bool HasOnlyEvolutionMoveIssue(PKM pkm)
    {
        var la = new LegalityAnalysis(pkm);
        var invalid = la.Results.Where(r => !r.Valid && r.Judgement == Severity.Invalid).ToList();
        return invalid.Count == 1 && invalid[0].Result == LegalityCheckResultCode.MoveEvoFCombination_0;
    }

    /// <summary>Gen6+: deja el Pokémon como "intercambiado a un amigo y de vuelta" — lo
    /// mínimo para que una evolución por intercambio sea coherente. Se llama UpdateHandler
    /// (API pública de PKHeX.Core que ya usa el editor para esto mismo) dos veces: primero
    /// con un entrenador ficticio distinto del OT, que lo pasa al HT (nombre, género,
    /// amistad base, geolocalización y recuerdo de intercambio los completa PKHeX), y luego
    /// con el dueño original, que devuelve CurrentHandler al OT sin borrar el HT. El
    /// resultado es el mismo estado que deja en el juego real un Pokémon que se intercambió
    /// para evolucionar y volvió a su entrenador.</summary>
    /// <summary>Gen6+: deja como Entrenador Actual (HT) al dueño del save, con el mismo nombre que el
    /// OT — el estado de un Pokémon propio que pasó a Gen6/7 por Banco. UpdateHandler solo
    /// asigna HT si el entrenador NO es el OT (comparando TID/SID/género/nombre), así que se
    /// le pasa uno con el mismo nombre pero SID distinto.</summary>
    /// <summary>Único punto de entrada para actualizar el HT. La mayoría de los formatos que
    /// llevan HT lo exponen por la interfaz <c>IHandlerUpdate</c> (PK5, PK6/PK7 vía G6PKM,
    /// PK8, PA8, PK9, PA9), pero PB8 (BDSP) — sorprendentemente — tiene el mismo método
    /// público <c>UpdateHandler(ITrainerInfo)</c> SIN declarar la interfaz (confirmado
    /// leyendo PKM/PB8.cs de PKHeX.Core 25.11.07: es la única clase con HT que no la
    /// implementa). Sin este segundo `is`, MarkAsTradedByFriend/MarkAsTransferred no hacían
    /// nada en BDSP y las 15 evoluciones por intercambio de Diamante/Perla Brillante
    /// (Alakazam, Machamp, Golem, Gengar...) seguían con "Encounter:EncInvalid".</summary>
    private static void ApplyHandlerUpdate(PKM entity, ITrainerInfo handler)
    {
        switch (entity)
        {
            case IHandlerUpdate h: h.UpdateHandler(handler); break;
            case PB8 pb8: pb8.UpdateHandler(handler); break;
        }
    }

    private static void MarkAsTransferred(PKM entity, SaveFile save)
    {
        var handler = new SimpleTrainerInfo(entity.Version)
        {
            OT       = save.OT,
            TID16    = save.TID16,
            SID16    = (ushort)(save.SID16 ^ 0x5A5A),
            Gender   = save.Gender,
            Language = entity.Language,
        };
        ApplyHandlerUpdate(entity, handler);
    }

    private static void MarkAsTradedByFriend(PKM entity)
    {
        var owner = new SimpleTrainerInfo(entity.Version)
        {
            OT       = entity.OriginalTrainerName,
            TID16    = entity.TID16,
            SID16    = entity.SID16,
            Gender   = entity.OriginalTrainerGender,
            Language = entity.Language,
        };
        var friend = new SimpleTrainerInfo(entity.Version)
        {
            OT       = "Amigo",
            TID16    = (ushort)(entity.TID16 ^ 0x5A5A),
            SID16    = (ushort)(entity.SID16 ^ 0xA5A5),
            Gender   = (byte)(entity.OriginalTrainerGender ^ 1),
            Language = entity.Language,
        };
        ApplyHandlerUpdate(entity, friend);
        ApplyHandlerUpdate(entity, owner);
    }

    /// <summary>Gen4+: al cambiar <c>Species</c> por la evolución, tres campos quedan del
    /// Pokémon anterior y PKHeX los marca Invalid (confirmado corriendo las 493 especies de
    /// Gen4 contra PKHeX.Core real: sin esto salían ~240 ilegales, casi todas por Apodo):
    /// - Apodo: el flag IsNicknamed=false exige que el texto sea EXACTAMENTE el nombre de la
    ///   especie actual — el huevo/salvaje traía "Turtwig" y ahora es Torterra. En Gen3 PK3
    ///   no pasa (IsNicknamed se deriva de comparar el texto), por eso solo se aplica a
    ///   Format >= 4, donde es un bit propio.
    /// - Habilidad: el slot (bit 0 del PID en Gen3-5) tiene que resolver a una habilidad de
    ///   la especie nueva (ej. Metapod→Butterfly cambia Shed Skin por Compound Eyes).
    /// - Género: en Gen3-5 se deriva del PID según la proporción de la especie NUEVA, que
    ///   puede diferir de la anterior (Azurill→Marill).
    /// Wurmple→Silcoon/Cascoon (rama decidida por el EC/PID) NO se arregla acá — se resuelve
    /// reintentando la conversión en el llamador.</summary>
    private static void FixupAfterEvolution(PKM built, IEncounterTemplate enc)
    {
        if (!built.IsNicknamed)
            built.Nickname = SpeciesName.GetSpeciesNameGeneration(
                built.Species, built.Language, (byte)built.Generation);
        FixupAbilityToMatchEncounter(built, enc);
        built.Gender = EntityGender.GetFromPID(built.Species, built.PID);

        // Let's Go Pikachu/Eevee (PB7): tres datos derivados que quedan desactualizados al
        // cambiar de especie y de nivel (confirmado contra PKHeX.Core: 39 de las 153 especies
        // salían con Encuentro/AVs inválidos): altura y peso absolutos y CP (dependen de la
        // especie), y los Valores de Despertar (AVs), que PKHeX exige >= a un mínimo que
        // crece con el nivel (AwakenedValueVerifier — "al subir de nivel se otorgan AVs, así
        // que el Pokémon debe estar en o por encima"). PB7 ya trae los dos métodos justos.
        RefreshLetsGoDerivedValues(built);
        RefreshScaledSize(built);
        // El moveset de Leyendas: Arceus (Gen8a) se recalcula más abajo, en el bucle que
        // llama a este método — no solo tras evolucionar, así que no hace falta acá (ver
        // RefreshMovesetForRigidLevelUpGames).
    }

    /// <summary>La mayoría de los juegos permiten que un Pokémon "olvide" movimientos de nivel
    /// (slots vacíos o con movimientos viejos son legales — PKHeX lo acepta con tal de que al
    /// menos un slot no sea de huevo, ver LearnVerifier.VerifyMoves), así que armar el moveset
    /// una sola vez al construirlo no daba problema — salvo en Leyendas: Arceus
    /// (EntityContext.Gen8a): ahí el juego real completa el moveset de CUALQUIER Pokémon
    /// salvaje con sus últimos movimientos de nivel al capturarlo, sin huecos ni movimientos
    /// "viejos" de una etapa anterior — PKHeX solo perdona los slots vacíos para Gen9a
    /// (Leyendas Z-A), no para Gen8a (ver el "if (pk.Context is EntityContext.Gen9a...)" en
    /// LearnVerifier.Verify). Confirmado con PKHeX.Core real, DOS causas distintas: 31
    /// especies evolucionadas por TryEvolveForward (Golbat, Kadabra, Alakazam...) conservaban
    /// el moveset incompleto de su pre-evolución; y por separado, unas 14 especies con
    /// encuentro PROPIO sin evolucionar (Vaporeon, Typhlosion, la línea de Wurmple...)
    /// también salían con "UnobtainableExpect" — su propio candidato de encuentro no les
    /// completaba los 4 slots al nivel final. Por eso se llama para TODO candidato en Gen8a
    /// (ver el bucle de más arriba), no solo tras evolucionar.
    ///
    /// Se recalcula con la misma fuente que ya usa el fallback armado a mano en BuildPokemon,
    /// pero con un detalle propio de Gen8a que la primera versión de este método pasó por
    /// alto: además de "levelup" (movimiento de nivel normal), la tabla `Learnsets` ya trae un
    /// segundo LearnMethod llamado "evolution" — el movimiento EXTRA que el juego real le
    /// regala a cualquier Pokémon evolucionado apenas evoluciona (documentado en el propio
    /// PKHeX.Core: Learnset.SetEvolutionMoves, y expuesto como nivel "0" al leer
    /// LearnSource8LA.Instance.GetLearnset). La base de datos del proyecto YA tenía esas 28
    /// filas cargadas para Leyendas: Arceus (confirmado consultándola directo) — el hueco
    /// estaba en esta consulta, que solo pedía "levelup" e ignoraba "evolution", así que
    /// Vaporeon, Jolteon, Flareon, Espeon, Umbreon, Leafeon, Glaceon, Sylveon, Silcoon,
    /// Cascoon, Beautifly, Dustox y Bibarel perdían justo ese movimiento — no hacía falta
    /// tocar la base de datos, sino esta línea. El movimiento de evolución (si existe) va
    /// SIEMPRE primero, y los de "levelup" hasta el nivel actual completan el resto.
    ///
    /// TRAMPA #30 — <c>PokemonDatabase.GetLearnsets</c> no filtra por <c>FormId</c> (consulta
    /// solo por especie y juego; es la misma trampa de aprendizaje-por-forma ya documentada
    /// para la pestaña Learnsets). Para Typhlosion en Leyendas: Arceus eso resultó SER la
    /// solución: su forma 0 (Kanto) no tiene NINGUNA fila propia ahí, así que la consulta sin
    /// filtro devuelve directo los datos de su forma 1 (Hisui), que es la que en verdad se
    /// construye. Pero para Vulpix/Ninetales (que sí tienen filas para AMBAS formas — Kanto y
    /// Alola — bajo el mismo SpeciesId) el resultado mezcla movimientos de las dos, generando
    /// un moveset que ni siquiera coincide con lo que el propio juego les daría. Confirmado
    /// con PKHeX.Core real: antes de este método ambos ya salían legales por su cuenta;
    /// tras el recálculo salían con DOS movimientos inválidos por la mezcla. Por eso, antes de
    /// aceptar el resultado, se compara cuántos movimientos quedan inválidos contra el
    /// moveset ORIGINAL (el que trajo el propio candidato de encuentro) — si el recálculo no
    /// mejora la cuenta, se revierte. No es una solución al problema de fondo (eso requiere
    /// que GetLearnsets filtre por forma, tarea de la fase de formas) pero evita que este
    /// método empeore algo que ya andaba bien.</summary>
    private static void RefreshMovesetForRigidLevelUpGames(PKM built)
    {
        // Espada/Escudo (Gen8): no tiene la regla rígida, pero sus huevos evolucionados (Lickilicky,
        // Mamoswine...) quedaban con slots vacíos; ahí solo se aplica el último recurso.
        if (built.Context is EntityContext.Gen8)
        {
            FallbackToSuggestedMoveset(built);
            return;
        }
        if (built.Context is not (EntityContext.Gen8a or EntityContext.Gen9)) return;
        RefreshMovesetFromDatabase(built);
        FallbackToSuggestedMoveset(built);
    }

    /// <summary>Último recurso: si tras el recálculo con nuestra tabla Learnsets aún quedan movimientos
    /// inválidos, usa el moveset sugerido por el propio PKHeX.Core para ESTE encuentro
    /// (<c>PKM.SetMoveset</c>). En Legends: Arceus resolvió 27 de las 37 especies que seguían con
    /// "UnobtainableExpect" (Golbat, Kadabra, Quilava, Bibarel, Vaporeon...): nuestra tabla no coincide
    /// siempre con la de PKHeX en esas líneas evolutivas (o no tiene filas y el recálculo no corre).
    /// Solo se adopta si deja MENOS movimientos inválidos.</summary>
    private static void FallbackToSuggestedMoveset(PKM built)
    {
        var invalidNow = CountInvalidMoves(built);
        if (invalidNow == 0) return;
        try
        {
            var suggested = built.Clone();
            suggested.SetMoveset();
            suggested.HealPP();
            if (CountInvalidMoves(suggested) < invalidNow)
            {
                built.SetMoves([suggested.Move1, suggested.Move2, suggested.Move3, suggested.Move4]);
                built.HealPP();
            }
        }
        catch
        {
            // sin sugerencia utilizable: se deja el moveset como estaba
        }
    }

    private static void RefreshMovesetFromDatabase(PKM built)
    {
        // Gen9 (Escarlata/Púrpura) resultó tener la MISMA regla rígida que Gen8a — confirmado
        // leyendo LearnVerifier.Verify: el perdón de slots vacíos ahí es literalmente
        // "if (pk.Context is EntityContext.Gen9a...)" (Leyendas Z-A), Gen9 a secas NO entra.
        // Se notó con Hydrapple (Applin→Dipplin→Hydrapple): tras inyectarle Dragón Ánimo (ver
        // más abajo en TryEvolveForward) seguía inválido por el slot 3, vacío, con
        // "EmptyInvalid" — el mismo síntoma exacto que Golbat en Leyendas: Arceus.
        if (built.Context is not (EntityContext.Gen8a or EntityContext.Gen9)) return;

        var db          = PokemonDatabase.Instance;
        var gameId      = MapVersionToGameId(built.Version);
        var learnsets   = db.GetLearnsets(built.Species, gameId, LearnsetFormFor(built.Species, built.Form));
        var evoMoveName = learnsets.FirstOrDefault(l => l.LearnMethod == "evolution")?.MoveName;

        var moveNames = learnsets
            .Where(l => l.LearnMethod == "levelup" && l.LearnLevel <= built.CurrentLevel)
            .OrderByDescending(l => l.LearnLevel)
            .Select(l => l.MoveName)
            .Distinct()
            .Take(evoMoveName is null ? 4 : 3)
            .Reverse()
            .ToList();
        if (evoMoveName is not null)
            moveNames.Add(evoMoveName); // último slot: el movimiento de evolución, si lo tiene
        if (moveNames.Count == 0) return; // sin datos — se deja el moveset como estaba

        Span<ushort> original = [built.Move1, built.Move2, built.Move3, built.Move4];
        var invalidBefore = CountInvalidMoves(built);

        Span<ushort> moves = stackalloc ushort[4];
        for (var i = 0; i < moveNames.Count; i++)
            moves[i] = (ushort)(db.GetMoveByName(moveNames[i])?.Id ?? 0);
        built.SetMoves(moves);
        built.HealPP();

        if (CountInvalidMoves(built) > invalidBefore)
        {
            // El recálculo empeoró las cosas — probablemente choque de formas con
            // aprendizajes mezclados (ver TRAMPA #30 arriba). Revertir al moveset original.
            built.SetMoves(original);
            built.HealPP();
        }
    }

    /// <summary>Leyendas: Z-A (PA9) guarda por cada movimiento "Plus" si ya fue dominado. PKHeX
    /// exige que estén marcados todos los que el Pokémon ya pudo dominar a su nivel actual
    /// (PlusMoveSufficientLevelMissing_0): un Pidgeot Nv.36 o un Tyrantrum Nv.39 salían con 9-14
    /// alertas de este tipo, porque ConvertToPKM solo marca los de su propio encuentro, no los
    /// de los niveles que se saltó al evolucionar o al subirlo al nivel mínimo. Se le pide a
    /// PKHeX.Core que los calcule (<c>PlusRecordApplicator.SetPlusFlags</c>, la misma API de su
    /// editor, sin Semilla de Maestría ni MT). No hace nada si el PKM no es PA9.</summary>
    private static void RepairPlusFlags(PKM pkm)
    {
        if (pkm is not PA9 pa9) return;
        try
        {
            var la = new LegalityAnalysis(pa9);
            if (pa9.PersonalInfo is IPermitPlus permit)
                pa9.SetPlusFlags(permit, la, seedOfMastery: false, tm: false);
        }
        catch
        {
            // Sin datos de Plus para esta especie: se deja como está.
        }
    }

    /// <summary>Gen6+ guarda los "movimientos a recordar" (Relearn) en el propio PKM. Cuando
    /// un huevo evoluciona (Bonsly → Sudowoodo, por ejemplo) los que trajo el huevo dejan de
    /// coincidir con lo que PKHeX espera para la especie final: Sudowoodo sale de un huevo de
    /// Bonsly con solo Lágrimas Falsas y Copión, y PKHeX pide además Coleada y Lanzarrocas —
    /// confirmado con PKHeX.Core real en Espada/Escudo y Diamante Brillante/Perla Reluciente
    /// (30 de 30 intentos fallaban). No es un dato que falte en la base de datos: los
    /// aprendizajes de Sudowoodo en tu DB son idénticos a los de PKHeX. Se resuelve pidiéndole
    /// al propio PKHeX los Relearn que sugiere para este PKM
    /// (<c>LegalityAnalysis.GetSuggestedRelearnMoves</c>, la misma API que usa su editor) y
    /// aplicándolos solo si hay alguno inválido. No hace nada en Gen1-5 (sin Relearn).</summary>
    private static void RepairRelearnMoves(PKM pkm)
    {
        if (pkm.Format < 6) return;

        pkm.RefreshChecksum();
        var la = new LegalityAnalysis(pkm);
        var anyInvalid = false;
        foreach (var r in la.Info.Relearn)
            if (!r.Valid) { anyInvalid = true; break; }
        if (!anyInvalid) return;

        Span<ushort> suggested = stackalloc ushort[4];
        la.GetSuggestedRelearnMoves(suggested);
        pkm.SetRelearnMoves(suggested);
    }

    /// <summary>Cuenta cuántos de los 4 movimientos actuales del PKM son inválidos según
    /// PKHeX — usado solo por RefreshMovesetForRigidLevelUpGames para decidir si su propio
    /// recálculo mejoró o empeoró el moveset original.</summary>
    private static int CountInvalidMoves(PKM pkm)
    {
        pkm.RefreshChecksum();
        var la = new LegalityAnalysis(pkm);
        var count = 0;
        foreach (var m in la.Info.Moves)
            if (!m.Valid) count++;
        return count;
    }

    /// <summary>Let's Go (PB7): recalcula los datos que dependen de especie y nivel — Valores
    /// de Despertar mínimos, CP, altura y peso absolutos. Hay que llamarlo cada vez que se
    /// cambia Species o CurrentLevel de un PB7 ya construido; si no, PKHeX lo marca con
    /// Encuentro/AVs inválidos (Caterpie, Weedle, Dragonite y Mew, que no evolucionan pero
    /// sí suben a Nv.5 mínimo, fallaban por esto). No hace nada en otros formatos.</summary>
    /// <summary>Fuerza la habilidad correcta cuando el encuentro exige una fija: PRIMERA,
    /// SEGUNDA u OCULTA (AbilityPermission.OnlyFirst/OnlySecond/OnlyHidden — las tres son
    /// "un único slot posible", no solo la oculta). Necesario porque el conversor de PKHeX
    /// solo respeta la fija cuando se le pasa un EncounterCriteria explícito con Ability=esa
    /// permission; con el criterio por defecto que usa esta búsqueda, la habilidad sale del
    /// PID sin mirar qué exige el encuentro (confirmado con PKHeX.Core: Blissey de Leyendas
    /// Arceus, que exige oculta, salía con "AbilityHiddenFail" en las 12 conversiones del
    /// reintento — no era cuestión de azar). Ojo: acá NO se puede simplificar a "si no es
    /// oculta, usar PID&1" — Diancie exige PRIMERA (su único slot no-oculto) y con esa
    /// simplificación salía con "AbilityMismatch" la mitad de las veces, cuando el PID caía
    /// en el slot 1.</summary>
    private static void FixupAbilityToMatchEncounter(PKM built, IEncounterTemplate enc)
    {
        if (enc is IFixedAbilityNumber { Ability: var permission } && permission.IsSingleValue(out var index))
            built.RefreshAbility(index);
        else
            built.RefreshAbility((int)(built.PID & 1));
    }

    private static void RefreshLetsGoDerivedValues(PKM pkm)
    {
        if (pkm is not PB7 letsGo) return;
        letsGo.AwakeningMinimum();
        letsGo.ResetCalculatedValues();
    }

    /// <summary>Gen8: PA8 (Leyendas Arceus) trae altura y peso absolutos, y algunos formatos
    /// además el CP — todos calculados a partir de la especie y quedan desactualizados al
    /// evolucionar o al subir el nivel de creación (confirmado contra PKHeX.Core: Raichu,
    /// Golbat, Ambipom y otras 20+ especies de Leyendas Arceus salían con "StatIncorrectHeight"
    /// / "StatIncorrectWeight"). PB7 tiene su propio camino (RefreshLetsGoDerivedValues, con
    /// un mínimo de AVs distinto) y no pasa por acá.</summary>
    private static void RefreshScaledSize(PKM pkm)
    {
        if (pkm is IScaledSizeValue sized)
        {
            sized.ResetHeight();
            sized.ResetWeight();
        }
        if (pkm is ICombatPower cp)
            cp.ResetCP();
    }

    /// <summary>Nivel de creación: al menos CreationLevel, sin bajar el que ya traiga el
    /// PKM (ver el comentario largo en BuildPokemon). En Let's Go además refresca los
    /// valores derivados del nivel.</summary>
    private static void ApplyMinimumCreationLevel(PKM pkm)
    {
        // Se asigna SIEMPRE (aunque el valor no cambie), igual que antes de este helper: el
        // setter de CurrentLevel también recalcula Exp al mínimo del nivel.
        pkm.CurrentLevel = (byte)Math.Max(CreationLevel, (int)pkm.CurrentLevel);
        RefreshLetsGoDerivedValues(pkm);
        RefreshScaledSize(pkm);
    }

    /// <summary>True si la base de datos del proyecto tiene registrado el movimiento
    /// <paramref name="moveId"/> como "levelup" para <paramref name="speciesId"/> en el juego
    /// correspondiente a <paramref name="version"/>, en cualquier nivel. Usado solo para
    /// decidir, dentro de TryEvolveForward, si conviene esperar a la sonda de nivel del
    /// llamador (cuando sí es un movimiento de nivel) o inyectar el movimiento directo
    /// (cuando no lo es — solo se consigue por MT/tutor, como Dragón Ánimo en Dipplin).</summary>
    private static bool IsLevelUpLearnable(ushort speciesId, GameVersion version, ushort moveId, byte form = 0)
    {
        var db       = PokemonDatabase.Instance;
        var gameId   = MapVersionToGameId(version);
        var moveName = db.GetMove(moveId)?.Name;
        if (moveName is null) return false;
        return db.GetLearnsets(speciesId, gameId, LearnsetFormFor(speciesId, form)).Any(l => l.LearnMethod == "levelup" && l.MoveName == moveName);
    }

    /// <summary>FormId a pasarle a <c>PokemonDatabase.GetLearnsets</c> para un PKM concreto: la
    /// forma solo si es una forma regional del <see cref="RegionalFormCatalog"/> (donde se
    /// verificó que <c>Learnsets.FormId</c> coincide con el índice de forma de PKHeX); null
    /// para todo lo demás (formas cosméticas, de combate, forma 0), que mantiene el
    /// comportamiento previo sin filtro — ver TRAMPA #30 en el docblock de
    /// <see cref="RefreshMovesetForRigidLevelUpGames"/>.</summary>
    public static int? LearnsetFormFor(int species, byte form) =>
        form != 0 && RegionalFormCatalog.All.Any(f => f.Species == species && f.Form == form) ? form : null;

    /// <summary>True si, siguiendo la cadena evolutiva hacia adelante desde
    /// (<paramref name="species"/>, <paramref name="form"/>), en como máximo
    /// <paramref name="depth"/> pasos más se llega a <paramref name="target"/>. Solo se usa para
    /// elegir rama en TryEvolveForward cuando hay varias y ninguna es el destino directo.</summary>
    private static bool ReachesSpecies(IEvolutionForward forward, ushort species, byte form, int target, int depth)
    {
        if (species == target) return true;
        if (depth <= 0) return false;
        var options = forward.GetForward(species, form).ToArray();
        foreach (var o in options)
            if (ReachesSpecies(forward, o.Species, o.GetDestinationForm(form), target, depth - 1))
                return true;
        return false;
    }

    /// <summary>Evoluciona "hacia adelante" un PKM ya construido desde un encuentro real
    /// (ej. un Kadabra salvaje) hasta la especie pedida (ej. Alakazam), cuando esa especie
    /// pedida no tiene ningún encuentro propio y solo se puede llegar a ella evolucionando
    /// algo capturable. Recorre el árbol de evolución correcto para la generación del PKM
    /// (<c>EvolutionTree.GetEvolutionTree(built.Context)</c> — así se elige solo Evolves1
    /// o Evolves2, etc., sin necesidad de mapear generación→árbol a mano) paso a paso desde
    /// la especie ya construida:
    /// - Si el paso es por intercambio (Kadabra→Alakazam en Gen1, etc.): no exige ningún
    ///   nivel — evolucionar por intercambio (entre dos cartuchos propios, sin necesidad de
    ///   un NPC de intercambio programado) es válido a cualquier nivel. Ver
    ///   GBRestrictions.IsTradeEvolution1 en PKHeX.Core para la lista de Gen1.
    /// - Si el paso es por nivel (Omanyte→Omastar Nv.40 en Gen1, etc.): acumula el nivel
    ///   mínimo necesario (Math.Max, nunca hacia abajo respecto al nivel que ya traía el
    ///   encuentro real).
    /// - Si el paso es por Belleza (Feebas→Milotic en Gen3): acumula el umbral de Belleza
    ///   mínimo necesario, de forma análoga al nivel — ver más abajo, es el único caso
    ///   nuevo de Gen3 que sí hace falta reforzar a mano.
    /// - Si hay más de una evolución posible desde la misma especie (Eevee→Vaporeon/
    ///   Jolteon/Flareon en Gen1, o esas tres más Espeon/Umbreon en Gen2, o Nincada→
    ///   Ninjask/Shedinja en Gen3): se prioriza la opción que coincide directo con la
    ///   especie pedida. Bug real encontrado: antes se tomaba siempre la primera opción a
    ///   ciegas, así que Vaporeon/Jolteon salían ilegales (la cadena terminaba en la
    ///   especie equivocada) mientras que Flareon sí funcionaba, por pura casualidad de
    ///   qué opción resultaba ser "la primera" en los datos de PKHeX.Core. Esto solo
    ///   resuelve ramificaciones TERMINALES (todas las que maneja hoy este método lo son)
    ///   — una rama a mitad de camino que requiera mirar más adelante en la cadena para
    ///   saber cuál seguir no está contemplada.
    /// Si la cadena no llega a la especie pedida en, como mucho, 3 pasos (el máximo posible
    /// en una cadena evolutiva de Gen1/Gen2/Gen3), devuelve false sin tocar nada.
    ///
    /// Gen2 — verificado a mano parseando el binario real de evoluciones de PKHeX.Core
    /// (evos_g2.pkl) antes de escribir esto, porque no quería asumir cómo se codifican
    /// amistad/amistad+hora sin comprobarlo:
    /// - Golbat→Crobat (amistad) está codificado como LevelUp con Nivel=0 — PKHeX NO
    ///   guarda ni verifica ningún umbral numérico de amistad en esta estructura, así que
    ///   no hace falta (ni se puede) reforzar CurrentFriendship acá.
    /// - Eevee→Espeon/Umbreon (amistad+hora) también son LevelUp con Nivel=0 cada uno, sin
    ///   ninguna distinción de hora del día codificada — se resuelven igual que cualquier
    ///   otra rama terminal (Eevee→piedras), sin necesidad de tocar MetTimeOfDay ni nada.
    /// - Politoed/Slowking/Steelix/Scizor/Kingdra (intercambio + objeto sostenido) están
    ///   codificados como UseItem — misma categoría "instantánea, sin nivel" que Trade —
    ///   así que tampoco exigen nada especial ni hace falta dejarles el objeto puesto.
    /// En esos tres casos, la conclusión es la misma que ya valía para los intercambios de
    /// Gen1: PKHeX no vuelve a verificar la "evidencia" de la evolución después del hecho
    /// (ni amistad, ni hora, ni item sostenido) — solo le importa que la especie sea
    /// alcanzable por ALGUNA cadena válida.
    ///
    /// Gen3 — mismo cuidado de verificar antes de asumir, esta vez con un resultado
    /// DISTINTO a los anteriores:
    /// - Nincada→Ninjask/Shedinja (evolución partida): sin relación de PID con un
    ///   "hermano" exigida en este contexto (ese glitch específico es exclusivo de Gen4,
    ///   confirmado revisando GenderVerifier de PKHeX.Core — ahí el chequeo está acotado a
    ///   pk.Format == 4). Se resuelve como cualquier otra rama terminal.
    /// - Clamperl→Huntail/Gorebyss, Seadra→Kingdra (intercambio + objeto): mismo patrón
    ///   UseItem/TradeHeldItem que Gen2, sin nivel exigido.
    /// - Feebas→Milotic (Belleza) — ACÁ SÍ HAY EXCEPCIÓN: a diferencia de amistad/
    ///   intercambio+objeto, PKHeX vuelve a verificar esto después del hecho. Confirmado
    ///   en EvolutionMethod.cs de PKHeX.Core: "LevelUpBeauty when pk is
    ///   IContestStatsReadOnly s && s.ContestBeauty < Argument => ... LowContestStat".
    ///   El umbral (170) vive en Argument, NO en Level (Level=0 para esta entrada) — por
    ///   eso hace falta un acumulador aparte (requiredBeauty) en vez de reusar
    ///   requiredLevel. IContestStats no está en la clase base PKM (recién aparece en
    ///   Gen3+), así que se aplica con cast condicional al final, sin romper nada para
    ///   PK1/PK2 si este método alguna vez se llama para ellos.
    ///
    /// Al final, CatchRate y Tipo1/Tipo2 NO se tocan a mano para PK1: el setter real de
    /// <c>PKM.Species</c> ahí (SetSpeciesValues, confirmado en PK1.cs de PKHeX.Core) ya
    /// actualiza Tipo1/Tipo2 solo según la especie nueva, y preserva el CatchRate original
    /// si sigue siendo válido para alguna pre-evolución en la cadena — el cartucho real
    /// nunca "recalcula" la tasa de captura al evolucionar. PK2/PK3 no tienen este
    /// mecanismo (su setter de Species es una asignación directa sin efectos secundarios)
    /// pero tampoco lo necesitan: a partir de Gen2 el tipo se deriva de la tabla de
    /// especie en vez de guardarse por individuo, así que no hay Tipo1/Tipo2 que se quede
    /// pisado, y ninguna generación después de Gen1 tiene un mecanismo tipo CatchRate-
    /// como-marca-de-intercambio que preservar. En todos los formatos alcanza con asignar
    /// Species por la propiedad real (nunca un campo interno) y llamar ResetPartyStats()
    /// para que las stats guardadas reflejen la especie/nivel finales.
    ///
    /// Alcance: habilitado para Gen1 a Gen6 (ver el "if (save.Generation is >= 1 and <= 6"
    /// en el llamador, TryBuildFromLegitEncounter). Gen4, Gen5 y Gen6 se verificaron completas
    /// contra PKHeX.Core (493, 649 y 721 especies sin alertas); ver FixupAfterEvolution y
    /// MarkAsTradedByFriend para lo que hay que corregir tras cambiar la especie. Sigue sin
    /// habilitar en Gen7+ (evolución en batalla, formas regionales, etc. — no investigado
    /// todavía) y las cadenas con rama no terminal (Wurmple→Cascoon→Dustox) siguen tomando la
    /// primera rama.
    ///
    /// Bug real encontrado DESPUÉS de probar Milotic con el fix de Belleza de arriba:
    /// no alcanza con subir ContestBeauty a mano — en el juego real, cada Pokébloque que
    /// sube una estadística de concurso TAMBIÉN sube el "Brillo" (Sheen) de forma
    /// correlacionada, y PKHeX vuelve a verificar esa correlación (ContestSheenGEQ_0 —
    /// confirmado en ContestStatVerifier.cs/ContestStatInfo.cs de PKHeX.Core, con una
    /// fórmula de mínimo/máximo bastante intrincada que depende de las 5 estadísticas de
    /// concurso a la vez, no solo Belleza). En vez de reimplementar esa fórmula a mano
    /// (alto riesgo de errores), se usa el helper que ya trae PKHeX.Core para esto mismo —
    /// SetSuggestedContestStats — que además tiene un caso especial hardcodeado
    /// específicamente para Milotic (confirmado leyendo el código: pone las 5
    /// estadísticas Y el Brillo al máximo (255) a la vez, la combinación más simple que
    /// siempre cae dentro del rango válido). Ese método necesita un IEncounterTemplate
    /// (el propio encuentro real, ya lo tenemos disponible en el llamador) y un
    /// EvolutionHistory — confirmado que un "new EvolutionHistory()" vacío alcanza para
    /// Gen3 (su constructor sin parámetros dejan todo en None/false, y
    /// GetContestStatRestriction ni siquiera lo consulta para pk.Format < 6).</summary>
    /// <summary>True si la última llamada a <see cref="TryEvolveForward"/> pasó por una evolución que
    /// exige CONOCER un movimiento (Ambipom, Tangrowth, Yanmega, Mamoswine, Tsareena...): en Gen4-7 el
    /// Pokémon recién evolucionado queda por debajo del nivel en que se aprende ese movimiento y se
    /// arregla subiendo el nivel (sonda de nivel). ThreadStatic: el constructor puede correr en un hilo de background.</summary>
    [ThreadStatic] private static bool _lastEvoKnowMoveByLevel;

    private static bool TryEvolveForward(PKM built, int targetSpeciesId, IEncounterTemplate enc, byte targetForm = 0)
    {
        _lastEvoKnowMoveByLevel = false;
        var forward = EvolutionTree.GetEvolutionTree(built.Context).Forward;
        var currentSpecies = built.Species;
        var currentForm = built.Form;
        var requiredLevel = built.CurrentLevel;
        var neededBeauty = false;
        var neededTrade  = false;
        ushort neededKnownMove = 0;
        var neededFormArgument = false;
        var originalSpeciesForFormArgument = built.Species;

        if (currentSpecies == targetSpeciesId)
            return false; // no debería llamarse en este caso — por las dudas, no hacer nada

        for (var step = 0; step < 3; step++) // 3 = cadena evolutiva más larga posible en Gen1/Gen2/Gen3
        {
            var options = forward.GetForward(currentSpecies, currentForm).Span;
            if (options.Length == 0)
                return false; // esta cadena no evoluciona más — callejón sin salida

            // Bug real encontrado: Vaporeon y Jolteon salían ilegales pero Flareon no.
            // Acá se tomaba siempre options[0] a ciegas, asumiendo que nunca hay
            // ramificaciones — pero Eevee evoluciona a varias especies distintas desde el
            // mismo punto (y en Gen3, Nincada también), y options[0] resultaba ser siempre
            // la misma (la que casualmente coincidía con Flareon). Ahora: si alguna opción
            // coincide directo con la especie pedida, se prioriza esa — cubre exactamente
            // las ramificaciones terminales tipo Eevee/Nincada. Si hay más de una opción y
            // ninguna coincide directo (una rama a mitad de camino, no terminal), no
            // sabemos por cuál seguir sin mirar más adelante en la cadena — se mantiene
            // options[0] como antes en ese caso (no debería pasar con las especies que
            // maneja hoy este método: todas sus ramificaciones son terminales, no
            // intermedias).
            // Gen7+: una misma especie puede tener DOS opciones hacia el mismo destino con
            // forma distinta (Pikachu→Raichu de Kanto por el Portal Ultra vs. Raichu de Alola
            // por Piedra Trueno; Exeggcute→Exeggutor igual; Rattata/Meowth/Diglett/Vulpix...
            // con su variante regional). Se prefiere la opción cuya forma destino es la PEDIDA
            // (targetForm): por defecto la 0 (la de siempre, así que sin forma pedida nada
            // cambia) y, desde la Fase 2 de formas, la regional cuando se pidió esa (Exeggutor
            // de Alola). Solo se cae a otra si no hay una que coincida. Si esa opción no
            // resulta legal en este juego (ej. el Portal Ultra solo existe en USUM), el
            // llamador sigue con el siguiente candidato/conjunto de versiones — que llega a
            // la evolución clásica de Gen6.
            var next = options[0];
            var found = false;
            foreach (var option in options)
            {
                if (option.Species != targetSpeciesId) continue;
                if (!found || option.GetDestinationForm(currentForm) == targetForm && next.GetDestinationForm(currentForm) != targetForm)
                    next = option;
                found = true;
            }

            // Maushold (y Dudunsparce): dos opciones que en verdad dependen del EC concreto
            // del propio Pokémon (EC100 = 1/100 de probabilidad, ECElse = el resto) — no es
            // ambigüedad real, PKHeX ya sabe por cuál rama pasó CUALQUIER PKM mirando su
            // propio EC. La preferencia "forma 0" de arriba fuerza casi siempre la rama
            // EC100 (Familia de Tres, la rara) sin que el EC ya asignado la cumpla, y PKHeX
            // lo rechaza. Se corrige acá si ambas ramas están presentes: se seguye la que de
            // verdad corresponde al EC que ya tiene el PKM. Confirmado con PKHeX.Core real:
            // Maushold salía "Encounter EncInvalid" de forma intermitente (solo pasaba en la
            // ~1% de las veces que el EC al azar cumplía EC100 por pura suerte).
            if (found)
            {
                EvolutionMethod? ec100 = null, ecElse = null;
                foreach (var option in options)
                {
                    if (option.Method is EvolutionType.LevelUpInBattleEC100 or EvolutionType.LevelUpKnowMoveEC100)
                        ec100 = option;
                    else if (option.Method is EvolutionType.LevelUpInBattleECElse or EvolutionType.LevelUpKnowMoveECElse)
                        ecElse = option;
                }
                if (ec100 is { } a && ecElse is { } b)
                    next = built.EncryptionConstant % 100 == 0 ? a : b;
            }

            // Rama INTERMEDIA (ninguna opción coincide directo, pero varias existen): se elige la
            // que en algún punto más adelante llega a la especie pedida. Antes se tomaba
            // options[0] a ciegas — y en Applin (Flapple / Pastelote / Dipplin, todas por
            // objeto) eso era siempre Flapple, así que Hydrapple (Applin → Dipplin → Hydrapple)
            // nunca se alcanzaba; lo mismo con Dustox (Wurmple → Cascoon → Dustox), que en Gen5
            // se dejó documentado como limitación. Confirmado con PKHeX.Core real: en
            // Escarlata/Púrpura todas las evoluciones de Applin fallaban ("evolve fail").
            if (!found && options.Length > 1)
            {
                foreach (var option in options)
                {
                    if (!ReachesSpecies(forward, option.Species, option.GetDestinationForm(currentForm), targetSpeciesId, 2))
                        continue;
                    next = option;
                    break;
                }
            }

            // Bug real encontrado con Milotic (Feebas de huevo, Nv.5, Esmeralda): salía con
            // un único error de legalidad, "Evolución" (EvoInvalid). Causa confirmada leyendo
            // EvolutionMethod.Check de PKHeX.Core 25.11.07:
            //     if (lvl < levelMin + LevelUp) return InsufficientLevel;
            // donde levelMin es el nivel mínimo del encuentro original (5 para un huevo
            // eclosionado) y LevelUp (1 en todas las generaciones salvo PLA) es cuántos
            // niveles hay que subir DESPUÉS de capturarlo/eclosionarlo para que dispare una
            // evolución "por nivel" (incluye Belleza, amistad y demás LevelUp*). Antes acá
            // solo se usaba el piso absoluto next.Level, que es 0 para Belleza/amistad, así
            // que el Pokémon quedaba en el mismo nivel del encuentro (5) — un Milotic que
            // "evolucionó sin subir ningún nivel", imposible. Ahora cada paso por nivel suma
            // next.LevelUp al nivel acumulado (una vez por paso, igual que
            // EvolutionUtil.ConditionEncounterNoMet en PKHeX) y respeta además el piso
            // absoluto de la especie (Omanyte→Omastar Nv.40, etc.) con Math.Max.
            if (next.Method.IsLevelUpRequired())
                requiredLevel = (byte)Math.Max(requiredLevel + next.LevelUp, next.Level);

            // Gen3 — único caso confirmado donde PKHeX vuelve a verificar la "evidencia"
            // de la evolución después del hecho (ver el docblock de arriba). El umbral
            // real (170) y el cálculo de Brillo correlacionado quedan en manos de
            // SetSuggestedContestStats más abajo — acá solo se registra QUE hizo falta.
            if (next.Method == EvolutionType.LevelUpBeauty)
                neededBeauty = true;

            // Gen6+: una evolución por intercambio exige que el Pokémon HAYA sido intercambiado,
            // y PKHeX lo comprueba con IsUntraded (= nombre del Entrenador Actual/HT vacío):
            // Alakazam, Golem, Politoed, Slowking, Kingdra, Porygon2/Z, Milotic (Prisma
            // Escama), Rhyperior, Electivire, Magmortar, Dusknoir, Gigalith, Conkeldurr,
            // Escavalier, Accelgor, Aromatisse, Slurpuff, Huntail, Gorebyss, Trevenant y
            // Gourgeist salían todos con "Encuentro" inválido en Gen6 (en Gen1-5 no hay
            // campo de HT, por eso allá nunca se notó). Ver MarkAsTradedByFriend.
            if (next.Method.IsTrade())
                neededTrade = true;

            // Gen4+: evoluciones que exigen CONOCER un movimiento concreto — PKHeX guarda ESE
            // movimiento directo en next.Argument (confirmado con PKHeX.Core real: para
            // Dipplin→Hydrapple, next.Argument = 913 = Dragón Ánimo).
            //
            // REGRESIÓN encontrada y corregida al probar esto: inyectar el movimiento a ciegas
            // rompía Ambipom/Yanmega/Mamoswine/Tsareena/Farigiraf/Dudunsparce, que venían
            // funcionando bien desde Gen4 gracias a la sonda de nivel de
            // TryBuildFromLegitEncounter (ver HasOnlyEvolutionMoveIssue) — PKHeX acepta un
            // movimiento puesto a mano solo si puede confirmar ALGÚN origen legítimo para él
            // (nivel, MT, tutor, huevo); Golpe Doble en Nv.5 no tiene ninguno, así que salía
            // "Unobtainable" en vez de arreglar nada. La sonda de nivel, en cambio, SÍ es un
            // origen legítimo (nivel real de aprendizaje) — pero nunca iba a encontrar Dragón
            // Ánimo porque Dipplin no lo aprende por nivel en ningún juego, solo por MT.
            //
            // Por eso acá se decide ANTES cuál de los dos hace falta: si el propio movimiento
            // ya figura como "levelup" para esta especie en algún nivel (nuestra base de
            // datos), se deja que la sonda de nivel de más arriba lo resuelva sin tocar nada
            // acá (evita la regresión). Solo si NO hay ningún nivel que lo enseñe — el caso de
            // Dragón Ánimo — se inyecta directo, como haría el jugador real usando el MT antes
            // de evolucionar.
            if (next.Method is EvolutionType.LevelUpKnowMove
                or EvolutionType.LevelUpKnowMoveECElse or EvolutionType.LevelUpKnowMoveEC100
                && !built.HasMove(next.Argument))
            {
                if (!IsLevelUpLearnable(next.Species, built.Version, next.Argument, next.GetDestinationForm(built.Form)))
                    neededKnownMove = next.Argument;
                _lastEvoKnowMoveByLevel = true;
            }

            // Gen9 — Annihilape (usar Puño Furia 20 veces) y Gholdengo (juntar 999 Gimmimonedas):
            // PKHeX NO lo guarda como un movimiento a aprender sino como un contador en
            // FormArgument (mismo campo que Hoopa/Furfrou) — ambos EvolutionType ya vienen
            // documentados en PKHeX.Core con el comentario de a qué especie corresponden.
            // Confirmado con PKHeX.Core real: salían con "FormArgumentGEQ_0" antes de este
            // ajuste (Gholdengo además con varios otros errores de evento — ver la nota
            // aparte, más abajo, sobre su origen real).
            if (next.Method is EvolutionType.LevelUpUseMoveSpecial or EvolutionType.LevelUpCollect999)
                neededFormArgument = true;

            // Legends: Arceus/Z-A — Wyrdeer (Psyshield Bash en Estilo Ágil), Overqwil (Estilo Fuerte) y
            // Basculegion (294 de daño por retroceso) también guardan un contador en FormArgument, y
            // PKHeX exige el mínimo (FormArgumentGEQ_0). Wyrdeer, además, solo es legal evolucionado
            // desde Nv.31 (FormArgumentVerifier: "Wyrdeer when enc.Species != Wyrdeer && CurrentLevel < 31").
            if (next.Method is EvolutionType.LevelUpRecoilDamageMale or EvolutionType.LevelUpRecoilDamageFemale
                or EvolutionType.UseMoveAgileStyle or EvolutionType.UseMoveStrongStyle)
            {
                neededFormArgument = true;
                if (next.Method == EvolutionType.UseMoveAgileStyle)
                    requiredLevel = (byte)Math.Max(requiredLevel, (byte)31);
            }

            currentSpecies = next.Species;
            currentForm    = next.GetDestinationForm(currentForm);

            if (currentSpecies == targetSpeciesId)
            {
                built.Species      = (ushort)targetSpeciesId;
                built.Form         = currentForm;
                built.CurrentLevel = requiredLevel;
                if (neededBeauty)
                    built.SetSuggestedContestStats(enc, new EvolutionHistory());
                if (neededKnownMove != 0)
                {
                    // Va al último slot; si ya tenía 4 movimientos reales se pisa el último
                    // (mismo criterio simple que el resto del creador: priorizar que la
                    // evolución sea legal por sobre preservar un moveset "ideal").
                    // Sin huecos: si el Pokémon tenía menos de 3 movimientos (Ambipom, Sudowoodo,
                    // Mamoswine... a su nivel solo aprenden 2), poner el requerido en el slot 4
                    // dejaba un slot vacío en el medio y PKHeX marcaba el moveset inválido.
                    // Se compactan los existentes (máx. 3, sin repetir el requerido) y el
                    // requerido va justo después.
                    var keptMoves = new[] { built.Move1, built.Move2, built.Move3, built.Move4 }
                        .Where(m => m != 0 && m != neededKnownMove).Take(3).ToList();
                    keptMoves.Add(neededKnownMove);
                    Span<ushort> movesWithRequired = [0, 0, 0, 0];
                    for (var mi = 0; mi < keptMoves.Count; mi++) movesWithRequired[mi] = keptMoves[mi];
                    built.SetMoves(movesWithRequired);
                    built.HealPP();

                    // Gen9: no alcanza con ponerlo en el moveset. Escarlata/Púrpura guarda,
                    // aparte, qué MTs tiene "desbloqueados" cada PK9 propio (el sistema de
                    // Cuadernos de MT) — sin ese bit, PKHeX lo marca "Unobtainable" aunque el
                    // movimiento esté puesto. Confirmado con PKHeX.Core real: Tsareena
                    // (Pisotón), Dudunsparce (Taladradora) e Hydrapple (Dragón Ánimo) seguían
                    // inválidos hasta marcar el bit correspondiente con SetMoveRecordFlag.
                    if (built is PK9 pk9)
                    {
                        var index = PersonalTable.SV[built.Species, built.Form].RecordPermitIndexes.IndexOf(neededKnownMove);
                        if (index >= 0)
                            pk9.SetMoveRecordFlag(index);
                    }
                }
                if (neededFormArgument)
                    built.SetSuggestedFormArgument(originalSpeciesForFormArgument);
                if (built.Format >= 4)
                    FixupAfterEvolution(built, enc);
                if (neededTrade)
                    MarkAsTradedByFriend(built);
                built.ResetPartyStats();
                return true;
            }
        }

        return false;
    }

    /// <summary>Deriva un género válido a partir del GenderRate estilo PokeAPI de nuestra
    /// propia DB (-1 sin género, 0 siempre macho, 8 siempre hembra, 1-7 proporción sobre 8).
    /// Solo se usa en Gen 6+ (Gen 3-5 lo derivan del PID directamente).</summary>
    private static byte ResolveGender(int? genderRate, Random rng) => genderRate switch
    {
        null or -1 => 2, // sin género
        0          => 0, // siempre macho
        8          => 1, // siempre hembra
        _          => (byte)(rng.Next(8) < genderRate ? 1 : 0),
    };

    /// <summary>
    /// Copia deliberada de PokemonEditorViewModel.MapVersionToGameId (Exxeguttor.UI) — el
    /// original vive en la capa UI y esta capa (Exxeguttor.App) no puede referenciarla
    /// (Exxeguttor.App no depende de Exxeguttor.UI, ver arquitectura en CLAUDE.md). Si se
    /// actualiza uno, actualizar el otro.
    ///
    /// BUG encontrado en sesión de diagnóstico Gen9: comparaba contra números mágicos
    /// (35, 36, 37... 50, 51) copiados de una versión vieja de PKHeX.Core — el valor
    /// entero real de GameVersion en 25.11.7 es otro completamente distinto (confirmado
    /// por reflection: GameVersion.E = 769, no 3), así que esto rompía el moveset de
    /// creación (BuildPokemon) para prácticamente cualquier generación, no solo Gen9.
    /// Ahora compara contra los miembros del enum por nombre en vez de por número.
    ///
    /// Segunda vuelta (sesión de diagnóstico Learnsets Gen5-9): la corrección de arriba
    /// solo cubría el miembro "agrupado" (BW, XY, SWSH, etc.) para Gen5-8, asumiendo que
    /// era lo que devolvía SaveFile.Version — sin confirmar con saves reales. Ahora se
    /// cubre también el miembro individual como respaldo (B/W además de BW, etc.), excepto
    /// S/W/W2 y B2/GN/GP que comparten valor entero entre sí (confirmado por reflection) y
    /// arriesgarían matchear el bucket equivocado — para esos se confía solo en el agrupado.
    ///
    /// `internal` (no `private`) desde la sesión de categorización de Mochila: reutilizado
    /// también por <see cref="ItemInventoryService"/> (mismo assembly, Exxeguttor.App) para
    /// resolver GameId al consultar PokemonDatabase.GetItemUserPouchCategories — evita una
    /// tercera copia dentro del mismo assembly (la duplicación con
    /// PokemonEditorViewModel.MapVersionToGameId, en Exxeguttor.UI, sigue siendo necesaria por
    /// la separación de capas documentada arriba).
    /// </summary>
    internal static int MapVersionToGameId(GameVersion version) => version switch
    {
        // Mismo hueco: faltaba GN (Verde, versión japonesa de Gen1) — ver el conjunto de
        // respaldo RD/GN/YW que ya se usa para Gen2 en TryBuildFromLegitEncounter.
        GameVersion.RB or GameVersion.RD or GameVersion.GN or GameVersion.BU => 16,
        GameVersion.YW                     => 23,
        GameVersion.GS or GameVersion.GD or GameVersion.SI => 9,
        GameVersion.C                      => 4,
        // Mismo hueco: un save de Rubí/Zafiro real informa R o S (nunca el agregado RS), y
        // faltaba S.
        GameVersion.RS or GameVersion.R or GameVersion.S      => 17,
        GameVersion.E                      => 6,
        GameVersion.FRLG or GameVersion.FR or GameVersion.LG => 7,
        GameVersion.CXD                    => 8,
        GameVersion.DP or GameVersion.D or GameVersion.P    => 5,
        GameVersion.Pt                     => 15,
        GameVersion.HGSS or GameVersion.HG or GameVersion.SS => 10,
        // TRAMPA real encontrada esta sesión: un save de verdad de Negro/Blanco o Negro 2/
        // Blanco 2 informa su versión ESPECÍFICA (B, W, B2 o W2) — nunca el valor agregado
        // BW/B2W2, que solo existe para referirse a "cualquiera de los dos" en otro contexto
        // (mismo patrón que la trampa #22 de Gen4 con GameVersion.DP/HGSS). Antes de este
        // arreglo, cualquier consulta a la base de datos con la versión real de un save de
        // Gen5 devolvía GameId=0 (el caso "_" de abajo) — datos de ningún juego real — y
        // fallaba en silencio. Confirmado con PKHeX.Core real: la sonda de movimiento de
        // evolución (ver TryEvolveForward/IsLevelUpLearnable) tomaba a Golpe Doble como "no
        // aprendible por nivel" en Negro 2 (siendo que sí lo es, a Nv.25) e inyectaba el
        // movimiento sin necesidad, rompiendo Ambipom/Yanmega/Mamoswine/Tangrowth/Lickilicky/
        // Sudowoodo — los mismos seis que ya se habían arreglado en la sesión de Gen5.
        GameVersion.BW or GameVersion.B or GameVersion.W       => 2,
        GameVersion.B2W2 or GameVersion.B2 or GameVersion.W2   => 1,
        GameVersion.XY or GameVersion.X or GameVersion.Y    => 22,
        GameVersion.ORAS or GameVersion.OR or GameVersion.AS => 14,
        GameVersion.SM or GameVersion.SN or GameVersion.MN  => 19,
        GameVersion.USUM or GameVersion.US or GameVersion.UM => 21,
        // Mismo hueco: faltaba GP (Let's Go Pikachu) — solo estaba GE (Eevee).
        GameVersion.GG or GameVersion.GE or GameVersion.GP    => 13,
        GameVersion.SWSH or GameVersion.SW or GameVersion.SH => 20,
        GameVersion.BDSP or GameVersion.BD or GameVersion.SP => 3,
        GameVersion.PLA                    => 11,
        GameVersion.ZA                     => 12,
        GameVersion.SL or GameVersion.VL or GameVersion.SV => 18,
        _                                   => 0
    };

    // ── Formas (fase 1: formas de combate reversibles + cosméticas) ──────────────────────────
    //
    // Taxonomía de 4 casos (ver hotfixes/formas_handoff/fase-formas-handoff.md):
    //   1. Forma de combate reversible con cambio de stats/tipo/habilidad
    //      (Giratina, Arceus, Genesect, Tornadus/Thundurus/Landorus, Keldeo, Meowstic, Ogerpon, Silvally)
    //      -> editable libremente desde el combo, con efectos secundarios en ApplyForm.
    //   2. Forma regional/de origen (Marowak de Alola, etc.)
    //      -> NO se excluye especialmente del combo. No es un toggle: está atada al origen
    //         del encuentro/evolución. Si el usuario fuerza una forma incompatible con el
    //         origen real del Pokémon, el Diagnóstico (LegalityAnalysis) la marcará ilegal
    //         de forma natural, igual que lo hace PKHeX. Decisión deliberada.
    //   3. Forma solo de batalla, nunca guardable (Mega, Primal, Castform, Cherrim, etc.)
    //      -> excluida del combo vía FormInfo.IsBattleOnlyForm.
    //   4. Forma cosmética sin impacto en stats/tipo/habilidad (Unown, Vivillon, Minior, etc.)
    //      -> aparece en el combo igual que el caso 1, pero ApplyForm no necesita tocar nada
    //         más allá de pkm.Form.
    //
    // Pendiente (fuera de esta fase, diferido explícitamente): BuildPokemon no permite pedir
    // una forma != 0 al CREAR un Pokémon desde cero (Fase 2 del handoff).
    //
    // Pendiente (deuda técnica relacionada, NO resuelta en esta fase — TRAMPA #30):
    //   RESUELTA para formas regionales: GetLearnsets acepta formId y todos los usos lo
    //   pasan vía LearnsetFormFor. Las formas no regionales (cosméticas, de combate) siguen
    //   sin filtro. El chequeo de "no empeorar" en RefreshMovesetForRigidLevelUpGames se
    //   mantiene como red de seguridad.

    /// <summary>Códigos de idioma de PKHeX que la UI puede producir (ver
    /// PokemonEditorViewModel.GetPKHeXLang). Se usa en <see cref="TryGetFormIndex"/> para
    /// resolver un nombre de forma sin depender del idioma activo de la app.</summary>
    private static readonly string[] FormLanguages = ["en", "es", "fr", "de", "it", "ja", "ko", "zh"];

    /// <summary>
    /// Devuelve las formas seleccionables para el PKM actual (excluyendo las de solo batalla),
    /// o null si la especie no tiene formas múltiples, solo tiene una elegible, o el Pokémon ya es
    /// de una forma regional (que es identidad, no un toggle).
    /// </summary>
    public static List<(byte Index, string Name)>? GetSelectableForms(PKM pkm, GameStrings strings)
        => GetSelectableFormsCore(pkm, strings, onlyLegalOrigin: true);

    /// <summary>Núcleo de <see cref="GetSelectableForms"/>. <paramref name="onlyLegalOrigin"/> = false
    /// devuelve todas las formas guardables sin probar legalidad: lo usa TryGetFormIndex, que solo
    /// necesita traducir un nombre a índice y corre una vez por idioma.</summary>
    private static List<(byte Index, string Name)>? GetSelectableFormsCore(PKM pkm, GameStrings strings, bool onlyLegalOrigin)
    {
        var names = FormConverter.GetFormList(
            pkm.Species, strings.Types, strings.forms, GameInfo.GenderSymbolUnicode, pkm.Context);
        if (names.Length <= 1) return null;

        // Una forma regional (Alola/Galar/Hisui/Paldea) es otro Pokémon, no un toggle: un
        // Exeggutor de Alola no "cambia de forma" a Exeggutor de Kanto (otros tipos, stats,
        // habilidades, movimientos y evolución). Si el Pokémon YA es de forma regional, no hay
        // nada que elegir; y a uno base no se le ofrecen las regionales.
        if (RegionalFormCatalog.Find(pkm.Species, pkm.Form) != null) return null;

        // Formas que el juego del Pokémon no contempla (p. ej. las gorras de Pikachu en
        // Legends: Arceus) producen un Pokémon sin movimientos válidos: no se ofrecen.
        IPersonalTable? table = null;
        try { table = GameData.GetPersonal((GameVersion)pkm.Version); } catch { /* sin filtro */ }

        var result = new List<(byte, string)>();
        for (byte f = 0; f < names.Length; f++)
        {
            if (FormInfo.IsBattleOnlyForm(pkm.Species, f, pkm.Format)) continue;
            if (IsMegaFormUnflaggedByPkhex(pkm.Species, f)) continue;
            if (RegionalFormCatalog.Find(pkm.Species, f) != null) continue;
            if (f != pkm.Form && table != null && !IsFormPresent(table, pkm.Species, f)) continue;
            result.Add((f, names[f].Length > 0 ? names[f] : $"Forma {f}"));
        }
        if (onlyLegalOrigin && result.Count > 1)
        {
            // El combo NUNCA debe dejar un Pokémon ilegal: se oculta toda forma que, aplicada con
            // ApplyForm (los mismos efectos que al exportar), produzca CUALQUIER alerta inválida
            // nueva respecto del estado actual del Pokémon (colores de Flabébé sin encuentro,
            // letras de Unown, recortes de Furfrou, placas de Arceus no liberadas, formas de
            // Vivillon de otra región...). Antes solo se ocultaban los problemas de origen y se
            // dejaban pasar los "corregibles a mano", pero un barrido de los 21 juegos mostró que
            // ~380 combinaciones salían ilegales sin remedio. La forma actual siempre se conserva.
            result = result.Where(f => f.Item1 == pkm.Form || !FormIsBlocked(pkm, f.Item1)).ToList();
        }

        return result.Count > 1 ? result : null;
    }

    /// <summary>
    /// True si pasar el Pokémon a <paramref name="form"/> (con los mismos efectos que tendrá al
    /// exportar, <see cref="ApplyForm"/>) lo deja con CUALQUIER alerta inválida nueva respecto de su
    /// estado actual (encuentro que no admite la forma, forma inexistente en el juego, objeto
    /// o argumento de forma, género, tamaño, etc.). Solo cuentan los problemas NUEVOS: un Pokémon que ya
    /// traía alertas no bloquea todas sus formas. Si el análisis falla, la forma no se ofrece.
    /// Cuesta ~1 ms por forma.
    /// </summary>
    private static bool FormIsBlocked(PKM pkm, byte form)
    {
        try
        {
            static bool IsInvalid(CheckResult r) => !r.Valid && r.Judgement == Severity.Invalid;

            var baseline = new LegalityAnalysis(pkm).Results.Where(IsInvalid).Select(r => r.Result).ToHashSet();

            var candidate = pkm.Clone();
            ApplyForm(candidate, form);
            candidate.RefreshChecksum();
            return new LegalityAnalysis(candidate).Results.Any(r => IsInvalid(r) && !baseline.Contains(r.Result));
        }
        catch
        {
            // Si no se puede analizar, por seguridad no se ofrece (antes se dejaba disponible).
            return true;
        }
    }

    /// <summary>Mega Zygarde (forma 5) y Mega Greninja (forma 3): formas de combate que PKHeX.Core
    /// 25.11.07 reconoce en <c>IsBattleMegaForm</c> pero NO marca como solo-batalla en
    /// <c>IsBattleOnlyForm</c> (esas dos especies no figuran en su lista interna de megas), así que el
    /// combo Forma las ofrecía y al elegirlas salía "Habilidad no corresponde a la especie o forma"
    /// (el save nunca guarda una forma Mega). Encontradas barriendo todas las especies/formas de
    /// Z-A, Escarlata/Púrpura, X/Y y OR/AS buscando formas "Mega" no marcadas como solo-batalla.</summary>
    private static bool IsMegaFormUnflaggedByPkhex(ushort species, byte form)
        => (species, form) is ((ushort)Species.Zygarde, 5) or ((ushort)Species.Greninja, 3);

    private static bool IsFormPresent(IPersonalTable table, ushort species, byte form)
    {
        try { return table.IsPresentInGame(species, form); }
        catch { return true; }
    }

    /// <summary>
    /// Traduce el nombre de forma elegido en el combo de vuelta a su índice numérico.
    /// El combo del editor se arma con los nombres del idioma activo de la app
    /// (<c>GameInfo.GetStrings(lang)</c>), pero este método se llama más tarde desde
    /// EditSessionService/EditApplyService, que no conocen ese idioma (y el usuario pudo
    /// haberlo cambiado entre editar y exportar) — por eso se prueba el nombre contra todos
    /// los idiomas de PKHeX en vez de contra un único <c>GameStrings</c>.
    /// </summary>
    public static bool TryGetFormIndex(PKM pkm, string formName, out byte index)
    {
        foreach (var lang in FormLanguages)
        {
            var forms = GetSelectableFormsCore(pkm, GameInfo.GetStrings(lang), onlyLegalOrigin: false);
            if (forms == null) continue;
            foreach (var (i, name) in forms)
            {
                if (name != formName) continue;
                index = i;
                return true;
            }
        }
        index = 0;
        return false;
    }

    /// <summary>
    /// Aplica un cambio de forma al PKM, incluyendo todos los efectos secundarios
    /// necesarios para que PKHeX.Core lo considere legal (ítem, habilidad, tipo Tera, género, moveset).
    /// Verificado (en la sesión que escribió el handoff) contra PKHeX.Core 25.11.07 real en
    /// 10 casos reales con 0 alertas de forma.
    /// </summary>
    public static void ApplyForm(PKM pkm, byte form)
    {
        pkm.Form = form;

        // Arceus Gen4 offset bug workaround — usar GetItemArceus, NO el genérico GetItem.
        // (FormItem.GetItem despacha Arceus siempre vía formato 8 internamente, pero en Gen4
        // el tipo "???" desplaza el índice de placa en 1 -> FormItemInvalid si se usa el genérico)
        if (pkm.Species == (int)Species.Arceus)
            pkm.HeldItem = FormItem.GetItemArceus(form, pkm.Format);
        else if (pkm.Species is (int)Species.Silvally or (int)Species.Genesect or (int)Species.Ogerpon)
            pkm.HeldItem = FormItem.GetItem(pkm.Species, form);

        // Formas Origen de Dialga/Palkia/Giratina: PKHeX exige el objeto que cada generación asocia a la
        // forma. Formato <= 8: solo Giratina, con el Orbe Griseo (112; no está en la tabla FormItem, es
        // mecánica antigua). Formato >= 9 (Escarlata/Púrpura): Cristal Adamante (1777), Globo Lustroso
        // (1778) y Núcleo Griseo (1779). La forma base no puede llevar ese objeto.
        if (pkm.Species is (int)Species.Dialga or (int)Species.Palkia or (int)Species.Giratina)
        {
            var originItem = (pkm.Species, pkm.Format) switch
            {
                ((int)Species.Dialga, >= 9) => 1777,
                ((int)Species.Palkia, >= 9) => 1778,
                ((int)Species.Giratina, >= 9) => 1779,
                ((int)Species.Giratina, _) => 112,
                _ => 0,
            };
            if (originItem != 0)
            {
                if (form == 1) pkm.HeldItem = originItem;
                else if (pkm.HeldItem == originItem) pkm.HeldItem = 0;
            }
        }

        // Hoopa Desatado y Furfrou con corte (Gen 6/7): el argumento de forma son los días que le quedan
        // en esa forma; PKHeX marca FormArgumentInvalid si queda en 0.
        if ((pkm.Species == (int)Species.Hoopa && form == 1 || pkm.Species == (int)Species.Furfrou && form != 0)
            && pkm is IFormArgument)
            pkm.SetSuggestedFormArgument();

        // Ogerpon: el Tipo Tera está atado a la máscara (TeraTypeUtil.IsValidOgerpon lo exige)
        if (pkm.Species == (int)Species.Ogerpon && pkm is ITeraType tera)
        {
            tera.TeraTypeOverride = (form & 3) switch
            {
                1 => MoveType.Water,
                2 => MoveType.Fire,
                3 => MoveType.Rock,
                _ => (MoveType)TeraTypeUtil.OverrideNone,
            };
        }

        // Refresco universal de habilidad — necesario no solo para el trío Therian,
        // también para Giratina Origin y Ogerpon (confirmado contra PKHeX real: sin esto
        // da AbilityUnexpected en ambos).
        var slot = pkm.AbilityNumber switch { 4 => 2, 2 => 1, _ => 0 };
        var pi = pkm.PersonalInfo;
        pkm.RefreshAbility(Math.Min(slot, pi.AbilityCount - 1));

        // Meowstic: la forma ES el género.
        if (pkm.Species == (int)Species.Meowstic)
            pkm.Gender = form;

        // Keldeo Resolute requiere conocer Espada Sagrada (548).
        if (pkm.Species == (int)Species.Keldeo && form == 1)
        {
            const ushort SecretSword = 548;
            if (!pkm.HasMove(SecretSword))
            {
                Span<ushort> moves = [pkm.Move1, pkm.Move2, pkm.Move3, SecretSword];
                pkm.SetMoves(moves);
                pkm.HealPP();
            }
        }

        // Alto/peso absolutos (Legends: Arceus, Let's Go, Gen 8-9): se calculan con las medidas de la
        // forma; si no se recalculan al cambiarla, PKHeX marca StatIncorrectHeight/Weight (Therian,
        // Origen, Shaymin Cielo...).
        if (pkm is IScaledSizeValue sized)
        {
            sized.HeightAbsolute = sized.CalcHeightAbsolute;
            sized.WeightAbsolute = sized.CalcWeightAbsolute;
        }

        // Algunas formas tienen un learnset DISTINTO al de la forma anterior (Meowstic hembra en
        // Z-A no aprende Imprison/Quick Guard/Misty Terrain del macho): los movimientos que ya no
        // son válidos se reemplazan por los que PKHeX sugiere para la forma nueva.
        FixMovesAfterFormChange(pkm);
    }

    /// <summary>Tras un cambio de forma, reemplaza los movimientos que dejaron de ser válidos por
    /// movimientos sugeridos por PKHeX para la forma nueva; los que siguen siendo válidos se
    /// conservan. Si el reemplazo no mejora la cantidad de movimientos inválidos, deja el moveset
    /// como estaba.</summary>
    private static void FixMovesAfterFormChange(PKM pkm)
    {
        pkm.RefreshChecksum();
        var la = new LegalityAnalysis(pkm);
        var results = la.Info.Moves;
        if (results.All(m => m.Valid)) return;

        Span<ushort> suggested = stackalloc ushort[4];
        // Solo movimientos por nivel/recuerdo: con todas las fuentes PKHeX sugiere TM/tutores que en
        // juegos como Z-A no existen y el moveset sale ilegal casi siempre (verificado con Meowstic).
        la.GetSuggestedCurrentMoves(suggested, MoveSourceType.Encounter);

        ushort[] current = [pkm.Move1, pkm.Move2, pkm.Move3, pkm.Move4];
        var kept = new ushort[4];
        var used = new HashSet<ushort>();
        for (var i = 0; i < 4; i++)
        {
            if (results[i].Valid && current[i] != 0) { kept[i] = current[i]; used.Add(current[i]); }
        }
        var pool = new Queue<ushort>(suggested.ToArray().Where(m => m != 0 && !used.Contains(m)).Distinct());
        for (var i = 0; i < 4 && pool.Count > 0; i++)
            if (kept[i] == 0) kept[i] = pool.Dequeue();

        var invalidBefore = results.Count(m => !m.Valid);
        pkm.SetMoves(kept);
        pkm.HealPP();
        if (CountInvalidMoves(pkm) >= invalidBefore)
        {
            pkm.SetMoves(current);   // el reemplazo no ayudó: se deja como estaba
            pkm.HealPP();
        }
    }

    /// <summary>El save cargado (null si no hay). Lo usa la UI para los servicios de corrección que
    /// necesitan el juego (recreación de encuentro, catch rate).</summary>
    public SaveFile? CurrentSave => _saveService.CurrentSave;

    private SaveFile RequireSave()
    {
        return _saveService.CurrentSave
            ?? throw new InvalidOperationException("No save file is loaded.");
    }
}
