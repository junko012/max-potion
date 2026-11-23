using Exxeguttor.App.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Máquina de estados de la sesión de edición: guarda qué propiedades se tocaron por Pokémon
/// (clave = <see cref="Exxeguttor.UI.ViewModels.PokemonSlotKey"/>) durante la sesión actual,
/// independiente de si eso ya se escribió al PKM real (todavía no — ver GAP CRÍTICO) o al
/// archivo de guardado.
///
/// Dos consumidores:
///  - PokemonEditorViewModel.Set&lt;T&gt; reporta acá cada cambio real (RecordEdit), y al volver
///    a cargar un slot ya editado, reaplica los valores guardados (ver GetRawEdits).
///  - MainWindowViewModel arma el modal de revisión antes de exportar leyendo BuildSummary().
/// </summary>
public class EditSessionService
{
    private readonly Dictionary<Exxeguttor.UI.ViewModels.PokemonSlotKey, Dictionary<string, object?>> _pendingEdits = new();

    /// <summary>Valor "tal cual venía" de cada propiedad tocada, la primera vez que se ve en
    /// cada slot (capturado desde PokemonEditorViewModel.Set&lt;T&gt; durante la carga, antes
    /// de que RestorePendingEdits reaplique ediciones de sesiones anteriores). Sirve para que
    /// <see cref="RecordEdit"/> pueda distinguir "el usuario cambió esto de verdad" de "el
    /// usuario lo tocó pero volvió al valor original" — antes cualquier toque quedaba
    /// marcado como cambio para siempre en la sesión, incluso si se revertía (ej. tildar y
    /// destildar Shiny), lo cual se veía "pegado" en la libreta/modal de exportación.</summary>
    private readonly Dictionary<Exxeguttor.UI.ViewModels.PokemonSlotKey, Dictionary<string, object?>> _pristineValues = new();
    private readonly Dictionary<Exxeguttor.UI.ViewModels.PokemonSlotKey, string> _displayNames = new();
    private readonly Dictionary<Exxeguttor.UI.ViewModels.PokemonSlotKey, string> _nicknames = new();

    /// <summary>
    /// Pokémon nuevos "creados" pero todavía sin aplicar al SaveFile real — mismo criterio
    /// que el resto de las ediciones (queda pendiente hasta que se confirma en el modal de
    /// revisión al exportar). A diferencia de _pendingEdits (que guarda un diff de campos
    /// sobre un PKM que YA existe en el save), acá se guarda el PKM completo recién
    /// construido, porque el slot todavía está vacío en el save real — no hay nada sobre lo
    /// que hacer diff.
    /// </summary>
    private readonly Dictionary<Exxeguttor.UI.ViewModels.PokemonSlotKey, PKM> _pendingCreations = new();

    public void RecordPendingCreation(Exxeguttor.UI.ViewModels.PokemonSlotKey key, PKM pkm) =>
        _pendingCreations[key] = pkm;

    public PKM? GetPendingCreation(Exxeguttor.UI.ViewModels.PokemonSlotKey key) =>
        _pendingCreations.TryGetValue(key, out var pkm) ? pkm : null;

    public bool HasPendingCreation(Exxeguttor.UI.ViewModels.PokemonSlotKey key) =>
        _pendingCreations.ContainsKey(key);

    // ── Liberaciones (ver PokemonReleaseService) ─────────────────────────────────────────
    // El save en memoria se modifica al liberar; acá queda el registro para poder deshacer (también
    // todo junto al "Descartar ediciones") y el estado de sesión (ediciones, prístinos, creación) del
    // slot liberado. Las claves de los slots posteriores se corren al compactar (ShiftSlotKeys).
    private readonly List<ReleaseRecord> _releases = new();
    public IReadOnlyList<ReleaseRecord> Releases => _releases;
    public void PushRelease(ReleaseRecord record) => _releases.Add(record);
    public void RemoveRelease(ReleaseRecord record) => _releases.Remove(record);
    public ReleaseRecord? PeekRelease() => _releases.Count > 0 ? _releases[^1] : null;
    public ReleaseRecord? PopRelease()
    {
        if (_releases.Count == 0) return null;
        var r = _releases[^1];
        _releases.RemoveAt(_releases.Count - 1);
        return r;
    }

    /// <summary>Estado de sesión de un slot (para guardarlo al liberar y devolverlo al deshacer).</summary>
    public sealed record SlotState(
        Dictionary<string, object?>? Edits, Dictionary<string, object?>? Pristine,
        string? DisplayName, string? Nickname, PKM? Creation);

    /// <summary>Saca (y devuelve) todo el estado de sesión de un slot.</summary>
    public SlotState TakeSlotState(Exxeguttor.UI.ViewModels.PokemonSlotKey key)
    {
        _pendingEdits.Remove(key, out var edits);
        _pristineValues.Remove(key, out var pristine);
        _displayNames.Remove(key, out var dn);
        _nicknames.Remove(key, out var nn);
        _pendingCreations.Remove(key, out var creation);
        return new SlotState(edits, pristine, dn, nn, creation);
    }

    /// <summary>Devuelve a un slot el estado guardado por <see cref="TakeSlotState"/>.</summary>
    public void PutSlotState(Exxeguttor.UI.ViewModels.PokemonSlotKey key, SlotState st)
    {
        if (st.Edits != null) _pendingEdits[key] = st.Edits;
        if (st.Pristine != null) _pristineValues[key] = st.Pristine;
        if (st.DisplayName != null) _displayNames[key] = st.DisplayName;
        if (st.Nickname != null) _nicknames[key] = st.Nickname;
        if (st.Creation != null) _pendingCreations[key] = st.Creation;
    }

    /// <summary>Corre <paramref name="delta"/> posiciones las claves de un contenedor (equipo, o una caja)
    /// cuyo índice sea &gt;= <paramref name="fromIndex"/>: lo que se compacta al liberar (−1) o se
    /// reabre al deshacer (+1). Mueve ediciones, prístinos, nombres y creaciones pendientes.</summary>
    public void ShiftSlotKeys(bool isParty, int boxIndex, int fromIndex, int delta)
    {
        bool InContainer(Exxeguttor.UI.ViewModels.PokemonSlotKey k) =>
            !k.IsBag && k.SlotIndex >= fromIndex && k.IsParty == isParty
            && (isParty ? k.BoxIndex == 0 : k.BoxIndex == boxIndex);

        Exxeguttor.UI.ViewModels.PokemonSlotKey Moved(Exxeguttor.UI.ViewModels.PokemonSlotKey k) => k with { SlotIndex = k.SlotIndex + delta };

        void Shift<T>(Dictionary<Exxeguttor.UI.ViewModels.PokemonSlotKey, T> dict)
        {
            var moving = dict.Where(kv => InContainer(kv.Key)).ToList();
            foreach (var kv in moving) dict.Remove(kv.Key);
            foreach (var kv in moving) dict[Moved(kv.Key)] = kv.Value;
        }
        Shift(_pendingEdits);
        Shift(_pristineValues);
        Shift(_displayNames);
        Shift(_nicknames);
        Shift(_pendingCreations);
    }

    /// <summary>Reset completo de la sesión de edición — TODO lo pendiente (ediciones,
    /// prístinos, nicknames, display names, creaciones) vuelve a cero. Usado en tres momentos
    /// (corrección de sesión "gestión de sesión de save"):
    ///   1. Al abrir un save nuevo (MainWindowViewModel.OpenSaveFromPathAsync) — bug encontrado:
    ///      antes NUNCA se llamaba acá, así que las ediciones pendientes de un save anterior
    ///      quedaban "pegadas" y se mostraban/aplicaban sobre el save recién abierto, aunque no
    ///      tuvieran nada que ver (las PokemonSlotKey son estructurales — Box1/Slot1 del save
    ///      viejo es la MISMA key que Box1/Slot1 del save nuevo).
    ///   2. Al cerrar el save actual (CloseSaveCommand) — mismo motivo, no hay save cargado
    ///      después de esto, no debería quedar nada pendiente en memoria.
    ///   3. Al limpiar ediciones sin cerrar el save (ClearEditsCommand, "volver al estado
    ///      inicial") — acá los prístinos igual se recapturan idénticos en el próximo reload
    ///      (nunca se mutó el SaveFile real), así que limpiarlos también es seguro y más simple
    ///      que tener un segundo método con semántica parecida pero distinta.
    /// </summary>
    public void ResetSession()
    {
        _releases.Clear();
        _pendingEdits.Clear();
        _pristineValues.Clear();
        _displayNames.Clear();
        _nicknames.Clear();
        _pendingCreations.Clear();
    }

    // ── Tabla de agrupación para ediciones del entrenador (TrainerViewModel) — aparte del
    // CategoryMap de Pokémon de abajo, aunque comparten el mismo mecanismo de
    // RecordEdit/CapturePristine vía la clave sentinel PokemonSlotKey.ForTrainer(). ──
    private static readonly Dictionary<string, (string Category, string? SubLabel)> TrainerCategoryMap = new()
    {
        ["Name"] = ("Nombre", null),
        ["Gender"] = ("Género", null),
        ["Money"] = ("Dinero", null),
        ["Coins"] = ("Monedas", null),
        ["BattlePoints"] = ("Battle Points", null),
    };

    // ── Tabla de agrupación: nombre crudo de propiedad → (categoría, sub-etiqueta) ──────
    // Puramente interna: el usuario nunca ve los nombres de propiedad, solo la categoría
    // (y la sub-etiqueta cuando aplica, ej. "Movimiento 1" en vez de repetir "Movimientos" x7).
    private static readonly Dictionary<string, (string Category, string? SubLabel)> CategoryMap = new()
    {
        ["Nickname"] = ("Apodo", null),
        ["IsNicknamedFlag"] = ("Apodo", null),
        ["PID"] = ("Datos internos", null),
        // Acompaña a "PID" cuando el solver de Method 1 (Gen3/4) tuvo que cambiar la habilidad
        // para que coincida con el PID elegido — ver Method1Solver y ApplyPidAbilitySlot.
        ["PIDAbilitySlot"] = ("Datos internos", null),
        // Resultado de la búsqueda de semilla (IvSeedSolver): fija EC, PID, IVs y lo que derive del
        // encuentro. Se aplica AL FINAL, encima de todo lo demás.
        ["IvSeedSolution"] = ("Datos internos", null),
        // Catch rate de Gen1 (botón "Corregir automáticamente" / modal de catch rate). Sin esta entrada la
        // edición se tomaba por "ruido derivado": no salía en la libreta ni en el modal de exportación, y
        // al exportar sin otros cambios no se abría el modal (bug reportado con un Magmar de Yellow).
        ["CatchRate"] = ("Datos internos", null),
        // Origen recreado (EncounterRecreationService): lugar, nivel/fecha de captura, bola, juego y PID.
        ["EncounterRecreation"] = ("Ubicación", null),
        ["Level"] = ("Nivel", null),
        ["Ability"] = ("Habilidad", null),
        ["Form"] = ("Forma", null),
        ["HeldItem"] = ("Objeto equipado", null),
        ["BallName"] = ("Bola", null),
        ["Nature"] = ("Naturaleza", null),
        ["SelectedNature"] = ("Naturaleza", null),
        ["Gender"] = ("Género", null),
        ["SelectedMetLocation"] = ("Ubicación", null),
        ["MetLevel"] = ("Ubicación", null),
        ["SelectedEggLocation"] = ("Huevo", null),
        ["EggDate"] = ("Huevo", null),
        ["HatchedFromEgg"] = ("Huevo", null),

        // Encontrado en sesión de revisión de UX: estos tres se trackeaban bien (Set<T> ya
        // llamaba RecordEdit para cualquier propiedad) pero al no estar en este mapa quedaban
        // como "ruido derivado" — la edición existía para restore-on-reload pero nunca
        // aparecía en la libreta ni en el modal de exportación. "Brillante" y "Huevo" ya
        // existían como categorías en LegalityMessageMapper (Exxeguttor.App) — mismo nombre acá
        // a propósito, para que el color de HasIssue en el modal correlacione bien si algún
        // día LegalityAnalysis reporta un problema de Shiny/Egg. "Pokérus" es nueva — no hay
        // CheckIdentifier de Pokérus en PKHeX.Core (no participa de la validación de
        // legalidad), así que nunca va a traer advertencia ⚠, y no hace falta que coincida
        // con ninguna categoría de LegalityMessageMapper.
        ["IsShiny"] = ("Brillante", null),
        ["IsEgg"] = ("Huevo", null),
        ["PkrsActive"] = ("Pokérus", null),

        ["IvHp"] = ("IVs/EVs", "PS (IV)"),
        ["IvAtk"] = ("IVs/EVs", "Ataque (IV)"),
        ["IvDef"] = ("IVs/EVs", "Defensa (IV)"),
        ["IvSpa"] = ("IVs/EVs", "At. Especial (IV)"),
        ["IvSpd"] = ("IVs/EVs", "Def. Especial (IV)"),
        ["IvSpe"] = ("IVs/EVs", "Velocidad (IV)"),
        ["EvHp"] = ("IVs/EVs", "PS (EV)"),
        ["EvAtk"] = ("IVs/EVs", "Ataque (EV)"),
        ["EvDef"] = ("IVs/EVs", "Defensa (EV)"),
        ["EvSpa"] = ("IVs/EVs", "At. Especial (EV)"),
        ["EvSpd"] = ("IVs/EVs", "Def. Especial (EV)"),
        ["EvSpe"] = ("IVs/EVs", "Velocidad (EV)"),

        ["Move1"] = ("Movimientos", "Movimiento 1"),
        ["Move1PPUps"] = ("Movimientos", "Movimiento 1"),
        ["Move2"] = ("Movimientos", "Movimiento 2"),
        ["Move2PPUps"] = ("Movimientos", "Movimiento 2"),
        ["Move3"] = ("Movimientos", "Movimiento 3"),
        ["Move3PPUps"] = ("Movimientos", "Movimiento 3"),
        ["Move4"] = ("Movimientos", "Movimiento 4"),
        ["Move4PPUps"] = ("Movimientos", "Movimiento 4"),

        ["DynamaxLevel"] = ("Dynamax / Gigantamax", null),
        ["CanGigantamax"] = ("Dynamax / Gigantamax", null),
        ["IsAlpha"] = ("Alpha / Noble", null),
        ["IsNoble"] = ("Alpha / Noble", null),
        ["SelectedTeraOverrideIndex"] = ("Tera Type", null),

        // Marca especial de "este Pokémon se creó esta sesión" — cuenta como categoría
        // tocada para el resumen liviano de la libreta (GetSimpleSummary), pero se excluye a
        // propósito de la lista de cambios normal en BuildSummary (se muestra aparte, como
        // badge "Creación", no mezclado con Nivel/Apodo/etc — ver IsNewlyCreated).
        ["__Created__"] = ("Creación", null),
    };

    // Las categorías de arriba son LLAVES internas (en español, por historia): LegalityMessageMapper
    // (Exxeguttor.App) devuelve los mismos nombres y así se correlaciona HasIssue/FriendlyMessage, y
    // CategoryOrder las ordena. Solo se traducen al mostrar (CatLabel/SubLabelText), nunca se renombran.
    private static readonly Dictionary<string, string> CategoryLabelKeys = new()
    {
        ["Nombre"] = "Review_Cat_Name", ["Género"] = "Review_Cat_Gender", ["Dinero"] = "Review_Cat_Money",
        ["Monedas"] = "Review_Cat_Coins", ["Battle Points"] = "Review_Cat_BattlePoints", ["Apodo"] = "Review_Cat_Nickname",
        ["Datos internos"] = "Review_Cat_Internal", ["Ubicación"] = "Review_Cat_Location", ["Nivel"] = "Review_Cat_Level",
        ["Habilidad"] = "Review_Cat_Ability", ["Forma"] = "Review_Cat_Form", ["Objeto equipado"] = "Review_Cat_HeldItem",
        ["Bola"] = "Review_Cat_Ball", ["Naturaleza"] = "Review_Cat_Nature", ["Huevo"] = "Review_Cat_Egg",
        ["Brillante"] = "Review_Cat_Shiny", ["Pokérus"] = "Review_Cat_Pokerus", ["IVs/EVs"] = "Review_Cat_IvsEvs",
        ["Movimientos"] = "Review_Cat_Moves", ["Dynamax / Gigantamax"] = "Review_Cat_Dynamax",
        ["Alpha / Noble"] = "Review_Cat_AlphaNoble", ["Tera Type"] = "Review_Cat_Tera", ["Cintas"] = "Review_Cat_Ribbons",
        ["Creación"] = "Review_Cat_Created",
    };

    /// <summary>Texto visible de una categoría (idioma de la UI). Una llave desconocida se muestra tal cual.</summary>
    private static string CatLabel(string category) =>
        CategoryLabelKeys.TryGetValue(category, out var key) ? UiText.T(key) : category;

    private static readonly string[] StatSubNames = { "PS", "Ataque", "Defensa", "At. Especial", "Def. Especial", "Velocidad" };
    private static readonly string[] StatSubKeys  = { "Hp", "Atk", "Def", "Spa", "Spd", "Spe" };

    /// <summary>Texto visible de una sub-etiqueta del mapa («Ataque (IV)», «Movimiento 2»), compuesto con claves.</summary>
    private static string SubLabelText(string sub)
    {
        for (var i = 0; i < StatSubNames.Length; i++)
        {
            if (sub == StatSubNames[i] + " (IV)") return UiText.F("Review_Sub_Iv", UiText.T("Review_Stat_" + StatSubKeys[i]));
            if (sub == StatSubNames[i] + " (EV)") return UiText.F("Review_Sub_Ev", UiText.T("Review_Stat_" + StatSubKeys[i]));
        }
        const string movePrefix = "Movimiento ";
        if (sub.StartsWith(movePrefix, StringComparison.Ordinal) && int.TryParse(sub[movePrefix.Length..], out var n))
            return UiText.F("Review_Sub_Move", n);
        return sub;
    }

    /// <summary>Orden fijo en que se muestran las categorías en cada tarjeta del modal —
    /// no alfabético, sigue el mismo criterio que el orden de tabs del editor.</summary>
    private static readonly string[] CategoryOrder =
    {
        "Nivel", "Apodo", "Género", "Brillante", "Habilidad", "Naturaleza", "Objeto equipado",
        "Ubicación", "Huevo", "IVs/EVs", "Movimientos", "Pokérus", "Dynamax / Gigantamax",
        "Alpha / Noble", "Tera Type", "Cintas",
    };

    /// <summary>Registra el valor "tal cual venía" de una propiedad para un slot — solo la
    /// primera vez (llamadas siguientes son no-op), porque representa el dato original del
    /// save, que no cambia aunque el usuario reabra el mismo Pokémon varias veces en la
    /// misma sesión. Llamado desde PokemonEditorViewModel.Set&lt;T&gt; mientras `_isLoading`
    /// es true (tanto en la población inicial desde el PKM como en RestorePendingEdits —
    /// la primera de las dos gana, que es siempre la población inicial real).</summary>
    public void CapturePristine(Exxeguttor.UI.ViewModels.PokemonSlotKey key, string propertyName, object? value)
    {
        if (!_pristineValues.TryGetValue(key, out var pristine))
            _pristineValues[key] = pristine = new Dictionary<string, object?>();
        if (!pristine.ContainsKey(propertyName))
            pristine[propertyName] = value;
    }

    /// <summary>Llamado desde PokemonEditorViewModel.Set&lt;T&gt; en cada cambio real (fuera de una carga).
    /// Si el nuevo valor coincide con el valor prístino (ver CapturePristine), se considera que
    /// la edición se revirtió y se borra la entrada — así tildar y destildar algo (o volver a
    /// una selección de combo anterior) no queda marcado como cambio para siempre.</summary>
    // DIAGNÓSTICO TEMPORAL — accesible desde afuera (PokemonEditorViewModel) para mostrarlo
    // en el mismo recuadro amarillo. Sacar apenas encontremos el problema real.
    public static string LastRecordEditDebug = "(nada todavía)";

    public void RecordEdit(Exxeguttor.UI.ViewModels.PokemonSlotKey key, string propertyName, object? value)
    {
        if (!_pendingEdits.TryGetValue(key, out var edits))
            _pendingEdits[key] = edits = new Dictionary<string, object?>();

        var hasPristineDict = _pristineValues.TryGetValue(key, out var pristine);
        object? original = null;
        var hasOriginal = hasPristineDict && pristine!.TryGetValue(propertyName, out original);
        var matches = hasOriginal && Equals(original, value);
        LastRecordEditDebug = $"RecordEdit({propertyName}, {value}): hasPristineDict={hasPristineDict}, hasOriginal={hasOriginal}, original={(hasOriginal ? original : "N/A")}, matches={matches} => {(matches ? "REMOVE" : "SET")}";

        if (matches)
        {
            edits.Remove(propertyName);
        }
        else
        {
            edits[propertyName] = value;
        }

        if (propertyName == "Nickname" && value is string nick) _nicknames[key] = nick;
    }

    /// <summary>Nombre para mostrar (especie) en la tarjeta del modal — la posición (Caja/Equipo)
    /// se arma aparte en el subtítulo. Se actualiza en cada carga (LoadPokemon).</summary>
    public void SetDisplayName(Exxeguttor.UI.ViewModels.PokemonSlotKey key, string displayName) =>
        _displayNames[key] = displayName;

    /// <summary>Apodo actual (para el subtítulo de la tarjeta) — se actualiza en cada carga,
    /// independiente de si el apodo se editó o no esta sesión.</summary>
    public void SetNickname(Exxeguttor.UI.ViewModels.PokemonSlotKey key, string nickname) =>
        _nicknames[key] = nickname;

    /// <summary>Valores crudos guardados para un slot (para reaplicar vía reflection al volver a cargarlo).</summary>
    public IReadOnlyDictionary<string, object?>? GetRawEdits(Exxeguttor.UI.ViewModels.PokemonSlotKey key) =>
        _pendingEdits.TryGetValue(key, out var edits) ? edits : null;

    /// <summary>True si hay algo que se perdería al cerrar el save o limpiar la sesión sin
    /// exportar — ediciones de propiedades O Pokémon creados pendientes (bug encontrado: antes
    /// solo miraba _pendingEdits, así que crear un Pokémon nuevo sin tocarle ninguna propiedad
    /// todavía no contaba como "hay cambios", y se podía perder en silencio).</summary>
    public bool HasAnyEdits => _pendingEdits.Values.Any(d => d.Count > 0) || _pendingCreations.Count > 0 || _releases.Count > 0;

    /// <summary>Resumen liviano para el ícono de la libreta: una línea por Pokémon con la
    /// cantidad de categorías tocadas — sin desglose y sin correr LegalityAnalysis (eso
    /// solo se calcula en BuildSummary, al momento de exportar, que es más costoso).</summary>
    public List<NotebookEntry> GetSimpleSummary()
    {
        var result = new List<NotebookEntry>();
        foreach (var (key, edits) in _pendingEdits)
        {
            if (edits.Count == 0) continue;

            if (key.IsBag)
            {
                // Cada key de "edits" acá es un ItemId (ver BagViewModel.RecordEdit) — no hay
                // categorías que mapear como con Pokémon, cada ítem tocado cuenta como 1
                // cambio. Mismo pouch se resume aparte con detalle en BuildBagSummaries()
                // para el modal de exportación; esto es solo la línea liviana de la libreta.
                var pouchType = key.BagPouchType;
                var prefix = key.IsBagAlternateStorage ? UiText.T("Review_Store_Pc") + " — " : "";
                var label = $"{BagPouchDatabase.Emoji(pouchType)} {prefix}{BagPouchDatabase.DisplayName(pouchType)}";
                result.Add(new NotebookEntry(label, edits.Count));
                continue;
            }

            if (key == Exxeguttor.UI.ViewModels.PokemonSlotKey.ForTrainer())
            {
                // Mismo motivo que el guard de BuildSummary: "Gender" es una clave compartida
                // con Pokémon en CategoryMap, así que sin este branch aparte el entrenador
                // terminaba etiquetado "Pokémon: Género" en la libreta en vez de mostrarse
                // como lo que es.
                result.Add(new NotebookEntry("🎓 " + UiText.T("Review_Trainer"), edits.Count));
                continue;
            }

            var categories = new HashSet<string>();
            foreach (var propName in edits.Keys)
            {
                if (propName.StartsWith("Ribbon:")) { categories.Add("Cintas"); continue; }
                if (CategoryMap.TryGetValue(propName, out var mapping)) categories.Add(mapping.Category);
            }
            if (categories.Count == 0) continue;

            var name = _displayNames.TryGetValue(key, out var n) ? n : "Pokémon";
            result.Add(new NotebookEntry(name, categories.Count));
        }
        foreach (var r in _releases)
            result.Add(new NotebookEntry(UiText.F("Release_Notebook", r.DisplayName), 1));
        return result.OrderBy(e => e.DisplayName).ToList();
    }

    /// <summary>
    /// Arma el resumen agrupado y legible para el modal de revisión antes de exportar.
    /// <paramref name="fetchPristine"/> permite releer el PKM original (sin ediciones de esta
    /// sesión) desde PokemonService para poder clonar + aplicar + correr LegalityAnalysis sin
    /// tocar el PKM real — puramente para saber si la edición dejaría al Pokémon ilegal.
    /// <paramref name="onProgress"/> es opcional (0.0-1.0) — agregado para el loader temático
    /// de la Pokédex (mockups/legality_scan): a diferencia de LoadLegalityAsync en
    /// PokemonEditorViewModel (un solo Pokémon, operación atómica sin granularidad real
    /// posible), acá SÍ hay un loop real sobre todas las entradas pendientes, así que el
    /// progreso puede ser real en vez de puramente decorativo. Se reporta por CADA entrada
    /// del loop (incluidas las que terminan en `continue` — Entrenador/Mochila/sin ediciones
    /// reales) para que el progreso siempre llegue a 1.0 al final del método sin importar
    /// cuántas terminen filtradas, no solo por las que sí generan un EditedPokemonSummary.
    /// <paramref name="onScanSpecies"/> es opcional — agregado junto con la escena de la
    /// Pokédex (feedback real: "debería mostrar el sprite del Pokémon siendo escaneado, si
    /// son varios de uno en uno"). A diferencia de <paramref name="onProgress"/>, este SOLO
    /// se invoca para entradas que sí llegan a resolver un <c>speciesId</c> real (reusa el
    /// mismo cálculo que ya existía para el sprite de <see cref="EditedPokemonSummary"/>, no
    /// duplica la lógica de <c>_pendingCreations</c> vs. <c>fetchPristine</c>) — nunca se
    /// llama con <c>0</c> ni con entradas de Entrenador/Mochila que ya se filtraron antes.
    /// </summary>
    public List<EditedPokemonSummary> BuildSummary(
        Func<Exxeguttor.UI.ViewModels.PokemonSlotKey, PKM?> fetchPristine,
        Action<double>? onProgress = null,
        Action<int>? onScanSpecies = null)
    {
        var result = new List<EditedPokemonSummary>();
        var total = _pendingEdits.Count;
        var processed = 0;
        foreach (var (key, edits) in _pendingEdits)
        {
            processed++;
            onProgress?.Invoke(total == 0 ? 1.0 : (double)processed / total);

            // BuildSummary es SOLO para Pokémon — el entrenador y la mochila tienen sus
            // propios Build*Summary (BuildTrainerSummary/BuildBagSummaries). Sin este guard,
            // si el usuario toca "Gender" del entrenador (clave compartida con el Gender de
            // Pokémon en CategoryMap), esta clave termina siendo tratada como Pokémon y
            // dispara fetchPristine(ForTrainer()) — que llama PokemonService.GetBox(-1) y
            // explota, porque -1 no es un índice de caja válido (bug real reportado en
            // producción, confirmado con esta combinación exacta: editar Trainer + Pokémon).
            if (key.IsBag || key == Exxeguttor.UI.ViewModels.PokemonSlotKey.ForTrainer()) continue;
            if (edits.Count == 0) continue;

            var grouped = new Dictionary<string, SortedSet<string>>();
            foreach (var propName in edits.Keys)
            {
                if (propName == "__Created__") continue; // se muestra aparte (IsNewlyCreated), no como categoría más
                if (!CategoryMap.TryGetValue(propName, out var mapping)) continue; // ruido derivado, se ignora
                if (!grouped.TryGetValue(mapping.Category, out var subLabels))
                    grouped[mapping.Category] = subLabels = new SortedSet<string>();
                if (mapping.SubLabel != null) subLabels.Add(SubLabelText(mapping.SubLabel));
            }
            var isNewlyCreated = edits.ContainsKey("__Created__");
            // Cintas se agrupan aparte (prefijo "Ribbon:", no pasa por CategoryMap normal)
            var ribbonLabels = edits.Keys
                .Where(k => k.StartsWith("Ribbon:"))
                .Select(k => RibbonDatabase.GetDisplayNameByKey(k["Ribbon:".Length..]))
                .ToList();
            if (ribbonLabels.Count > 0)
                grouped["Cintas"] = new SortedSet<string>(ribbonLabels);

            if (grouped.Count == 0 && !isNewlyCreated) continue; // todo lo tocado era ruido derivado

            // Mismo problema que ya se había resuelto para el sprite un poco más abajo (ver
            // comentario ahí), pero acá no se le había hecho el mismo arreglo: para una
            // creación pendiente, el slot real todavía está vacío en el save — fetchPristine
            // no encuentra nada útil ahí, así que el preview de legalidad terminaba
            // analizando un PKM en blanco (Species=0) y eso PKHeX lo marca ilegal siempre.
            // Resultado real reportado: "casi todos los Pokémon que creo salen ilegales en
            // el modal, aunque en el editor se vean legales" — el editor principal SÍ
            // agarra bien el PKM recién creado (via _currentPkm, no fetchPristine), por eso
            // ahí se veía bien; este BuildSummary es el único lugar que todavía usaba el
            // fetchPristine sin corregir.
            PKM? FetchPristineOrPendingCreation(Exxeguttor.UI.ViewModels.PokemonSlotKey k) =>
                _pendingCreations.TryGetValue(k, out var created) ? created : fetchPristine(k);

            var legality = AnalyzeLegality(key, edits, FetchPristineOrPendingCreation);

            EditedFieldSummary MakeField(string cat) => new(
                CatLabel(cat), grouped[cat].ToList(), legality.Categories.Contains(cat),
                legality.FriendlyByCategory.TryGetValue(cat, out var msg) ? msg : null);

            var lines = CategoryOrder
                .Where(grouped.ContainsKey)
                .Select(MakeField)
                .Concat(grouped.Keys.Except(CategoryOrder) // por si aparece algo no contemplado en el orden fijo
                    .Select(MakeField))
                .ToList();

            // Sprite: para una creación pendiente, la especie sale del PKM recién construido
            // (fetchPristine no sirve acá — el slot real todavía está vacío en el save). Para
            // una edición sobre un Pokémon que ya existía, sale del PKM prístino.
            var speciesId = _pendingCreations.TryGetValue(key, out var createdPkm)
                ? createdPkm.Species
                : fetchPristine(key)?.Species ?? 0;
            var sprite = speciesId > 0 ? SpriteService.Instance.GetPokemonSprite(speciesId, false) : null;
            if (speciesId > 0) onScanSpecies?.Invoke(speciesId);

            result.Add(new EditedPokemonSummary(
                key,
                _displayNames.TryGetValue(key, out var name) ? name : "Pokémon",
                _nicknames.TryGetValue(key, out var nick) ? nick : string.Empty,
                legality.IsLegal,
                legality.FriendlySummaryText,
                legality.TechnicalDetail,
                lines,
                isNewlyCreated,
                sprite));
        }
        return result.OrderBy(s => s.DisplayName).ToList();
    }

    /// <summary>Etapa 4.6.2/4.6.3 — construye el mismo preview que usa AnalyzeLegality (clon
    /// del PKM prístino + edits "seguros" aplicados) y corre LegalityAnalysis sobre él, sin
    /// todavía traducir el resultado a nada (ni LegalitySummary ni DiagnosticStep) — eso lo
    /// hace cada caller según lo que necesite. Extraído para no duplicar esta lógica entre
    /// AnalyzeLegality (resumen amigable, modal de revisión) y BuildLiveDiagnosticSteps
    /// (fichas + resaltado de campo, edición en vivo) — antes solo existía la primera.</summary>
    private static bool TryBuildPreviewAnalysis(
        Exxeguttor.UI.ViewModels.PokemonSlotKey key,
        IReadOnlyDictionary<string, object?> edits,
        Func<Exxeguttor.UI.ViewModels.PokemonSlotKey, PKM?> fetchPristine,
        out PKM previewPkm,
        out LegalityAnalysis analysis)
    {
        previewPkm = null!;
        analysis = null!;

        PKM? pristine;
        try { pristine = fetchPristine(key); }
        catch { return false; } // no se pudo releer, no bloqueamos la revisión por esto

        if (pristine == null) return false;

        PKM preview;
        try { preview = pristine.Clone(); }
        catch { return false; }

        try
        {
            if (edits.TryGetValue("Nickname", out var nick) && nick is string nickStr)
                preview.Nickname = nickStr;

            // Post-4.6 — ver mismo comentario en EditApplyService.ApplyFieldsToPkm: antes
            // forzado a true sin condición junto con el Nickname, ahora es su propio campo
            // con checkbox propio (IsNicknamedFlag), para que el preview de legalidad
            // refleje lo que se va a escribir de verdad.
            if (edits.TryGetValue("IsNicknamedFlag", out var nicknamedVal) && nicknamedVal is bool nicknamedBool)
                preview.IsNicknamed = nicknamedBool;

            // Post-4.6 — mismo comentario que EditApplyService.ApplyFieldsToPkm: "PID" solo
            // lo escribe el botón "Corregir automáticamente" del tab Diagnóstico, nunca un
            // control de UI directo.
            if (edits.TryGetValue("PID", out var pidVal) && pidVal is uint pidUint)
                preview.PID = pidUint;

            // Gen3/4: la habilidad sale del bit más bajo del PID. Setear Ability a mano no
            // alcanza en Gen3 (el bit vive en el IV32) — hay que refrescarla por slot.
            if (edits.TryGetValue("PIDAbilitySlot", out var pidSlotVal) && pidSlotVal is int pidSlot)
                preview.RefreshAbility(pidSlot);

            // Mismo mapeo que EditApplyService.ApplyFieldsToPkm. Antes el preview ignoraba el
            // Género: ahora hace falta porque el solver de Method 1 puede cambiarlo (en Gen4 es
            // un campo guardado, no derivado del PID).
            if (edits.TryGetValue("Gender", out var genderVal) && genderVal is string genderStr)
                preview.Gender = genderStr switch { "♂" => 0, "♀" => 1, _ => 2 };

            if (edits.TryGetValue("Level", out var lvl) && lvl is int lvlInt)
                preview.CurrentLevel = (byte)lvlInt;

            // Post-4.6 — ver mismo comentario en EditApplyService.ApplyFieldsToPkm.
            if (edits.TryGetValue("MetLevel", out var metLvl) && metLvl is int metLvlInt)
                preview.MetLevel = (byte)metLvlInt;

            // Mecánicas del tab Special — mismo mapeo que EditApplyService.ApplyFieldsToPkm. Antes el
            // preview las ignoraba: Gigantamax/Alpha/Tera editados no se reflejaban en el diagnóstico.
            if (preview is PK9 pvTera && edits.TryGetValue("SelectedTeraOverrideIndex", out var pvTeraVal) && pvTeraVal is int pvTeraIdx)
                pvTera.TeraTypeOverride = (MoveType)TeraOverrideCodec.ToCode(pvTeraIdx);
            if (preview is PK8 pvDmax)
            {
                if (edits.TryGetValue("DynamaxLevel", out var pvDl) && pvDl is int pvDlInt) pvDmax.DynamaxLevel = (byte)pvDlInt;
                if (edits.TryGetValue("CanGigantamax", out var pvCg) && pvCg is bool pvCgBool) pvDmax.CanGigantamax = pvCgBool;
            }
            if (preview is PA8 pvPa8)
            {
                if (edits.TryGetValue("IsAlpha", out var pvIa) && pvIa is bool pvIaBool) pvPa8.IsAlpha = pvIaBool;
                if (edits.TryGetValue("IsNoble", out var pvIn) && pvIn is bool pvInBool) pvPa8.IsNoble = pvInBool;
            }
            if (preview is PA9 pvPa9 && edits.TryGetValue("IsAlpha", out var pvIa9) && pvIa9 is bool pvIa9Bool)
                pvPa9.IsAlpha = pvIa9Bool;

            if ((edits.TryGetValue("Nature", out var natVal) && natVal is string natStr) ||
                (edits.TryGetValue("SelectedNature", out natVal) && natVal is string natStr2 && (natStr = natStr2) != null))
            {
                if (Enum.TryParse<Nature>(natStr, out var natureEnum))
                    preview.Nature = natureEnum;
            }

            void ApplyIv(string key2, Action<int> setter) { if (edits.TryGetValue(key2, out var v) && v is int i) setter(i); }
            ApplyIv("IvHp",  v => preview.IV_HP  = v);
            ApplyIv("IvAtk", v => preview.IV_ATK = v);
            ApplyIv("IvDef", v => preview.IV_DEF = v);
            ApplyIv("IvSpa", v => preview.IV_SPA = v);
            ApplyIv("IvSpd", v => preview.IV_SPD = v);
            ApplyIv("IvSpe", v => preview.IV_SPE = v);
            ApplyIv("EvHp",  v => preview.EV_HP  = v);
            ApplyIv("EvAtk", v => preview.EV_ATK = v);
            ApplyIv("EvDef", v => preview.EV_DEF = v);
            ApplyIv("EvSpa", v => preview.EV_SPA = v);
            ApplyIv("EvSpd", v => preview.EV_SPD = v);
            ApplyIv("EvSpe", v => preview.EV_SPE = v);

            // Al cambiar el movimiento (o sus PP Ups) de un slot, el PP ACTUAL guardado en el PKM
            // sigue siendo el del movimiento anterior (setear MoveN no lo toca). Con un movimiento
            // nuevo de menos PP máximos, PKHeX marca "PP por encima del máximo". La UI ya muestra
            // el PP máximo del movimiento nuevo (ApplyMoveSelection), así que acá se replica:
            // HealPPIndex = GetMovePP(move, ppUps) — DESPUÉS de setear movimiento y PP Ups.
            void ApplyMove(int index, string nameKey, string idKey, string ppUpsKey, Action<ushort> setMove, Action<int> setPPUps)
            {
                bool touched = false;
                // Por id: no depende del idioma de los datos en que se eligió el nombre (gana sobre el nombre).
                if (edits.TryGetValue(idKey, out var mvId) && mvId is int moveIdVal && moveIdVal > 0)
                {
                    setMove((ushort)moveIdVal); touched = true;
                }
                else if (edits.TryGetValue(nameKey, out var mv) && mv is string moveName)
                {
                    var entry = MoveDatabase.Instance.GetByName(moveName);
                    if (entry != null) { setMove((ushort)entry.Id); touched = true; }
                }
                if (edits.TryGetValue(ppUpsKey, out var pu) && pu is int puInt)
                {
                    setPPUps(puInt);
                    touched = true;
                }
                if (touched) preview.HealPPIndex(index);
            }
            ApplyMove(0, "Move1", "Move1Id", "Move1PPUps", v => preview.Move1 = v, v => preview.Move1_PPUps = v);
            ApplyMove(1, "Move2", "Move2Id", "Move2PPUps", v => preview.Move2 = v, v => preview.Move2_PPUps = v);
            ApplyMove(2, "Move3", "Move3Id", "Move3PPUps", v => preview.Move3 = v, v => preview.Move3_PPUps = v);
            ApplyMove(3, "Move4", "Move4Id", "Move4PPUps", v => preview.Move4 = v, v => preview.Move4_PPUps = v);

            // Habilidad/HeldItem/Ball/Ubicación/Huevo — antes quedaban afuera de este preview
            // (ver comentario de clase), lo que significaba que el diagnosticador de legalidad
            // (Etapa 4) no podía reflejar en vivo estos campos. Mismo mapeo nombre→id que
            // EditApplyService.ApplyFieldsToPkm (fuente de verdad para la escritura real) —
            // si ese mapeo cambia, hay que actualizar los dos lugares.

            // Forma — ANTES que Habilidad/HeldItem: su efecto secundario (que trae su propia
            // habilidad/ítem por defecto) debe aplicarse primero, para que si el usuario también
            // edita Habilidad manualmente en la misma sesión, su elección explícita gane.
            if (edits.TryGetValue("Form", out var formVal) && formVal is string formName
                && PokemonService.TryGetFormIndex(preview, formName, out var formIdx))
            {
                PokemonService.ApplyForm(preview, formIdx);
            }

            if (edits.TryGetValue("Ability", out var abilityVal) && abilityVal is string abilityName)
            {
                var entry = AbilityDatabase.Instance.GetByName(abilityName);
                if (entry != null) preview.Ability = entry.Id;
            }

            if (edits.TryGetValue("HeldItem", out var itemVal) && itemVal is string itemName)
            {
                var entry = ItemDatabase.Instance.GetByName(itemName);
                if (entry != null) preview.HeldItem = entry.Id;
            }
            // Mismo criterio que EditApplyService: el id crudo del tab Special gana sobre el nombre.
            if (edits.TryGetValue("HeldItemRawId", out var pvRawItemVal) && pvRawItemVal is int pvRawItemId && pvRawItemId > 0)
                preview.HeldItem = pvRawItemId;

            if (edits.TryGetValue("BallName", out var ballVal) && ballVal is string ballName)
            {
                var entry = ItemDatabase.Instance.GetByName(ballName);
                if (entry != null) preview.Ball = (byte)entry.Id;
            }

            if (edits.TryGetValue("SelectedMetLocation", out var metLocVal) && metLocVal is ComboItem metLocItem)
                preview.MetLocation = (ushort)metLocItem.Value;

            if (edits.TryGetValue("SelectedEggLocation", out var eggLocVal) && eggLocVal is ComboItem eggLocItem)
                preview.EggLocation = (ushort)eggLocItem.Value;

            // CatchRate (Gen1): lo escribe el botón "Corregir automáticamente" de los códigos G1CatchRate*.
            // Antes solo se aplicaba al EXPORTAR (EditApplyService) y no en este preview, así que tras tocar
            // el botón el análisis de legalidad seguía mostrando el rate viejo y la alerta no se apagaba
            // (bug reportado con un Magmar de Yellow).
            if (edits.TryGetValue("CatchRate", out var catchRateVal) && catchRateVal is byte catchRateByte
                && preview is PK1 previewPk1)
                previewPk1.CatchRate = catchRateByte;

            // Origen recreado: reescribe lugar/nivel/fecha de captura, bola y PID con el Pokémon ya verificado.
            if (edits.TryGetValue("EncounterRecreation", out var recVal) && recVal is EncounterRecreation rec)
                rec.ApplyTo(preview);

            // Solución de la búsqueda de semilla: va AL FINAL para que gane sobre cualquier edición
            // de IVs/PID anterior (los IVs que trae son los que el usuario eligió en el modal).
            if (edits.TryGetValue("IvSeedSolution", out var seedSolVal) && seedSolVal is IvSeedSolution seedSol)
                seedSol.ApplyTo(preview);

            preview.RefreshChecksum();
        }
        catch
        {
            // Si algo de esto falla (versión de PKHeX con nombres distintos, tipo de PKM
            // inesperado, etc.) preferimos no bloquear la revisión — se muestra como legal
            // por defecto y sin correlación, en vez de tirar una excepción al usuario.
            return false;
        }

        try { analysis = new LegalityAnalysis(preview); }
        catch { return false; }

        previewPkm = preview;
        return true;
    }

    /// <summary>Clona el PKM prístino, aplica los campos "seguros" de la edición (los que
    /// tenemos mapeo confiable de cómo escribirlos en un PKM real: Nickname, Level, Nature,
    /// IVs, EVs, Moves+PPUps, Habilidad, HeldItem, Ball, Ubicación, Huevo) y corre
    /// LegalityAnalysis — solo para saber si quedaría ilegal, nunca se guarda ni se toca el
    /// PKM real. Ribbons/Special quedan afuera de este análisis por ahora (se muestran igual
    /// en la lista de cambios, solo no participan de la correlación de legalidad).</summary>
    // Público a partir de la Etapa 4.6.2: PokemonEditorViewModel lo llama directo para el
    // recálculo de legalidad en vivo mientras se edita (antes solo se usaba internamente
    // desde BuildSummary, para el modal de revisión pre-exportación). Misma firma, mismo
    // comportamiento — solo cambia quién puede invocarlo.
    /// <summary>El Pokémon con TODAS las ediciones pendientes aplicadas (mismo preview que usa el
    /// análisis de legalidad en vivo). Lo necesitan los botones "Corregir automáticamente": antes
    /// calculaban sobre el PKM original del save y por eso ignoraban lo recién editado — p. ej.
    /// el fix de Method 1 armaba un PID para los IVs VIEJOS y el mensaje de ilegalidad seguía.</summary>
    public static bool TryBuildEditedPkm(
        Exxeguttor.UI.ViewModels.PokemonSlotKey key,
        IReadOnlyDictionary<string, object?> edits,
        Func<Exxeguttor.UI.ViewModels.PokemonSlotKey, PKM?> fetchPristine,
        out PKM editedPkm)
    {
        var ok = TryBuildPreviewAnalysis(key, edits, fetchPristine, out editedPkm, out _);
        return ok;
    }

    /// <summary>Quita UNA edición ya registrada (no existe equivalente en RecordEdit: ese compara
    /// contra el valor original y solo borra si coincide). Lo usa el solver de semillas para
    /// descartar su resultado cuando el usuario vuelve a tocar IVs a mano.</summary>
    public void RemoveEdit(Exxeguttor.UI.ViewModels.PokemonSlotKey key, string propertyName)
    {
        if (_pendingEdits.TryGetValue(key, out var bag) && bag.Remove(propertyName) && bag.Count == 0)
            _pendingEdits.Remove(key);
    }

    public static LegalitySummary AnalyzeLegality(
        Exxeguttor.UI.ViewModels.PokemonSlotKey key,
        IReadOnlyDictionary<string, object?> edits,
        Func<Exxeguttor.UI.ViewModels.PokemonSlotKey, PKM?> fetchPristine)
    {
        if (!TryBuildPreviewAnalysis(key, edits, fetchPristine, out _, out var la))
            return LegalitySummary.LegalResult;

        // Categorización + traducción a mensajes amigables: única fuente compartida con
        // PokemonService.GetLegalitySummary (badge del panel principal) — ver
        // LegalityMessageMapper (Exxeguttor.App).
        return LegalityMessageMapper.Summarize(la);
    }

    /// <summary>Etapa 4.6.3 — mismo preview que AnalyzeLegality de arriba, pero devuelve los
    /// DiagnosticStep completos (categoría + severidad + FieldFix accionables por campo) en
    /// vez del resumen de una línea, para las fichas de categoría y el resaltado de campo del
    /// editor. Lista vacía si el preview no se pudo construir (mismo criterio "no bloquear la
    /// UI" que el resto de este archivo) — nunca null, para que el caller pueda hacer
    /// foreach/Clear+AddRange sin chequeo extra.</summary>
    public static List<DiagnosticStep> BuildLiveDiagnosticSteps(
        Exxeguttor.UI.ViewModels.PokemonSlotKey key,
        IReadOnlyDictionary<string, object?> edits,
        Func<Exxeguttor.UI.ViewModels.PokemonSlotKey, PKM?> fetchPristine)
    {
        if (!TryBuildPreviewAnalysis(key, edits, fetchPristine, out var preview, out var la))
            return [];

        try { return LegalityDiagnosticBuilder.BuildDiagnosticSteps(la, preview); }
        catch { return []; } // mismo criterio: un fallo acá no debe romper la edición en vivo
    }

    /// <summary>Arma la tarjeta de entrenador para el modal de revisión — mismo mecanismo
    /// de diff contra prístino que los Pokémon (ver PokemonSlotKey.ForTrainer() y
    /// TrainerViewModel.Set&lt;T&gt;), pero sin legalidad (no hay CheckIdentifier para datos
    /// de entrenador) — por eso HasIssue siempre queda en false: las píldoras de categoría
    /// se ven igual de "bonitas" que las de Pokémon, solo que siempre en verde.
    /// Devuelve null si no hay ninguna edición pendiente del entrenador.</summary>
    public EditedTrainerSummary? BuildTrainerSummary(string displayName, bool isFemale, int? gameVersionId = null)
    {
        var edits = GetRawEdits(Exxeguttor.UI.ViewModels.PokemonSlotKey.ForTrainer());
        if (edits == null || edits.Count == 0) return null;

        var grouped = new Dictionary<string, SortedSet<string>>();
        foreach (var propName in edits.Keys)
        {
            if (!TrainerCategoryMap.TryGetValue(propName, out var mapping)) continue;
            if (!grouped.TryGetValue(mapping.Category, out var subLabels))
                grouped[mapping.Category] = subLabels = new SortedSet<string>();
            if (mapping.SubLabel != null) subLabels.Add(SubLabelText(mapping.SubLabel));
        }
        if (grouped.Count == 0) return null; // todo lo tocado era "ruido derivado" sin mapear

        var changes = grouped
            .Select(kv => new EditedFieldSummary(CatLabel(kv.Key), kv.Value.ToList(), false, null))
            .ToList();

        // Ícono por juego (ver SpriteService.GetTrainerSprite(int, bool)); sin versión → genérico.
        var sprite = gameVersionId is { } gv
            ? SpriteService.Instance.GetTrainerSprite(gv, isFemale)
            : SpriteService.Instance.GetTrainerSprite(isFemale);
        return new EditedTrainerSummary(displayName, changes, sprite);
    }

    /// <summary>Arma UNA tarjeta por origen (Mochila / PC) con todas las categorías tocadas
    /// mezcladas adentro — corrección de sesión "categorización de Mochila" (antes salía una
    /// tarjeta por categoría). Cada línea usa el nombre capturado en el momento de la edición
    /// (ver BagItemEditValue) — ya no hace falta ningún lookup por ID acá, así que da lo mismo
    /// si el "ItemId" de la clave es un RawItemId de PKHeX.Core o cualquier otra cosa: nunca se
    /// usa para mostrar texto. Sin legalidad (no hay CheckIdentifier para objetos) — mismo
    /// criterio que BuildTrainerSummary.</summary>
    public List<EditedBagPouchSummary> BuildBagSummaries()
    {
        var groups = _pendingEdits
            .Where(kv => kv.Key.IsBag && kv.Value.Count > 0)
            .GroupBy(kv => kv.Key.IsBagAlternateStorage);

        var result = new List<EditedBagPouchSummary>();
        foreach (var originGroup in groups)
        {
            var isAlt = originGroup.Key;
            var lines = new List<EditedBagItemLine>();
            var typesIncluded = new List<PKHeX.Core.InventoryType>();

            foreach (var (key, edits) in originGroup)
            {
                var type = key.BagPouchType;
                var categoryLabel = BagPouchDatabase.DisplayName(type);
                var hadAny = false;

                foreach (var (_, value) in edits)
                {
                    if (value is not BagItemEditValue bev) continue;
                    lines.Add(new EditedBagItemLine(categoryLabel, bev.ItemName, bev.Quantity));
                    hadAny = true;
                }
                if (hadAny) typesIncluded.Add(type);
            }
            if (lines.Count == 0) continue;

            var title = isAlt ? UiText.T("Review_Store_Pc") : UiText.T("Bag_Store_Bag");
            var icon = isAlt ? "🖥️" : "🎒";
            result.Add(new EditedBagPouchSummary(
                title,
                icon,
                lines.OrderBy(l => l.CategoryLabel, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(l => l.ItemName, StringComparer.CurrentCultureIgnoreCase)
                     .ToList(),
                typesIncluded,
                isAlt));
        }
        return result.OrderBy(s => s.PouchDisplayName).ToList();
    }

    /// <summary>Se llama tras un guardado/exportado exitoso — lo ya aplicado deja de ser "pendiente".</summary>
    public void ClearAll()
    {
        _pendingEdits.Clear();
        _displayNames.Clear();
        _nicknames.Clear();
        // Con liberaciones aplicadas, los índices de los Pokémon pudieron correrse: lo que quedara
        // guardado por slot (creaciones ya aplicadas, prístinos) apuntaría a otro Pokémon. Se descarta
        // para que se relea desde el save, que ya refleja todo.
        if (_releases.Count > 0)
        {
            _pendingCreations.Clear();
            _pristineValues.Clear();
        }
        _releases.Clear();
    }
}

/// <summary>Una categoría cambiada dentro de la tarjeta de un Pokémon (ej. "Movimientos" con
/// sub-etiquetas ["Movimiento 1", "Movimiento 3"]). HasIssue = algún chequeo de legalidad
/// fallido correlaciona con esta categoría (best-effort, ver LegalityMessageMapper en
/// Exxeguttor.App). FriendlyMessage trae el mensaje amigable de esa categoría, solo cuando
/// HasIssue es true.</summary>
/// <summary>Una línea del popup de la libreta: Pokémon + cuántas categorías tocó (sin desglose).</summary>
public sealed record NotebookEntry(string DisplayName, int ChangeCount);

/// <summary>Una liberación hecha en esta sesión (ver PokemonReleaseService). <c>Original</c> es el
/// Pokémon que se sacó del save (null si era una creación pendiente que nunca llegó al save);
/// <c>Compacted</c> indica que los siguientes subieron un lugar (equipo, o caja Gen 1/2/LGPE).</summary>
public sealed record ReleaseRecord(
    Exxeguttor.UI.ViewModels.PokemonSlotKey Key, PKM? Original, EditSessionService.SlotState State,
    bool Compacted, string DisplayName, string Where)
{
    /// <summary>Destildado en el modal de exportación = no liberar (solo posible si <c>PokemonReleaseService.CanExclude</c>).</summary>
    public bool Include { get; set; } = true;
}

public sealed record EditedFieldSummary(string Category, List<string> SubLabels, bool HasIssue, string? FriendlyMessage)
{
    /// <summary>Invertido para bindear directo contra LegalityColorConverter (que espera
    /// "es legal" = true → verde), sin tener que escribir un converter nuevo solo para esto.</summary>
    public bool IsOk => !HasIssue;
}

/// <summary>Una tarjeta del modal de revisión: un Pokémon editado, con checkbox de inclusión.</summary>
public sealed class EditedPokemonSummary
{
    public Exxeguttor.UI.ViewModels.PokemonSlotKey SlotKey { get; }
    public string DisplayName { get; }
    public string Nickname { get; }
    public bool IsLegal { get; }

    /// <summary>Mensaje amigable, sin jerga de PKHeX, una viñeta por categoría con problema
    /// (ver LegalityMessageMapper.FriendlySummaryText). Vacío si es legal.</summary>
    public string? FriendlyReason { get; }

    /// <summary>Detalle técnico crudo de PKHeX.Core (CheckResult.ToString() por cada issue) —
    /// nunca se descarta, pero queda oculto detrás de un "Ver detalle técnico" expandible en
    /// vez de ser lo primero que ve el usuario. Vacío si es legal.</summary>
    public string TechnicalDetail { get; }

    public List<EditedFieldSummary> Changes { get; }
    public bool IsIncluded { get; set; } = true; // tildado por defecto, revisión fresca cada vez

    /// <summary>True cuando este Pokémon todavía no existe en el save real — se creó esta
    /// sesión (ver EditSessionService.RecordPendingCreation) y sigue pendiente de exportar.
    /// Se muestra como badge aparte, arriba de la lista de cambios normales, en vez de mezclarse
    /// como una categoría más — ver Views/MainWindow.axaml.</summary>
    public bool IsNewlyCreated { get; }

    /// <summary>Sprite del Pokémon para la tarjeta — antes solo se mostraba el nombre.</summary>
    public Avalonia.Media.Imaging.Bitmap? Sprite { get; }

    public EditedPokemonSummary(Exxeguttor.UI.ViewModels.PokemonSlotKey slotKey, string displayName, string nickname,
        bool isLegal, string? friendlyReason, string technicalDetail, List<EditedFieldSummary> changes,
        bool isNewlyCreated, Avalonia.Media.Imaging.Bitmap? sprite)
    {
        SlotKey = slotKey;
        DisplayName = displayName;
        Nickname = nickname;
        IsLegal = isLegal;
        FriendlyReason = friendlyReason;
        TechnicalDetail = technicalDetail;
        Changes = changes;
        IsNewlyCreated = isNewlyCreated;
        Sprite = sprite;
    }
}

/// <summary>Tarjeta de entrenador para el modal de revisión — análoga a EditedPokemonSummary
/// pero sin legalidad (los datos de entrenador no tienen CheckIdentifier). Ahora sí tiene
/// checkbox de inclusión real (antes se exportaba siempre que hubiera alguna edición; el
/// pipeline real ahora filtra igual que Pokémon/Mochila).</summary>
public sealed class EditedTrainerSummary
{
    public string DisplayName { get; }
    public List<EditedFieldSummary> Changes { get; }
    public bool IsIncluded { get; set; } = true; // tildado por defecto, revisión fresca cada vez

    /// <summary>Sprite de entrenador/entrenadora — puede ser null si el proyecto todavía no
    /// tiene esos assets embebidos (ver SpriteService.GetTrainerSprite). La tarjeta debe
    /// manejar el caso null sin romperse.</summary>
    public Avalonia.Media.Imaging.Bitmap? Sprite { get; }

    public EditedTrainerSummary(string displayName, List<EditedFieldSummary> changes, Avalonia.Media.Imaging.Bitmap? sprite)
    {
        DisplayName = displayName;
        Changes = changes;
        Sprite = sprite;
    }
}

/// <summary>Un ítem cambiado dentro de la tarjeta de mochila — CategoryLabel identifica de qué
/// pouch/categoría es (ej. "Bolas", "MTs/MOs"), visible en la línea porque ahora una sola
/// tarjeta mezcla todas las categorías tocadas de un mismo origen (ver EditedBagPouchSummary).</summary>
public sealed record EditedBagItemLine(string CategoryLabel, string ItemName, int Quantity);

/// <summary>Tarjeta de mochila para el modal de revisión — UNA por origen (Mochila / PC), sin
/// importar cuántas categorías distintas se tocaron adentro (corrección de sesión
/// "categorización de Mochila": antes salía una tarjeta por categoría, quedaba fragmentado si
/// tocabas Bolas y Medicina en la misma sesión — el pedido fue "la mochila debería ser tarjeta
/// única"). El checkbox de inclusión ahora aplica a TODO el origen junto, no por categoría.
///
/// PouchTypesIncluded son las categorías reales que hay que aplicar al exportar — se necesita
/// aparte de Changes (que es solo para mostrar) porque EditApplyService.ApplyBagPouch escribe
/// por categoría física, una por una, y el checkbox de esta tarjeta ahora decide para TODAS a
/// la vez.
///
/// Sigue siendo <c>record</c> pero IsIncluded es mutable (init no, porque el binding del
/// checkbox necesita poder reasignarlo tras construir la tarjeta).</summary>
public sealed record EditedBagPouchSummary(
    string PouchDisplayName,
    string Icon,
    List<EditedBagItemLine> Changes,
    List<PKHeX.Core.InventoryType> PouchTypesIncluded,
    bool IsAlternateStorage = false)
{
    public bool IsIncluded { get; set; } = true;
}

/// <summary>Valor guardado por RecordEdit/CapturePristine para un ítem de mochila — reemplaza al
/// <c>int</c> plano de las versiones anteriores. Corrección de sesión "categorización de
/// Mochila": BuildBagSummaries resolvía el nombre a mostrar volviendo a buscar por ItemId vía
/// ItemDatabase — pero desde el fix de RawItemId, el "ItemId" que usa Mochila para sus claves
/// de edición ES el índice crudo de PKHeX.Core, no el ItemId unificado de pokemon.db, así que
/// ese lookup encontraba (o no) un ítem totalmente distinto ("el texto... no tiene nada que ver
/// con el objeto editado"). Ahora el nombre se captura UNA vez, en el momento de la edición
/// (BagViewModel ya lo tiene disponible ahí, de ItemInventoryService.GetItems) — nunca más hace
/// falta reconstruirlo a partir del ID.</summary>
public readonly record struct BagItemEditValue(string ItemName, int Quantity)
{
    // La igualdad ignora el nombre: es solo texto para mostrar y cambia con el idioma de los datos.
    // RecordEdit compara contra el valor prístino con Equals para detectar «volvió al original»;
    // si el nombre contara, cambiar de idioma a mitad de sesión dejaría ediciones revertidas como cambios.
    public bool Equals(BagItemEditValue other) => Quantity == other.Quantity;
    public override int GetHashCode() => Quantity;
}
