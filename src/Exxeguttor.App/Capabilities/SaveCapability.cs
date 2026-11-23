namespace Exxeguttor.App.Capabilities;

/// <summary>
/// Enumera todas las características que puede tener un save según su generación.
/// 
/// Para agregar soporte a una característica nueva (ej: una mecánica de Gen 10):
///   1. Agregar el valor aquí
///   2. Agregar la detección en SaveCapabilities.Detect()
///   3. Agregar la propiedad en SaveCapabilities (ej: public bool HasNuevaMecanica)
///   4. Agregar el panel visual en la vista XAML correspondiente
/// </summary>
public enum SaveCapability
{
    // ── Básicos ────────────────────────────────────────────────────────
    /// <summary>Gen 2+ — Pokémon puede cargar un objeto</summary>
    HeldItems,

    /// <summary>Gen 2+ — Pokémon puede ser Shiny</summary>
    Shiny,

    /// <summary>Gen 2+ — Pokémon tiene género</summary>
    Gender,

    /// <summary>Gen 3+ — Pokémon tiene Naturaleza</summary>
    Natures,

    /// <summary>Gen 3+ — Pokémon tiene Habilidad</summary>
    Abilities,

    /// <summary>Gen 3+ — EVs con límite 255 por stat (reemplaza sistema Gen 1-2)</summary>
    EVsModern,

    /// <summary>Gen 3+ — Pokémon puede tener Cintas (Ribbons)</summary>
    Ribbons,

    /// <summary>Gen 4+ — Pokémon tiene stat de Condición (Contest stats)</summary>
    ContestStats,

    /// <summary>Gen 6+ — Pokémon tiene IVs "Perfect" marcables</summary>
    HyperTraining,

    /// <summary>Gen 6+ — Pokémon puede tener Huevo de destino / Marca especial</summary>
    DestinyMark,

    // ── Mecánicas de batalla especiales ───────────────────────────────
    /// <summary>Gen 6 (X/Y, OR/AS) — Mega Evolución</summary>
    Mega,

    /// <summary>Gen 7 (Sun/Moon, US/UM) — Movimientos Z</summary>
    ZMoves,

    /// <summary>Gen 7 (Let's Go) — Pokémon puede ser compañero</summary>
    Buddy,

    /// <summary>Gen 8 (Sword/Shield) — Dinamax y Gigamax</summary>
    Dynamax,

    /// <summary>Gen 8 (Sword/Shield) — Factor Gigamax específico</summary>
    Gigamax,

    /// <summary>Gen 8 (Legends Arceus) — Pokémon Alpha</summary>
    Alpha,

    /// <summary>Gen 8 (Legends Arceus) — Nivel de nobleza</summary>
    NobleLevel,

    /// <summary>Gen 9 (Scarlet/Violet) — Tipo Terastal</summary>
    Terastal,

    // ── Campos de origen / ubicación ─────────────────────────────────
    /// <summary>Gen 2+ — Pokémon tiene nivel de captura</summary>
    MetLevel,

    /// <summary>Gen 3+ — Pokémon tiene ubicación de captura con nombre completo</summary>
    MetLocation,

    /// <summary>Gen 4+ — Pokémon tiene fecha de captura</summary>
    MetDate,

    /// <summary>Gen 4+ — Pokémon tiene fecha de huevo</summary>
    EggDate,

    /// <summary>Gen 3+ — Pokémon tiene ubicación de huevo</summary>
    EggLocation,

    /// <summary>Gen 2 y Gen 4 — Pokémon tiene hora del día de captura</summary>
    MetTimeOfDay,

    /// <summary>Gen 2+ — Pokémon tiene Pokéball de captura</summary>
    Ball,

    /// <summary>Gen 5+ — Pokémon tiene Handler (último entrenador tras intercambio)</summary>
    HandlerName,

    /// <summary>Gen 7+ — Pokémon puede tener Hyper Training en IVs individuales</summary>
    HyperTrainingPerStat,

    /// <summary>Gen 6+ — Pokémon tiene país y región de origen</summary>
    GeoLocation,

    /// <summary>Gen 8+ — Pokémon tiene Stat Nature (mint) diferente a Nature real</summary>
    StatNature,

    /// <summary>Gen 2+ — Pokémon puede tener Pokérus</summary>
    Pokerus,

    /// <summary>Gen 2+ — Pokémon puede ser/haber sido un huevo sin eclosionar. Gen1 no tiene
    /// mecánica de cría (Day Care) en absoluto: PK1.IsEgg está hardcodeado a
    /// "get => false; set { }" en PKHeX.Core — un no-op total, no un simple campo vacío.
    /// Distinto de EggLocation/EggDate (arriba, gen3+/gen4+): esos son "sabemos IsEgg=true
    /// mas no dónde/cuándo"; esto es "puede existir el concepto mismo de huevo".</summary>
    Breeding,

    // ── Espacio para mecánicas futuras ────────────────────────────────
    // Descomenta y agrega la detección en SaveCapabilities.Detect()
    // cuando aparezca el juego correspondiente:
    //
    // /// <summary>Gen 10 — Nueva mecánica pendiente de anuncio</summary>
    // NewMechanic,
}
