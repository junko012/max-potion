using PKHeX.Core;

namespace Exxeguttor.App.Capabilities;

/// <summary>
/// Detecta y expone las capacidades de un SaveFile cargado.
///
/// Uso:
///   var caps = new SaveCapabilities(saveFile);
///   if (caps.HasTerastal) { ... }
///
/// La UI usa estas propiedades para mostrar u ocultar campos según
/// la generación del juego — sin hardcodear lógica de generaciones en la vista.
/// </summary>
public class SaveCapabilities
{
    private readonly HashSet<SaveCapability> _capabilities;

    /// <summary>Generación del juego cargado (1–9)</summary>
    public int Generation { get; }

    /// <summary>Nombre del juego detectado por PKHeX.Core</summary>
    public string GameName { get; }

    /// <summary>Versión del juego (GameVersion enum de PKHeX.Core)</summary>
    public GameVersion GameVersion { get; }

    /// <summary>ID máximo de ítem válido para este juego (PKHeX SaveFile.MaxItemID).</summary>
    public int MaxItemID { get; }

    /// <summary>ID máximo de Poké Ball válida para este juego (PKHeX SaveFile.MaxBallID).</summary>
    public int MaxBallID { get; }

    public SaveCapabilities(SaveFile save)
    {
        Generation  = save.Generation;
        GameName    = save.Version.ToString();
        GameVersion = save.Version;
        MaxItemID   = save.MaxItemID;
        MaxBallID   = save.MaxBallID;
        _capabilities = Detect(save);
    }

    /// <summary>
    /// Verifica si el save tiene una capacidad específica.
    /// Método base — las propiedades de abajo son atajos convenientes.
    /// </summary>
    public bool Has(SaveCapability cap) => _capabilities.Contains(cap);

    // ── Propiedades de acceso rápido para la UI ───────────────────────

    public bool HasHeldItems     => Has(SaveCapability.HeldItems);
    public bool HasShiny         => Has(SaveCapability.Shiny);
    public bool HasGender        => Has(SaveCapability.Gender);
    public bool HasNatures       => Has(SaveCapability.Natures);
    public bool HasAbilities     => Has(SaveCapability.Abilities);
    public bool HasEVsModern     => Has(SaveCapability.EVsModern);
    public bool HasRibbons       => Has(SaveCapability.Ribbons);
    public bool HasContestStats  => Has(SaveCapability.ContestStats);
    public bool HasHyperTraining => Has(SaveCapability.HyperTraining);
    public bool HasMega          => Has(SaveCapability.Mega);
    public bool HasZMoves        => Has(SaveCapability.ZMoves);
    public bool HasBuddy         => Has(SaveCapability.Buddy);
    public bool HasDynamax       => Has(SaveCapability.Dynamax);
    public bool HasGigamax       => Has(SaveCapability.Gigamax);
    public bool HasAlpha         => Has(SaveCapability.Alpha);
    public bool HasNobleLevel    => Has(SaveCapability.NobleLevel);
    public bool HasTerastal      => Has(SaveCapability.Terastal);

    // ── Campos de origen / ubicación ─────────────────────────────────
    public bool HasMetLevel       => Has(SaveCapability.MetLevel);
    public bool HasMetLocation    => Has(SaveCapability.MetLocation);
    public bool HasMetDate        => Has(SaveCapability.MetDate);
    public bool HasEggDate        => Has(SaveCapability.EggDate);
    public bool HasEggLocation    => Has(SaveCapability.EggLocation);
    public bool HasBreeding       => Has(SaveCapability.Breeding);
    public bool HasMetTimeOfDay   => Has(SaveCapability.MetTimeOfDay);
    public bool HasBall           => Has(SaveCapability.Ball);
    public bool HasHandlerName    => Has(SaveCapability.HandlerName);
    public bool HasGeoLocation    => Has(SaveCapability.GeoLocation);
    public bool HasStatNature     => Has(SaveCapability.StatNature);
    public bool HasPokerus        => Has(SaveCapability.Pokerus);

    // ── Detección ─────────────────────────────────────────────────────

    private static HashSet<SaveCapability> Detect(SaveFile save)
    {
        var caps = new HashSet<SaveCapability>();
        int gen     = save.Generation;
        var version = save.Version;

        // ── Por generación (acumulativo) ──────────────────────────────

        if (gen >= 2)
        {
            caps.Add(SaveCapability.HeldItems);
            caps.Add(SaveCapability.Shiny);
            caps.Add(SaveCapability.Gender);
            caps.Add(SaveCapability.MetLevel);
            caps.Add(SaveCapability.Pokerus);
            caps.Add(SaveCapability.Breeding);
        }

        // Gen 2 Crystal — hora del día
        if (gen == 2)
            caps.Add(SaveCapability.MetTimeOfDay);

        if (gen >= 3)
        {
            caps.Add(SaveCapability.Natures);
            caps.Add(SaveCapability.Abilities);
            caps.Add(SaveCapability.EVsModern);
            caps.Add(SaveCapability.Ribbons);
            caps.Add(SaveCapability.MetLocation);
            caps.Add(SaveCapability.EggLocation);
            // Ball: recién desde Gen3 los juegos registran con qué Poké Ball se atrapó al
            // Pokémon. En Gen1/Gen2 el dato no existe en el formato de guardado — todo lo
            // capturado en esas generaciones se muestra como Poké Ball estándar al pasar a
            // generaciones posteriores (confirmado, no es límite de PKHeX ni nuestro: Bulbapedia,
            // artículo "Poké Ball (item)"). Antes vivía en el bloque gen>=2 de arriba, lo que
            // dejaba el combo de Ball editable y aparentemente funcional en Gen2, pero cualquier
            // cambio se perdía en silencio al exportar porque pkm.Ball no tiene dónde persistir
            // en un PK2.
            caps.Add(SaveCapability.Ball);
        }

        if (gen >= 4)
        {
            caps.Add(SaveCapability.ContestStats);
            caps.Add(SaveCapability.MetDate);
            caps.Add(SaveCapability.EggDate);
            // Gen 4 no expone MetTimeOfDay en PK4 — solo Gen2 (PK2) lo tiene
        }

        if (gen >= 6)
        {
            caps.Add(SaveCapability.HyperTraining);
            caps.Add(SaveCapability.DestinyMark);
            caps.Add(SaveCapability.GeoLocation);
        }

        if (gen >= 5)
            caps.Add(SaveCapability.HandlerName);

        if (gen >= 8)
            caps.Add(SaveCapability.StatNature);

        // ── Por juego específico usando GameVersion ───────────────────
        // GameVersion es un enum estable de PKHeX.Core que no cambia
        // entre versiones menores del paquete.

        switch (version)
        {
            // Gen 6 — Mega Evolución. Se suman los miembros "agrupados" (XY, ORAS) además
            // de los individuales (X/Y/OR/AS) — encontrado en sesión de diagnóstico Gen9
            // que SaveFile.Version puede devolver cualquiera de los dos según el caso, y
            // el switch viejo solo cubría uno. Ver MapVersionToGameId (PokemonService/
            // PokemonEditorViewModel) para el mismo hallazgo con más detalle.
            case GameVersion.X:
            case GameVersion.Y:
            case GameVersion.XY:
            case GameVersion.OR:
            case GameVersion.AS:
            case GameVersion.ORAS:
                caps.Add(SaveCapability.Mega);
                break;

            // Gen 7 — Movimientos Z (Sun, Moon, US, UM)
            case GameVersion.SN:
            case GameVersion.MN:
            case GameVersion.SM:
            case GameVersion.US:
            case GameVersion.UM:
            case GameVersion.USUM:
                caps.Add(SaveCapability.ZMoves);
                caps.Add(SaveCapability.Mega);
                break;

            // Gen 7 — Let's Go (compañero)
            case GameVersion.GP:
            case GameVersion.GE:
            case GameVersion.GG:
                caps.Add(SaveCapability.Buddy);
                RemoveEggMechanics(caps);
                break;

            // Gen 8 — Sword / Shield
            case GameVersion.SW:
            case GameVersion.SH:
            case GameVersion.SWSH:
                caps.Add(SaveCapability.Dynamax);
                caps.Add(SaveCapability.Gigamax);
                break;

            // Gen 8 — Brilliant Diamond / Shining Pearl
            case GameVersion.BD:
            case GameVersion.SP:
            case GameVersion.BDSP:
                break; // sin mecánicas nuevas más allá de Gen 4 base

            // Gen 8 — Legends Arceus
            case GameVersion.PLA:
                caps.Add(SaveCapability.Alpha);
                caps.Add(SaveCapability.NobleLevel);
                RemoveEggMechanics(caps);
                break;

            // Gen 9 (era) — Legends Z-A. Formato de PKM propio (PA9, no PK9 — confirmado por
            // reflection sobre el DLL: SAV9ZA es la clase de save, PA9 la de Pokémon, ambas
            // completamente separadas de SAV9/PK9 de Escarlata/Púrpura pese a ser "misma
            // generación"). PA9 expone IsAlpha (igual que PA8 en Legends Arceus) pero NO
            // IsNoble — reusa el mecanismo de Alpha, sin NobleLevel. No tiene Tera (PA9 no
            // implementa IRibbonIndex ni tiene TeraType, a diferencia de PK9). Sin este case,
            // GameVersion.ZA no matcheaba nada acá y el tab Special quedaba siempre oculto
            // para estos saves (bug real reportado, sin ningún case para ZA hasta ahora).
            case GameVersion.ZA:
                caps.Add(SaveCapability.Alpha);
                RemoveEggMechanics(caps);
                break;

            // Gen 9 — Scarlet / Violet. Se suma también el valor genérico GameVersion.Gen9
            // por el mismo motivo que X/Y/XY arriba (SaveFile.Version puede devolver el
            // agregado en vez del juego específico según el caso) — este es justo el caso
            // que el comentario de arriba decía haber encontrado "en sesión de diagnóstico
            // Gen9", pero el fix solo se había aplicado a Gen6 en su momento, no acá. Bug
            // real reportado: el tab Special no aparecía para ningún save de Gen9.
            case GameVersion.SL:
            case GameVersion.VL:
            case GameVersion.SV:
            case GameVersion.Gen9:
                caps.Add(SaveCapability.Terastal);
                break;

            // ── Espacio para juegos futuros ───────────────────────────
            // Agregar aquí el case correspondiente cuando salga Gen 10:
            // case GameVersion.NuevoJuego:
            //     caps.Add(SaveCapability.NuevaMecanica);
            //     break;
        }

        return caps;
    }

    /// <summary>
    /// Let's Go, Leyendas: Arceus y Z-A no tienen huevos: PKHeX no trae ninguna plantilla de huevo para ellos (sí existen para
    /// Gen2 a Gen9 de Escarlata/Púrpura, no para Gen7b, Gen8a ni Gen9a). Sin este gate los checkboxes "Egg"/"From egg" y los
    /// campos de ubicación y fecha de huevo quedaban habilitados pero no escribían nada que PKHeX pudiera validar: tildar
    /// "From egg" seguía saliendo legal y, al recargar el save exportado, el check aparecía sin marcar (reportado con un
    /// Pokémon de Z-A). Mismo criterio que Gen1, que ya quita Breeding por no tener cría.
    /// </summary>
    private static void RemoveEggMechanics(HashSet<SaveCapability> caps)
    {
        caps.Remove(SaveCapability.Breeding);
        caps.Remove(SaveCapability.EggLocation);
        caps.Remove(SaveCapability.EggDate);
    }
}
