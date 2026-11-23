using System.Collections.Generic;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Un paso del diagnosticador de legalidad — uno por categoría con problema (misma
/// agrupación que ya arma <see cref="LegalityMessageMapper"/>, enriquecida con datos
/// accionables). Pensado desde el arranque para servir DOS propósitos con la misma
/// forma, sin tener que rediseñar nada cuando llegue la v2:
///
/// <list type="bullet">
/// <item>v1 (mostrar): listar los <see cref="Fixes"/> como checklist en el tab
/// Diagnóstico, y resaltar en la UI de edición el campo de cada <see cref="FieldFix"/>
/// vía su <see cref="FieldFix.FieldId"/>.</item>
/// <item>v2 (aplicar, a futuro): un botón "Aplicar" por <see cref="FieldFix"/> que
/// escriba <see cref="FieldFix.SuggestedValueRaw"/> en el campo del PKM que indica
/// <see cref="FieldFix.FieldId"/> — reusando el mismo pipeline de escritura
/// (EditApplyService) que ya existe para ediciones manuales, nunca un motor de
/// regeneración tipo AutoMod (ver notas de la sesión de análisis de legalidad).</item>
/// </list>
///
/// <see cref="Fixes"/> puede estar vacío — pasa cuando el código de PKHeX detrás de
/// este step es puramente descriptivo (Nivel C: no hay un valor "correcto" conocible,
/// ej. "existen varios movimientos ilegales posibles, no un único reemplazo"). En ese
/// caso el step se sigue mostrando (con <see cref="Summary"/>/<see cref="TechnicalDetail"/>)
/// pero sin ningún campo resaltable ni botón "Aplicar".
/// </summary>
public sealed class DiagnosticStep
{
    /// <summary>Orden de resolución sugerido — NO el orden en que PKHeX corrió sus
    /// verificadores internos (ese es arbitrario para el usuario). Prioridad por
    /// dependencia: Origen del encuentro/Juego de origen/Huevo primero (todo lo demás
    /// deriva de esto) → Especie/Forma/Habilidad/Género → PID/Naturaleza/IVs/Brillante →
    /// Nivel/EVs/Movimientos → Apodo/Idioma/Entrenador/Ball/Objeto/Cintas/Marcas/
    /// Recuerdos al final. Ver la tabla de prioridad por <see cref="CheckIdentifier"/> en
    /// LegalityMessageMapper (a extender en la Etapa 3).</summary>
    public required int Order { get; init; }

    /// <summary>Categoría amigable ya existente ("Ubicación", "IVs/EVs", "Movimientos",
    /// etc.) — la misma que hoy arma <see cref="LegalityMessageMapper"/>.</summary>
    public required string Category { get; init; }

    /// <summary>Nombre visible de <see cref="Category"/> en el idioma de la UI. <see cref="Category"/> es la
    /// LLAVE interna (la comparan el editor y la UI): enlazar el texto a esta propiedad, no a la llave.</summary>
    public string CategoryLabel => LegalityMessageMapper.GetCategoryLabel(Category);

    /// <summary>Solo <see cref="Severity.Invalid"/> (bloquea la legalidad, va como ficha
    /// roja en la cabecera) o <see cref="Severity.Fishy"/> (sospechoso pero ya es legal
    /// así como está — solo aparece en el tab, nunca como ficha, para no mandar la señal
    /// equivocada de "esto también hay que arreglarlo"). Nunca
    /// <see cref="Severity.Valid"/> — un DiagnosticStep solo existe para un problema.</summary>
    public required Severity Severity { get; init; }

    /// <summary>El texto amigable a nivel categoría que ya existe hoy.</summary>
    public required string Summary { get; init; }

    /// <summary>El detalle técnico crudo (<c>CheckResult.ToString()</c> concatenado) que
    /// ya existe hoy — para power users o reporte de bugs.</summary>
    public required string TechnicalDetail { get; init; }

    /// <summary>0 a N arreglos concretos bajo este step — 0 si es Nivel C (sin valor
    /// conocible). Un step puede traer varios (ej. "IVs/EVs" corrigiendo 3 stats a la
    /// vez) — por eso es una lista separada del step, no un campo más del step mismo.</summary>
    public IReadOnlyList<FieldFix> Fixes { get; init; } = [];
}

/// <summary>
/// Un arreglo concreto y accionable para un campo puntual del PKM, dentro de un
/// <see cref="DiagnosticStep"/>. Separa expresamente el valor para MOSTRAR
/// (<see cref="SuggestedValueDisplay"/>) del valor para ESCRIBIR
/// (<see cref="SuggestedValueRaw"/>) — v1 solo necesita el primero, v2 va a necesitar el
/// segundo, y evita tener que rehacer esta forma cuando llegue esa v2.
/// </summary>
public sealed class FieldFix
{
    /// <summary>Identificador estable del campo (<c>"Nickname"</c>,
    /// <c>"MetLocation"</c>, <c>"Ability"</c>, <c>"IV_HP"</c>, etc.) — el mismo string
    /// sirve para dos cosas: (a) en v1, qué control resaltar en la UI de edición; (b) en
    /// v2, qué propiedad del PKM escribir. No es libre — tiene que salir de una lista
    /// cerrada y documentada en el mapeo de la Etapa 3 (un FieldId inventado ad-hoc no le
    /// sirve a ninguna de las dos puntas).</summary>
    public required string FieldId { get; init; }

    /// <summary>Nombre visible del campo en el idioma de la UI («Ataque (IV)», «PP del movimiento 2»…).
    /// <see cref="FieldId"/> sigue siendo la llave estable para resaltar/escribir el campo.</summary>
    public string FieldLabel => LegalityMessageMapper.GetFieldLabel(FieldId);

    /// <summary>Lo que el Pokémon tiene HOY en ese campo, en texto — para mostrar
    /// "tenías: X" al lado de la sugerencia.</summary>
    public required string CurrentValueDisplay { get; init; }

    /// <summary>El valor sugerido, ya resuelto a texto legible (nombre de especie,
    /// movimiento, ubicación, etc. — nunca un ID crudo sin traducir). Esta es la capa de
    /// "traducir números a nombres" identificada como pendiente en la sesión de
    /// análisis.</summary>
    public required string SuggestedValueDisplay { get; init; }

    /// <summary>El valor sugerido, tipado y listo para escribir en una futura v2
    /// (<see cref="ushort"/>, <see cref="int"/>, <see cref="string"/>, según el campo).
    /// v1 no lo necesita para nada visual — vive acá desde ahora para no tener que
    /// agregar esta pieza más tarde. Aplicar esto requiere saber, por
    /// <see cref="FieldId"/>, a qué tipo castear — esa tabla se define recién en la v2,
    /// no en la Etapa 3.</summary>
    public required object? SuggestedValueRaw { get; init; }

    /// <summary>De dónde salió el valor sugerido — nunca hace falta un valor "Ninguno":
    /// si no hay sugerencia, directamente no se crea el <see cref="FieldFix"/>.</summary>
    public required FieldFixSource Source { get; init; }

    /// <summary>Post-4.6 — la v2 que ya estaba prevista en el comentario de clase de
    /// arriba, para los casos donde "escribir SuggestedValueRaw directo en el campo" no
    /// alcanza (PID/EC no son campos con un control propio en la UI — arreglarlos es una
    /// ACCIÓN que recalcula un valor nuevo, no "pegar un número"). <see
    /// cref="FieldFixAction.None"/> por default: la enorme mayoría de los FieldFix
    /// (Nickname, MetLocation, Level, IVs/EVs) se siguen arreglando a mano por el usuario
    /// tocando el campo resaltado, no con un botón — esto es solo para el puñado de
    /// casos que genuinamente necesitan una acción calculada.</summary>
    public FieldFixAction Action { get; init; } = FieldFixAction.None;

    /// <summary>Para bindear IsVisible del botón directo, sin necesidad de un converter de
    /// enum en el XAML.</summary>
    public bool HasAutoFix => Action != FieldFixAction.None;

    /// <summary>Texto explicativo propio de la fila (ya localizado). Cuando existe, la fila NO muestra
    /// el renglón "tenías / sugerido" (que no tiene sentido para acciones como "Recrear con un
    /// encuentro válido") y muestra este texto en su lugar.</summary>
    public string? Note { get; init; }

    /// <summary>Texto del botón (ya localizado); si es null se usa el genérico (clave <c>Legality_AutoFix</c>).</summary>
    public string? ButtonLabel { get; init; }

    public bool HasNote => Note != null;
    public bool ShowSuggestion => Note == null;
    public string ButtonText => ButtonLabel ?? AppText.T("Legality_AutoFix");
}

/// <summary>Acciones automáticas disponibles desde el tab Diagnóstico — un botón por
/// FieldFix cuando corresponde, que ejecuta código real (nunca un motor de regeneración
/// tipo AutoMod completo) para recalcular un valor que no tiene un control de UI propio
/// donde el usuario lo tocaría a mano.</summary>
public enum FieldFixAction
{
    /// <summary>Sin acción automática — el usuario edita el campo resaltado a mano
    /// (el caso normal, la enorme mayoría de los FieldFix).</summary>
    None,

    /// <summary>Busca un PID nuevo que satisfaga el Género/Naturaleza(Gen3-5)/Habilidad/
    /// Brillante que el Pokémon YA tiene seteados — nunca al revés (no se tocan esos
    /// campos para "hacer lugar" a un PID al azar). Mismo criterio que
    /// PokemonService.BuildPokemon usa para generar un Pokémon nuevo, aplicado ahora
    /// "hacia atrás" sobre uno existente.</summary>
    RegeneratePid,

    /// <summary>Post-4.6 (2) — mismo concepto que RegeneratePid pero para Method 1
    /// (Gen3/4, encuentros estáticos): PID e IVs salen de la misma semilla, así que acá
    /// se preservan las IVs y se busca un PID válido para ellas — Naturaleza/Habilidad/
    /// Género pueden terminar cambiando como consecuencia (ver
    /// PokemonService.TryRegeneratePidMethod1 para el porqué).</summary>
    RegeneratePidMethod1,

    /// <summary>Gen8/9 con semilla (Tera raids, estáticos de mundo abierto): abre el modal
    /// "Buscar IVs legales" (IvSeedSolver). No calcula nada acá: la búsqueda tarda segundos y
    /// necesita progreso y cancelar, así que la dispara el editor y la muestra la ventana
    /// principal. Solo tiene efecto si el editor encontró un plan de búsqueda para el Pokémon.</summary>
    SearchIvSeed,

    /// <summary>Gen1 (G1CatchRateItem/G1CatchRateEvo) — recalcula <c>PK1.CatchRate</c> con
    /// la misma lógica que usa el propio editor oficial de PKHeX
    /// (<c>CatchRateApplicator.GetSuggestedCatchRate</c>): rate de la especie según la
    /// tabla RB o Yellow (usa la del save cargado), o el rate de una pre-evolución si el
    /// Pokémon evolucionó después de capturado. Bug real encontrado: el flujo de creación
    /// de PokemonService.BuildPokemon nunca seteaba este campo para Gen1 y quedaba en 0 —
    /// válido SOLO si el Pokémon visitó Gen2 (tradeback), inválido para cualquier otro
    /// caso (confirmado en GBRestrictions.IsTimeCapsuleTransferred/MiscG1Verifier de
    /// PKHeX.Core). Necesita el SaveFile activo (para saber si es RB o Yellow) — por eso
    /// el valor sugerido se calcula recién al aplicar (PokemonService.RequireSave()), no
    /// al armar el FieldFix.</summary>
    RecalculateCatchRate,

    /// <summary>Gen2–Gen4 con error de encuentro: abre el modal "Recrear con un encuentro válido"
    /// (EncounterRecreationService). El fix solo aparece si ya se verificó que hay al menos una opción.</summary>
    RecreateEncounter,

    /// <summary>Gen1 con error de encuentro por nivel demasiado bajo: abre el modal para subir el
    /// nivel al mínimo con el que existe un encuentro válido.</summary>
    RaiseLevel,

    /// <summary>Sin opciones de recreación (ningún encuentro coincide con los movimientos): abre el
    /// selector del primer movimiento ilegal. Es un atajo, no una corrección.</summary>
    GoToMoves,
}

/// <summary>De dónde sale el valor de un <see cref="FieldFix"/> — ver sesión de análisis
/// de legalidad para la definición completa de cada nivel.</summary>
public enum FieldFixSource
{
    /// <summary>Nivel A — el valor viene directo de <c>CheckResult.Value</c>/
    /// <c>.Argument</c>/<c>.Argument2</c> (códigos de PKHeX con sufijo <c>_0</c>/
    /// <c>_01</c>/<c>_H</c>, los ~81 con "Formattable Argument Present").</summary>
    DirectArgument,

    /// <summary>Nivel B — el valor no está en el <c>CheckResult</c>, hay que cruzarlo
    /// contra <c>LegalityAnalysis.EncounterMatch</c> (ej. <c>ILocation</c> para
    /// ubicación, <c>IFixedIVSet</c> para IVs fijas de un regalo/estático).</summary>
    EncounterMatch,

    /// <summary>El valor sale de <c>LegalityAnalysis.Info.Moves</c>/<c>.Relearn</c>
    /// (array de <c>MoveResult</c>, uno por slot 1-4) — un sistema totalmente aparte del
    /// <c>CheckResult</c>/<c>Results</c> que cubren los otros dos niveles. El índice del
    /// array da el slot exacto sin ambigüedad, y <c>MoveResult.Expect</c> a veces ya trae
    /// el ID del movimiento que debería ir ahí en vez del actual.</summary>
    MoveResultExpect,

    /// <summary>El valor no sale de ningún dato que exponga PKHeX — lo calculamos
    /// nosotros mismos inspeccionando el propio PKM directamente (ej. la suma total de
    /// EVs/IVs: PKHeX ni siquiera da un conteo para <c>EffortAbove510</c>, así que la
    /// sumamos nosotros a mano recorriendo <c>pkm.EVs</c>).</summary>
    DerivedFromPkm,
}
