using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.ViewModels;
using PKHeX.Core;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Aplica de verdad las ediciones pendientes de la sesión (EditSessionService) al SaveFile
/// real, respetando el checkbox IsIncluded de cada tarjeta del modal de revisión. Hasta esta
/// clase, todo el proyecto solo trackeaba "qué cambió" en memoria (pendiente-hasta-exportar) y
/// `PerformExportAsync` escribía el SaveFile tal cual estaba, sin aplicar nada — este es el
/// pipeline de escritura real que cierra ese GAP CRÍTICO (ver CLAUDE.md/context.md).
///
/// Cuatro superficies, cada una con su propio mecanismo de aplicación (no son un diff
/// genérico, ver análisis de la sesión que armó esto):
///   1. Pokémon (Box/Party) — releer el PKM real (o tomar el ya construido si es una
///      creación pendiente), aplicar campos vía reflection/setters directos, RefreshChecksum,
///      SetBoxSlotAtIndex/SetPartySlotAtIndex.
///   2. Cintas — mismo PKM de arriba, vía RibbonWriter (dos caminos: IRibbonIndex moderno o
///      interfaces IRibbonSetX viejas, según formato).
///   3. Entrenador — directo sobre SaveFile, sin clon (ver TrainerService.ApplyEdits).
///   4. Mochila — reconstruir el pouch (array de tamaño FIJO, nunca cambia de largo — ver
///      WritePipelineRoundTripTests.MutarMochila_SeReflejaInmediatamente) y reasignar
///      save.Inventory completo.
///
/// Llamar SIEMPRE después de que el usuario confirmó el modal de revisión (ConfirmExportCommand
/// en MainWindowViewModel) y ANTES de PerformExportAsync/SaveFileService.SaveAsAsync — este
/// servicio muta el SaveFile en memoria, pero no lo escribe a disco (eso lo sigue haciendo
/// SaveFileService, sin cambios).
/// </summary>
public class EditApplyService
{
    private readonly EditSessionService _editSession;
    private readonly SaveFileService    _saveService;
    private readonly TrainerService     _trainerService;
    private readonly LocalizationService _loc;
    private readonly PokemonService     _pokemonService;

    public EditApplyService(EditSessionService editSession, SaveFileService saveService,
        TrainerService trainerService, LocalizationService loc, PokemonService pokemonService)
    {
        _editSession    = editSession;
        _saveService    = saveService;
        _trainerService = trainerService;
        _loc            = loc;
        _pokemonService = pokemonService;
    }

    /// <summary>Punto de entrada único — aplica las tres/cuatro superficies, respetando
    /// IsIncluded en cada tarjeta. No hace nada si no hay save cargado.</summary>
    public void ApplyIncluded(
        IEnumerable<EditedPokemonSummary> pokemonSummaries,
        EditedTrainerSummary? trainerSummary,
        IEnumerable<EditedBagPouchSummary> bagSummaries)
    {
        var save = _saveService.CurrentSave;
        if (save == null) return;

        foreach (var summary in pokemonSummaries)
        {
            if (!summary.IsIncluded) continue;
            ApplyPokemonSlot(save, summary.SlotKey);
        }

        if (trainerSummary is { IsIncluded: true })
            ApplyTrainer();

        foreach (var bag in bagSummaries)
        {
            if (!bag.IsIncluded) continue;
            // Una tarjeta ahora agrupa TODAS las categorías tocadas de un mismo origen (ver
            // EditedBagPouchSummary) — el checkbox de la tarjeta decide para todas juntas, pero
            // cada categoría se sigue escribiendo por separado (son pouches físicos distintos).
            foreach (var type in bag.PouchTypesIncluded)
                ApplyBagPouch(save, type, bag.IsAlternateStorage);
        }
    }

    // ── 1 y 2: Pokémon + Cintas ─────────────────────────────────────────────────────────────

    private void ApplyPokemonSlot(SaveFile save, PokemonSlotKey key)
    {
        var creation = _editSession.GetPendingCreation(key);
        var edits = _editSession.GetRawEdits(key);
        PKM pkm;

        if (creation != null)
        {
            // Creación pendiente: el PKM ya viene completo desde PokemonService.BuildPokemon
            // (ver "Crear Pokémon" en context.md). Igual hay que aplicar edits encima — el
            // usuario puede haber creado el Pokémon y después tocado algo más (Apodo, IVs,
            // etc.) sobre el mismo slot antes de exportar, y esas ediciones quedan en el
            // mismo diccionario que "__Created__", no en uno aparte.
            pkm = creation;
            if (edits != null && edits.Count > 0) ApplyFieldsToPkm(pkm, edits);
        }
        else
        {
            if (edits == null || edits.Count == 0) return;

            pkm = key.IsParty
                ? save.GetPartySlotAtIndex(key.SlotIndex)
                : save.GetBoxSlotAtIndex(key.BoxIndex, key.SlotIndex);

            ApplyFieldsToPkm(pkm, edits);
        }

        pkm.RefreshChecksum();

        // EntityImportSettings default (struct, "lo conservador" — no toca Pokédex ni
        // records) — confirmado que compila sin constructor explícito porque hereda de
        // System.ValueType, y que SetBoxSlotAtIndex/SetPartySlotAtIndex lo esperan como
        // último parámetro (confirmado por reflection sobre el DLL real).
        if (key.IsParty)
            save.SetPartySlotAtIndex(pkm, key.SlotIndex, default);
        else
            save.SetBoxSlotAtIndex(pkm, key.BoxIndex, key.SlotIndex, default);
    }

    /// <summary>Mismo mapeo campo→PKM que EditSessionService.AnalyzeLegality (preview de
    /// legalidad), pero escribiendo de verdad — extendido acá a Habilidad/HeldItem/Cintas/
    /// Dynamax/Alpha/Noble/Brillante/Huevo/Amistad/OT/TID/Género/Fechas, que el preview de
    /// legalidad no cubre todavía.
    ///
    /// Etapa "campos huérfanos del pipeline de escritura": hasta esta sesión, varios campos
    /// editables en el panel principal (Shiny, Huevo, Amistad, Género del Pokémon, OT, TID
    /// propio, Fecha de encuentro, Fecha/Ubicación de huevo) se grababan bien en
    /// EditSessionService (Set&lt;T&gt; llama RecordEdit siempre) y aparecían en la libreta/
    /// modal de revisión, pero nunca tenían un case acá — la edición se perdía en silencio
    /// al exportar, mismo síntoma que tuvo Met level/Ball/Ubicación antes de arreglarse (ver
    /// comentario de MetLevel más abajo). Confirmados con PKHeX.Core clonado (PKM.cs):
    /// Gender/CurrentFriendship/IsEgg/TID16/OriginalTrainerName son abstract get;set (settable
    /// directo); MetDate/EggMetDate son DateOnly? con setter propio; IsShiny es
    /// "virtual bool IsShiny => TSV == PSV" — get-only, sin setter — por eso pasa por
    /// PokemonService.TrySetShiny (sorteo de PID) en vez de una asignación directa.</summary>
    private void ApplyFieldsToPkm(PKM pkm, IReadOnlyDictionary<string, object?> edits)
    {
        // Forma — primero que todo: sus efectos secundarios (ítem, habilidad, tipo Tera,
        // género) deben quedar aplicados antes de que cualquier edición explícita de
        // Habilidad/Ítem en el mismo set de edits pueda sobreescribirlos a propósito.
        if (edits.TryGetValue("Form", out var formVal) && formVal is string formName
            && PokemonService.TryGetFormIndex(pkm, formName, out var formIdx))
        {
            PokemonService.ApplyForm(pkm, formIdx);
        }

        if (edits.TryGetValue("Nickname", out var nick) && nick is string nickStr)
            pkm.Nickname = nickStr;

        // Post-4.6 — antes esto vivía pegado al edit de "Nickname" de arriba y se forzaba a
        // true sin condición, sin ninguna forma de verlo ni de destildarlo desde la UI (causa
        // real de "apodo ilegal con texto normal": el flag no coincidía con el contenido).
        // Ahora es un campo independiente con su propio checkbox (IsNicknamedFlag).
        if (edits.TryGetValue("IsNicknamedFlag", out var nicknamedVal) && nicknamedVal is bool nicknamedBool)
            pkm.IsNicknamed = nicknamedBool;

        // Post-4.6 — "PID" no tiene un control propio en la UI (no tiene sentido pedirle al
        // usuario que tipee un hex a mano): esta clave solo la escribe
        // PokemonEditorViewModel.RegeneratePid, vía el botón "Corregir automáticamente" del
        // tab Diagnóstico (FieldFixAction.RegeneratePid) — ver PokemonService.
        // TryRegeneratePid para cómo se calcula el valor.
        if (edits.TryGetValue("PID", out var pidVal) && pidVal is uint pidUint)
            pkm.PID = pidUint;

        // Ver Method1Solver: en Gen3/4 la habilidad la dicta el bit bajo del PID; se refresca por
        // slot (en Gen3 el bit vive en el IV32, así que setear Ability por id no alcanzaría).
        if (edits.TryGetValue("PIDAbilitySlot", out var pidSlotVal) && pidSlotVal is int pidSlot)
            pkm.RefreshAbility(pidSlot);

        // CatchRate (PK1 únicamente — botón "Corregir automáticamente" de
        // G1CatchRateItem/G1CatchRateEvo en el tab Diagnóstico). El valor ya viene
        // calculado desde PokemonEditorViewModel.ApplyAutoFix (PokemonService.
        // TryRecalculateCatchRate), acá solo se escribe.
        if (edits.TryGetValue("CatchRate", out var catchRateVal) && catchRateVal is byte catchRateByte
            && pkm is PK1 pk1CatchRate)
        {
            pk1CatchRate.CatchRate = catchRateByte;
        }

        // Brillante — a diferencia de todos los demás checkbox/campos de acá, IsShiny NO
        // tiene setter propio en PKHeX.Core (get-only: "TSV == PSV"), así que no alcanza con
        // una asignación directa. Reusa el mismo sorteo de PID que ya usa el botón "Corregir
        // automáticamente" del tab Diagnóstico (PokemonService.TryRegeneratePid), pero acá el
        // estado deseado es el que el usuario tildó/destildó, no el que el PKM ya tenía.
        // Si el sorteo no encuentra combinación en el tope de intentos (caso estadísticamente
        // muy raro, ej. shiny + naturaleza puntual en Gen3-5), se ignora en silencio — mismo
        // criterio que TryRegeneratePid, no hay ningún estado parcial que limpiar.
        if (edits.TryGetValue("IsShiny", out var shinyVal) && shinyVal is bool wantShiny)
        {
            if (_pokemonService.TrySetShiny(pkm, pkm.Generation, wantShiny, out var newShinyPid))
                pkm.PID = newShinyPid;
        }

        // Huevo — pkm.IsEgg SÍ es un campo real y settable (confirmado en PKM.cs:
        // "public abstract bool IsEgg { get; set; }"). Distinto de HatchedFromEgg (más abajo,
        // junto a Ubicación/Fecha de huevo), que es un concepto propio de la UI sin campo
        // directo en el PKM.
        if (edits.TryGetValue("IsEgg", out var eggVal) && eggVal is bool isEggBool)
            pkm.IsEgg = isEggBool;

        if (edits.TryGetValue("Friendship", out var friendVal) && friendVal is int friendInt)
            pkm.CurrentFriendship = (byte)friendInt;

        // OT/TID del PROPIO Pokémon — distintos de "TID" del entrenador (ese vive en su
        // propio diccionario de edits, bajo PokemonSlotKey.ForTrainer(), ver ApplyTrainer más
        // abajo). Acá "OT"/"TID" son los que se ven en el panel Origin del editor de Pokémon,
        // separados de quién es el entrenador actual del save.
        if (edits.TryGetValue("OT", out var otVal) && otVal is string otStr)
            pkm.OriginalTrainerName = otStr;

        if (edits.TryGetValue("TID", out var tidVal) && tidVal is int tidInt)
            pkm.TID16 = (ushort)tidInt;

        // Género del Pokémon — comparte nombre de clave ("Gender") con el género del
        // entrenador, pero cada uno vive en su propio diccionario de edits (slot del Pokémon
        // vs PokemonSlotKey.ForTrainer()), así que no hay colisión real. "—" cubre especies
        // sin género (ver AvailableGenders en PokemonEditorViewModel.LoadPokemon).
        if (edits.TryGetValue("Gender", out var genderVal) && genderVal is string genderStr)
            pkm.Gender = genderStr switch { "♂" => 0, "♀" => 1, _ => 2 };

        if (edits.TryGetValue("MetDate", out var metDateVal) && metDateVal is string metDateStr
            && DateOnly.TryParseExact(metDateStr, "yyyy-MM-dd", out var metDateParsed))
        {
            pkm.MetDate = metDateParsed;
        }

        if (edits.TryGetValue("Level", out var lvl) && lvl is int lvlInt)
            pkm.CurrentLevel = (byte)lvlInt;

        // Post-4.6 — mismo bug que Shiny/Egg/Friendship/OT/TeraType (ver tabla de flags
        // rotos de la sesión): editable en la UI, el número cambia en pantalla, pero nunca
        // se aplicaba de verdad acá. Confirmado con un caso real (Drifloon/Drifblim,
        // Platinum) — el usuario corrigió Met level de 22 a 15 (el valor correcto en
        // Platinum, distinto del 22 de Diamond/Pearl) y la ilegalidad de "Origen del
        // encuentro" seguía ahí porque el cambio nunca llegaba a pkm.MetLevel.
        if (edits.TryGetValue("MetLevel", out var metLvl) && metLvl is int metLvlInt)
            pkm.MetLevel = (byte)metLvlInt;

        // Gen2 Cristal: hora del día de captura/eclosión (1 Mañana, 2 Día, 3 Noche). Antes el valor solo se mostraba y
        // nunca llegaba al PK2; PKHeX exige 1–3 en un huevo eclosionado (MetDetailTimeOfDay).
        if (pkm is PK2 { HasOriginalMetLocation: true } pk2Tod
            && edits.TryGetValue("TimeOfDay", out var todVal) && todVal is string todStr)
        {
            var tod = todStr switch { "Morning" => 1, "Day" => 2, "Night" => 3, _ => 0 };
            if (tod != 0) pk2Tod.MetTimeOfDay = tod;
        }

        if (edits.TryGetValue("Nature", out var natVal) && natVal is string natStr
            && Enum.TryParse<Nature>(natStr, out var natureEnum))
        {
            pkm.Nature = natureEnum;
        }
        else if (edits.TryGetValue("SelectedNature", out var natVal2) && natVal2 is string natStr2
            && Enum.TryParse<Nature>(natStr2, out var natureEnum2))
        {
            pkm.Nature = natureEnum2;
        }

        // IVs/EVs — sin clamp especial acá para Gen1/2 (DVs 0-15, EVs = Stat Experience
        // 0-65.535): la UI ya no debería dejar pasar valores fuera de rango (ver fix de
        // PokemonStatsView.axaml.cs), y escribir el valor tal cual llega es más simple que
        // reimplementar el mismo clamp acá. Si en algún momento se detecta un valor viejo
        // fuera de rango colado desde antes del fix, PKHeX.Core lo trunca/ignora en vez de
        // tirar (confirmado con IV_HP en Gen1 — ver WritePipelineRoundTripTests).
        void ApplyIv(string k, Action<int> setter) { if (edits.TryGetValue(k, out var v) && v is int i) setter(i); }
        ApplyIv("IvHp",  v => pkm.IV_HP  = v);
        ApplyIv("IvAtk", v => pkm.IV_ATK = v);
        ApplyIv("IvDef", v => pkm.IV_DEF = v);
        ApplyIv("IvSpa", v => pkm.IV_SPA = v);
        ApplyIv("IvSpd", v => pkm.IV_SPD = v);
        ApplyIv("IvSpe", v => pkm.IV_SPE = v);
        ApplyIv("EvHp",  v => pkm.EV_HP  = v);
        ApplyIv("EvAtk", v => pkm.EV_ATK = v);
        ApplyIv("EvDef", v => pkm.EV_DEF = v);
        ApplyIv("EvSpa", v => pkm.EV_SPA = v);
        ApplyIv("EvSpd", v => pkm.EV_SPD = v);
        ApplyIv("EvSpe", v => pkm.EV_SPE = v);

        // Mismo criterio que el preview de EditSessionService.AnalyzeLegality: si cambió el
        // movimiento o sus PP Ups, el PP actual del slot se recalcula al máximo del movimiento
        // nuevo (HealPPIndex), después de setear ambos. Sin esto el slot conservaba el PP del
        // movimiento anterior y quedaba ilegal ("PP por encima del máximo") al exportar.
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
            if (touched) pkm.HealPPIndex(index);
        }
        ApplyMove(0, "Move1", "Move1Id", "Move1PPUps", v => pkm.Move1 = v, v => pkm.Move1_PPUps = v);
        ApplyMove(1, "Move2", "Move2Id", "Move2PPUps", v => pkm.Move2 = v, v => pkm.Move2_PPUps = v);
        ApplyMove(2, "Move3", "Move3Id", "Move3PPUps", v => pkm.Move3 = v, v => pkm.Move3_PPUps = v);
        ApplyMove(3, "Move4", "Move4Id", "Move4PPUps", v => pkm.Move4 = v, v => pkm.Move4_PPUps = v);

        if (edits.TryGetValue("Ability", out var abilityVal) && abilityVal is string abilityName)
        {
            var entry = AbilityDatabase.Instance.GetByName(abilityName);
            if (entry != null) pkm.Ability = entry.Id;
        }
        // Por ID (el VM lo registra junto al nombre y no depende del idioma de la UI): gana sobre el nombre.
        if (edits.TryGetValue("AbilityId", out var abilityIdVal) && abilityIdVal is int abilityId && abilityId > 0)
            pkm.Ability = abilityId;

        if (edits.TryGetValue("HeldItem", out var itemVal) && itemVal is string itemName)
        {
            var entry = ItemDatabase.Instance.GetByName(itemName);
            if (entry != null) pkm.HeldItem = entry.Id;
        }
        // Por ID: gana sobre el nombre. 0 = sin objeto (antes quitar el objeto no se exportaba: el nombre vacío
        // no resolvía en la DB y no se escribía nada). También lo fijan Mega Stones / Z-Crystals del tab Special.
        if (edits.TryGetValue("HeldItemId", out var heldIdVal) && heldIdVal is int heldId && heldId >= 0)
            pkm.HeldItem = heldId;

        // Ball/Ubicación/Huevo — hasta ahora estos tres combos eran editables en la UI y se
        // trackeaban en la sesión (Set<T> siempre llama RecordEdit), pero nunca se escribían
        // de verdad acá: la edición se perdía en silencio al exportar. BallName se resuelve
        // igual que HeldItem (nombre → ItemDatabase → id numérico, mismo espacio de ítems que
        // usa pkm.Ball). MetLocation/EggLocation llegan como ComboItem (misma lista que arma
        // PokemonEditorViewModel vía GameInfo.GetLocationList) — su .Value YA es el ID crudo
        // que espera el PKM, sin necesidad de ningún lookup extra.
        if (edits.TryGetValue("BallName", out var ballVal) && ballVal is string ballName)
        {
            var entry = ItemDatabase.Instance.GetByName(ballName);
            if (entry != null) pkm.Ball = (byte)entry.Id;
        }
        if (edits.TryGetValue("BallId", out var ballIdVal) && ballIdVal is int ballId && ballId > 0)
            pkm.Ball = (byte)ballId;

        if (edits.TryGetValue("SelectedMetLocation", out var metLocVal) && metLocVal is ComboItem metLocItem)
            pkm.MetLocation = (ushort)metLocItem.Value;

        // Huevo: fecha de captura, "From egg" (false explícito = "ya no tiene origen de huevo"), ubicación y fecha de huevo.
        // Mismo código que usa la vista previa de legalidad (EditSessionService): antes cada una tenía su propio bloque y
        // no coincidían. Ver EggEditApplier para las reglas (ubicación y fecha juntas, terreno de captura, plantilla).
        EggEditApplier.Apply(pkm, edits);

        // Dynamax/Gigantamax (PK8), Alpha/Noble (PA8), Alpha (PA9 — Legends Z-A, sin Noble)
        // — pattern matching por tipo concreto, mismo criterio que el resto del proyecto (ver
        // "Tab Special" en context.md). Tera: solo el Override (ver TeraOverrideCodec).
        if (pkm is PK8 pk8)
        {
            if (edits.TryGetValue("DynamaxLevel", out var dl) && dl is int dlInt) pk8.DynamaxLevel = (byte)dlInt;
            if (edits.TryGetValue("CanGigantamax", out var cg) && cg is bool cgBool) pk8.CanGigantamax = cgBool;
        }
        // Tera Type (PK9): solo el Override es editable; el Original lo define el encuentro.
        if (pkm is PK9 pk9tera && edits.TryGetValue("SelectedTeraOverrideIndex", out var teraVal) && teraVal is int teraIdx)
            pk9tera.TeraTypeOverride = (MoveType)TeraOverrideCodec.ToCode(teraIdx);
        if (pkm is PA8 pa8)
        {
            if (edits.TryGetValue("IsAlpha", out var ia) && ia is bool iaBool) pa8.IsAlpha = iaBool;
            if (edits.TryGetValue("IsNoble", out var ino) && ino is bool inoBool) pa8.IsNoble = inoBool;
        }
        if (pkm is PA9 pa9)
        {
            // PA9 (Z-A) no tiene IsNoble — solo se aplica IsAlpha acá.
            if (edits.TryGetValue("IsAlpha", out var ia9) && ia9 is bool ia9Bool) pa9.IsAlpha = ia9Bool;
        }

        // Cintas — prefijo "Ribbon:" (ver EditSessionService/RibbonReader), vía RibbonWriter
        // (simétrico a RibbonReader, mismo mapeo de interfaces por generación).
        foreach (var (propName, value) in edits)
        {
            if (!propName.StartsWith("Ribbon:")) continue;
            if (value is not bool boolValue) continue;
            var ribbonName = propName["Ribbon:".Length..];
            if (Enum.TryParse<RibbonIndex>(ribbonName, out var idx))
                RibbonWriter.TrySet(pkm, idx, boolValue);
        }

        // Origen recreado (EncounterRecreationService): lugar/nivel/fecha de captura, bola, juego y PID.
        if (edits.TryGetValue("EncounterRecreation", out var recVal) && recVal is EncounterRecreation rec)
            rec.ApplyTo(pkm);

        // Solución de la búsqueda de semilla (IvSeedSolver): fija EC, PID, IVs y todo lo que el
        // encuentro deriva de la semilla. Va AL FINAL para ganar sobre cualquier edición manual de
        // IVs/PID anterior; el editor la descarta si el usuario vuelve a tocar IVs a mano.
        if (edits.TryGetValue("IvSeedSolution", out var seedSolVal) && seedSolVal is IvSeedSolution seedSol)
            seedSol.ApplyTo(pkm);
    }

    // ── 3: Entrenador ────────────────────────────────────────────────────────────────────────

    private void ApplyTrainer()
    {
        var edits = _editSession.GetRawEdits(PokemonSlotKey.ForTrainer());
        if (edits == null || edits.Count == 0) return;

        string? name = edits.TryGetValue("Name", out var n) && n is string nameStr ? nameStr : null;
        int? tid = edits.TryGetValue("TID", out var t) && t is int tidInt ? tidInt : null;
        int? sid = edits.TryGetValue("SID", out var s) && s is int sidInt ? sidInt : null;
        int? money = edits.TryGetValue("Money", out var m) && m is int moneyInt ? moneyInt : null;
        int? coins = edits.TryGetValue("Coins", out var c) && c is int coinsInt ? coinsInt : null;
        int? bp = edits.TryGetValue("BattlePoints", out var b) && b is int bpInt ? bpInt : null;

        // Gender se guarda como el string localizado tal cual lo muestra el ComboBox (ver
        // TrainerViewModel.Gender/IsFemale) — mismo criterio de comparación que
        // TrainerViewModel.IsFemale, para no depender de que el idioma activo sea español.
        bool? isFemale = edits.TryGetValue("Gender", out var g) && g is string genderStr
            ? !_loc.AllTranslations("Trainer_Male").Contains(genderStr) // «Masculino» en cualquier idioma
            : null;

        _trainerService.ApplyEdits(name, tid, sid, isFemale, money, coins, bp);
    }

    // ── 4: Mochila ───────────────────────────────────────────────────────────────────────────

    private void ApplyBagPouch(SaveFile save, InventoryType type, bool isAlternateStorage = false)
    {
        var key = PokemonSlotKey.ForBag(type, isAlternateStorage);
        var edits = _editSession.GetRawEdits(key);
        if (edits == null || edits.Count == 0) return;

        var inventory = save.Inventory.ToArray();

        InventoryPouch? pouch;
        if (isAlternateStorage)
        {
            // Origen PC: un solo pouch físico real sirve a TODAS las categorías de ese
            // origen (ver ItemInventoryService.GetItems) — no hay fallback a Items acá, si
            // esta generación no tiene almacén aparte directamente no debería haber podido
            // generarse esta tarjeta (BagViewModel.ShowStoreNav ya lo filtra).
            pouch = inventory.FirstOrDefault(p => p.Type is InventoryType.PCItems or InventoryType.FreeSpace);
        }
        else
        {
            // Fallback al pouch genérico Items — mismo criterio que ItemInventoryService
            // (lectura, ver sesión de categorización de Mochila / hotfix fix_database). En
            // Gen1/2/4/5/6 puede no existir un pouch REAL dedicado para esta tile de UI (ej.
            // "Bolas" en Gen1 — el juego solo tiene un pouch Items donde vive todo mezclado);
            // sin este fallback la edición se descartaría en silencio acá abajo.
            pouch = inventory.FirstOrDefault(p => p.Type == type)
                ?? inventory.FirstOrDefault(p => p.Type == InventoryType.Items);
        }
        if (pouch == null) return;

        // Array de tamaño FIJO — nunca se agrega/saca un slot, se busca el existente (para
        // actualizar cantidad o vaciarlo) o el primer slot libre (Count==0) para un ítem
        // nuevo. Confirmado con WritePipelineRoundTripTests.MutarMochila_SeReflejaInmediatamente
        // (InventoryPouch.SetPouch tira ArgumentOutOfRangeException si el largo cambia).
        var items = pouch.Items;
        foreach (var (propName, value) in edits)
        {
            if (!int.TryParse(propName, out var itemId)) continue;
            if (value is not int qty) continue;

            var slot = Array.FindIndex(items, i => i.Index == itemId);
            if (slot < 0)
            {
                if (qty <= 0) continue; // no estaba y se está "sacando" — no hay nada que hacer
                slot = Array.FindIndex(items, i => i.Count == 0);
                if (slot < 0) continue; // pouch lleno, sin slots libres — se ignora en silencio
            }

            items[slot] = qty > 0 ? new InventoryItem { Index = itemId, Count = qty } : new InventoryItem();
        }
        pouch.Items = items;
        save.Inventory = inventory;
    }
}
