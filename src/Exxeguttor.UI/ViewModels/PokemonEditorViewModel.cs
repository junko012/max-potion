using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Capabilities;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Converters;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

public class PokemonEditorViewModel : INotifyPropertyChanged
{
    private readonly LocalizationService _loc;

    /// <summary>Textos localizados para los bindings de las vistas del editor (<c>{Binding Loc[Clave]}</c>).</summary>
    public LocalizationService Loc => _loc;
    private readonly PokemonService      _pokemonService;
    private readonly EditSessionService  _editSession;
    private readonly BusyStateService    _busyState;
    private PKM? _currentPkm;

    // Máquina de estados de sesión (ver Set<T> y RestorePendingEdits): guarda todo lo editado
    // por Pokémon (clave = posición de caja/equipo) durante la sesión actual, independiente de
    // si eso ya se escribió al PKM real (todavía no — ver GAP CRÍTICO) o al archivo de guardado.
    // El storage en sí ahora vive en EditSessionService (compartido con MainWindowViewModel
    // para el modal de revisión antes de exportar), no acá.
    private PokemonSlotKey? _currentSlotKey;
    private bool _isLoading;
    // Bug real encontrado (sesión post-4.6): CapturePristine guarda a propósito el PRIMER
    // valor que ve para cada propiedad (para no pisar el original con recargas
    // posteriores) — pero ClearFields() resetea IVs/EVs/Level/TID/Friendship a 0 vía sus
    // propiedades reales ANTES de que el valor de verdad del PKM se cargue, y eso también
    // pasa durante _isLoading=true. Resultado: el "prístino" quedaba en 0 en vez del valor
    // real del save (255, etc.) — así que poner una EV en 0 a mano coincidía con ese
    // prístino falso y la edición se descartaba en vez de guardarse (bug reportado: "pongo
    // 0 en una EV y no se corrige"). Esta bandera separa las dos cosas que antes compartían
    // _isLoading: seguir bloqueando RecordEdit durante el reseteo (ya andaba bien) pero
    // ADEMÁS bloquear CapturePristine específicamente durante ese reseteo (lo que faltaba).
    private bool _isClearingFields;
    private int  _loadToken; // se incrementa en cada LoadPokemon/Close — permite descartar
                              // resultados de legalidad en background que ya quedaron viejos
                              // (el usuario seleccionó otro Pokémon antes de que terminara).

    // ── Etapa 4.6.2 — recálculo de legalidad en vivo mientras se edita ──────────────────────
    // Debounce corto: cada edición "relevante para legalidad" (ver LegalityRelevantFields)
    // cancela cualquier recálculo todavía pendiente y arranca uno nuevo. Un CancellationTokenSource
    // por edición (en vez de reusar uno solo) evita la clásica trampa de "Cancel() + reasignar":
    // acá cada Task.Delay tiene su propio token, así que cancelar el anterior nunca interfiere
    // con el que se acaba de crear. _loadToken (arriba) se sigue usando aparte para descartar
    // resultados de un Pokémon distinto — los dos mecanismos son independientes a propósito
    // (cambiar de Pokémon invalida por _loadToken; seguir editando el mismo invalida por CTS).
    private CancellationTokenSource? _legalityDebounceCts;

    // ── Campos privados ───────────────────────────────────────────────
    private bool    _isVisible;
    private bool    _isLegal;
    private string  _legalityText = string.Empty;
    private string  _legalityIssueTags = string.Empty;
    private string  _legalityTooltip = string.Empty;
    private bool    _isLegalityLoading;
    private ISet<string> _highlightedFieldIds = new HashSet<string>();
    private bool    _isLegalityPulsing;
    private Bitmap? _sprite;

    // Info básica
    private string _species = string.Empty, _nickname = string.Empty;
    private bool _isNicknamedFlag;
    private int    _level;
    private string _gender = string.Empty;
    private bool   _isShiny, _isEgg;
    private string _nature = string.Empty, _ability = string.Empty;
    private string _abilityDesc = string.Empty, _heldItem = string.Empty;
    private string _itemDesc = string.Empty, _ot = string.Empty;
    private int    _tid, _metLevel, _friendship;
    private ComboItem? _selectedMetLocation;
    private List<ComboItem> _availableMetLocations = [];
    private string _metDate = string.Empty;
    private string _teraType = string.Empty, _type1 = string.Empty, _type2 = string.Empty;
    private bool   _hasType2;

    // Campos nuevos Col1/Col2/Col3
    private string _form = string.Empty;
    private bool   _hasForm;
    private List<string> _availableForms = [];
    private string _ballName = string.Empty;
    private string _timeOfDay = string.Empty;
    private bool   _hasTimeOfDay;
    private bool   _hasPokerus;
    private bool   _pkrsActive;
    private string _experience    = string.Empty;
    private byte   _growthRate;
    private MoveSelectorViewModel? _moveSelector;                          // PKHeX EXPGrowth byte (0-5)
    private List<string> _availableItems = [];
    private List<string> _availableBalls = [];
    private List<string> _megaStoneOptions = [];
    private List<string> _zCrystalOptions = [];
    private List<NatureEntry>  _availableNatures  = [];
    private List<string>       _availableGenders  = [];
    private List<string>       _availableAbilities = [];

    private string _handlerName = string.Empty;
    private ComboItem? _selectedEggLocation;
    private List<ComboItem> _availableEggLocations = [];
    private string _eggDate = string.Empty;
    private bool   _hasEggData;
    private bool   _hatchedFromEgg;

    // Stats base
    private int  _baseHp, _baseAtk, _baseDef, _baseSpa, _baseSpd, _baseSpe, _baseTotal;
    private bool _hasBaseStats;

    // IVs
    private int    _ivHp, _ivAtk, _ivDef, _ivSpa, _ivSpd, _ivSpe;
    private string _hiddenPowerType = string.Empty;
    private bool   _showHiddenPower;

    // EVs
    private int _evHp, _evAtk, _evDef, _evSpa, _evSpd, _evSpe;

    // Stats totales calculados
    private int _totalHp, _totalAtk, _totalDef, _totalSpa, _totalSpd, _totalSpe;

    // Movimientos
    private string _move1 = string.Empty, _move2 = string.Empty,
                   _move3 = string.Empty, _move4 = string.Empty;
    private string _move1Type = string.Empty, _move2Type = string.Empty,
                   _move3Type = string.Empty, _move4Type = string.Empty;
    private string _move1Class = string.Empty, _move2Class = string.Empty,
                   _move3Class = string.Empty, _move4Class = string.Empty;
    private int?   _move1Power, _move2Power, _move3Power, _move4Power;
    private int?   _move1Accuracy, _move2Accuracy, _move3Accuracy, _move4Accuracy;
    private int    _move1BasePP, _move2BasePP, _move3BasePP, _move4BasePP;
    private int    _move1PP, _move2PP, _move3PP, _move4PP;
    private int    _move1PPUps, _move2PPUps, _move3PPUps, _move4PPUps;
    private string _move1Effect = string.Empty, _move2Effect = string.Empty,
                   _move3Effect = string.Empty, _move4Effect = string.Empty;

    // Tabs
    private bool _tabIvsEvs = true, _tabMoves = true, _tabLearnsets = true,
                 _tabRibbons, _tabMega, _tabZMoves, _tabDynamax,
                 _tabAlpha, _tabTerastal, _tabSets, _tabSpecial;

    // Ribbons
    private List<RibbonGroupViewModel> _ribbonGroups = new();
    private List<RibbonChipViewModel> _ribbonCategoryChips = new();
    private string _ribbonSummary = string.Empty;
    private string _selectedRibbonCategory = "Todas";
    private string _ribbonSearchText = string.Empty;

    // Dynamax / Gigantamax (Gen 8: Sword/Shield)
    private int  _dynamaxLevel;
    private bool _canGigantamax;

    // Alpha / Noble (Gen 8: Legends Arceus)
    private bool _isAlpha;
    private bool _isNoble;

    // Mega Evolution (Gen 6 XY/ORAS, Gen 7 SM/USUM)
    // Reversa nombre-localizado -> ItemId crudo, construida en LoadItemLists() a partir
    // del mismo GameStrings usado para poblar AvailableItems — evita el problema de
    // slugificar un nombre ya traducido (ver RecalcItemDescription, que sí lo sufre).
    private Dictionary<string, int> _itemNameToId = new();
    private Dictionary<int, string> _itemIdToName = new();
    private Dictionary<string, int> _megaOptionToItemId = new();
    private Dictionary<string, int> _zCrystalOptionToItemId = new();
    private bool   _megaCompatible;
    private string _megaFormName = string.Empty;
    private string _megaSummary  = string.Empty;
    private string _megaType1 = string.Empty;
    private string _megaType2 = string.Empty;

    // Z-Moves (Gen 7)
    private bool   _zMoveCompatible;
    private string _zMoveResultName = string.Empty;
    private string _zMoveSourceMove = string.Empty;
    private string _zMoveNote       = string.Empty;
    private string _zMoveType       = string.Empty;

    public PokemonEditorViewModel(LocalizationService loc, PokemonService pokemonService, EditSessionService editSession, BusyStateService busyState)
    {
        _loc            = loc;
        _pokemonService = pokemonService;
        _editSession    = editSession;
        _busyState      = busyState;
        LegalityIssues  = new ObservableCollection<string>();
        TypeWeaknesses  = new ObservableCollection<TypeEffectivenessEntry>();
        RecommendedSets      = new ObservableCollection<RecommendedSet>();
        RecommendedSetGroups = new ObservableCollection<RecommendedSetGroup>();
        Learnsets       = new ObservableCollection<LearnsetEntry>();
        CloseCommand    = new RelayCommand(Close);
        OpenIvSolverCommand = new RelayCommand(RequestIvSolver);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        // Captura del valor "tal cual venía" ANTES del chequeo de "no cambió" de abajo — si
        // se capturara después, un valor que ya coincide con el default (ej. IsShiny=false,
        // el caso más común) nunca se registraría como prístino, y comparar más tarde contra
        // "no hay prístino" rompería el diff cuando el usuario vuelve a ese mismo valor (ver
        // EditSessionService.CapturePristine — corrige el bug de "Shiny/Huevo/Pokérus quedan
        // pegados al destildar").
        if (_isLoading && !_isClearingFields && _currentSlotKey is { } loadKey && name != null)
            _editSession.CapturePristine(loadKey, name, value);

        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);

        // Máquina de estados de sesión: mientras NO estemos en medio de una carga (_isLoading),
        // cualquier cambio real de una propiedad se reporta al EditSessionService compartido
        // (también usado por MainWindowViewModel para el modal de revisión antes de exportar).
        // Así, si el usuario navega a otro Pokémon y vuelve, la edición se restaura en vez de
        // perderse (LoadPokemon siempre relee un PKM "fresco" del SaveFile).
        if (name is "EvHp" or "EvAtk" or "EvDef" or "EvSpa" or "EvSpd" or "EvSpe" or "IvHp" or "IvAtk")
        {
            // DIAGNÓSTICO TEMPORAL — el punto más temprano posible de toda la cadena: acá
            // vemos el estado EXACTO de _isLoading/_currentSlotKey en el instante mismo en
            // que se intenta registrar la edición, antes de cualquier otra lógica.
            AppendDebugLine($"Set<T> name={name} value={value} _isLoading={_isLoading} _currentSlotKey={(_currentSlotKey.HasValue ? _currentSlotKey.ToString() : "null")} @ {DateTime.Now:HH:mm:ss.fff}");
        }

        if (!_isLoading && _currentSlotKey is { } key && name != null)
        {
            _editSession.RecordEdit(key, name, value);
            if (name is "EvHp" or "EvAtk" or "EvDef" or "EvSpa" or "EvSpd" or "EvSpe" or "IvHp" or "IvAtk")
                AppendDebugLine(EditSessionService.LastRecordEditDebug);

            // Etapa 4.6.2: recálculo de legalidad en vivo — solo para los campos que
            // EditSessionService.AnalyzeLegality efectivamente sabe aplicar a un preview
            // (ver LegalityRelevantFields). Cualquier otro campo (Ribbons, Special, etc.)
            // sigue quedando afuera de la correlación de legalidad, igual que antes.
            // La solución de la búsqueda de semilla fija IVs/Naturaleza/Habilidad/Género/PID a la vez:
            // si el usuario vuelve a tocar cualquiera de ellos a mano, la solución ya no corresponde
            // y se descarta (si no, al exportar pisaría su edición). Aplicar una solución actualiza
            // los campos con _isLoading = true, así que NO pasa por acá.
            if (name is "IvHp" or "IvAtk" or "IvDef" or "IvSpa" or "IvSpd" or "IvSpe"
                    or "Nature" or "SelectedNature" or "Ability" or "Gender" or "PID")
                _editSession.RemoveEdit(key, "IvSeedSolution");

            // Lo mismo con el origen recreado: si el usuario toca a mano algo que ella fija, ya no corresponde.
            if (name is "SelectedMetLocation" or "MetLevel" or "MetDate" or "BallName" or "Nature" or "SelectedNature"
                    or "Gender" or "Ability" or "PID" or "SelectedEggLocation" or "EggDate"
                || (name == "HatchedFromEgg" && !(_currentPkm != null && EggOriginService.StoresEggOnlyAsMetLevel(_currentPkm))))
                // En Gen3 "HatchedFromEgg" no es un dato propio sino un atajo sobre Met level/ubicación/bola (cada uno
                // descarta la corrección si realmente cambia). Descartarla por el check solo perdía el PID nuevo: el
                // panel seguía "legal" y la exportación, que analiza desde cero, lo veía ilegal.
                _editSession.RemoveEdit(key, "EncounterRecreation");
            if (name is "IvHp" or "IvAtk" or "IvDef" or "IvSpa" or "IvSpd" or "IvSpe"
                && _editSession.GetRawEdits(key) is { } pendingEdits
                && pendingEdits.TryGetValue("EncounterRecreation", out var recEdit)
                && recEdit is EncounterRecreation { CopyIvs: true })
                _editSession.RemoveEdit(key, "EncounterRecreation");

            if (LegalityRelevantFields.Contains(name))
                ScheduleLegalityRecalc(key);
        }
    }

    /// <summary>Mismo set de nombres de propiedad que EditSessionService.AnalyzeLegality sabe
    /// mapear a un campo real del PKM — si ese método aprende un campo nuevo, agregarlo acá
    /// también, o el recálculo en vivo no va a reaccionar a esa edición.</summary>
    private static readonly HashSet<string> LegalityRelevantFields = new(StringComparer.Ordinal)
    {
        "Nickname", "Level", "MetLevel", "Nature", "SelectedNature", "IsNicknamedFlag",
        "IvHp", "IvAtk", "IvDef", "IvSpa", "IvSpd", "IvSpe",
        "EvHp", "EvAtk", "EvDef", "EvSpa", "EvSpd", "EvSpe",
        "Move1", "Move2", "Move3", "Move4",
        "Move1Id", "Move2Id", "Move3Id", "Move4Id",
        "Move1PPUps", "Move2PPUps", "Move3PPUps", "Move4PPUps",
        "Ability", "AbilityId", "HeldItem", "HeldItemId", "BallName", "BallId", "SelectedMetLocation", "SelectedEggLocation", "PID",
        "PIDAbilitySlot", "IvSeedSolution", "EncounterRecreation", "CatchRate", "Gender", "Form", "HatchedFromEgg",
        // Tab Special: AnalyzeLegality los aplica al preview (ver "Mecánicas del tab Special"). Sin estos
        // nombres el badge/diagnóstico en vivo no reaccionaba y la ilegalidad recién aparecía en el
        // modal de exportación, que analiza el Pokémon desde cero.
        "IsAlpha", "IsNoble", "DynamaxLevel", "CanGigantamax", "SelectedTeraOverrideIndex",
    };

    /// <summary>Contador de generación para el recálculo en vivo — mismo patrón que
    /// <c>_loadToken</c> más arriba, aplicado acá porque el CancellationTokenSource por sí
    /// solo no alcanza: cancelar el token de una corrida vieja NO aborta un Task.Run que ya
    /// arrancó (Task.Run con token solo evita arrancar si ya estaba cancelado ANTES de
    /// empezar, nunca interrumpe uno en curso). Si varias ediciones seguidas disparan varios
    /// Task.Run casi en simultáneo, el orden en que TERMINAN no es necesariamente el orden en
    /// que arrancaron — una corrida vieja más lenta puede terminar después de una más nueva y
    /// pisar su resultado con datos desactualizados (bug real encontrado: el total de EVs
    /// mostrado quedaba pegado en un valor intermedio, ni el original ni el final, después de
    /// varias ediciones seguidas). Comparar contra este contador justo antes de tocar el
    /// ViewModel es una garantía mucho más dura que confiar en el token: no importa en qué
    /// orden terminen las tareas, solo la del número más alto (la más nueva) puede aplicar
    /// su resultado.</summary>
    private int _legalityRecalcGeneration;

    /// <summary>Cancela cualquier recálculo todavía en vuelo (para no malgastar CPU en un
    /// Task.Delay que ya no importa) y arranca uno nuevo con su propia generación.</summary>
    /// <summary>DIAGNÓSTICO TEMPORAL — sacar apenas encontremos el problema real. Se
    /// actualiza en 3 puntos de RunLegalityRecalcAsync (programado / calculado / aplicado)
    /// para ver exactamente dónde se corta la cadena, ya que varias rondas de hipótesis por
    /// lectura de código no lograron explicar por qué el recálculo en vivo no parece correr
    /// para las ediciones de EV. Bindeado temporalmente en PokemonStatsView.axaml, al lado
    /// de "Total EVs".</summary>
    public string DebugRecalcInfo { get => _debugRecalcInfo; set { _debugRecalcInfo = value; OnPropertyChanged(); } }
    private string _debugRecalcInfo = "(sin correr todavía)";

    /// <summary>DIAGNÓSTICO TEMPORAL — agrega una línea en vez de reemplazar todo el
    /// contenido (como hacía antes): cada etapa de RunLegalityRecalcAsync pisaba el mensaje
    /// de la etapa anterior, así que para cuando el usuario sacaba la captura solo quedaba
    /// la última línea ("APLICADO"), perdiendo justo la info intermedia que necesitábamos
    /// ver. Guarda las últimas 4 líneas nomás para que no crezca sin límite.</summary>
    private void AppendDebugLine(string line)
    {
        var lines = _debugRecalcInfo == "(sin correr todavía)"
            ? new List<string>()
            : _debugRecalcInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        lines.Add(line);
        if (lines.Count > 12) lines = lines.Skip(lines.Count - 12).ToList();
        DebugRecalcInfo = string.Join("\n", lines);
    }

    private void ScheduleLegalityRecalc(PokemonSlotKey key)
    {
        _legalityDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _legalityDebounceCts = cts;
        var generation = ++_legalityRecalcGeneration;
        AppendDebugLine($"programado #{generation} @ {DateTime.Now:HH:mm:ss.fff}");
        _ = RunLegalityRecalcAsync(key, cts.Token, generation);
    }

    /// <summary>Debounce de 350ms (dentro del rango 300-500ms acordado) + recálculo en
    /// background (Task.Run, nunca bloqueando el hilo de UI — misma lección del splash
    /// screen). Deliberadamente SIN el Task.Delay(1500) artificial de LoadLegalityAsync: ese
    /// mínimo tiene sentido en la carga inicial (evita parpadeo), pero acá rompería la
    /// sensación de "instantáneo" que pidió el usuario para la edición en vivo.</summary>
    private async Task RunLegalityRecalcAsync(PokemonSlotKey key, CancellationToken token, int generation)
    {
        try { await Task.Delay(350, token); }
        catch (OperationCanceledException) { return; }

        if (token.IsCancellationRequested) return;
        if (_currentSlotKey != key || generation != _legalityRecalcGeneration) return; // el usuario ya navegó a otro Pokémon/cerró, o hay una corrida más nueva en camino

        // Sin ediciones pendientes (el usuario deshizo todo y volvió al estado original) también hay que
        // recalcular: antes se salía acá y el diagnóstico quedaba con el resultado de la edición anterior
        // (ej. quitar un objeto que había vuelto ilegal al Pokémon no limpiaba el aviso).
        var edits = _editSession.GetRawEdits(key) ?? new Dictionary<string, object?>();
        var editsCountMsg = $"edits.Count={edits.Count} #{generation} @ {DateTime.Now:HH:mm:ss.fff}";
        {
            // DIAGNÓSTICO TEMPORAL — el conteo solo no alcanzaba para ver si el diccionario
            // realmente tiene el valor de cada EV puntual (y no, por ejemplo, cayendo de
            // vuelta al valor original del save porque esa clave nunca se registró).
            string EvDebug(string k) => edits.TryGetValue(k, out var v) ? v?.ToString() ?? "null" : "(no está)";
            editsCountMsg += $" | EvHp={EvDebug("EvHp")} EvAtk={EvDebug("EvAtk")} EvDef={EvDebug("EvDef")} EvSpa={EvDebug("EvSpa")} EvSpd={EvDebug("EvSpd")} EvSpe={EvDebug("EvSpe")}";
        }
        AppendDebugLine(editsCountMsg);
        LegalitySummary summary;
        List<DiagnosticStep> steps;
        try
        {
            // Dos llamadas separadas (cada una reconstruye su propio preview + corre
            // LegalityAnalysis por dentro) en vez de una sola combinada — redundante en CPU
            // (dos análisis en vez de uno), pero mantiene AnalyzeLegality/
            // BuildLiveDiagnosticSteps totalmente independientes uno del otro, y esto corre
            // debounced en background nunca más de una vez cada 350ms de inactividad, así
            // que el costo real es insignificante. Si en el futuro esto se vuelve un cuello
            // de botella real, unificar en un solo método que devuelva las dos cosas.
            (summary, steps) = await Task.Run(() =>
            {
                var s = EditSessionService.AnalyzeLegality(key, edits, _ => _currentPkm);
                var d = EditSessionService.BuildLiveDiagnosticSteps(key, edits, _ => _currentPkm);
                return (s, d);
            }, token);
        }
        catch (OperationCanceledException) { return; }
        catch { return; } // mismo criterio que AnalyzeLegality: ante cualquier falla, no
                           // rompemos la UI ni dejamos un estado a medio actualizar.

        // ¿Los IVs editados rompen la semilla del encuentro y se puede buscar una combinación legal?
        // (IvSeedSolver.TryCreatePlan — solo Gen8/9 con semilla). Fuera de la UI, mismo criterio
        // de falla silenciosa que el análisis de arriba.
        IvSeedPlan? ivPlan = null;
        try { ivPlan = await Task.Run(() => ComputeIvSeedPlan(key, edits), token); }
        catch (OperationCanceledException) { return; }
        catch { ivPlan = null; }

        if (token.IsCancellationRequested) return;
        if (_currentSlotKey != key || generation != _legalityRecalcGeneration) return; // chequeo doble: pudo cambiar mientras corría el análisis, o quedó obsoleto por una corrida más nueva (Task.Run no se puede abortar a mitad de camino, así que ESTE chequeo -por número de generación, no por el token- es la garantía real acá)

        SetIvSolverPlan(ivPlan);

        // DIAGNÓSTICO TEMPORAL — buscamos puntualmente el FieldFix de EV_TOTAL (si existe)
        // dentro de los steps recién calculados, para comparar el total que USÓ el preview
        // del backend contra el que se ve en pantalla (EvTotalDisplay, que lee directo de
        // las propiedades del ViewModel, no del diccionario de edits).
        var evTotalFix = steps.SelectMany(s => s.Fixes).FirstOrDefault(f => f.FieldId == "EV_TOTAL");
        var evTotalInfo = evTotalFix != null
            ? $"EV_TOTAL_interno(tenías={evTotalFix.CurrentValueDisplay})"
            : "EV_TOTAL_interno=(sin fix)";
        AppendDebugLine($"calculado #{generation}: IsLegal={summary.IsLegal}, steps={steps.Count}, {evTotalInfo} @ {DateTime.Now:HH:mm:ss.fff}");

        // Etapa 4.6.4-fix(3) — antes esto solo pulsaba cuando el Pokémon entero pasaba de
        // ilegal a legal (wasIllegal && summary.IsLegal). Un Pokémon con VARIOS problemas a
        // la vez (ej. Habilidad + Datos internos + IVs/EVs, como el Probopass reportado)
        // nunca cumplía esa condición al arreglar solo uno: la ficha de esa categoría
        // desaparecía igual (se recalcula del análisis nuevo), pero sin pulso, porque el
        // Pokémon seguía ilegal por los otros dos. Ahora comparamos qué categorías Invalid
        // había ANTES contra las que quedan DESPUÉS — si al menos una desapareció, pulsa,
        // sin importar si quedan otras pendientes. Nunca pulsa si el conjunto solo creció o
        // se mantuvo igual (evita pulsar por una edición que empeoró o no cambió nada).
        var categoriesBefore = new HashSet<string>(LegalityChips.Select(c => c.Category), StringComparer.Ordinal);
        var categoriesAfter  = new HashSet<string>(
            steps.Where(s => s.Severity == Severity.Invalid).Select(s => s.Category), StringComparer.Ordinal);
        var somethingResolved = categoriesBefore.Except(categoriesAfter).Any();

        // Etapa 4.6.4-fix — el pulso tiene que verse SOBRE el estado viejo (fichas todavía
        // puestas + badge todavía en su color viejo) antes de limpiar nada. Antes se llamaba
        // ApplyDiagnosticSteps (que vacía LegalityChips) ANTES de disparar el pulso — para
        // cuando la animación arrancaba, el contenedor de fichas ya estaba vacío, así que no
        // había nada que pulsar (de ahí el bug original reportado: "desaparece pero no
        // flashea"). Ahora el pulso corre primero, sobre el contenido que está por cambiar,
        // y recién después se aplica el estado nuevo.
        if (somethingResolved)
        {
            IsLegalityPulsing = true;
            await Task.Delay(450, CancellationToken.None); // sin token: el pulso visual debe
                                                             // terminar aunque el usuario ya
                                                             // haya arrancado otra edición

            // Etapa 4.6.4-fix(5) — condición de carrera real encontrada acá: si durante esta
            // espera de 450ms el usuario siguió editando (ej. varias EVs seguidas), cada
            // edición nueva ya canceló `token` (mismo CTS que este método recibió, vía
            // ScheduleLegalityRecalc) — pero como el Task.Delay de arriba usa
            // CancellationToken.None a propósito, esa cancelación no cortaba el pulso, y el
            // código de abajo seguía aplicando el `summary`/`steps` VIEJOS (calculados antes
            // de la espera) sin volver a chequear nada, pisando el resultado de la edición
            // más nueva si esta terminaba antes. Efecto observado: el diagnosticador se
            // quedaba pegado en un estado intermedio (ej. un EV_TOTAL que no era ni el
            // original ni el final). El pulso en sí completa igual (efecto cosmético, no
            // importa que termine "de más"), pero acá SÍ hay que volver a chequear antes
            // de tocar el estado real.
            if (token.IsCancellationRequested) return;
            if (_currentSlotKey != key || generation != _legalityRecalcGeneration) return;
        }

        IsLegal           = summary.IsLegal;
        LegalityText      = summary.IsLegal ? _loc["Pokemon_Legality_Legal"] : _loc["Pokemon_Legality_Illegal"];
        LegalityIssueTags = summary.CategoryTags;
        LegalityTooltip   = BuildLegalityTooltip(summary);
        LegalityIssues.Clear();
        foreach (var line in summary.TechnicalDetail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(10))
            LegalityIssues.Add(line);

        ApplyDiagnosticSteps(steps);
        AppendDebugLine($"APLICADO #{generation}: IsLegal={summary.IsLegal}, chips={LegalityChips.Count}, flagged={HighlightedFieldIds.Count} @ {DateTime.Now:HH:mm:ss.fff}");

        if (somethingResolved)
            IsLegalityPulsing = false;
    }

    /// <summary>Etapa 4.6.3/4.6.4 — reparte un List&lt;DiagnosticStep&gt; recién calculado
    /// (sea de la carga inicial o de un recálculo en vivo) entre las tres propiedades que lo
    /// consumen: fichas (solo Invalid), lista completa (para el futuro tab Diagnóstico) y el
    /// set de FieldId resaltados. Centralizado acá para que LoadLegalityAsync y
    /// RunLegalityRecalcAsync no dupliquen esta lógica de reparto.</summary>
    private void ApplyDiagnosticSteps(List<DiagnosticStep> steps)
    {
        DiagnosticSteps.Clear();
        _specialAdvisoryStep = null; // Clear() ya lo sacó de la lista
        foreach (var step in steps)
            DiagnosticSteps.Add(step);
        RefreshSpecialAdvisories();

        LegalityChips.Clear();
        var flagged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (step.Severity != Severity.Invalid) continue;
            LegalityChips.Add(step);
            foreach (var fix in step.Fixes)
                flagged.Add(fix.FieldId);
        }
        HighlightedFieldIds = flagged;

        // Post-4.6 — alerta en el header de la tab correspondiente, para que el usuario se
        // entere de que hay un problema sin tener que estar parado en la tab "Diagnóstico".
        // Basado en la CATEGORÍA (no en FieldId puntual) porque una tab entera puede tener
        // el problema aunque el campo específico no tenga un FieldFix con nombre propio
        // (ej. "Movimientos" cuando el motivo es Duplicate/Unobtainable sin un reemplazo
        // puntual — la ficha igual existe, el resaltado de campo puede que no).
        var invalidCategories = new HashSet<string>(
            LegalityChips.Select(c => c.Category), StringComparer.Ordinal);
        HasIvsEvsIssue   = invalidCategories.Contains("IVs/EVs");
        HasMovesIssue    = invalidCategories.Contains("Movimientos");
        HasRibbonsIssue  = invalidCategories.Contains("Cintas");
        // El tab Diagnóstico muestra la alerta siempre que exista CUALQUIER ilegalidad: es adonde hay que
        // ir para ver el detalle o aplicar una corrección.
        HasDiagnosticIssue = LegalityChips.Count > 0;

        // "Recrear con un encuentro válido" / nivel de Gen1: se calcula aparte, en segundo plano.
        _ = RefreshRecreateOfferAsync();
    }

    private bool _hasIvsEvsIssue, _hasMovesIssue, _hasRibbonsIssue;
    public bool HasIvsEvsIssue  { get => _hasIvsEvsIssue;  set { _hasIvsEvsIssue = value; OnPropertyChanged(); } }
    private bool _hasDiagnosticIssue;
    public bool HasDiagnosticIssue { get => _hasDiagnosticIssue; set { _hasDiagnosticIssue = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiagnosticTabOk)); } }
    /// <summary>Todo en orden: la pestaña Legal se marca en verde (rojo cuando HasDiagnosticIssue).</summary>
    public bool DiagnosticTabOk => IsLegal && !HasDiagnosticIssue && !IsLegalityLoading;

    // ── Búsqueda de semilla para IVs (IvSeedSolver) ──────────────────────────────────────────
    private IvSeedPlan? _ivSolverPlan;
    private bool _ivSolverAvailable;

    /// <summary>True cuando los IVs editados rompen la semilla del encuentro y existe una búsqueda
    /// que puede resolverlo. Lo usa el aviso del tab IVs/EVs (solo se muestra si hay alerta) y se
    /// apaga solo cuando el Pokémon vuelve a ser legal.</summary>
    public bool IvSolverAvailable
    {
        get => _ivSolverAvailable;
        private set { if (_ivSolverAvailable == value) return; _ivSolverAvailable = value; OnPropertyChanged(); }
    }

    /// <summary>Lo escucha MainWindowViewModel para abrir el modal de búsqueda.</summary>
    public event Action<IvSeedPlan>? IvSolverRequested;

    public ICommand OpenIvSolverCommand { get; }

    // Textos del aviso del tab IVs/EVs (ver PokemonStatsView.axaml)
    public string IvSolverBannerText => _loc["IvSolver_Banner_Text"];
    public string IvSolverBannerButton => _loc["IvSolver_Banner_Button"];

    private void SetIvSolverPlan(IvSeedPlan? plan)
    {
        _ivSolverPlan = plan;
        IvSolverAvailable = plan != null;
    }

    private void RequestIvSolver()
    {
        if (_ivSolverPlan is { } plan) IvSolverRequested?.Invoke(plan);
    }

    /// <summary>Corre en segundo plano: el Pokémon con las ediciones aplicadas + el original (de ahí
    /// sale el encuentro) → plan de búsqueda, o null si no corresponde.</summary>
    private IvSeedPlan? ComputeIvSeedPlan(PokemonSlotKey key, IReadOnlyDictionary<string, object?> edits)
    {
        var pristine = _currentPkm;
        if (pristine == null) return null;
        if (!EditSessionService.TryBuildEditedPkm(key, edits, _ => _currentPkm, out var edited)) return null;
        return IvSeedSolver.TryCreatePlan(pristine, edited, out var plan) ? plan : null;
    }

    /// <summary>El usuario eligió una opción en el modal: se registra como UNA edición compuesta
    /// (se reaplica igual en el preview y al exportar) y se refleja en pantalla lo que la semilla
    /// fijó. Los campos se actualizan con _isLoading = true para que no se registren como
    /// ediciones sueltas ni descarten la solución (ver Set).</summary>
    public void ApplyIvSeedOption(IvSeedPlan plan, IvSeedOption option)
    {
        if (_currentPkm == null || _currentSlotKey is not { } key) return;

        var solution = new IvSeedSolution(plan, option.Seed);
        var solved = plan.Edited.Clone();
        if (!solution.ApplyTo(solved)) return;

        _editSession.RecordEdit(key, "IvSeedSolution", solution);

        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            IvHp = solved.IV_HP; IvAtk = solved.IV_ATK; IvDef = solved.IV_DEF;
            IvSpa = solved.IV_SPA; IvSpd = solved.IV_SPD; IvSpe = solved.IV_SPE;

            if (Capabilities?.HasNatures == true && solved.Nature > 0 && NatureDatabase.ById((int)solved.Nature) is { } solvedNature)
                Nature = solvedNature.Name;
            if (Capabilities?.HasAbilities == true && solved.Ability > 0 && solved.Ability < strings.Ability.Count
                && AvailableAbilities.Contains(strings.Ability[solved.Ability]))
                Ability = strings.Ability[solved.Ability];
            if (Capabilities?.HasGender == true)
                Gender = solved.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };
        }
        finally { _isLoading = wasLoading; }

        ScheduleLegalityRecalc(key);
    }
    public bool HasMovesIssue   { get => _hasMovesIssue;   set { _hasMovesIssue = value; OnPropertyChanged(); } }
    public bool HasRibbonsIssue { get => _hasRibbonsIssue; set { _hasRibbonsIssue = value; OnPropertyChanged(); } }

    /// <summary>Post-4.6 — botón "Corregir automáticamente" del tab Diagnóstico. Solo
    /// conoce por ahora FieldFixAction.RegeneratePid; cualquier otra acción futura se suma
    /// acá mismo, un case nuevo por vez, igual que se fueron sumando los casos de
    /// LegalityArgumentFormatter. Nunca toca el PKM real directo — graba "PID" como un
    /// edit más (mismo pipeline que cualquier otro campo), así que sigue apareciendo en la
    /// libreta de revisión antes de exportar y el recálculo en vivo lo refleja solo.</summary>
    /// <summary>La ventana principal lo asigna para mostrar el modal de feedback (Rotom PC) mientras
    /// corre una corrección automática: recibe el título ya localizado y el trabajo (devuelve true si
    /// se aplicó). Si nadie lo asigna, la corrección corre igual pero sin modal.</summary>
    public Func<string, Func<Task<bool>>, Task>? AutoFixFeedback { get; set; }

    /// <summary>Como <see cref="AutoFixFeedback"/> pero para búsquedas que terminan abriendo otro modal
    /// (se cierra solo al terminar bien, sin "Corrección aplicada").</summary>
    public Func<string, Func<Task<bool>>, string?, Task>? AutoFixQuiet { get; set; }

    public void ApplyAutoFix(FieldFix fix) => _ = ApplyAutoFixAsync(fix);

    private async Task ApplyAutoFixAsync(FieldFix fix)
    {
        if (_currentPkm == null || Capabilities == null || _currentSlotKey is not { } key) return;

        // Atajo "Ir a Moves" (no corrige nada): abre el selector del primer movimiento ilegal.
        if (fix.Action == FieldFixAction.GoToMoves)
        {
            if (fix.SuggestedValueRaw is int slot) OpenMoveSelector(slot);
            return;
        }

        // Correcciones que se confirman en el modal de "qué cambia" antes de aplicarse.
        if (fix.Action is FieldFixAction.RecreateEncounter or FieldFixAction.RaiseLevel
            || (fix.Action == FieldFixAction.RecalculateCatchRate && await TryRequestCatchRateModalAsync()))
        {
            if (fix.Action != FieldFixAction.RecalculateCatchRate) RequestRecreateModal(fix.Action);
            return;
        }

        // No calcula acá: abre el modal de búsqueda de semilla (ver IvSolverViewModel). Sin plan, no hace nada.
        if (fix.Action == FieldFixAction.SearchIvSeed)
        {
            RequestIvSolver();
            return;
        }

        // Los fixes se calculan sobre el Pokémon CON las ediciones pendientes aplicadas (IVs,
        // naturaleza, género, etc. recién tocados), no sobre el original del save: si no, el PID
        // calculado corresponde a los valores viejos y el mensaje de ilegalidad sigue igual
        // (bug reportado: "le doy a Corregir automáticamente y continúa el mensaje").
        var current = _currentPkm;
        var rawEdits = _editSession.GetRawEdits(key);
        if (rawEdits is { Count: > 0 }
            && EditSessionService.TryBuildEditedPkm(key, rawEdits, _ => _currentPkm, out var edited))
        {
            current = edited;
        }

        var generation = Capabilities.Generation;

        // Method 1: si NINGÚN PID calza con los IVs actuales (esa combinación no puede existir en el encuentro),
        // regenerar el PID no sirve: se buscan IVs parecidos que sí tengan un PID válido y se confirman en el modal.
        // Huevo de Gen3: el PID NO puede seguir el patrón de Method 1 (PKHeX espera PIDType.None), así que ni el
        // solver de Method 1 ni la búsqueda de IVs sirven — se genera un PID sin patrón (EggOriginService).
        var eggOrigin = false;
        if (fix.Action == FieldFixAction.RegeneratePidMethod1)
        {
            var (isEgg, hasEncounterError) = await Task.Run(() =>
            {
                var la = new LegalityAnalysis(current);
                return (EggOriginService.IsSupported(current) && EggOriginService.IsEggEncounter(la),
                        EncounterRecreationService.HasEncounterError(la));
            });
            eggOrigin = isEgg;

            if (!eggOrigin)
            {
                // Con error de encuentro el PID no es el problema: buscar IVs no puede resolverlo (y recorría
                // millones de semillas sin fin). Se avisa en vez de lanzar la búsqueda.
                if (hasEncounterError)
                {
                    var blocked = _loc["Recreate_Iv_NoneFound"];
                    if (AutoFixQuiet is { } q) await q(_loc["AutoFix_Working_Pid"], () => Task.FromResult(false), blocked);
                    return;
                }

                var solvable = await Task.Run(() => Method1Solver.TrySolve(current, out _));
                if (!solvable)
                {
                    if (Equals(_currentSlotKey, key)) await OpenIvSearchAsync(current, key);
                    return;
                }
            }
        }

        var title = fix.Action switch
        {
            FieldFixAction.RegeneratePid or FieldFixAction.RegeneratePidMethod1 => _loc["AutoFix_Working_Pid"],
            FieldFixAction.RecalculateCatchRate => _loc["AutoFix_Working_CatchRate"],
            _ => _loc["AutoFix_Working_Generic"],
        };

        // El cálculo pesado va en segundo plano (el sorteo de PID puede tardar y antes congelaba la
        // ventana); lo que toca la sesión de edición vuelve al hilo de UI. Devuelve true si se aplicó.
        async Task<bool> Work()
        {
            switch (fix.Action)
            {
                case FieldFixAction.RegeneratePid:
                {
                    uint pid = 0;
                    var ok = await Task.Run(() =>
                    {
                        var r = _pokemonService.TryRegeneratePid(current, generation, out var p);
                        pid = p;
                        return r;
                    });
                    // Si devuelve false (combinación estadísticamente casi imposible de lograr por sorteo)
                    // no hay ningún dato parcial que limpiar: el modal ofrece Reintentar (nueva tanda).
                    if (!ok || !Equals(_currentSlotKey, key)) return false;
                    _editSession.RecordEdit(key, "PID", pid);
                    ScheduleLegalityRecalc(key);
                    return true;
                }

                case FieldFixAction.RegeneratePidMethod1 when eggOrigin:
                {
                    uint eggPid = 0;
                    var ok = await Task.Run(() =>
                    {
                        var r = EggOriginService.TrySolvePid(current, out var p);
                        eggPid = p;
                        return r;
                    });
                    if (!ok || !Equals(_currentSlotKey, key)) return false;
                    // Naturaleza/Género/Habilidad/Brillante se conservan (el PID se busca con ellos), no hay
                    // nada más que registrar que el PID.
                    _editSession.RecordEdit(key, "PID", eggPid);
                    _editSession.RecordEdit(key, "PIDAbilitySlot", null);
                    ScheduleLegalityRecalc(key);
                    return true;
                }

                case FieldFixAction.RegeneratePidMethod1:
                {
                    // Method1Solver mantiene los IVs actuales, elige el PID entre los pocos candidatos del
                    // LCRNG y lo VERIFICA contra LegalityAnalysis antes de aceptarlo. Si ninguno cierra, esa
                    // combinación exacta de IVs no puede existir en este encuentro.
                    var (found, solved) = await Task.Run(() => (Method1Solver.TrySolve(current, out var r), r));
                    if (!found || !Equals(_currentSlotKey, key)) return false;

                    _editSession.RecordEdit(key, "PID", solved.Pid);
                    // La Naturaleza se deriva del PID en Gen3/4 (no hay nada que registrar). La habilidad y
                    // —solo en Gen4, donde es un campo guardado— el género sí. Se registran siempre (o se
                    // limpian) para no dejar restos de un intento anterior.
                    _editSession.RecordEdit(key, "PIDAbilitySlot",
                        solved.AbilityChanged ? (int)(solved.Pid & 1) : null);
                    if (solved.GenderChanged)
                    {
                        var g = EntityGender.GetFromPID(current.Species, solved.Pid);
                        _editSession.RecordEdit(key, "Gender", g switch { 0 => "♂", 1 => "♀", _ => "-" });
                    }
                    ScheduleLegalityRecalc(key);
                    return true;
                }

                case FieldFixAction.RecalculateCatchRate:
                {
                    // Solo devuelve false si el PKM no es un PK1 (no debería pasar: este FieldFix únicamente
                    // lo arman los códigos G1CatchRate*, exclusivos de Gen1).
                    if (!_pokemonService.TryRecalculateCatchRate(current, out var newCatchRate)) return false;
                    // Si el valor sugerido es el que ya tiene, esta corrección no cambia nada: se informa
                    // como "no se pudo" en vez de fingir que se aplicó.
                    if (current is PK1 pk1 && pk1.CatchRate == newCatchRate) return false;
                    _editSession.RecordEdit(key, "CatchRate", newCatchRate);
                    ScheduleLegalityRecalc(key);
                    return true;
                }
            }
            return false;
        }

        var feedback = AutoFixFeedback;
        if (feedback != null) await feedback(title, Work);
        else await Work();
    }

    // ── Recrear con un encuentro válido (mockups/encounter_recreate/) ───────────────────────
    private const string EncounterCategory = "Origen del encuentro";
    private int _offerGeneration;
    private RecreateAnalysis? _recreateAnalysis;
    private (int Level, uint Exp)? _raiseLevel;
    private PKM? _recreateSource;

    /// <summary>Lo escucha MainWindowViewModel para abrir el modal de confirmación.</summary>
    public event Action<RecreateRequest>? RecreateRequested;

    private void ClearRecreateOffer()
    {
        _offerGeneration++;
        _recreateAnalysis = null;
        _raiseLevel = null;
    }

    /// <summary>Calcula (en segundo plano) si hay algo que ofrecer en la fila "Origen del encuentro" y, si
    /// lo hay, reemplaza ese paso por una copia con el botón. Sin error de encuentro no hace nada.</summary>
    private async Task RefreshRecreateOfferAsync()
    {
        var gen = ++_offerGeneration;
        _recreateAnalysis = null;
        _raiseLevel = null;

        var step = DiagnosticSteps.FirstOrDefault(s => s.Category == EncounterCategory);
        if (step == null || _currentPkm == null || Capabilities == null || _currentSlotKey is not { } key) return;
        var save = _pokemonService.CurrentSave;
        if (save == null) return;

        var pristine = _currentPkm;
        var rawEdits = _editSession.GetRawEdits(key);
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        RecreateAnalysis? analysis = null;
        (int Level, uint Exp)? raise = null;
        PKM current = pristine;
        try
        {
            (analysis, raise, current) = await Task.Run<(RecreateAnalysis?, (int Level, uint Exp)?, PKM)>(() =>
            {
                // El Pokémon CON las ediciones pendientes (mismo criterio que los demás botones de corrección).
                var cur = pristine;
                if (rawEdits is { Count: > 0 }
                    && EditSessionService.TryBuildEditedPkm(key, rawEdits, _ => pristine, out var edited))
                    cur = edited;

                if (cur is PK1 pk1)
                {
                    (int, uint)? up = EncounterRecreationService.TryRaiseLevel(pk1, out var lv, out var xp) ? (lv, xp) : null;
                    return (null, up, cur);
                }
                if (!EncounterRecreationService.IsSupported(cur)) return (null, null, cur);
                return (EncounterRecreationService.Analyze(cur, save, strings), null, cur);
            });
        }
        catch { return; }

        if (gen != _offerGeneration || _currentSlotKey != key) return;   // llegó tarde: hay un cálculo más nuevo
        _recreateSource = current;
        var idx = DiagnosticSteps.IndexOf(step);
        if (idx < 0) return;

        var extra = new List<FieldFix>();
        var species = current.Species < strings.specieslist.Length ? strings.specieslist[current.Species] : "?";

        if (raise is { } r)
        {
            _raiseLevel = r;
            extra.Add(new FieldFix
            {
                FieldId = "EncounterLevel", CurrentValueDisplay = string.Empty, SuggestedValueDisplay = string.Empty,
                SuggestedValueRaw = r.Level, Source = FieldFixSource.DerivedFromPkm, Action = FieldFixAction.RaiseLevel,
                Note = string.Format(System.Globalization.CultureInfo.CurrentCulture, _loc["Recreate_Level_Row"], species, r.Level),
                ButtonLabel = _loc["Recreate_Level_Button"],
            });
        }
        else if (analysis is { HasEncounterError: true })
        {
            _recreateAnalysis = analysis;
            if (analysis.Options.Count > 0)
            {
                var keeps = _loc[current.Format == 2 ? "Recreate_Keeps_Dvs" : "Recreate_Keeps_Ivs"];
                extra.Add(new FieldFix
                {
                    FieldId = "EncounterRecreate", CurrentValueDisplay = string.Empty, SuggestedValueDisplay = string.Empty,
                    SuggestedValueRaw = null, Source = FieldFixSource.DerivedFromPkm, Action = FieldFixAction.RecreateEncounter,
                    Note = _loc["Recreate_Row_Available"] + "\n" + string.Format(
                        System.Globalization.CultureInfo.CurrentCulture, _loc["Recreate_Row_Text"], analysis.Options.Count, keeps),
                    ButtonLabel = _loc["Recreate_Row_Button"],
                });
            }
            else
            {
                var text = string.Format(System.Globalization.CultureInfo.CurrentCulture, _loc["Recreate_NoFix_Text"], species);
                if (analysis.IllegalMoves.Count > 0) text += "\n• " + string.Join("\n• ", analysis.IllegalMoves);
                extra.Add(new FieldFix
                {
                    FieldId = "EncounterRecreate", CurrentValueDisplay = string.Empty, SuggestedValueDisplay = string.Empty,
                    SuggestedValueRaw = null, Source = FieldFixSource.DerivedFromPkm,
                    Note = _loc["Recreate_NoFix_Title"] + "\n" + text + "\n" + _loc["Recreate_NoFix_Hint"],
                });
                if (analysis.IllegalSlots.Count > 0)
                    extra.Add(new FieldFix
                    {
                        FieldId = "EncounterRecreateMoves", CurrentValueDisplay = string.Empty, SuggestedValueDisplay = string.Empty,
                        SuggestedValueRaw = analysis.IllegalSlots[0], Source = FieldFixSource.DerivedFromPkm,
                        Action = FieldFixAction.GoToMoves, Note = string.Empty, ButtonLabel = _loc["Recreate_NoFix_GoMoves"],
                    });
            }
        }
        if (extra.Count == 0) return;

        DiagnosticSteps[idx] = new DiagnosticStep
        {
            Order = step.Order, Category = step.Category, Severity = step.Severity, Summary = step.Summary,
            TechnicalDetail = step.TechnicalDetail, Fixes = [.. step.Fixes, .. extra],
        };
    }

    private async Task OpenIvSearchAsync(PKM current, PokemonSlotKey key)
    {
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        IReadOnlyList<IvSearchOption>? found = null;
        async Task<bool> Work()
        {
            found = await Task.Run(() => EncounterRecreationService.FindIvOptions(current, strings));
            return found is { Count: > 0 };
        }

        var title = _loc["Recreate_Iv_Working"];
        if (AutoFixQuiet is { } quiet) await quiet(title, Work, _loc["Recreate_Iv_NoneFound"]);
        else if (AutoFixFeedback is { } feedback) await feedback(title, Work);
        else await Work();

        if (found is not { Count: > 0 })
        {
            // Para poder diagnosticar un caso así: con qué encuentro lo emparejó PKHeX.
            try
            {
                var la = await Task.Run(() => new LegalityAnalysis(current));
                AppendDebugLine($"búsqueda de IVs sin resultado: encuentro={la.EncounterMatch.GetType().Name}, PIDType={la.Info.PIDIV.Type}, especie={current.Species}");
            }
            catch { }
        }

        if (found is { Count: > 0 } options && Equals(_currentSlotKey, key) && Capabilities != null)
        {
            var species = current.Species < strings.specieslist.Length ? strings.specieslist[current.Species] : "?";
            RecreateRequested?.Invoke(new RecreateRequest(RecreateKind.IvSearch, Capabilities.Generation, species,
                [], [], 0, 0, 0, 0, 0, null, options));
        }
    }

    /// <summary>Abre el modal de confirmación para Recrear (Gen2–4) o Subir nivel (Gen1).</summary>
    private void RequestRecreateModal(FieldFixAction action)
    {
        if (_currentPkm == null || Capabilities == null) return;
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        var species = _currentPkm.Species < strings.specieslist.Length ? strings.specieslist[_currentPkm.Species] : "?";

        if (action == FieldFixAction.RecreateEncounter && _recreateAnalysis is { Options.Count: > 0 } a)
        {
            var source = _recreateSource;
            var save = _pokemonService.CurrentSave;
            Func<bool, CancellationToken, Task<IReadOnlyList<RecreateOption>>>? reload = null;
            if (source != null && save != null)
                reload = (keep, ct) => Task.Run<IReadOnlyList<RecreateOption>>(
                    () => EncounterRecreationService.Analyze(source, save, strings, keepNature: keep, ct: ct).Options, ct);
            RecreateRequested?.Invoke(new RecreateRequest(RecreateKind.Encounter, Capabilities.Generation, species,
                a.Options, [], 0, 0, 0, 0, 0, reload));
        }
        else if (action == FieldFixAction.RaiseLevel && _raiseLevel is { } r && _currentPkm is PK1)
        {
            RecreateRequested?.Invoke(new RecreateRequest(RecreateKind.Level, 1, species, [], [], 0,
                Level, r.Level, ParseExp(Experience), r.Exp));
        }
    }

    private static uint ParseExp(string text) =>
        uint.TryParse(new string(text.Where(char.IsDigit).ToArray()), out var v) ? v : 0;

    /// <summary>Gen1: en vez de aplicar el catch rate de una, abre el modal con el valor propuesto (0) y los
    /// otros valores válidos. Devuelve true si abrió el modal (false → se sigue con el flujo directo).</summary>
    private async Task<bool> TryRequestCatchRateModalAsync()
    {
        if (_currentPkm == null || Capabilities == null || _currentSlotKey is not { } key) return false;
        var save = _pokemonService.CurrentSave;
        if (save == null) return false;

        var current = _currentPkm;
        var rawEdits = _editSession.GetRawEdits(key);
        if (rawEdits is { Count: > 0 }
            && EditSessionService.TryBuildEditedPkm(key, rawEdits, _ => _currentPkm, out var edited))
            current = edited;
        if (current is not PK1 pk1) return false;

        var values = await Task.Run(() => CatchRateSolver.GetValidCandidates(pk1, save));
        values = values.Where(v => v != pk1.CatchRate).ToList();
        if (values.Count == 0 || !Equals(_currentSlotKey, key)) return false;

        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        var species = current.Species < strings.specieslist.Length ? strings.specieslist[current.Species] : "?";
        RecreateRequested?.Invoke(new RecreateRequest(RecreateKind.CatchRate, 1, species, [], values,
            pk1.CatchRate, 0, 0, 0, 0));
        return true;
    }

    /// <summary>El usuario confirmó en el modal: se aplica con el feedback de "Corregir automáticamente".</summary>
    public async Task ApplyRecreateChoiceAsync(RecreateChoice choice)
    {
        if (_currentPkm == null || _currentSlotKey is not { } key) return;

        var title = _loc[choice.Kind switch
        {
            RecreateKind.Encounter => "Recreate_Working",
            RecreateKind.CatchRate => "AutoFix_Working_CatchRate",
            RecreateKind.IvSearch => "AutoFix_Working_Pid",
            _ => "Recreate_Working_Level",
        }];

        Task<bool> Work()
        {
            if (!Equals(_currentSlotKey, key)) return Task.FromResult(false);
            switch (choice.Kind)
            {
                case RecreateKind.Encounter when choice.Option is { } o:
                    _editSession.RecordEdit(key, "EncounterRecreation", new EncounterRecreation(o.Result, o.IvsChanged, o.Groups));
                    ShowRecreatedOrigin(o.Result, o.IvsChanged);
                    ScheduleLegalityRecalc(key);
                    return Task.FromResult(true);
                case RecreateKind.IvSearch when choice.Candidate is { } c:
                {
                    // Los IVs van como ediciones normales (libreta de revisión); el PID, como en el botón de Method 1.
                    IvHp = c.Ivs[0]; IvAtk = c.Ivs[1]; IvDef = c.Ivs[2];
                    IvSpa = c.Ivs[3]; IvSpd = c.Ivs[4]; IvSpe = c.Ivs[5];
                    _editSession.RecordEdit(key, "PID", c.Pid);
                    _editSession.RecordEdit(key, "PIDAbilitySlot", null);
                    if (Capabilities?.Generation == 4 && Capabilities.HasNatures)
                    {
                        var nat = (int)(c.Pid % 25);
                        if (NatureDatabase.ById(nat) is { } pidNature) Nature = pidNature.Name;
                    }
                    ScheduleLegalityRecalc(key);
                    return Task.FromResult(true);
                }
                case RecreateKind.CatchRate:
                    _editSession.RecordEdit(key, "CatchRate", (byte)choice.Value);
                    ScheduleLegalityRecalc(key);
                    return Task.FromResult(true);
                case RecreateKind.Level:
                    Level = choice.Value;   // registra la edición y recalcula la experiencia
                    return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        var feedback = AutoFixFeedback;
        if (feedback != null) await feedback(title, Work);
        else await Work();
    }

    /// <summary>Refleja en los campos del editor el origen recién recreado. Con _isLoading = true para que
    /// no se registren como ediciones sueltas ni descarten la edición compuesta (ver Set).</summary>
    private void ShowRecreatedOrigin(PKM r, bool ivsChanged = false)
    {
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            if (Capabilities?.HasMetLocation == true)
            {
                var item = AvailableMetLocations.FirstOrDefault(l => l.Value == r.MetLocation);
                if (item == null)
                {
                    try
                    {
                        var all = GameInfo.GetLocationList(r.Version == 0 ? _currentPkm!.Version : r.Version, r.Context, egg: false);
                        item = all.FirstOrDefault(l => l.Value == r.MetLocation);
                        if (item != null) AvailableMetLocations = [.. AvailableMetLocations, item];
                    }
                    catch { }
                }
                if (item != null) SelectedMetLocation = item;
            }
            MetLevel = r.MetLevel;
            if (EggOriginService.StoresEggOnlyAsMetLevel(r)) HatchedFromEgg = EggOriginService.IsHatchedMeta(r, r.MetLevel);   // Gen3/Cristal: el huevo se reconoce por el Met level
            if (ivsChanged)
            {
                IvHp = r.IV_HP; IvAtk = r.IV_ATK; IvDef = r.IV_DEF;
                IvSpa = r.IV_SPA; IvSpd = r.IV_SPD; IvSpe = r.IV_SPE;
            }
            if (Capabilities?.HasMetDate == true && r.MetDate.HasValue) MetDate = r.MetDate.Value.ToString("yyyy-MM-dd");
            if (Capabilities?.HasBall == true && r.Ball > 0 && r.Ball < strings.Item.Count)
            {
                BallName = strings.Item[r.Ball];
                BallId = r.Ball;
            }
            if (Capabilities?.HasNatures == true && NatureDatabase.ById((int)r.Nature) is { } recreatedNature)
                Nature = recreatedNature.Name;
            if (Capabilities?.HasAbilities == true && r.Ability > 0 && r.Ability < strings.Ability.Count
                && AvailableAbilities.Contains(strings.Ability[r.Ability]))
                Ability = strings.Ability[r.Ability];
            if (Capabilities?.HasGender == true)
                Gender = r.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };
            if (Capabilities?.HasShiny == true && r.Format >= 5) IsShiny = r.IsShiny;   // Gen5+: PID/EC pueden venir del encuentro

            if (r.EggLocation != 0 && Capabilities?.HasEggLocation == true)
            {
                HatchedFromEgg = true;
                SelectedEggLocation = AvailableEggLocations.FirstOrDefault(l => l.Value == r.EggLocation);
                if (Capabilities?.HasEggDate == true && r.EggMetDate.HasValue) EggDate = r.EggMetDate.Value.ToString("yyyy-MM-dd");
            }
            if (Capabilities?.HasMetTimeOfDay == true && r is PK2 p2)
            {
                TimeOfDay = p2.MetTimeOfDay switch { 1 => "Morning", 2 => "Day", 3 => "Night", _ => string.Empty };
                HasTimeOfDay = !string.IsNullOrEmpty(TimeOfDay);
            }
        }
        finally { _isLoading = wasLoading; }
    }

    // ── Capacidades ───────────────────────────────────────────────────
    public SaveCapabilities? Capabilities { get; private set; }

    // ── Visibilidad de campos ─────────────────────────────────────────
    public bool ShowNatures    => Capabilities?.HasNatures    ?? false;
    public bool ShowAbilities  => Capabilities?.HasAbilities  ?? false;
    public bool ShowHeldItems  => Capabilities?.HasHeldItems  ?? false;
    public bool ShowShiny      => Capabilities?.HasShiny      ?? false;
    public bool ShowGender     => Capabilities?.HasGender     ?? false;
    public bool ShowMetLocation => SelectedMetLocation != null;
    public bool ShowMetLevel   => Capabilities?.HasMetLevel    ?? false;
    public bool ShowMetDate    => Capabilities?.HasMetDate      ?? false;
    public bool ShowFriendship => (Capabilities?.Generation    ?? 0) >= 2;

    // ── Propiedades públicas ──────────────────────────────────────────
    public bool    IsVisible    { get => _isVisible;    set => Set(ref _isVisible,    value); }
    public bool    IsLegal      { get => _isLegal;      set { Set(ref _isLegal, value); OnPropertyChanged(nameof(DiagnosticTabOk)); } }
    public string  LegalityText { get => _legalityText; set => Set(ref _legalityText, value); }

    /// <summary>Resumen rápido de una línea cuando es ilegal (ej. "Movimientos, Nivel") —
    /// categorías afectadas, sin jerga técnica. Vacío si es legal. Ver LegalityTooltip para
    /// el detalle completo (mensaje amigable por categoría + detalle técnico de PKHeX).</summary>
    public string LegalityIssueTags { get => _legalityIssueTags; set => Set(ref _legalityIssueTags, value); }

    /// <summary>Tooltip del badge de legalidad: mensaje amigable por categoría afectada,
    /// seguido del detalle técnico crudo de PKHeX.Core (para casos raros que el mensaje
    /// amigable no explique bien). Vacío si es legal.</summary>
    public string LegalityTooltip { get => _legalityTooltip; set => Set(ref _legalityTooltip, value); }

    /// <summary>True mientras GetLegalitySummary corre en background (ver LoadLegalityAsync)
    /// — el resto del Pokémon ya se ve normal, solo el badge de legalidad muestra un
    /// indicador de carga hasta que esto termina.</summary>
    public bool IsLegalityLoading { get => _isLegalityLoading; set { Set(ref _isLegalityLoading, value); OnPropertyChanged(nameof(DiagnosticTabOk)); } }

    /// <summary>Etapa 4.6.3 — fichas de categoría bajo el sprite. Solo Severity.Invalid
    /// (bloquean la legalidad) — Fishy nunca aparece acá, solo en DiagnosticSteps completo
    /// (para el tab Diagnóstico de la Etapa 4.6.6, todavía no construido). Se reemplaza
    /// entera (Clear+AddRange) en cada recálculo, tanto en la carga inicial
    /// (LoadLegalityAsync) como en vivo (RunLegalityRecalcAsync) — así las fichas ya
    /// aparecen al abrir un Pokémon ilegal, sin esperar a la primera edición.</summary>
    public ObservableCollection<DiagnosticStep> LegalityChips { get; } = new();

    /// <summary>Etapa 4.6.3 — el set completo (Invalid + Fishy), para cuando se construya el
    /// tab Diagnóstico (Etapa 4.6.6). Expuesto desde ahora para no tener que volver a tocar
    /// el recálculo en ese momento.</summary>
    public ObservableCollection<DiagnosticStep> DiagnosticSteps { get; } = new();

    /// <summary>Etapa 4.6.4 — FieldId (mismo formato que FieldFix.FieldId: "IV_HP",
    /// "MetLocation", etc.) de todos los Fixes bajo un step Invalid, para que
    /// FieldHighlightConverter resuelva si un control puntual debe resaltarse. Se reemplaza
    /// la instancia entera en cada recálculo (nunca se muta en el lugar) porque el binding
    /// solo reacciona a que la propiedad cambie, no al contenido interno de un HashSet.</summary>
    // Deliberadamente FUERA de Set<T>: esta propiedad no es una "edición" del usuario (no
    // tiene sentido que EditSessionService.RecordEdit la trackeara, como pasaba antes al
    // pasar por Set<T> sin querer) y no queremos ningún chequeo de igualdad de por medio —
    // se reemplaza la instancia entera en cada recálculo, así que la notificación tiene que
    // ser incondicional siempre. Descartando esto como posible causa de que el resaltado
    // se quede pegado en el estado de la carga inicial sin actualizarse en vivo.
    public ISet<string> HighlightedFieldIds
    {
        get => _highlightedFieldIds;
        set { _highlightedFieldIds = value; OnPropertyChanged(); }
    }

    /// <summary>Etapa 4.6.3 — true por 450ms cuando el badge pasa de ilegal a legal (mismo
    /// patrón que MainWindowViewModel.IsNotebookPulsing/CheckpointAsync). No dispara solo
    /// para "OnPropertyChanged", a propósito no pasa por Set&lt;T&gt; — esto no es una
    /// edición del Pokémon, grabarla en EditSessionService no tendría sentido.</summary>
    public bool IsLegalityPulsing { get => _isLegalityPulsing; set { _isLegalityPulsing = value; OnPropertyChanged(); } }

    public Bitmap? Sprite       { get => _sprite;       set { Set(ref _sprite, value); OnPropertyChanged(nameof(HasSprite)); } }
    public bool    HasSprite    => _sprite != null;

    public string Species     { get => _species;     set => Set(ref _species,     value); }
    public string Nickname
    {
        get => _nickname;
        set
        {
            Set(ref _nickname, value);
            // Conveniencia: escribir texto en el Nickname lo marca "tiene apodo"
            // automáticamente (mismo comportamiento que la pantalla de renombrar del juego
            // real). El usuario puede destildar IsNicknamedFlag a mano después si hace
            // falta — antes esto se forzaba sin dar ninguna forma de verlo ni revertirlo.
            if (!_isLoading) IsNicknamedFlag = true;
        }
    }

    /// <summary>Etapa post-4.6 — flag pkm.IsNicknamed expuesto por primera vez en la UI (antes
    /// solo se leía para decidir qué mostrar en la lista de caja/equipo, y se forzaba a true
    /// en cada edición del Nickname sin dar forma de verlo ni destildarlo — causa real,
    /// invisible, de "apodo ilegal con texto normal": el flag no coincidía con el texto). Al
    /// tipear en el TextBox de Nickname (ver setter de arriba llamado desde el code-behind/
    /// binding, no acá) se sigue marcando esto en true automáticamente como conveniencia, pero
    /// ahora queda visible y destildable a mano.</summary>
    public bool IsNicknamedFlag { get => _isNicknamedFlag; set => Set(ref _isNicknamedFlag, value); }
    public int    Level
    {
        get => _level;
        set
        {
            var clamped = Math.Clamp(value, 1, 100);
            if (_level == clamped) return;
            Set(ref _level, clamped);
            RecalcExperience();
            RecalcStats();
        }
    }
    public string Gender
    {
        get => _gender;
        set
        {
            Set(ref _gender, value);
            // El sprite depende del género (formas hembra distintas en varias especies) —
            // refrescarlo al toque, igual que ya hace Nature con los stats recalculados.
            if (_currentPkm != null)
                Sprite = SpriteService.Instance.GetPokemonSprite(_currentPkm.Species, IsShiny, value == "♀", _currentPkm.Form);
        }
    }

    /// <summary>Opciones de género para el ComboBox — 1 sola opción si la especie es de
    /// género fijo o sin género, 2 (♂/♀) si puede ser cualquiera. Ver LoadPokemon.</summary>
    public List<string> AvailableGenders { get => _availableGenders; set => Set(ref _availableGenders, value); }
    public bool IsShiny
    {
        get => _isShiny;
        set
        {
            Set(ref _isShiny, value);
            // Mismo bug que tenía Gender antes de arreglarlo: el sprite se calculaba una
            // sola vez en LoadPokemon a partir de pkm.IsShiny, sin engancharse a esta
            // propiedad — tildar/destildar Shiny no se veía reflejado en el sprite.
            if (_currentPkm != null)
                Sprite = SpriteService.Instance.GetPokemonSprite(_currentPkm.Species, value, Gender == "♀", _currentPkm.Form);
        }
    }
    public string Nature
    {
        get => _nature;
        set
        {
            var changed = _nature != value;
            Set(ref _nature, value);
            if (!changed) return; // nada más que sincronizar si en los hechos no cambió
            // Mantener _selectedNature sincronizado sin reentrar al setter de SelectedNature
            var match = AvailableNatures.FirstOrDefault(n =>
                string.Equals(n.Name, value, StringComparison.OrdinalIgnoreCase));
            if (!ReferenceEquals(_selectedNature, match))
            {
                _selectedNature = match;
                OnPropertyChanged(nameof(SelectedNature));
            }
            // La naturaleza afecta los multiplicadores de stat (+10%/-10%)
            RecalcStats();
        }
    }

    private NatureEntry? _selectedNature;
    /// <summary>Entry completo seleccionado en el ComboBox (incluye +stat/-stat).</summary>
    public NatureEntry? SelectedNature
    {
        get => _selectedNature;
        set
        {
            var changed = !ReferenceEquals(_selectedNature, value);
            Set(ref _selectedNature, value);
            if (changed) Nature = value?.Name ?? string.Empty;
        }
    }
    public string Ability
    {
        get => _ability;
        set
        {
            var changed = _ability != value;
            Set(ref _ability, value);
            AbilityId = ResolveAbilityId(value);
            if (changed) RecalcAbilityDescription();
        }
    }
    public string AbilityDescription { get => _abilityDesc; set => Set(ref _abilityDesc, value); }
    public string HeldItem
    {
        get => _heldItem;
        set
        {
             // El ComboBox del tab Info empuja null cuando el objeto equipado no está en su lista...
            if (value is null) return;
            var changed = _heldItem != value;
            Set(ref _heldItem, value);
            
            //var changed = _heldItem != value;
            //Set(ref _heldItem, value);
            // Id del objeto: salvo que lo esté fijando un selector del tab Special (que pone el suyo).
            if (!_equippingFromSpecial) HeldItemId = ResolveItemId(value);
            if (!changed) return;
            RecalcItemDescription();
            RecalcMega();
            RecalcZMove();
            // Un cambio que NO viene de los selectores del tab Special (p. ej. el combo del tab Info)
            // invalida el aviso "Reemplazó: …" de la tarjeta.
            if (!_equippingFromSpecial) { _megaSwapNote = string.Empty; _zSwapNote = string.Empty; }
            RecalcEquippedSpecialItem();
        }
    }
    // ── Ids de lo que el usuario edita en los combos (independientes del idioma de la UI) ──────────
    // HeldItem, Ability y BallName guardan el NOMBRE de PKHeX en el idioma de la UI (strings.Item /
    // strings.Ability), pero pokemon.db solo tiene nombres en inglés: resolver ese nombre contra la DB al
    // exportar daba null con la UI en otro idioma y la edición se perdía en silencio. Estos ids viajan junto
    // al nombre como edición propia (Set<T> la registra sola) y EditApplyService / EditSessionService
    // escriben por id; el nombre queda para mostrar y para el resumen de ediciones.
    // Salen del mismo índice de strings con el que se arman los combos; al cargar se toman directo del PKM.
    private int _heldItemId;
    private int _abilityId;
    private int _ballId;
    public int HeldItemId { get => _heldItemId; private set => Set(ref _heldItemId, value); }
    public int AbilityId  { get => _abilityId;  private set => Set(ref _abilityId,  value); }
    public int BallId     { get => _ballId;     private set => Set(ref _ballId,     value); }
    private Dictionary<string, int> _abilityNameToId = new(StringComparer.Ordinal);
    private Dictionary<string, int> _moveNameToId    = new(StringComparer.Ordinal);

    private int ResolveItemId(string? name) =>
        !string.IsNullOrEmpty(name) && _itemNameToId.TryGetValue(name, out var id) ? id : 0;
    private int ResolveAbilityId(string? name) =>
        !string.IsNullOrEmpty(name) && _abilityNameToId.TryGetValue(name, out var id) ? id : 0;

    /// <summary>Nombre → índice de una lista de strings de PKHeX (índice = id). Con nombres repetidos gana el primero
    /// (<paramref name="firstWins"/>) o el último.</summary>
    private static Dictionary<string, int> BuildNameIndex(IReadOnlyList<string> names, bool firstWins)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 1; i < names.Count; i++)
        {
            var n = names[i];
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (firstWins) d.TryAdd(n, i); else d[n] = i;
        }
        return d;
    }
    public string ItemDescription    { get => _itemDesc;    set => Set(ref _itemDesc,    value); }

    /// <summary>Nota para el tooltip del combo de Held Item: si el juego tiene Mega/Z-Moves,
    /// aclara que esos ítems se eligen desde el tab Special (ya no aparecen en esta lista).</summary>
    public string HeldItemHint =>
        (TabMega, TabZMoves) switch
        {
            (true, true)  => _loc["Hint_Item_Both"],
            (true, false) => _loc["Hint_Item_Mega"],
            (false, true) => _loc["Hint_Item_Z"],
            _ => string.Empty,
        };
    public string OT          { get => _ot;          set => Set(ref _ot,          value); }
    /// <summary>ID de entrenador del Pokémon: 16 bits (0-65535). Al guardar se castea a ushort, así que un valor
    /// mayor daría la vuelta en silencio; por eso se topa acá.</summary>
    public int    TID         { get => _tid;         set => Set(ref _tid, Math.Clamp(value, 0, ushort.MaxValue)); }
    public string TidDisplay  
    { 
        get => _tid.ToString();
        set
        {
            var digits = new string((value ?? string.Empty).Where(c => c >= '0' && c <= '9').ToArray());
            if (digits.Length > 0) TID = digits.Length > 5 ? ushort.MaxValue : Math.Min(int.Parse(digits), ushort.MaxValue);
            OnPropertyChanged(nameof(TidDisplay)); // reescribe el texto aunque el valor no cambie (ej. se pasó del tope)
        }
    }
    /// <summary>Ubicaciones válidas para este juego puntual — sacadas directo de PKHeX.Core
    /// (GameInfo.GetLocationList), no de una fuente propia. Ver LoadPokemon.</summary>
    public List<ComboItem> AvailableMetLocations { get => _availableMetLocations; set => Set(ref _availableMetLocations, value); }
    // Mientras RefineMetLocationsAsync reemplaza la lista del ComboBox, este deja de aceptar el
    // "write-back" de selección que el control puede empujar al cambiar de ItemsSource — sin esto
    // un null transitorio quedaría registrado como edición del Pokémon.
    private bool _refiningMetLocations;
    public ComboItem? SelectedMetLocation
    {
        get => _selectedMetLocation;
        set { if (_refiningMetLocations) return; Set(ref _selectedMetLocation, value); }
    }
    public int    MetLevel    { get => _metLevel;    set { Set(ref _metLevel, value); SyncGen3HatchedFlag(); } }
    public string MetLevelDisplay
    {
        get => _metLevel.ToString();
        set { if (int.TryParse(value, out var v)) MetLevel = v; }
    }
    public string MetDate     { get => _metDate;     set => Set(ref _metDate,     value); }
    public int    Friendship  { get => _friendship;  set => Set(ref _friendship,  value); }
    public bool IsEgg
    {
        get => _isEgg;
        set
        {
            var changed = _isEgg != value;
            Set(ref _isEgg, value);
            if (!changed) return;
            OnPropertyChanged(nameof(EnabledHatchedFromEgg));
            // Si sigue siendo huevo sin eclosionar, "nació de huevo" es forzosamente
            // cierto — el checkbox además queda deshabilitado (EnabledHatchedFromEgg).
            if (value) HatchedFromEgg = true;
        }
    }
    public string TeraType    { get => _teraType;    set => Set(ref _teraType,    value); }
    public string Type1       { get => _type1;       set => Set(ref _type1,       value); }
    public string Type2       { get => _type2;       set => Set(ref _type2,       value); }
    public bool   HasType2    { get => _hasType2;    set => Set(ref _hasType2,    value); }

    // Col 1 — Identity
    public string Form
    {
        get => _form;
        set
        {
            var changed = _form != value;
            Set(ref _form, value);
            // Elegir otra forma en el combo (Giratina Altered -> Origen) cambia sprite, tipos,
            // stats base y habilidades: se refleja ya, sin esperar a exportar.
            if (changed && !_isLoading) RefreshFormDerivedData();
        }
    }
    public bool   HasForm    { get => _hasForm;     set => Set(ref _hasForm,    value); }
    public List<string> AvailableForms { get => _availableForms; set => Set(ref _availableForms, value); }

    // Col 2 — Status
    public string Experience  { get => _experience; }
    public string HandlerName { get => _handlerName;  set => Set(ref _handlerName, value); }
    public bool   HasPokerus  { get => _hasPokerus;   set => Set(ref _hasPokerus,  value); }
    public bool   PkrsActive  { get => _pkrsActive;   set => Set(ref _pkrsActive,  value); }

    // Col 3 — Origin
    public string BallName
    {
        get => _ballName;
        set { Set(ref _ballName, value); BallId = ResolveItemId(value); }
    }
    public string TimeOfDay   { get => _timeOfDay;   set => Set(ref _timeOfDay,  value); }

    // Listas para ComboBox
    public List<string>    AvailableItems { get => _availableItems; set => Set(ref _availableItems, value); }
    public List<string>    AvailableBalls { get => _availableBalls; set => Set(ref _availableBalls, value); }

    /// <summary>Mega Stones compatibles con la especie actual (0, 1 o 2 — ej. Charizard/Mewtwo). Vacía = sin opciones.</summary>
    public List<string> MegaStoneOptions
    {
        get => _megaStoneOptions;
        set { Set(ref _megaStoneOptions, value); OnPropertyChanged(nameof(HasMegaStoneOptions)); OnPropertyChanged(nameof(MegaOtherItemNote)); OnPropertyChanged(nameof(MegaStatusText)); }
    }
    public bool HasMegaStoneOptions => MegaStoneOptions.Count > 0;
    /// <summary>Equipa la Mega Stone elegida en el selector rápido de la tarjeta Mega. Lo invoca el
    /// code-behind (SelectionChanged) y después vacía el ComboBox: antes el selector era un SelectedItem
    /// bindeado cuyo setter se "reseteaba" a vacío a sí mismo, y Avalonia no re-empuja un valor que el
    /// propio setter cambia durante el binding — el combo quedaba mostrando el item elegido y volver a
    /// elegirlo no disparaba nada.</summary>
    public void EquipMegaOption(string option)
    {
        if (string.IsNullOrEmpty(option)) return;
        // 'option' es el nombre limpio (de pokemon.db) que se mostró en el picker; lo resolvemos
        // al ItemId real y de ahí al string de PKHeX que espera el resto del pipeline (HeldItem).
        if (!_megaOptionToItemId.TryGetValue(option, out var itemId) || !_itemIdToName.TryGetValue(itemId, out var pkhexName)) return;
        var previous = _heldItem;
        _equippingFromSpecial = true;
        try { HeldItem = pkhexName; HeldItemId = itemId; } finally { _equippingFromSpecial = false; }
        _megaSwapNote = string.IsNullOrEmpty(previous) || previous == pkhexName ? string.Empty : string.Format(_loc["Special_Replaced"], previous);
        _zSwapNote = string.Empty;
        RecalcEquippedSpecialItem();
    }

    /// <summary>Z-Crystals relevantes para el Pokémon actual (compatibles con sus movimientos o su especie).</summary>
    public List<string> ZCrystalOptions
    {
        get => _zCrystalOptions;
        set { Set(ref _zCrystalOptions, value); OnPropertyChanged(nameof(HasZCrystalOptions)); OnPropertyChanged(nameof(ZOtherItemNote)); }
    }
    public bool HasZCrystalOptions => ZCrystalOptions.Count > 0;
    /// <summary>Equipa el Z-Crystal elegido en el selector rápido de la tarjeta Z-Moves (ver EquipMegaOption).</summary>
    public void EquipZCrystalOption(string option)
    {
        if (string.IsNullOrEmpty(option)) return;
        // El texto del picker puede traer un sufijo " (requiere …)" en los cristales exclusivos:
        // _zCrystalOptionToItemId está indexado por ese mismo texto.
        if (!_zCrystalOptionToItemId.TryGetValue(option, out var itemId) || !_itemIdToName.TryGetValue(itemId, out var pkhexName)) return;
        var previous = _heldItem;
        _equippingFromSpecial = true;
        try { HeldItem = pkhexName; HeldItemId = itemId; } finally { _equippingFromSpecial = false; }
        _zSwapNote = string.IsNullOrEmpty(previous) || previous == pkhexName ? string.Empty : string.Format(_loc["Special_Replaced"], previous);
        _megaSwapNote = string.Empty;
        RecalcEquippedSpecialItem();
    }
    // ── Objeto equipado desde el tab Special ──────────────────────────
    // Mega Stones y Z-Crystals NO están en el combo de Held Item del tab Info (ver LoadItemLists):
    // este tab es su único punto de entrada, así que también muestra cuál hay equipado y permite
    // quitarlo. Son derivados de UI: campos directos (sin Set<T>), no se registran como edición.
    private bool _equippingFromSpecial;
    private HashSet<int> _megaItemIds = [];
    private HashSet<int> _zItemIds = [];
    private string _equippedMegaItemName = string.Empty;
    private string _equippedZItemName = string.Empty;
    private string _megaSwapNote = string.Empty;
    private string _zSwapNote = string.Empty;

    public string EquippedMegaItemName => _equippedMegaItemName;
    public string EquippedZItemName    => _equippedZItemName;
    public bool   HasEquippedMegaItem  => _equippedMegaItemName.Length > 0;
    public bool   HasEquippedZItem     => _equippedZItemName.Length > 0;
    /// <summary>"Reemplazó: Leftovers" — solo justo después de equipar desde la tarjeta Mega.</summary>
    public string MegaSwapNote => _megaSwapNote;
    /// <summary>Ídem para la tarjeta Z-Moves.</summary>
    public string ZSwapNote    => _zSwapNote;
    /// <summary>Mega y Z comparten el slot de objeto: avisa que equipar una Mega Stone reemplaza el Z-Crystal.</summary>
    public string MegaOtherItemNote => HasMegaStoneOptions && _equippedZItemName.Length > 0
        ? string.Format(_loc["Special_Warn_ZReplacesMega"], _equippedZItemName) : string.Empty;
    public string ZOtherItemNote => HasZCrystalOptions && _equippedMegaItemName.Length > 0
        ? string.Format(_loc["Special_Warn_MegaReplacesZ"], _equippedMegaItemName) : string.Empty;

    /// <summary>Botón "Quitar" de las tarjetas Mega/Z-Moves: deja el Pokémon sin objeto equipado.</summary>
    public void UnequipHeldItem()
    {
        _megaSwapNote = string.Empty;
        _zSwapNote = string.Empty;
        HeldItem = string.Empty;
        RecalcEquippedSpecialItem();
    }

    private void RecalcEquippedSpecialItem()
    {
        _equippedMegaItemName = string.Empty;
        _equippedZItemName = string.Empty;
        if (!string.IsNullOrEmpty(_heldItem) && _itemNameToId.TryGetValue(_heldItem, out var id))
        {
            // _heldItem ya es el nombre de PKHeX en el idioma de la UI; no se pasa por pokemon.db porque los ids
            // de los Z-Crystals son crudos de PKHeX y ahí podrían apuntar a otro objeto.
            if (_megaItemIds.Contains(id)) _equippedMegaItemName = _heldItem;
            else if (_zItemIds.Contains(id)) _equippedZItemName = _heldItem;
        }
        OnPropertyChanged(nameof(EquippedMegaItemName));
        OnPropertyChanged(nameof(EquippedZItemName));
        OnPropertyChanged(nameof(HasEquippedMegaItem));
        OnPropertyChanged(nameof(HasEquippedZItem));
        OnPropertyChanged(nameof(MegaSwapNote));
        OnPropertyChanged(nameof(ZSwapNote));
        OnPropertyChanged(nameof(MegaOtherItemNote));
        OnPropertyChanged(nameof(ZOtherItemNote));
        RefreshSpecialAdvisories();
    }

    public List<NatureEntry> AvailableNatures   { get => _availableNatures;   set => Set(ref _availableNatures,   value); }

    /// <summary>
    /// ViewModel del panel de selección de movimiento.
    /// null cuando el panel está cerrado.
    /// </summary>
    public MoveSelectorViewModel? MoveSelector
    {
        get => _moveSelector;
        private set
        {
            Set(ref _moveSelector, value);
            OnPropertyChanged(nameof(IsMoveSelecting));
        }
    }
    public bool IsMoveSelecting => _moveSelector != null;
    public List<string>      AvailableAbilities { get => _availableAbilities; set => Set(ref _availableAbilities, value); }

    public bool   HasTimeOfDay{ get => _hasTimeOfDay;set => Set(ref _hasTimeOfDay,value); }

    /// <summary>Ubicaciones de huevo válidas para este juego — misma fuente
    /// (GameInfo.GetLocationList, egg: true) que AvailableMetLocations.</summary>
    public List<ComboItem> AvailableEggLocations { get => _availableEggLocations; set => Set(ref _availableEggLocations, value); }
    public ComboItem? SelectedEggLocation { get => _selectedEggLocation; set => Set(ref _selectedEggLocation, value); }
    public string EggDate     { get => _eggDate;     set => Set(ref _eggDate,    value); }
    public bool   HasEggData  { get => _hasEggData;  set => Set(ref _hasEggData, value); }

    /// <summary>Checkbox explícito "nació de huevo" — independiente de IsEgg (que significa
    /// "sigue siendo huevo sin eclosionar ahora mismo", un concepto distinto). Habilita
    /// Egg Location/Egg Date para editar. Nace tildado solo si el Pokémon cargado ya tenía
    /// datos de huevo (HasEggData) — el usuario no tiene que volver a marcarlo para ver algo
    /// que ya existía, solo lo necesita para agregar un origen de huevo nuevo.</summary>
    public bool HatchedFromEgg
    {
        get => _hatchedFromEgg;
        set
        {
            var changed = _hatchedFromEgg != value;
            Set(ref _hatchedFromEgg, value);
            if (changed)
            {
                OnPropertyChanged(nameof(EnabledEggLocation));
                OnPropertyChanged(nameof(EnabledEggDate));
                // Gen3 no guarda ubicación/fecha de huevo: el origen de huevo es Met level 0 + ubicación de
                // eclosión + Poké Ball. Tildar el check lo aplica de verdad (antes no escribía nada).
                if (!_isLoading)
                {
                    if (value) { ApplyGen3HatchedOrigin(); ApplyTemplateHatchedOrigin(); }
                    else RestoreGen3LoadedOrigin();
                }
            }
        }
    }

    /// <summary>Gen4+: tildar "From egg" pone lo que PKHeX espera de un huevo eclosionado — Met level, Poké Ball,
    /// ubicación de huevo y fecha de huevo (ver <see cref="EggOriginService.TryGetEggTemplate"/>). Sin esto el
    /// usuario tenía que acertar a mano el Met level (0 en Gen4, 1 en Gen5+) y quedaba <c>EggFMetLevel_0</c>.
    /// No pisa una ubicación ni una fecha de huevo ya elegidas.</summary>
    private void ApplyTemplateHatchedOrigin()
    {
        var save = _pokemonService.CurrentSave;
        if (_currentPkm == null || save == null || _currentPkm.Format < 4 || Capabilities?.HasEggLocation != true) return;
        if (!EggOriginService.TryGetEggTemplate(_currentPkm, save, out var t)) return;
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));

        MetLevel = t.MetLevel;
        if (t.Ball > 0 && t.Ball < strings.Item.Count && AvailableBalls.Contains(strings.Item[t.Ball])) BallName = strings.Item[t.Ball];
        // Solo donde PKHeX lo exige (ORAS): la ubicación de eclosión debe ser una válida para ese huevo.
        if (t.NeedsMetLocation && AvailableMetLocations.FirstOrDefault(l => l.Value == t.MetLocation) is { } hatch)
            SelectedMetLocation = hatch;
        if (SelectedEggLocation == null || SelectedEggLocation.Value == 0)
            SelectedEggLocation = AvailableEggLocations.FirstOrDefault(l => l.Value == t.EggLocation) ?? SelectedEggLocation;
        if (Capabilities?.HasEggDate == true && string.IsNullOrWhiteSpace(EggDate))
        {
            // Misma fecha que la de captura: la de huevo no puede ser posterior (EggMetDate <= MetDate).
            if (!string.IsNullOrWhiteSpace(MetDate)) EggDate = MetDate;
            else if (t.EggDate is { } d) EggDate = d.ToString("yyyy-MM-dd");
        }
    }

    /// <summary>Gen3: destildar "From egg" devuelve el origen que el Pokémon traía del save (Met level, ubicación
    /// y bola), si ese origen NO era de huevo. Si ya venía de huevo no hay un origen salvaje que restaurar: el
    /// check sigue reflejando el Met level (ver <see cref="SyncGen3HatchedFlag"/>) y no queda "cosmético".</summary>
    private void RestoreGen3LoadedOrigin()
    {
        if (_currentPkm == null || !EggOriginService.StoresEggOnlyAsMetLevel(_currentPkm) || EggOriginService.IsHatchedMeta(_currentPkm, _currentPkm.MetLevel)) return;
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));

        MetLevel = _currentPkm.MetLevel;
        if (AvailableMetLocations.FirstOrDefault(l => l.Value == _currentPkm.MetLocation) is { } place) SelectedMetLocation = place;
        var ball = (int)_currentPkm.Ball;
        if (ball > 0 && ball < strings.Item.Count && AvailableBalls.Contains(strings.Item[ball])) BallName = strings.Item[ball];
    }

    /// <summary>Gen3 no guarda datos de huevo propios: "nació de huevo" == Met level 0. Mantiene el check
    /// alineado con el Met level cuando este cambia por cualquier vía (a mano, "Recrear", etc.). Se asigna con
    /// <c>_isLoading</c> para no registrar una edición ni disparar de nuevo la aplicación del origen.</summary>
    private void SyncGen3HatchedFlag()
    {
        if (_isLoading || _currentPkm == null || !EggOriginService.StoresEggOnlyAsMetLevel(_currentPkm)) return;
        var hatched = EggOriginService.IsHatchedMeta(_currentPkm, _metLevel);
        if (_hatchedFromEgg == hatched) return;
        _isLoading = true;
        try { HatchedFromEgg = hatched; }
        finally { _isLoading = false; }
    }

    /// <summary>Gen3 y Cristal: tildar "From egg" fija el origen que PKHeX espera de un huevo eclosionado (ver
    /// <see cref="EggOriginService.HatchedMeta"/>). El PID sin patrón de RNG lo genera el botón "Corregir automáticamente"
    /// del tab Legal. Cada campo pasa por su propio setter, así que queda como una edición más en la sesión.</summary>
    private void ApplyGen3HatchedOrigin()
    {
        if (_currentPkm == null || !EggOriginService.TryGetDirectHatchedMeta(_currentPkm, out var meta)) return;
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));

        MetLevel = meta.MetLevel;
        if (meta.MetLocation is { } loc && AvailableMetLocations.FirstOrDefault(l => l.Value == loc) is { } place) SelectedMetLocation = place;
        if (meta.Ball is { } ball && ball < strings.Item.Count && AvailableBalls.Contains(strings.Item[ball])) BallName = strings.Item[ball];
        // Cristal: PKHeX exige una hora del día 1–3 (la muestra el editor y la aplica EditApplyService).
        if (meta.TimeOfDay is { } tod && Capabilities?.HasMetTimeOfDay == true && string.IsNullOrEmpty(TimeOfDay))
        {
            TimeOfDay = tod switch { 1 => "Morning", 2 => "Day", _ => "Night" };
            HasTimeOfDay = true;
        }
    }

    /// <summary>Gen1 (PK1) no tiene mecánica de cría — PKHeX.Core hardcodea
    /// "IsEgg { get => false; set { } }" para ese formato, así que tildar este checkbox en
    /// un save Gen1 se ve como que funciona (queda tildado en pantalla, se graba en el
    /// pipeline de ediciones) pero al exportar el propio PKM ignora la escritura en
    /// silencio — no hay dónde guardar ese estado en el formato. Bug real reportado:
    /// probado con un Ditto de un save Gen1, el check no sobrevivía la exportación.</summary>
    public bool EnabledIsEgg => Capabilities?.HasBreeding ?? false;

    /// <summary>Si todavía es huevo sin eclosionar (IsEgg=true), "nació de huevo" es
    /// forzosamente cierto — no tiene sentido dejarlo destildar en ese caso. Gateado además
    /// por HasBreeding por la misma razón que EnabledIsEgg de arriba.</summary>
    public bool EnabledHatchedFromEgg => (Capabilities?.HasBreeding ?? false) && !IsEgg;
    public bool EnabledEggLocation => (Capabilities?.HasEggLocation ?? false) && HatchedFromEgg;
    public bool EnabledEggDate     => (Capabilities?.HasEggDate     ?? false) && HatchedFromEgg;

    // ── Flags de visibilidad por generación ──────────────────────────
    public bool ShowForm       => _hasForm;
    public bool ShowBall       => Capabilities?.HasBall        ?? false;
    public bool ShowTimeOfDay  => Capabilities?.HasMetTimeOfDay ?? false;
    public bool ShowHandler    => (Capabilities?.HasHandlerName ?? false) && !string.IsNullOrEmpty(HandlerName);
    public bool ShowEggData    => _hasEggData;
    public bool ShowPokerus    => Capabilities?.HasPokerus      ?? false;

    // ── *Enabled — para IsEnabled en la vista (siempre visible, solo depende de capabilities) ──
    public bool EnabledForm        => _hasForm;
    public bool EnabledGender      => Capabilities?.HasGender       ?? false;
    public bool EnabledNatures     => Capabilities?.HasNatures      ?? false;
    public bool EnabledAbilities   => Capabilities?.HasAbilities    ?? false;
    public bool EnabledHeldItems   => Capabilities?.HasHeldItems    ?? false;
    public bool EnabledFriendship  => (Capabilities?.Generation     ?? 0) >= 2;
    public bool EnabledHandler     => Capabilities?.HasHandlerName  ?? false;
    public bool EnabledPokerus     => Capabilities?.HasPokerus      ?? false;
    public bool EnabledShiny       => Capabilities?.HasShiny        ?? false;
    public bool EnabledMetLocation => Capabilities?.HasMetLocation  ?? false;
    public bool EnabledMetLevel    => Capabilities?.HasMetLevel     ?? false;
    public bool EnabledBall        => Capabilities?.HasBall         ?? false;
    public bool EnabledMetDate     => Capabilities?.HasMetDate      ?? false;
    public bool EnabledTimeOfDay   => Capabilities?.HasMetTimeOfDay ?? false;
    public bool ShowStatNature => Capabilities?.HasStatNature   ?? false;

    // Stats base (solo lectura)
    public int  BaseHp     { get => _baseHp;    set => Set(ref _baseHp,    value); }
    public int  BaseAtk    { get => _baseAtk;   set => Set(ref _baseAtk,   value); }
    public int  BaseDef    { get => _baseDef;   set => Set(ref _baseDef,   value); }
    public int  BaseSpa    { get => _baseSpa;   set => Set(ref _baseSpa,   value); }
    public int  BaseSpd    { get => _baseSpd;   set => Set(ref _baseSpd,   value); }
    public int  BaseSpe    { get => _baseSpe;   set => Set(ref _baseSpe,   value); }
    public int  BaseTotal  { get => _baseTotal; set => Set(ref _baseTotal, value); }
    public bool HasBaseStats { get => _hasBaseStats; set => Set(ref _hasBaseStats, value); }

    /// <summary>
    /// true cuando el save es Gen 1 — en esa generación no existían Ataque Especial
    /// y Defensa Especial por separado; ambos eran una sola estadística "Special".
    /// Se usa para colapsar AtqE/DefE en una única fila "Special" en la UI.
    /// </summary>
    public bool IsGen1 => Capabilities?.Generation == 1;

    /// <summary>
    /// true en Gen 1 y Gen 2 — ahí los IVs son en realidad DVs (0-15, no 0-31 como el resto
    /// de las generaciones), y el DV de PS no es un valor propio: PKHeX.Core lo deriva del
    /// bit menos significativo de Atq/Def/Vel/Especial (confirmado con
    /// WritePipelineRoundTripTests.MutarPokemon_SeReflejaAlReleerDelMismoSave — asignar
    /// IV_HP directo en Gen1 se ignora, HP sale derivado sí o sí).
    /// TAMBIÉN gobierna los EVs: en estas dos generaciones no son EVs modernos (0-252) sino
    /// "Stat Experience" (0-65.535 por stat, mismo mecanismo en Gen1 y Gen2). Se usa para:
    /// (a) topear el stepper de IV a 15 en vez de 31, (b) subir el tope del stepper de EV a
    /// 65.535 en vez de 252 (ver EffectiveMax en PokemonStatsView.axaml.cs), (c) deshabilitar
    /// el stepper de PS (no tiene ningún efecto editarlo), y (d) usar
    /// floor(sqrt(StatExp)/4) en vez de floor(EV/4) al calcular stats (ver RecalcStats).
    /// </summary>
    public bool UsesLegacyIVs => Capabilities?.Generation is 1 or 2;

    /// <summary>Tooltip del IV de PS: solo en Gen 1/2 (donde el DV de PS se deriva de los otros DV y el
    /// campo está deshabilitado). En el resto de generaciones es null → no se muestra ningún tooltip.</summary>
    public string? HpIvTooltip => UsesLegacyIVs
        ? _loc["Stats_HpDvNote"]
        : null;

    // IVs (editables 0–31, o 0–15 en Gen1/2 — ver UsesLegacyIVs)
    public int    IvHp  { get => _ivHp;  set { Set(ref _ivHp,  value); RecalcStats(); RecalcHiddenPower(); } }
    public int    IvAtk { get => _ivAtk; set { Set(ref _ivAtk, value); RecalcStats(); RecalcHiddenPower(); } }
    public int    IvDef { get => _ivDef; set { Set(ref _ivDef, value); RecalcStats(); RecalcHiddenPower(); } }
    public int    IvSpa
    {
        get => _ivSpa;
        set
        {
            Set(ref _ivSpa, value);
            // Gen 1: Ataque Especial y Defensa Especial son la misma stat "Special" — se
            // reflejan siempre juntos. Antes esto asignaba el campo privado (_ivSpd) directo
            // + OnPropertyChanged, sin pasar por Set<T> — el espejo se VEÍA en pantalla pero
            // nunca quedaba grabado en EditSessionService (RecordEdit nunca se llamaba para
            // "IvSpd"), así que en un save Gen1 donde el usuario solo tocara IvSpa el cambio
            // de IvSpd se perdía en silencio al exportar. Llamar al setter completo de IvSpd
            // (en vez del campo privado) lo hace pasar por Set<T> como cualquier otra
            // edición — sin loop infinito, porque para cuando IvSpd.set corre, _ivSpa ya es
            // igual a value.
            if (IsGen1 && _ivSpd != value) IvSpd = value;
            RecalcStats(); RecalcHiddenPower();
        }
    }
    public int    IvSpd
    {
        get => _ivSpd;
        set
        {
            Set(ref _ivSpd, value);
            if (IsGen1 && _ivSpa != value) IvSpa = value;
            RecalcStats(); RecalcHiddenPower();
        }
    }
    public int    IvSpe { get => _ivSpe; set { Set(ref _ivSpe, value); RecalcStats(); RecalcHiddenPower(); } }
    public string HiddenPowerType { get => _hiddenPowerType; set => Set(ref _hiddenPowerType, value); }
    public bool   ShowHiddenPower { get => _showHiddenPower; set => Set(ref _showHiddenPower, value); }

    // EVs — editables 0–252 en Gen3+ (suma total máx 510, no forzada acá todavía). En Gen1/2
    // esto es en realidad "Stat Experience" (Puntos de Experiencia de estadística), 0–65.535
    // por stat, sin tope de suma total — ver UsesLegacyIVs para el clamp del stepper y
    // RecalcStats/EvContribution para el aporte correcto a la fórmula de stats.
    public int EvHp  { get => _evHp;  set { Set(ref _evHp,  value); RecalcStats(); } }
    public int EvAtk { get => _evAtk; set { Set(ref _evAtk, value); RecalcStats(); } }
    public int EvDef { get => _evDef; set { Set(ref _evDef, value); RecalcStats(); } }
    public int EvSpa
    {
        get => _evSpa;
        set
        {
            Set(ref _evSpa, value);
            // Mismo fix que IvSpa/IvSpd arriba: espejar vía el setter completo (no el campo
            // privado) para que el lado espejado también quede grabado en EditSessionService.
            if (IsGen1 && _evSpd != value) EvSpd = value;
            RecalcStats();
        }
    }
    public int EvSpd
    {
        get => _evSpd;
        set
        {
            Set(ref _evSpd, value);
            if (IsGen1 && _evSpa != value) EvSpa = value;
            RecalcStats();
        }
    }
    public int EvSpe { get => _evSpe; set { Set(ref _evSpe, value); RecalcStats(); } }

    // Stats totales (solo lectura, calculados)
    public int TotalHp  { get => _totalHp;  set => Set(ref _totalHp,  value); }
    public int TotalAtk { get => _totalAtk; set => Set(ref _totalAtk, value); }
    public int TotalDef { get => _totalDef; set => Set(ref _totalDef, value); }
    public int TotalSpa { get => _totalSpa; set => Set(ref _totalSpa, value); }
    public int TotalSpd { get => _totalSpd; set => Set(ref _totalSpd, value); }
    public int TotalSpe { get => _totalSpe; set => Set(ref _totalSpe, value); }

    /// <summary>Etapa 4.6.5 — suma de las 6 EVs, calculada (no hay backing field: siempre se
    /// deriva de las 6 propiedades EvXxx actuales). En Gen1, EvSpa/EvSpd están sincronizadas
    /// entre sí (ver setters de arriba — Gen1 tiene una sola stat "Special"), así que la suma
    /// ahí cuenta ese valor dos veces; es un número informativo nomás en ese caso, ya que el
    /// tope real de 510 (y el FieldId "EV_TOTAL" que dispara el resaltado) es un concepto que
    /// solo existe desde Gen3 — LegalityAnalysis no genera esa advertencia para un PK1/PK2, así
    /// que el campo simplemente no se resalta ahí, sin necesidad de ocultarlo a mano.</summary>
    public int EvTotal => EvHp + EvAtk + EvDef + EvSpa + EvSpd + EvSpe;

    /// <summary>Texto ya formado para el campo — distinto según generación, no solo
    /// cosmético: en Gen1/Gen2 los "EVs" son en realidad Stat Experience (0-65535 POR
    /// STAT, sin ningún tope combinado — ver UsesLegacyIVs/EffectiveMax en
    /// PokemonStatsView.axaml.cs, que ya resuelve el máximo del stepper individual
    /// correctamente). Mostrar "/510" ahí es directamente incorrecto (ese tope es un
    /// concepto de Gen3+ que no existe en el juego real para esas generaciones) y la suma
    /// puede dar números de 6 cifras que se ven rotos contra un 510 que no aplica.</summary>
    public string EvTotalDisplay => UsesLegacyIVs
        ? string.Format(_loc["Stats_EvTotal_NoCap"], EvTotal)
        : $"{EvTotal} / 510";

    // ── Datos para las barras segmentadas (base vs aporte IV/EV/nivel/naturaleza) ──
    // Escala fija a 255 (máximo teórico de cualquier stat en el juego) para que
    // la barra no cambie de proporción al editar.
    private const double StatBarMax = 255.0;

    public StatBarInfo BarHp  => BuildStatBar(BaseHp,  TotalHp);
    public StatBarInfo BarAtk => BuildStatBar(BaseAtk, TotalAtk);
    public StatBarInfo BarDef => BuildStatBar(BaseDef, TotalDef);
    public StatBarInfo BarSpa => BuildStatBar(BaseSpa, TotalSpa);
    public StatBarInfo BarSpd => BuildStatBar(BaseSpd, TotalSpd);
    public StatBarInfo BarSpe => BuildStatBar(BaseSpe, TotalSpe);

    private static StatBarInfo BuildStatBar(int baseStat, int total)
    {
        // El segmento base nunca puede superar el total (por seguridad ante datos inconsistentes)
        var baseClamped = Math.Min(baseStat, total);
        var delta       = Math.Max(0, total - baseClamped);

        return new StatBarInfo
        {
            BaseValue   = baseClamped,
            DeltaValue  = delta,
            TotalValue  = total,
            BaseWidth   = Math.Clamp(baseClamped / StatBarMax, 0, 1),
            DeltaWidth  = Math.Clamp(delta       / StatBarMax, 0, 1),
            DeltaLabel  = $"+{delta}",
            ShowDeltaLabel = delta >= 10, // ocultar el "+N" si el segmento es demasiado angosto
        };
    }

    // ── Tab IVs/EVs: radar fijo + barras por fila (mockup ivs-evs-layout) ──────────────
    // Total, base (polígono punteado del radar) y barras de fila (verde = actual, gris = base) se
    // normalizan TODOS contra 500, para que radar y barras muestren la base con la misma escala.
    private const double TotalBarMax = 500.0;

    // Stat total "tal cual venía en el save" — se congela en LoadExtraData ANTES de reaplicar las
    // ediciones pendientes de la sesión (RestorePendingEdits), así el delta siempre es contra el save.
    private int _origTotalHp, _origTotalAtk, _origTotalDef, _origTotalSpa, _origTotalSpd, _origTotalSpe;
    private bool _captureOriginal;

    /// <summary>
    /// Valores normalizados (0-1) de los 6 stats Total para el radar chart.
    /// Usado para el polígono sólido "actual".
    /// </summary>
    public double RadarHp  => Math.Clamp(TotalHp  / TotalBarMax, 0, 1);
    public double RadarAtk => Math.Clamp(TotalAtk / TotalBarMax, 0, 1);
    public double RadarDef => Math.Clamp(TotalDef / TotalBarMax, 0, 1);
    public double RadarSpa => Math.Clamp(TotalSpa / TotalBarMax, 0, 1);
    public double RadarSpd => Math.Clamp(TotalSpd / TotalBarMax, 0, 1);
    public double RadarSpe => Math.Clamp(TotalSpe / TotalBarMax, 0, 1);

    /// <summary>Polígono de contorno "original del save" (fantasma) — mismo orden/escala que Radar*.</summary>
    public double RadarOrigHp  => Math.Clamp(_origTotalHp  / TotalBarMax, 0, 1);
    public double RadarOrigAtk => Math.Clamp(_origTotalAtk / TotalBarMax, 0, 1);
    public double RadarOrigDef => Math.Clamp(_origTotalDef / TotalBarMax, 0, 1);
    public double RadarOrigSpa => Math.Clamp(_origTotalSpa / TotalBarMax, 0, 1);
    public double RadarOrigSpd => Math.Clamp(_origTotalSpd / TotalBarMax, 0, 1);
    public double RadarOrigSpe => Math.Clamp(_origTotalSpe / TotalBarMax, 0, 1);

    // Barras por fila: verde = total (hasta el menor entre actual/original), verde claro = ganado,
    // rojo = perdido, marca = original; barra gris aparte debajo = stat base.
    public RowBarInfo RowBarHp  => BuildRowBar(BaseAtLevel(BaseHp, true),  TotalHp,  _origTotalHp);
    public RowBarInfo RowBarAtk => BuildRowBar(BaseAtLevel(BaseAtk, false), TotalAtk, _origTotalAtk);
    public RowBarInfo RowBarDef => BuildRowBar(BaseAtLevel(BaseDef, false), TotalDef, _origTotalDef);
    public RowBarInfo RowBarSpa => BuildRowBar(BaseAtLevel(BaseSpa, false), TotalSpa, _origTotalSpa);
    public RowBarInfo RowBarSpd => BuildRowBar(BaseAtLevel(BaseSpd, false), TotalSpd, _origTotalSpd);
    public RowBarInfo RowBarSpe => BuildRowBar(BaseAtLevel(BaseSpe, false), TotalSpe, _origTotalSpe);

    private static RowBarInfo BuildRowBar(int baseStat, int total, int orig)
    {
        static double F(double v) => Math.Clamp(v / TotalBarMax, 0, 1);
        var diff = total - orig;
        var fill = F(Math.Min(total, orig));
        return new RowBarInfo
        {
            FillWidth = fill,
            GainWidth = diff > 0 ? Math.Max(0, F(total) - fill) : 0,
            LossWidth = diff < 0 ? Math.Max(0, F(orig) - fill) : 0,
            OrigWidth = F(orig),
            BaseWidth = F(baseStat),
            DeltaText = diff == 0 ? string.Empty : (diff > 0 ? "+" : "−") + Math.Abs(diff),
            IsGain    = diff > 0,
            IsLoss    = diff < 0,
        };
    }

    // Naturaleza sobre el radar: insignia + / − junto al nombre del stat (PS nunca se afecta).
    private int NatureDir(int statIndex)
    {
        var m = GetNatureMultiplier(statIndex);
        return m > 1.0 ? 1 : m < 1.0 ? -1 : 0;
    }
    public bool NatUpAtk   => NatureDir(0) > 0;  public bool NatDownAtk => NatureDir(0) < 0;
    public bool NatUpDef   => NatureDir(1) > 0;  public bool NatDownDef => NatureDir(1) < 0;
    public bool NatUpSpa   => NatureDir(2) > 0;  public bool NatDownSpa => NatureDir(2) < 0;
    public bool NatUpSpd   => NatureDir(3) > 0;  public bool NatDownSpd => NatureDir(3) < 0;
    public bool NatUpSpe   => NatureDir(4) > 0;  public bool NatDownSpe => NatureDir(4) < 0;

    /// <summary>"+Vel −AtqE" / "Neutral" para el bloque Naturaleza del encabezado del tab.</summary>
    public string NatureModsDisplay
    {
        get
        {
            if (!ShowNatures) return string.Empty;
            if (_selectedNature is not { IsNeutral: false } n)
            {
                var neutral = NatureDatabase.Translate("Nature_Neutral");
                return neutral.Length > 0 ? char.ToUpperInvariant(neutral[0]) + neutral[1..] : neutral;
            }
            return $"+{NatureDatabase.StatLabel(n.Increased)} −{NatureDatabase.StatLabel(n.Decreased)}";
        }
    }

    // Medidor de EVs totales (solo Gen 3+, tope 510; en Gen 1/2 no hay tope combinado).
    public double EvMeterFill => UsesLegacyIVs ? 0 : Math.Clamp(EvTotal / 510.0, 0, 1);
    public double EvMeterRest => 1 - EvMeterFill;
    public bool   EvTotalOver => !UsesLegacyIVs && EvTotal > 510;

    // Vértices del radar (puntos + zona de hover). Se reconstruyen en cada RecalcStats.
    private List<RadarVertexInfo> _radarVertices = new();
    public IReadOnlyList<RadarVertexInfo> RadarVertices => _radarVertices;

    // Hover/foco cruzado fila ↔ radar. Índices del radar: 0=PS 1=Atq 2=Def 3=Vel 4=DefE 5=AtqE.
    // Deliberadamente NO usa Set<T>: no es un dato del Pokémon, no debe registrarse como edición.
    private int _hoveredStat = -1;
    public int HoveredStat
    {
        get => _hoveredStat;
        set
        {
            if (_hoveredStat == value) return;
            _hoveredStat = value;
            foreach (var v in _radarVertices) v.IsHot = v.Index == value;
            OnPropertyChanged();
        }
    }

    private void RebuildRadarVertices()
    {
        int[] cur  = [TotalHp, TotalAtk, TotalDef, TotalSpe, TotalSpd, TotalSpa];
        int[] orig = [_origTotalHp, _origTotalAtk, _origTotalDef, _origTotalSpe, _origTotalSpd, _origTotalSpa];
        var list = new List<RadarVertexInfo>(6);
        for (int i = 0; i < 6; i++)
        {
            var v   = Math.Clamp(cur[i] / TotalBarMax, 0, 1);
            var rad = RadarPointsConverter.AnglesDeg[i] * Math.PI / 180.0;
            var x   = RadarPointsConverter.CenterX + v * RadarPointsConverter.Radius * Math.Cos(rad);
            var y   = RadarPointsConverter.CenterY + v * RadarPointsConverter.Radius * Math.Sin(rad);
            list.Add(new RadarVertexInfo(i, x - 12, y - 12, cur[i].CompareTo(orig[i]), i == _hoveredStat));
        }
        _radarVertices = list;
        OnPropertyChanged(nameof(RadarVertices));
    }

    /// <summary>
    /// Valores normalizados (0-1) de los 6 stats base para el radar chart.
    /// Usado para el polígono punteado "base" (constante, no cambia con IV/EV).
    /// </summary>
    public double RadarBaseHp  => Math.Clamp(BaseAtLevel(BaseHp, true) / TotalBarMax, 0, 1);
    public double RadarBaseAtk => Math.Clamp(BaseAtLevel(BaseAtk, false) / TotalBarMax, 0, 1);
    public double RadarBaseDef => Math.Clamp(BaseAtLevel(BaseDef, false) / TotalBarMax, 0, 1);
    public double RadarBaseSpa => Math.Clamp(BaseAtLevel(BaseSpa, false) / TotalBarMax, 0, 1);
    public double RadarBaseSpd => Math.Clamp(BaseAtLevel(BaseSpd, false) / TotalBarMax, 0, 1);
    public double RadarBaseSpe => Math.Clamp(BaseAtLevel(BaseSpe, false) / TotalBarMax, 0, 1);

    // Movimientos
    public string Move1     { get => _move1;     set => Set(ref _move1,     value); }
    public string Move2     { get => _move2;     set => Set(ref _move2,     value); }
    public string Move3     { get => _move3;     set => Set(ref _move3,     value); }
    public string Move4     { get => _move4;     set => Set(ref _move4,     value); }

    // Id real de cada movimiento (el que escribe al PKM): fuente de verdad de la edición. El nombre
    // (Move1…) es solo para mostrar y cambia con el idioma de los datos. Mismo patrón que AbilityId.
    private int _move1Id, _move2Id, _move3Id, _move4Id;
    public int Move1Id { get => _move1Id; private set => Set(ref _move1Id, value); }
    public int Move2Id { get => _move2Id; private set => Set(ref _move2Id, value); }
    public int Move3Id { get => _move3Id; private set => Set(ref _move3Id, value); }
    public int Move4Id { get => _move4Id; private set => Set(ref _move4Id, value); }

    public bool Move1IsEmpty => string.IsNullOrEmpty(Move1);
    public bool Move2IsEmpty => string.IsNullOrEmpty(Move2);
    public bool Move3IsEmpty => string.IsNullOrEmpty(Move3);
    public bool Move4IsEmpty => string.IsNullOrEmpty(Move4);

    private bool _move1IsCreateSlot;
    private bool _move2IsCreateSlot;
    private bool _move3IsCreateSlot;
    private bool _move4IsCreateSlot;

    /// <summary>Solo el PRIMER slot de movimiento vacío ofrece el "+" para elegir uno nuevo
    /// (mismo criterio que el equipo: no tiene sentido ofrecerlo en huecos posteriores si el
    /// anterior sigue vacío). Se recalcula en RecalcMoveCreateSlots(), llamado después de
    /// cargar el Pokémon, aplicar una selección de movimiento, o limpiar el editor.</summary>
    public bool Move1IsCreateSlot { get => _move1IsCreateSlot; private set => Set(ref _move1IsCreateSlot, value); }
    public bool Move2IsCreateSlot { get => _move2IsCreateSlot; private set => Set(ref _move2IsCreateSlot, value); }
    public bool Move3IsCreateSlot { get => _move3IsCreateSlot; private set => Set(ref _move3IsCreateSlot, value); }
    public bool Move4IsCreateSlot { get => _move4IsCreateSlot; private set => Set(ref _move4IsCreateSlot, value); }

    /// <summary>Vacío mostrando el "—" tenue de siempre, distinto del "+" — solo los slots
    /// vacíos que NO son el primero (evita que el "+" y el "—" se dibujen superpuestos en el
    /// mismo slot, ver Move1IsCreateSlot).</summary>
    public bool Move1ShowDash => Move1IsEmpty && !Move1IsCreateSlot;
    public bool Move2ShowDash => Move2IsEmpty && !Move2IsCreateSlot;
    public bool Move3ShowDash => Move3IsEmpty && !Move3IsCreateSlot;
    public bool Move4ShowDash => Move4IsEmpty && !Move4IsCreateSlot;

    private void RecalcMoveCreateSlots()
    {
        Move1IsCreateSlot = Move1IsEmpty;
        Move2IsCreateSlot = !Move1IsEmpty && Move2IsEmpty;
        Move3IsCreateSlot = !Move1IsEmpty && !Move2IsEmpty && Move3IsEmpty;
        Move4IsCreateSlot = !Move1IsEmpty && !Move2IsEmpty && !Move3IsEmpty && Move4IsEmpty;
        // Notificar los "IsEmpty"/"ShowDash" también acá, ya que son computados desde
        // Move1-4 (que sí notifican los suyos) y desde los IsCreateSlot recién asignados,
        // pero no se notifican solos.
        OnPropertyChanged(nameof(Move1IsEmpty));
        OnPropertyChanged(nameof(Move2IsEmpty));
        OnPropertyChanged(nameof(Move3IsEmpty));
        OnPropertyChanged(nameof(Move4IsEmpty));
        OnPropertyChanged(nameof(Move1ShowDash));
        OnPropertyChanged(nameof(Move2ShowDash));
        OnPropertyChanged(nameof(Move3ShowDash));
        OnPropertyChanged(nameof(Move4ShowDash));
    }
    public string Move1Type { get => _move1Type; set => Set(ref _move1Type, value); }
    public string Move2Type { get => _move2Type; set => Set(ref _move2Type, value); }
    public string Move3Type { get => _move3Type; set => Set(ref _move3Type, value); }
    public string Move4Type { get => _move4Type; set => Set(ref _move4Type, value); }
    public string Move1Class { get => _move1Class; set => Set(ref _move1Class, value); }
    public string Move2Class { get => _move2Class; set => Set(ref _move2Class, value); }
    public string Move3Class { get => _move3Class; set => Set(ref _move3Class, value); }
    public string Move4Class { get => _move4Class; set => Set(ref _move4Class, value); }
    public int?   Move1Power    { get => _move1Power;    set => Set(ref _move1Power,    value); }
    public int?   Move2Power    { get => _move2Power;    set => Set(ref _move2Power,    value); }
    public int?   Move3Power    { get => _move3Power;    set => Set(ref _move3Power,    value); }
    public int?   Move4Power    { get => _move4Power;    set => Set(ref _move4Power,    value); }
    public int?   Move1Accuracy { get => _move1Accuracy; set => Set(ref _move1Accuracy, value); }
    public int?   Move2Accuracy { get => _move2Accuracy; set => Set(ref _move2Accuracy, value); }
    public int?   Move3Accuracy { get => _move3Accuracy; set => Set(ref _move3Accuracy, value); }
    public int?   Move4Accuracy { get => _move4Accuracy; set => Set(ref _move4Accuracy, value); }
    public int    Move1BasePP   { get => _move1BasePP;   set { Set(ref _move1BasePP,   value); OnPropertyChanged(nameof(Move1CanPPUp)); OnPropertyChanged(nameof(Move1MaxPP)); OnPropertyChanged(nameof(Move1PpSegments)); } }
    public int    Move2BasePP   { get => _move2BasePP;   set { Set(ref _move2BasePP,   value); OnPropertyChanged(nameof(Move2CanPPUp)); OnPropertyChanged(nameof(Move2MaxPP)); OnPropertyChanged(nameof(Move2PpSegments)); } }
    public int    Move3BasePP   { get => _move3BasePP;   set { Set(ref _move3BasePP,   value); OnPropertyChanged(nameof(Move3CanPPUp)); OnPropertyChanged(nameof(Move3MaxPP)); OnPropertyChanged(nameof(Move3PpSegments)); } }
    public int    Move4BasePP   { get => _move4BasePP;   set { Set(ref _move4BasePP,   value); OnPropertyChanged(nameof(Move4CanPPUp)); OnPropertyChanged(nameof(Move4MaxPP)); OnPropertyChanged(nameof(Move4PpSegments)); } }
    public int    Move1PP   { get => _move1PP;   set { Set(ref _move1PP,   value); OnPropertyChanged(nameof(Move1MaxPP)); OnPropertyChanged(nameof(Move1PpSegments)); } }
    /// <summary>PP máximos del movimiento con sus PP Ups actuales (denominador de "PP actual / máx").</summary>
    public int    Move1MaxPP => CalcPP(_move1BasePP, _move1PPUps);
    /// <summary>64 segmentos de la barra de PP (1 por PP): llenos = PP actual, hasta el máximo del movimiento = faltantes.</summary>
    public IReadOnlyList<PpSegment> Move1PpSegments => PpSegment.Build(_move1PP, Move1MaxPP);
    public int    Move2PP   { get => _move2PP;   set { Set(ref _move2PP,   value); OnPropertyChanged(nameof(Move2MaxPP)); OnPropertyChanged(nameof(Move2PpSegments)); } }
    /// <summary>PP máximos del movimiento con sus PP Ups actuales (denominador de "PP actual / máx").</summary>
    public int    Move2MaxPP => CalcPP(_move2BasePP, _move2PPUps);
    /// <summary>64 segmentos de la barra de PP (1 por PP): llenos = PP actual, hasta el máximo del movimiento = faltantes.</summary>
    public IReadOnlyList<PpSegment> Move2PpSegments => PpSegment.Build(_move2PP, Move2MaxPP);
    public int    Move3PP   { get => _move3PP;   set { Set(ref _move3PP,   value); OnPropertyChanged(nameof(Move3MaxPP)); OnPropertyChanged(nameof(Move3PpSegments)); } }
    /// <summary>PP máximos del movimiento con sus PP Ups actuales (denominador de "PP actual / máx").</summary>
    public int    Move3MaxPP => CalcPP(_move3BasePP, _move3PPUps);
    /// <summary>64 segmentos de la barra de PP (1 por PP): llenos = PP actual, hasta el máximo del movimiento = faltantes.</summary>
    public IReadOnlyList<PpSegment> Move3PpSegments => PpSegment.Build(_move3PP, Move3MaxPP);
    public int    Move4PP   { get => _move4PP;   set { Set(ref _move4PP,   value); OnPropertyChanged(nameof(Move4MaxPP)); OnPropertyChanged(nameof(Move4PpSegments)); } }
    /// <summary>PP máximos del movimiento con sus PP Ups actuales (denominador de "PP actual / máx").</summary>
    public int    Move4MaxPP => CalcPP(_move4BasePP, _move4PPUps);
    /// <summary>64 segmentos de la barra de PP (1 por PP): llenos = PP actual, hasta el máximo del movimiento = faltantes.</summary>
    public IReadOnlyList<PpSegment> Move4PpSegments => PpSegment.Build(_move4PP, Move4MaxPP);
    public string Move1Effect { get => _move1Effect; set => Set(ref _move1Effect, value); }
    public string Move2Effect { get => _move2Effect; set => Set(ref _move2Effect, value); }
    public string Move3Effect { get => _move3Effect; set => Set(ref _move3Effect, value); }
    public string Move4Effect { get => _move4Effect; set => Set(ref _move4Effect, value); }

    // Límite de PP Ups por movimiento: Sketch y Revival Blessing (PP base = 1)
    // nunca reciben PP Ups en el juego real. Se detecta por PP base < 5 en vez
    // de por nombre, para no depender del idioma activo de la UI.
    public bool Move1CanPPUp => Move1BasePP >= 5;
    public bool Move2CanPPUp => Move2BasePP >= 5;
    public bool Move3CanPPUp => Move3BasePP >= 5;
    public bool Move4CanPPUp => Move4BasePP >= 5;

    // PP Ups (editables 0–3, o fijos en 0 si Move*CanPPUp es false) — al cambiar recalculan PP actuales
    public int Move1PPUps
    {
        get => _move1PPUps;
        set { Set(ref _move1PPUps, Clamp(value, 0, Move1CanPPUp ? 3 : 0)); Move1PP = CalcPP(_move1BasePP, _move1PPUps); OnPropertyChanged(nameof(Move1MaxPP)); OnPropertyChanged(nameof(Move1PpSegments)); }
    }
    public int Move2PPUps
    {
        get => _move2PPUps;
        set { Set(ref _move2PPUps, Clamp(value, 0, Move2CanPPUp ? 3 : 0)); Move2PP = CalcPP(_move2BasePP, _move2PPUps); OnPropertyChanged(nameof(Move2MaxPP)); OnPropertyChanged(nameof(Move2PpSegments)); }
    }
    public int Move3PPUps
    {
        get => _move3PPUps;
        set { Set(ref _move3PPUps, Clamp(value, 0, Move3CanPPUp ? 3 : 0)); Move3PP = CalcPP(_move3BasePP, _move3PPUps); OnPropertyChanged(nameof(Move3MaxPP)); OnPropertyChanged(nameof(Move3PpSegments)); }
    }
    public int Move4PPUps
    {
        get => _move4PPUps;
        set { Set(ref _move4PPUps, Clamp(value, 0, Move4CanPPUp ? 3 : 0)); Move4PP = CalcPP(_move4BasePP, _move4PPUps); OnPropertyChanged(nameof(Move4MaxPP)); OnPropertyChanged(nameof(Move4PpSegments)); }
    }

    // Tabs
    public bool TabIvsEvs   { get => _tabIvsEvs;   set => Set(ref _tabIvsEvs,   value); }
    public bool TabMoves    { get => _tabMoves;    set => Set(ref _tabMoves,    value); }
    public bool TabLearnsets{ get => _tabLearnsets;set => Set(ref _tabLearnsets,value); }
    public bool TabSets     { get => _tabSets;     set => Set(ref _tabSets,     value); }
    public bool TabRibbons  { get => _tabRibbons;  set => Set(ref _tabRibbons,  value); }

    // ── Ribbons ─────────────────────────────────────────────────────
    // Layout híbrido: chips de filtro por categoría + headers de sección siempre visibles
    // (con "obtenidas/total"), más buscador de texto. Ver RibbonDatabase para el catálogo
    // completo (reusa el enum RibbonIndex de PKHeX.Core) y RibbonReader para la lectura
    // (soporta tanto formatos con IRibbonIndex — PK9/PA8/WC8 — como los más viejos).
    private List<RibbonItemViewModel> _allRibbons = new();
    public List<RibbonGroupViewModel> RibbonGroups { get => _ribbonGroups; set => Set(ref _ribbonGroups, value); }
    public List<RibbonChipViewModel> RibbonCategoryChips { get => _ribbonCategoryChips; set => Set(ref _ribbonCategoryChips, value); }
    public string RibbonSummary { get => _ribbonSummary; set => Set(ref _ribbonSummary, value); }

    // Barra de total bajo el buscador del tab Ribbons (columnas Star, 0-1).
    public double RibbonProgressFill => _allRibbons.Count == 0 ? 0 : (double)_allRibbons.Count(i => i.IsObtained) / _allRibbons.Count;
    public double RibbonProgressRest => 1 - RibbonProgressFill;

    // Plegado por categoría en la vista "Todas" — vive acá (no en cada grupo) porque la lista de
    // grupos se reconstruye con cada filtro/búsqueda. Marcas (53 cintas) arranca plegada.
    private readonly Dictionary<string, bool> _ribbonCollapsed = new(StringComparer.Ordinal) { ["Mark"] = true };

    public string SelectedRibbonCategory
    {
        get => _selectedRibbonCategory;
        set
        {
            Set(ref _selectedRibbonCategory, value);
            // Cada chip lleva su propio IsSelected (ver RibbonChipViewModel) — se sincroniza
            // acá en vez de reconstruir la lista completa, para no perder la instancia (y con
            // ella, los suscriptores de PropertyChanged) de cada chip en cada cambio de filtro.
            foreach (var chip in _ribbonCategoryChips)
                chip.IsSelected = chip.Category == value;
            RecalcRibbonGroups();
        }
    }
    public string RibbonSearchText
    {
        get => _ribbonSearchText;
        set { Set(ref _ribbonSearchText, value); RecalcRibbonGroups(); }
    }

    // Mecánicas especiales por generación (Mega/Z-Moves/Dynamax/Alpha/Tera) — cada una
    // conserva su propio flag para mostrar/ocultar su tarjeta, pero ahora viven todas
    // dentro de un único tab "Special" (TabSpecial = true si alguna aplica).
    public bool TabMega     { get => _tabMega;     set => Set(ref _tabMega,     value); }
    public bool TabZMoves   { get => _tabZMoves;   set => Set(ref _tabZMoves,   value); }
    public bool TabDynamax  { get => _tabDynamax;  set => Set(ref _tabDynamax,  value); }
    public bool TabAlpha    { get => _tabAlpha;    set => Set(ref _tabAlpha,    value); }
    public bool TabTerastal { get => _tabTerastal; set => Set(ref _tabTerastal, value); }
    public bool TabSpecial  { get => _tabSpecial;  set => Set(ref _tabSpecial,  value); }

    // ── Dynamax / Gigantamax (Gen 8: Sword/Shield) ────────────────────
    // Campos reales en PKHeX.Core: PK8.DynamaxLevel (0-10) y PK8.CanGigantamax (bool).
    // A diferencia de Mega/Z-Moves, esta mecánica SÍ tiene estado propio persistido
    // por Pokémon (no depende del held item).
    public int DynamaxLevel
    {
        get => _dynamaxLevel;
        set { Set(ref _dynamaxLevel, Clamp(value, 0, 10)); OnPropertyChanged(nameof(DynamaxHpBonusText)); OnPropertyChanged(nameof(DynamaxActive)); OnPropertyChanged(nameof(DynamaxSegments)); }
    }
    public bool CanGigantamax
    {
        get => _canGigantamax;
        set { Set(ref _canGigantamax, value); OnPropertyChanged(nameof(DynamaxActive)); OnPropertyChanged(nameof(GigantamaxEditable)); OnPropertyChanged(nameof(GigantamaxNote)); }
    }

    /// <summary>Bonus de HP en combate al Dynamax-earse, según el nivel (20% por nivel).</summary>
    public string DynamaxHpBonusText => string.Format(_loc["Special_DynamaxHpBonus"], DynamaxLevel * 20);
    /// <summary>Para resaltar la tarjeta en el tab Special: hay algo configurado más allá del default.</summary>
    public bool DynamaxActive => DynamaxLevel > 0 || CanGigantamax;

    /// <summary>Barra de 10 segmentos del Dynamax Level (mismo estilo que la barra de PP).</summary>
    public IReadOnlyList<PpSegment> DynamaxSegments => PpSegment.BuildLevel(_dynamaxLevel, 10);

    // Gigantamax solo es legal en especies que lo admiten (Gigantamax.CanToggle de PKHeX). Si el
    // Pokémon ya trae el flag en una especie que no lo admite, el checkbox sigue habilitado
    // para poder destildarlo y corregirlo.
    private bool _gmaxSpeciesCapable;
    public bool   GigantamaxEditable => _gmaxSpeciesCapable || _canGigantamax;
    public string GigantamaxNote => GigantamaxEditable ? string.Empty : _loc["Special_GmaxNote"];

    // ── Alpha / Noble (Gen 8: Legends Arceus) ─────────────────────────
    // Campos reales en PKHeX.Core: PA8.IsAlpha y PA8.IsNoble (ambos bool).
    // Nota de legalidad (para cuando exista el pipeline de escritura): PKHeX valida que
    // IsAlpha coincida con el tipo de encuentro original — cambiarlo a mano puede volver
    // el Pokémon ilegal. Por ahora esto es solo lectura/edición en UI, sin persistir.
    public bool IsAlpha
    {
        get => _isAlpha;
        set { Set(ref _isAlpha, value); OnPropertyChanged(nameof(AlphaActive)); }
    }
    public bool IsNoble
    {
        get => _isNoble;
        set { Set(ref _isNoble, value); OnPropertyChanged(nameof(AlphaActive)); }
    }
    /// <summary>Para resaltar la tarjeta en el tab Special.</summary>
    public bool AlphaActive => IsAlpha || IsNoble;

    // Noble solo existe en Legends Arceus (PA8); en Z-A (PA9) la fila no se muestra.
    private bool _hasNoble;
    public bool HasNoble
    {
        get => _hasNoble;
        set { Set(ref _hasNoble, value); OnPropertyChanged(nameof(AlphaCardTitle)); }
    }
    public string AlphaCardTitle => _hasNoble ? "Alpha / Noble" : "Alpha";

    // ── Tera Type (Gen 9) ─────────────────────────────────────────────
    // El Tera Type Original lo define el encuentro (solo lectura); el Override es lo único editable
    // (equivale a gastar Tera Shards). Combo: 0 = sin override, 1..18 = tipos, 19 = Stellar
    // (ver TeraOverrideCodec). Ogerpon, Terapagos y los huevos quedan bloqueados.
    private List<string> _teraOptionNames = [];
    private string _teraOriginalName = string.Empty;
    private int    _teraOverrideIndex;
    private bool   _teraOverrideEditable;
    private string _teraOverrideNote = string.Empty;
    public IReadOnlyList<string> TeraOverrideOptions
    {
        get => _teraOptionNames;
        private set { _teraOptionNames = value.ToList(); OnPropertyChanged(); }
    }
    public string TeraOriginalType { get => _teraOriginalName; private set { _teraOriginalName = value; OnPropertyChanged(); } }
    public int SelectedTeraOverrideIndex
    {
        get => _teraOverrideIndex;
        set
        {
            if (value < 0) return; // el ComboBox manda -1 cuando se reemplaza la lista de opciones
            Set(ref _teraOverrideIndex, value);
            if (_teraOptionNames.Count > 0)
                TeraType = value > 0 && value < _teraOptionNames.Count ? _teraOptionNames[value] : _teraOriginalName;
        }
    }
    public bool   TeraOverrideEditable { get => _teraOverrideEditable; private set { _teraOverrideEditable = value; OnPropertyChanged(); } }
    public string TeraOverrideNote     { get => _teraOverrideNote;     private set { _teraOverrideNote = value; OnPropertyChanged(); } }

    /// <summary>Nombre del Tera Type a partir del byte de PKHeX (0-17 = tipos, 99/18 = Stellar).</summary>
    private static string TeraName(GameStrings strings, byte type) =>
        type <= 17 ? strings.Types[type] : type is 18 or 99 ? "Stellar" : string.Empty;

    // ── Mega Evolution (Gen 6/7) ───────────────────────────────────────
    // Sin campo propio en el PKM: se deriva 100% del Held Item equipado (Mega Stone) +
    // la especie del Pokémon. Se recalcula cada vez que HeldItem cambia (ver su setter),
    // así reacciona en vivo si el usuario prueba otra piedra en el tab Info.
    public bool   MegaCompatible { get => _megaCompatible; set => Set(ref _megaCompatible, value); }
    public string MegaFormName  { get => _megaFormName;  set => Set(ref _megaFormName,  value); }
    public string MegaSummary   { get => _megaSummary;   set => Set(ref _megaSummary,   value); }
    /// <summary>Tipo(s) de la forma Mega resultante — solo para colorear la tarjeta (badge/fondo), vacíos si no aplica.</summary>
    public string MegaType1 { get => _megaType1; set => Set(ref _megaType1, value); }
    public string MegaType2 { get => _megaType2; set => Set(ref _megaType2, value); }

    // ── Z-Moves (Gen 7) ─────────────────────────────────────────────────
    // Igual que Mega: sin campo propio en el PKM, depende del held item (Z-Crystal) +
    // que el Pokémon tenga un movimiento compatible. Los Z-Crystal exclusivos por especie
    // (Pikanium Z, Snorlium Z, etc.) validan la especie pero NO el movimiento exacto
    // (ver nota en ZCrystalDatabase) — se muestra como advertencia, no como bloqueo.
    public bool   ZMoveCompatible { get => _zMoveCompatible; set => Set(ref _zMoveCompatible, value); }
    public string ZMoveResultName{ get => _zMoveResultName;set => Set(ref _zMoveResultName,value); }
    public string ZMoveSourceMove{ get => _zMoveSourceMove;set => Set(ref _zMoveSourceMove,value); }
    public string ZMoveNote      { get => _zMoveNote;      set => Set(ref _zMoveNote,      value); }
    // Texto de la línea "✗" de la tarjeta Z-Moves: distingue "sin cristal" de "cristal equipado pero inutilizable".
    private string _zMoveStatusText = string.Empty; // se fija en el primer recálculo (necesita _loc)
    public string ZMoveStatusText
    {
        get => _zMoveStatusText;
        private set { if (_zMoveStatusText == value) return; _zMoveStatusText = value; OnPropertyChanged(); }
    }
    // Idem para la tarjeta Mega: especie sin Mega vs. especie con Mega pero sin piedra equipada.
    public string MegaStatusText => HasMegaStoneOptions
        ? _loc["Special_Mega_NoStone"]
        : _loc["Special_Mega_None"];
    /// <summary>Tipo del Z-Crystal activo — solo para colorear la tarjeta. Vacío en cristales
    /// exclusivos por especie (no tienen un tipo asociado, ej. Pikanium Z).</summary>
    public string ZMoveType { get => _zMoveType; set => Set(ref _zMoveType, value); }

    // Colecciones
    public ObservableCollection<string>                 LegalityIssues  { get; }
    public ObservableCollection<TypeEffectivenessEntry> TypeWeaknesses  { get; }
    public ObservableCollection<RecommendedSet>         RecommendedSets  { get; }
    public ObservableCollection<RecommendedSetGroup>    RecommendedSetGroups { get; }
    public ObservableCollection<LearnsetEntry>          Learnsets       { get; }

    // Comandos
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Learnsets agrupados por método para binding en la vista.
    /// Se recalcula cuando cambia la colección Learnsets.
    /// </summary>
    public IEnumerable<LearnsetGroup> LearnsetGroups =>
        Learnsets
            .GroupBy(e => e.LearnMethod)
            .OrderBy(g => MethodOrder(g.Key))
            .Select(g => new LearnsetGroup
            {
                Method = g.Key,
                Moves  = g.ToList(),
            });

    private static int MethodOrder(string method) => method switch
    {
        "levelup"   => 0,
        "egg"       => 1,
        "tm"        => 2,
        "tr"        => 3,
        "tutor"     => 4,
        "reminder"  => 5,
        "evolution" => 6,
        "oyabun"    => 7,
        "zmove"     => 8,
        "special"   => 9,
        _           => 10,
    };

    // ── Carga principal ───────────────────────────────────────────────
    public void LoadPokemon(PKM pkm, SaveCapabilities capabilities, PokemonSlotKey slotKey)
    {
        if (pkm.Species == 0) { Close(); return; }

        var perfTotal = Perf.Start();

        _isLoading = true;
        _formMoveSlots.Clear();
        _formChangedGender = false;
        _loadToken++;
        _legalityDebounceCts?.Cancel(); // el Pokémon anterior ya no existe, no dejar un
                                         // recálculo en vivo viejo corriendo de fondo
        _currentSlotKey = slotKey;

        // Limpiar siempre antes de cargar para evitar datos pegados
        ClearFields();

        _currentPkm  = pkm;
        Capabilities = capabilities;
        LegacyDamageClass.Generation = capabilities.Generation; // Gen 1-3: clase de daño por tipo

        var lang    = GetPKHeXLang(_loc.CurrentLocale);
        var strings = GameInfo.GetStrings(lang);
        var gen     = capabilities.Generation;

        // Datos básicos
        Species    = FormNameFormatter.Format(strings.Species[pkm.Species], pkm.Species, pkm.Form);
        Nickname   = pkm.Nickname;
        IsNicknamedFlag = pkm.IsNicknamed;
        _editSession.SetDisplayName(slotKey, BuildSlotDisplayName(Species, slotKey));
        _editSession.SetNickname(slotKey, Nickname);
        Level      = pkm.CurrentLevel;
        OT         = pkm.OriginalTrainerName;
        TID        = pkm.TID16;
        IsShiny    = pkm.IsShiny;
        IsEgg      = pkm.IsEgg;
        Friendship = pkm.CurrentFriendship;

        // Tipos — usar DB para evitar el bug de índices Gen1/2 vs Gen3+
        // PersonalInfo.Type1/Type2 devuelve índices internos del juego que difieren
        // entre generaciones. La DB tiene los tipos correctos por species ID.
        var personal = pkm.PersonalInfo;

        // Género: qué opciones mostrar depende de la especie — algunas son de un solo
        // género fijo (ej. Nidoran♂/♀, Volbeat/Illumise), otras sin género, y el resto puede
        // ser cualquiera de los dos. personal.Genderless/IsDualGender vienen de PKHeX.Core
        // (PersonalInfo de la especie en ESTE formato/generación), más confiable acá que
        // nuestra propia DB porque es exactamente lo que usa LegalityAnalysis para validar.
        AvailableGenders = personal.Genderless ? ["—"]
            : personal.IsDualGender ? ["♂", "♀"]
            : pkm.Gender == 1 ? ["♀"] : ["♂"]; // especie de género único fijo
        Gender = pkm.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };
        var speciesTypes = SpeciesDatabase.Instance.GetByForm(pkm.Species, pkm.Form);
        if (speciesTypes != null && speciesTypes.Types.Count > 0)
        {
            Type1    = Capitalize(speciesTypes.Types[0]);
            HasType2 = speciesTypes.Types.Count > 1;
            Type2    = HasType2 ? Capitalize(speciesTypes.Types[1]) : string.Empty;
        }
        else
        {
            // Fallback a PKHeX si la DB no tiene la especie
            Type1 = strings.Types[personal.Type1];
            if (personal.Type1 != personal.Type2)
            { Type2 = strings.Types[personal.Type2]; HasType2 = true; }
            else
            { Type2 = string.Empty; HasType2 = false; }
        }

        // Naturaleza, Habilidad, Objeto
        // Naturalezas: las 25 son fijas, todas disponibles desde Gen 3 (cuando Nature existe)
        AvailableNatures = capabilities.HasNatures ? NatureDatabase.CreateList() : [];

        // Habilidades: las posibles para esta especie, filtrando hidden si la gen no las soporta
        AvailableAbilities = LoadAbilitiesForSpecies(SpeciesFormResolver.ResolveDbSpeciesId(pkm.Species, pkm.Form), strings, capabilities);

        // Llave inglesa (nombre del enum), NO el texto de PKHeX: es lo que se compara con las entradas del
        // combo y lo que parsea EditApplyService. El texto mostrado sale de NatureEntry.LocalizedName.
        // Hardy es Nature 0: antes `> 0` lo dejaba sin naturaleza.
        Nature   = capabilities.HasNatures   && NatureDatabase.ById((int)pkm.Nature) is { } loadedNature ? loadedNature.Name : string.Empty;
        _abilityNameToId = BuildNameIndex(strings.Ability, firstWins: true);
        _moveNameToId    = BuildNameIndex(strings.Move,    firstWins: true);
        Ability  = capabilities.HasAbilities && pkm.Ability > 0 ? strings.Ability[pkm.Ability]         : string.Empty;
        AbilityId = capabilities.HasAbilities ? pkm.Ability : 0; // id real del PKM como estado original

        // Guardar growth rate para recalcular EXP al cambiar Level
        _growthRate = pkm.PersonalInfo.EXPGrowth;

        // Listas filtradas de items y balls para esta versión
        LoadItemLists(strings, capabilities);

        HeldItem = capabilities.HasHeldItems && pkm.HeldItem > 0 ? strings.Item[pkm.HeldItem] : string.Empty;
        HeldItemId = capabilities.HasHeldItems ? pkm.HeldItem : 0; // id real del PKM como estado original

        // Encuentro — Location es un selector con las ubicaciones donde esta especie
        // realmente puede aparecer en este juego puntual, no todas las del juego.
        //
        // Antes: GameInfo.GetLocationList(pkm.Version, pkm.Context, egg: false) a secas —
        // la tabla completa de ubicaciones nombradas del juego, sin filtrar por especie
        // (bug confirmado en sesión de análisis de legalidad: un usuario no tenía forma de
        // saber cuáles de esas ubicaciones eran válidas para el Pokémon que tenía
        // seleccionado). Ahora: mismos generadores de encuentro que ya usa
        // PokemonService.BuildPokemon para crear Pokémon reales
        // (EncounterMovesetGenerator.GenerateEncounters, sin sumar PKHeX.Core.AutoMod) —
        // se toma la unión de ubicaciones (ILocation.GetLocation()) de todas las plantillas
        // de encuentro real para esta especie/juego, y se filtra la lista completa a solo
        // esas. Si el filtro queda vacío (especie sin ninguna plantilla con ubicación
        // conocida — típico de regalos/trades fijos) se cae a la lista completa de
        // siempre, antes que dejar al usuario sin ninguna opción.
        // PERF: GenerateEncounters (PKHeX) puede tardar varios segundos y antes corría ACÁ, en el hilo
        // de UI, congelando la ventana entre el clic y la primera pintura. Ahora el combo arranca con la
        // lista completa de ubicaciones del juego (instantáneo) y el filtrado por encuentros reales
        // corre en background (RefineMetLocationsAsync), en paralelo con el análisis de legalidad.
        var perfEnc = Perf.Start();
        if (capabilities.HasMetLocation)
        {
            List<ComboItem> allLocations;
            try { allLocations = GameInfo.GetLocationList(pkm.Version, pkm.Context, egg: false).ToList(); }
            catch { allLocations = []; }   // versión/contexto sin datos de ubicación — queda sin opciones

            AvailableMetLocations = allLocations;
            SelectedMetLocation   = allLocations.FirstOrDefault(l => l.Value == pkm.MetLocation);

            if (allLocations.Count > 0)
                _ = RefineMetLocationsAsync(pkm.Clone(), allLocations, _loadToken);
        }
        else
        {
            AvailableMetLocations = [];
            SelectedMetLocation = null;
        }
        Perf.Log("LoadPokemon: ubicaciones de encuentro (lista completa; filtro en background)", perfEnc);
        MetLevel    = gen >= 3 ? pkm.MetLevel : 0;
        MetDate     = gen >= 4 && pkm.MetDate.HasValue ? pkm.MetDate.Value.ToString("yyyy-MM-dd") : string.Empty;

        // Tera
        TeraType = capabilities.HasTerastal && pkm is PK9 pk9 ? TeraName(strings, (byte)pk9.TeraType) : string.Empty;
        if (capabilities.HasTerastal && pkm is PK9 pk9o)
        {
            var teraNames = new List<string> { _loc["Tera_NoOverride"] };
            for (var teraI = 0; teraI <= 17; teraI++) teraNames.Add(strings.Types[teraI]);
            teraNames.Add("Stellar");
            TeraOriginalType = TeraName(strings, (byte)pk9o.TeraTypeOriginal);
            TeraOverrideOptions = teraNames;
            SelectedTeraOverrideIndex = TeraOverrideCodec.ToIndex((byte)pk9o.TeraTypeOverride);
            OnPropertyChanged(nameof(SelectedTeraOverrideIndex)); // re-empuja el índice al ComboBox aunque el valor no haya cambiado
            var teraLocked = pk9o.IsEgg
                || pk9o.Species == (ushort)PKHeX.Core.Species.Ogerpon
                || pk9o.Species == (ushort)PKHeX.Core.Species.Terapagos;
            TeraOverrideEditable = !teraLocked;
            TeraOverrideNote = pk9o.IsEgg ? _loc["Tera_Note_Egg"]
                : pk9o.Species == (ushort)PKHeX.Core.Species.Ogerpon ? _loc["Tera_Note_Ogerpon"]
                : pk9o.Species == (ushort)PKHeX.Core.Species.Terapagos ? _loc["Tera_Note_Terapagos"]
                : _loc["Tera_Note_Default"];
        }
        else
        {
            TeraOverrideOptions = [];
            TeraOriginalType = string.Empty;
            TeraOverrideEditable = false;
            TeraOverrideNote = string.Empty;
        }

        // IVs — asignar directo sin disparar RecalcStats aún
        _ivHp  = pkm.IV_HP;  _ivAtk = pkm.IV_ATK; _ivDef = pkm.IV_DEF;
        _ivSpa = pkm.IV_SPA; _ivSpd = pkm.IV_SPD; _ivSpe = pkm.IV_SPE;
        foreach (var p in new[] { nameof(IvHp), nameof(IvAtk), nameof(IvDef),
                                   nameof(IvSpa), nameof(IvSpd), nameof(IvSpe) })
            OnPropertyChanged(p);

        // EVs
        _evHp  = pkm.EV_HP;  _evAtk = pkm.EV_ATK; _evDef = pkm.EV_DEF;
        _evSpa = pkm.EV_SPA; _evSpd = pkm.EV_SPD; _evSpe = pkm.EV_SPE;
        foreach (var p in new[] { nameof(EvHp), nameof(EvAtk), nameof(EvDef),
                                   nameof(EvSpa), nameof(EvSpd), nameof(EvSpe) })
            OnPropertyChanged(p);

        // Hidden Power (Gen 2-7)
        if (gen >= 2 && gen <= 7)
        {
            Span<int> hpIvs = stackalloc int[6];
            pkm.GetIVs(hpIvs);
            HiddenPowerType = HiddenPowerTypeName(strings, hpIvs, pkm.Context); ShowHiddenPower = true;
        }
        else
        { HiddenPowerType = string.Empty; ShowHiddenPower = false; }

        // Movimientos
        LoadMove(pkm.Move1, pkm.Move1_PP, pkm.Move1_PPUps, strings,
            out string m1, out string m1t, out string m1c, out int? m1p, out int? m1a,
            out int m1bpp, out int m1pp, out int m1ups, out string m1e);
        LoadMove(pkm.Move2, pkm.Move2_PP, pkm.Move2_PPUps, strings,
            out string m2, out string m2t, out string m2c, out int? m2p, out int? m2a,
            out int m2bpp, out int m2pp, out int m2ups, out string m2e);
        LoadMove(pkm.Move3, pkm.Move3_PP, pkm.Move3_PPUps, strings,
            out string m3, out string m3t, out string m3c, out int? m3p, out int? m3a,
            out int m3bpp, out int m3pp, out int m3ups, out string m3e);
        LoadMove(pkm.Move4, pkm.Move4_PP, pkm.Move4_PPUps, strings,
            out string m4, out string m4t, out string m4c, out int? m4p, out int? m4a,
            out int m4bpp, out int m4pp, out int m4ups, out string m4e);

        Move1 = m1; Move1Type = m1t; Move1Class = m1c; Move1Power = m1p; Move1Accuracy = m1a;
        Move1BasePP = m1bpp; _move1PPUps = Clamp(m1ups, 0, Move1CanPPUp ? 3 : 0); Move1PP = m1pp;
        Move2 = m2; Move2Type = m2t; Move2Class = m2c; Move2Power = m2p; Move2Accuracy = m2a;
        Move2BasePP = m2bpp; _move2PPUps = Clamp(m2ups, 0, Move2CanPPUp ? 3 : 0); Move2PP = m2pp;
        Move3 = m3; Move3Type = m3t; Move3Class = m3c; Move3Power = m3p; Move3Accuracy = m3a;
        Move3BasePP = m3bpp; _move3PPUps = Clamp(m3ups, 0, Move3CanPPUp ? 3 : 0); Move3PP = m3pp;
        Move4 = m4; Move4Type = m4t; Move4Class = m4c; Move4Power = m4p; Move4Accuracy = m4a;
        Move4BasePP = m4bpp; _move4PPUps = Clamp(m4ups, 0, Move4CanPPUp ? 3 : 0); Move4PP = m4pp;
        Move1Effect = m1e; Move2Effect = m2e; Move3Effect = m3e; Move4Effect = m4e;
        Move1Id = pkm.Move1; Move2Id = pkm.Move2; Move3Id = pkm.Move3; Move4Id = pkm.Move4;
        RecalcMoveCreateSlots();
        foreach (var p in new[] { nameof(Move1PPUps), nameof(Move2PPUps),
                                   nameof(Move3PPUps), nameof(Move4PPUps) })
            OnPropertyChanged(p);

        // Form — editable (fase 1 de formas: ver hotfixes/formas_handoff/fase-formas-handoff.md)
        var selectableForms = PokemonService.GetSelectableForms(pkm, strings);
        if (selectableForms != null)
        {
            AvailableForms = selectableForms.Select(f => f.Name).ToList();
            var current = selectableForms.FirstOrDefault(f => f.Index == pkm.Form);
            Form    = current.Name ?? AvailableForms[0];
            HasForm = true;
        }
        else { Form = string.Empty; HasForm = false; AvailableForms = []; }

        // Ball
        BallName = capabilities.HasBall && pkm.Ball > 0 && pkm.Ball < strings.Item.Count
            ? strings.Item[pkm.Ball] : string.Empty;
        BallId = capabilities.HasBall ? pkm.Ball : 0; // id real del PKM como estado original

        // Time of day — solo Gen2 (PK2.MetTimeOfDay); Gen4 no lo expone en PK4
        if (capabilities.HasMetTimeOfDay && pkm is PK2 pk2Tod)
        {
            TimeOfDay = pk2Tod.MetTimeOfDay switch
            {
                1 => "Morning", 2 => "Day", 3 => "Night",
                _ => string.Empty
            };
            HasTimeOfDay = !string.IsNullOrEmpty(TimeOfDay);
        }
        else { TimeOfDay = string.Empty; HasTimeOfDay = false; }

        // Pokérus — PokerusDays y IsPokerusInfected están en PKM base (abstract)
        HasPokerus = capabilities.HasPokerus;
        PkrsActive = capabilities.HasPokerus && pkm.IsPokerusInfected;

        // Experience
        _experience = pkm.EXP.ToString("N0"); OnPropertyChanged(nameof(Experience));

        // Handler (Gen5+)
        HandlerName = capabilities.HasHandlerName ? pkm.HandlingTrainerName : string.Empty;

        // Egg data
        HasEggData = pkm.IsEgg || pkm.EggLocation > 0 || EggOriginService.IsHatchedMeta(pkm, pkm.MetLevel);
        // Nace tildado solo si ya había datos de huevo — el usuario no tiene que volver a
        // marcarlo para ver algo que ya existía en el Pokémon cargado.
        HatchedFromEgg = HasEggData;
        if (capabilities.HasEggLocation)
        {
            try
            {
                AvailableEggLocations = GameInfo.GetLocationList(pkm.Version, pkm.Context, egg: true).ToList();
            }
            catch
            {
                AvailableEggLocations = [];
            }
            SelectedEggLocation = AvailableEggLocations.FirstOrDefault(l => l.Value == pkm.EggLocation);
        }
        else
        {
            AvailableEggLocations = [];
            SelectedEggLocation = null;
        }
        EggDate = capabilities.HasEggDate && pkm.EggMetDate.HasValue
            ? pkm.EggMetDate.Value.ToString("yyyy-MM-dd") : string.Empty;

        // Sprite
        Sprite = SpriteService.Instance.GetPokemonSprite(pkm.Species, pkm.IsShiny, pkm.Gender == 1, pkm.Form);

        // Tabs
        TabIvsEvs   = true;
        TabMoves    = true;
        TabLearnsets= true;
        TabRibbons  = gen >= 3;
        LoadRibbons(pkm, gen);
        TabMega     = capabilities.HasMega;
        RecalcMega(); // HeldItem ya se cargó antes de que TabMega existiera; recalcular ahora
        TabZMoves   = capabilities.HasZMoves;
        LoadZCrystalOptions();
        RecalcZMove(); // ídem: HeldItem y los 4 moves ya están cargados, faltaba el flag
        RecalcEquippedSpecialItem();
        OnPropertyChanged(nameof(HeldItemHint));
        TabDynamax  = capabilities.HasDynamax;
        TabAlpha    = capabilities.HasAlpha;
        TabTerastal = capabilities.HasTerastal;
        TabSpecial  = TabMega || TabZMoves || TabDynamax || TabAlpha || TabTerastal;

        if (capabilities.HasDynamax && pkm is PK8 pk8dmax)
        {
            _gmaxSpeciesCapable = Gigantamax.CanToggle(pk8dmax.Species, pk8dmax.Form);
            DynamaxLevel = pk8dmax.DynamaxLevel;
            CanGigantamax = pk8dmax.CanGigantamax;
        }
        else
        {
            _gmaxSpeciesCapable = false;
            DynamaxLevel = 0;
            CanGigantamax = false;
        }
        OnPropertyChanged(nameof(GigantamaxEditable));
        OnPropertyChanged(nameof(GigantamaxNote));
        HasNoble = capabilities.HasAlpha && pkm is PA8;

        // PA9 (Legends Z-A) tiene IsAlpha igual que PA8 (Legends Arceus), pero NO IsNoble
        // (confirmado por reflection — PA9 no declara esa propiedad). Sin esta rama, un save
        // de Z-A entraba directo al "else" de abajo (pkm is PA8 daba false) y el tab Special
        // quedaba sin nada que mostrar pese a que HasAlpha ya es true para ZA (ver
        // SaveCapabilities.Detect()).
        if (capabilities.HasAlpha && pkm is PA8 pa8)
        {
            IsAlpha = pa8.IsAlpha;
            IsNoble = pa8.IsNoble;
        }
        else if (capabilities.HasAlpha && pkm is PA9 pa9)
        {
            IsAlpha = pa9.IsAlpha;
            IsNoble = false; // no existe en PA9 — el checkbox de Noble no debería mostrarse
                              // para Z-A, pendiente de un flag más fino que HasAlpha si hace
                              // falta distinguir Arceus de Z-A en la UI más adelante.
        }
        else
        {
            IsAlpha = false;
            IsNoble = false;
        }

        // Tabla de debilidades — usar tipos de DB (Gen-independent)
        TypeWeaknesses.Clear();
        var dbSpecies = SpeciesDatabase.Instance.GetByForm(pkm.Species, pkm.Form);
        var t1 = dbSpecies?.Types.Count > 0
            ? dbSpecies.Types[0].ToLowerInvariant()
            : TypeNames.ToKey(strings.Types[personal.Type1]);
        var t2 = dbSpecies?.Types.Count > 1
            ? dbSpecies.Types[1].ToLowerInvariant()
            : (personal.Type1 != personal.Type2
                ? TypeNames.ToKey(strings.Types[personal.Type2]) : string.Empty);
        foreach (var entry in TypeEffectivenessDatabase.Instance.GetMatchups(t1, t2))
            TypeWeaknesses.Add(entry);

        // Datos extra
        var perfExtra = Perf.Start();
        LoadExtraData(pkm, strings);
        Perf.Log("LoadPokemon: LoadExtraData (learnsets, sets, ribbons...)", perfExtra);

        // Legalidad — GetLegalitySummary llama a LegalityAnalysis de PKHeX.Core, que puede
        // demorar lo suficiente como para congelar la ventana si corre en el hilo de UI
        // (más ahora que BuildPokemon también puede buscar encuentros legítimos). Se dispara
        // en background (LoadLegalityAsync) y el resto de la carga de este Pokémon sigue de
        // largo sin esperarla — el badge de legalidad se ve "cargando" hasta que termine.
        Perf.Log("LoadPokemon: total hasta lanzar legalidad", perfTotal);
        IsLegalityLoading = true;
        _ = LoadLegalityAsync(pkm, _loadToken);

        // Notificar visibilidad condicional
        foreach (var p in new[] {
            nameof(ShowNatures), nameof(ShowAbilities), nameof(ShowHeldItems),
            nameof(ShowShiny),   nameof(ShowGender),    nameof(ShowMetLevel),
            nameof(ShowMetDate), nameof(ShowFriendship),nameof(ShowForm),
            nameof(ShowBall),    nameof(ShowTimeOfDay), nameof(ShowHandler),
            nameof(ShowEggData), nameof(ShowPokerus),   nameof(ShowStatNature),
            nameof(ShowMetLocation),
            nameof(EnabledForm),       nameof(EnabledGender),    nameof(EnabledNatures),
            nameof(EnabledAbilities),  nameof(EnabledHeldItems), nameof(EnabledFriendship),
            nameof(EnabledHandler),    nameof(EnabledPokerus),   nameof(EnabledShiny),
            nameof(EnabledMetLocation),nameof(EnabledMetLevel),  nameof(EnabledBall),
            nameof(EnabledMetDate),    nameof(EnabledTimeOfDay),
            nameof(EnabledEggLocation),nameof(EnabledEggDate),   nameof(EnabledHatchedFromEgg),
            nameof(EnabledIsEgg),
            nameof(IsGen1),            nameof(UsesLegacyIVs),   nameof(HpIvTooltip),
            nameof(EvTotal),           nameof(EvTotalDisplay) })
            OnPropertyChanged(p);

        RestorePendingEdits(slotKey);
        _isLoading = false;

        IsVisible = true;
    }

    /// <summary>Reaplica sobre este ViewModel cualquier edición previa de este slot en la sesión
    /// actual (ver Set&lt;T&gt;). Se ejecuta todavía con _isLoading en true, así que no se
    /// re-graba a sí misma como una edición nueva.</summary>
    /// <summary>"Especie — Caja N, Slot M" / "Especie — Equipo #N", para el modal de revisión.</summary>
    private static string BuildSlotDisplayName(string species, PokemonSlotKey key) => key.IsParty
        ? UiText.F("Slot_Party", species, key.SlotIndex + 1)
        : UiText.F("Slot_Box", species, key.BoxIndex + 1, key.SlotIndex + 1);

    private void RestorePendingEdits(PokemonSlotKey slotKey)
    {
        var edits = _editSession.GetRawEdits(slotKey);
        if (edits == null || edits.Count == 0) return;

        var type = GetType();
        foreach (var (propName, value) in edits)
        {
            if (propName.StartsWith("Ribbon:")) continue; // esas se restauran aparte
            var prop = type.GetProperty(propName);
            if (prop is { CanWrite: true })
                prop.SetValue(this, value);
        }

        RestorePendingRibbonEdits(slotKey);
        ReapplyMoveNamesFromIds();
        // Restaurar Move1-4 (por reflexión, arriba) cambia qué slots están vacíos DESPUÉS de que la carga
        // ya calculó los "+" (ver RecalcMoveCreateSlots en LoadPokemon): sin recalcular, un Pokémon con
        // movimientos pendientes quedaba con slots vacíos sin "+" ni "—" (bug visto en Yellow).
        RecalcMoveCreateSlots();
    }


    /// <summary>Slots de movimiento (1-4) cuyo valor en pantalla viene de la vista previa de una
    /// forma (ver <see cref="RefreshFormDerivedData"/>), para poder restaurarlos al volver atrás.</summary>
    private readonly HashSet<int> _formMoveSlots = new();

    /// <summary>True si el género que se ve en pantalla viene de la vista previa de una forma.</summary>
    private bool _formChangedGender;

    /// <summary>Muestra en el slot (1-4) el movimiento que tiene <paramref name="source"/> en ese slot
    /// (mismo LoadMove que la carga inicial).</summary>
    private void ShowMoveInSlot(int slot, PKM source, GameStrings strings)
    {
        var id  = slot switch { 1 => source.Move1, 2 => source.Move2, 3 => source.Move3, _ => source.Move4 };
        var pp  = slot switch { 1 => source.Move1_PP, 2 => source.Move2_PP, 3 => source.Move3_PP, _ => source.Move4_PP };
        var ups = slot switch { 1 => source.Move1_PPUps, 2 => source.Move2_PPUps, 3 => source.Move3_PPUps, _ => source.Move4_PPUps };
        LoadMove(id, pp, ups, strings, out var name, out var type, out var cls, out var power, out var acc,
                 out var basePP, out var curPP, out var ppUps, out var effect);
        switch (slot)
        {
            case 1: Move1Id = id; Move1 = name; Move1Type = type; Move1Class = cls; Move1Power = power; Move1Accuracy = acc; Move1BasePP = basePP; _move1PPUps = Clamp(ppUps, 0, Move1CanPPUp ? 3 : 0); Move1PP = curPP; Move1Effect = effect; OnPropertyChanged(nameof(Move1PPUps)); break;
            case 2: Move2Id = id; Move2 = name; Move2Type = type; Move2Class = cls; Move2Power = power; Move2Accuracy = acc; Move2BasePP = basePP; _move2PPUps = Clamp(ppUps, 0, Move2CanPPUp ? 3 : 0); Move2PP = curPP; Move2Effect = effect; OnPropertyChanged(nameof(Move2PPUps)); break;
            case 3: Move3Id = id; Move3 = name; Move3Type = type; Move3Class = cls; Move3Power = power; Move3Accuracy = acc; Move3BasePP = basePP; _move3PPUps = Clamp(ppUps, 0, Move3CanPPUp ? 3 : 0); Move3PP = curPP; Move3Effect = effect; OnPropertyChanged(nameof(Move3PPUps)); break;
            default: Move4Id = id; Move4 = name; Move4Type = type; Move4Class = cls; Move4Power = power; Move4Accuracy = acc; Move4BasePP = basePP; _move4PPUps = Clamp(ppUps, 0, Move4CanPPUp ? 3 : 0); Move4PP = curPP; Move4Effect = effect; OnPropertyChanged(nameof(Move4PPUps)); break;
        }
    }

    /// <summary>Recalcula lo que depende de la FORMA elegida en el combo (sprite, tipos,
    /// debilidades, stats base/radar y lista de habilidades) sin tocar el PKM: la forma recién se
    /// aplica al PKM al exportar (<see cref="PokemonService.ApplyForm"/>). Corre con _isLoading en
    /// true a propósito: estos son valores DERIVADOS, no ediciones del usuario — así no se
    /// registran como cambios propios ni pisan el valor original de la sesión. La habilidad se
    /// mantiene en el mismo "slot" (igual que hace ApplyForm), que es lo que va a pasar al exportar.</summary>
    private void RefreshFormDerivedData()
    {
        if (_currentPkm == null || Capabilities == null || !_hasForm || string.IsNullOrEmpty(_form)) return;
        if (!PokemonService.TryGetFormIndex(_currentPkm, _form, out var form)) return;

        var pkm = _currentPkm;
        var strings = GameInfo.GetStrings(GetPKHeXLang(_loc.CurrentLocale));
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            Sprite = SpriteService.Instance.GetPokemonSprite(pkm.Species, pkm.IsShiny, pkm.Gender == 1, form);

            var entry = SpeciesDatabase.Instance.GetByForm(pkm.Species, form);
            if (entry != null && entry.Types.Count > 0)
            {
                Type1    = Capitalize(entry.Types[0]);
                HasType2 = entry.Types.Count > 1;
                Type2    = HasType2 ? Capitalize(entry.Types[1]) : string.Empty;

                TypeWeaknesses.Clear();
                var t1 = entry.Types[0].ToLowerInvariant();
                var t2 = entry.Types.Count > 1 ? entry.Types[1].ToLowerInvariant() : string.Empty;
                foreach (var m in TypeEffectivenessDatabase.Instance.GetMatchups(t1, t2))
                    TypeWeaknesses.Add(m);
            }

            if (entry != null)
            {
                BaseHp  = entry.BaseStats.Hp;  BaseAtk = entry.BaseStats.Atk;
                BaseDef = entry.BaseStats.Def; BaseSpa = entry.BaseStats.Spa;
                BaseSpd = entry.BaseStats.Spd;
                BaseSpe = entry.BaseStats.Spe;
                BaseTotal = entry.BaseStatTotal;
                ApplyGen1Special(entry);
                RecalcStats();
                foreach (var p in new[] { nameof(RadarBaseHp), nameof(RadarBaseAtk), nameof(RadarBaseDef),
                                           nameof(RadarBaseSpa), nameof(RadarBaseSpd), nameof(RadarBaseSpe) })
                    OnPropertyChanged(p);
            }

            // Habilidades de la nueva forma; se conserva la posición de la elegida (0, 1, oculta).
            var oldAbilities = AvailableAbilities;
            var newAbilities = LoadAbilitiesForSpecies(
                SpeciesFormResolver.ResolveDbSpeciesId(pkm.Species, form), strings, Capabilities);
            if (newAbilities.Count > 0 && !newAbilities.SequenceEqual(oldAbilities))
            {
                var slot = Math.Max(0, oldAbilities.IndexOf(Ability));
                AvailableAbilities = newAbilities;
                Ability = newAbilities.Contains(Ability) ? Ability : newAbilities[Math.Min(slot, newAbilities.Count - 1)];
            }

            // Efectos de la forma sobre objeto, habilidad y género (Giratina Origen -> Orbe Griseo,
            // Arceus -> placa, Meowstic -> género...): se calculan aplicando la forma a una COPIA del
            // PKM con el mismo código que usa la exportación (PokemonService.ApplyForm), así lo que
            // se ve acá es exactamente lo que va a quedar al exportar.
            try
            {
                var preview = pkm.Clone();
                PokemonService.ApplyForm(preview, form);
                if (Capabilities.HasHeldItems)
                    HeldItem = preview.HeldItem > 0 && preview.HeldItem < strings.Item.Count
                        ? strings.Item[preview.HeldItem] : string.Empty;
                if (Capabilities.HasAbilities && preview.Ability > 0 && preview.Ability < strings.Ability.Count
                    && AvailableAbilities.Contains(strings.Ability[preview.Ability]))
                    Ability = strings.Ability[preview.Ability];
                // Género: en Meowstic/Indeedee/Basculegion/Oinkologne la forma ES el género, y cada forma
                // tiene su propia proporción de género — la lista de opciones tiene que acompañarla
                // (mismo criterio que la carga inicial) o el combo queda en blanco.
                if (Capabilities.HasGender)
                {
                    var pi = preview.PersonalInfo;
                    List<string> genders = pi.Genderless ? ["—"]
                        : pi.IsDualGender ? ["♂", "♀"]
                        : preview.Gender == 1 ? ["♀"] : ["♂"];
                    if (!AvailableGenders.SequenceEqual(genders)) AvailableGenders = genders;
                    var genderChangedByForm = preview.Gender != pkm.Gender;
                    if (genderChangedByForm || !pi.IsDualGender || _formChangedGender)
                    {
                        Gender = preview.Gender switch { 0 => "♂", 1 => "♀", _ => "—" };
                        Sprite = SpriteService.Instance.GetPokemonSprite(pkm.Species, pkm.IsShiny, preview.Gender == 1, form);
                    }
                    _formChangedGender = genderChangedByForm;
                }
                // Tipo Tera atado a la máscara de Ogerpon.
                if (Capabilities.HasTerastal && preview is PK9 preview9)
                    TeraType = TeraName(strings, (byte)preview9.TeraType);
                // Movimientos que la forma agrega (Keldeo Resolute -> Espada Sagrada): solo se tocan
                // los slots que ApplyForm cambió, así no se pisan ediciones de movimientos del usuario;
                // al volver a la forma original se restaura el movimiento del PKM.
                var before = new[] { pkm.Move1, pkm.Move2, pkm.Move3, pkm.Move4 };
                var after  = new[] { preview.Move1, preview.Move2, preview.Move3, preview.Move4 };
                for (var i = 0; i < 4; i++)
                {
                    if (after[i] != before[i]) { ShowMoveInSlot(i + 1, preview, strings); _formMoveSlots.Add(i + 1); }
                    else if (_formMoveSlots.Remove(i + 1)) ShowMoveInSlot(i + 1, pkm, strings);
                }
                RecalcMoveCreateSlots();
            }
            catch (Exception) { /* la vista previa es solo informativa: al exportar se aplica igual */ }
        }
        finally { _isLoading = wasLoading; }
    }

    /// <summary>Gen 1: Ataque Esp. y Defensa Esp. son UN solo stat (Especial) — el radar y las barras deben
    /// mostrar el mismo valor en ambos ejes. Se usa el Especial real de Gen 1 de la DB (columna
    /// BaseSpecialGen1), NO el Ataque Esp. de Gen 2+ (que difiere: Charizard 85 vs 109). Si la especie no
    /// tiene el dato (DB sin el hotfix), se conserva el comportamiento anterior: Spd espeja a Spa.</summary>
    private void ApplyGen1Special(SpeciesEntry entry)
    {
        if (!IsGen1) return;
        if (entry.BaseStats.Gen1Special is { } special)
        {
            BaseSpa = BaseSpd = special;
            BaseTotal = entry.BaseStatTotalGen1;
        }
        else BaseSpd = BaseSpa;
    }

    private void LoadExtraData(PKM pkm, GameStrings strings)
    {
        var speciesEntry = SpeciesDatabase.Instance.GetByForm(pkm.Species, pkm.Form);
        if (speciesEntry != null)
        {
            BaseHp    = speciesEntry.BaseStats.Hp;  BaseAtk = speciesEntry.BaseStats.Atk;
            BaseDef   = speciesEntry.BaseStats.Def; BaseSpa = speciesEntry.BaseStats.Spa;
            BaseSpd   = speciesEntry.BaseStats.Spd; BaseSpe = speciesEntry.BaseStats.Spe;
            BaseTotal = speciesEntry.BaseStatTotal;
            ApplyGen1Special(speciesEntry);
            // Notificar RadarBase* (constantes por especie, no dependen de IV/EV)
            foreach (var p in new[] { nameof(RadarBaseHp), nameof(RadarBaseAtk), nameof(RadarBaseDef),
                                       nameof(RadarBaseSpa), nameof(RadarBaseSpd), nameof(RadarBaseSpe) })
                OnPropertyChanged(p);
            HasBaseStats = true;
            // Esta primera pasada corre ANTES de RestorePendingEdits (ver LoadPokemon): lo que
            // calcula con los valores del PKM real es el "original" contra el que se mide el delta.
            _captureOriginal = true;
            RecalcStats();
            _captureOriginal = false;
        }
        else HasBaseStats = false;

        // Learnsets — usa Capabilities (propiedad del ViewModel, ya asignada en LoadPokemon)
        if (Capabilities != null)
            LoadLearnsets(pkm, Capabilities);

        // Sets recomendados — agrupar por (Source, Generation) para la vista.
        // Filtrado a la generación del juego actual: antes traía todas las generaciones
        // donde exista un set para esta especie (ej. mostraba sets de Gen 9 en un save de
        // Gen 3), ahora solo los de Capabilities.Generation.
        RecommendedSets.Clear();
        RecommendedSetGroups.Clear();
        var sets = MovesetDatabase.Instance.GetSets(RegionalFormCatalog.ShowdownName(
                // Los sets están indexados por nombre inglés (Showdown): con los datos en otro idioma
                // strings.Species ya no coincide y la pestaña quedaba vacía.
                GameInfo.GetStrings(DataLanguageService.Fallback).Species[pkm.Species], pkm.Species, pkm.Form))
            .Where(s => Capabilities == null || s.Generation == Capabilities.Generation)
            .ToList();
        foreach (var s in sets) RecommendedSets.Add(s);
        var groups = sets
            .GroupBy(s => (s.Source, s.Generation))
            .OrderBy(g => g.Key.Source)
            .ThenBy(g => g.Key.Generation)
            .Select(g => new RecommendedSetGroup
            {
                Source     = g.Key.Source,
                Generation = g.Key.Generation,
                Sets       = g.ToList(),
            });
        foreach (var g in groups) RecommendedSetGroups.Add(g);
        TabSets = RecommendedSetGroups.Count > 0;
    }

    // ── Item / Ball lists ────────────────────────────────────────────────────

    // ── Move selector ────────────────────────────────────────────────────

    /// <summary>
    /// Abre el panel de selección para el slot indicado (1-4).
    /// La lista proviene del Learnset ya cargado — sin nueva query.
    /// </summary>
    public void OpenMoveSelector(int slot)
    {
        var currentMove = slot switch
        {
            1 => Move1, 2 => Move2, 3 => Move3, 4 => Move4, _ => string.Empty
        };
        var currentMoveId = slot switch { 1 => Move1Id, 2 => Move2Id, 3 => Move3Id, 4 => Move4Id, _ => 0 };
        MoveSelector = new MoveSelectorViewModel(slot, currentMove, currentMoveId, Learnsets);
    }

    /// <summary>
    /// Aplica el movimiento seleccionado en el panel al slot correspondiente.
    /// </summary>
    public void ApplyMoveSelection()
    {
        if (MoveSelector?.Selected == null) return;

        var sel   = MoveSelector.Selected;
        var type  = sel.TypeDisplay;
        var cls   = sel.DamageClass;
        var power = sel.Power;
        var db    = sel.MoveId > 0 ? MoveDatabase.Instance.Get(sel.MoveId) : MoveDatabase.Instance.GetByName(sel.MoveName);
        int moveId = sel.MoveId > 0 ? sel.MoveId : db?.Id ?? 0;
        // Nombre en el idioma de los datos (lo que muestra el resto del editor); la edición viaja por id.
        var name  = LocalizedMoveName(moveId) ?? sel.MoveName;
        int? acc  = db?.Accuracy;
        int basePP = db?.Pp ?? 0;

        var effect = db?.EffectEntries ?? string.Empty;

        switch (MoveSelector.SlotIndex)
        {
            case 1:
                Move1Id = moveId;
                Move1 = name; Move1Type = type; Move1Class = cls; Move1Power = power; Move1Accuracy = acc; Move1BasePP = basePP; Move1PP = basePP; Move1Effect = effect;
                Move1PPUps = _move1PPUps; // reclampea (ej. a 0 si el nuevo move es Sketch/Revival Blessing)
                break;
            case 2:
                Move2Id = moveId;
                Move2 = name; Move2Type = type; Move2Class = cls; Move2Power = power; Move2Accuracy = acc; Move2BasePP = basePP; Move2PP = basePP; Move2Effect = effect;
                Move2PPUps = _move2PPUps;
                break;
            case 3:
                Move3Id = moveId;
                Move3 = name; Move3Type = type; Move3Class = cls; Move3Power = power; Move3Accuracy = acc; Move3BasePP = basePP; Move3PP = basePP; Move3Effect = effect;
                Move3PPUps = _move3PPUps;
                break;
            case 4:
                Move4Id = moveId;
                Move4 = name; Move4Type = type; Move4Class = cls; Move4Power = power; Move4Accuracy = acc; Move4BasePP = basePP; Move4PP = basePP; Move4Effect = effect;
                Move4PPUps = _move4PPUps;
                break;
        }
        MoveSelector = null;
        LoadZCrystalOptions(); // los tipos de movimiento pudieron cambiar
        RecalcZMove(); // el movimiento cambiado puede habilitar/deshabilitar el Z-Move actual
        RecalcMoveCreateSlots(); // el "+" tiene que correrse al siguiente slot vacío
    }

    public void CancelMoveSelection() => MoveSelector = null;

    /// <summary>Nombre del movimiento en el idioma de los datos activo, o null si PKHeX no lo tiene.</summary>
    private static string? LocalizedMoveName(int moveId)
    {
        var list = DataLanguageService.Instance.Strings.Move;
        return moveId > 0 && moveId < list.Count && !string.IsNullOrEmpty(list[moveId]) ? list[moveId] : null;
    }

    /// <summary>Tras restaurar ediciones pendientes (que guardan el nombre en el idioma en que se
    /// editó) vuelve a derivar los nombres de los slots desde sus ids, en el idioma actual de los datos.</summary>
    private void ReapplyMoveNamesFromIds()
    {
        if (LocalizedMoveName(Move1Id) is { } n1) Move1 = n1;
        if (LocalizedMoveName(Move2Id) is { } n2) Move2 = n2;
        if (LocalizedMoveName(Move3Id) is { } n3) Move3 = n3;
        if (LocalizedMoveName(Move4Id) is { } n4) Move4 = n4;
    }

    /// <summary>
    /// Devuelve los nombres display de las habilidades posibles para una especie,
    /// excluyendo la hidden ability si la generación actual no la soporta (Gen &lt; 5).
    /// </summary>
    private static List<string> LoadAbilitiesForSpecies(
        int speciesId, GameStrings strings, SaveCapabilities capabilities)
    {
        var entries = PokemonDatabase.Instance.GetAbilitiesBySpecies(speciesId);
        bool allowHidden = capabilities.Generation >= 5;

        var result = new List<string>();
        foreach (var (slug, isHidden) in entries)
        {
            if (isHidden && !allowHidden) continue;
            var ability = PokemonDatabase.Instance.GetAbilityByName(slug);
            if (ability == null) continue;
            // strings.Ability está indexado por AbilityId de PKHeX — usamos el nombre
            // de la DB convertido a Title Case como fallback si el índice no calza.
            var display = ability.Id < strings.Ability.Count && ability.Id > 0
                ? strings.Ability[ability.Id]
                : ToTitleCase(slug);
            if (!string.IsNullOrWhiteSpace(display) && !result.Contains(display))
                result.Add(display);
        }
        return result;
    }

    private static string ToTitleCase(string slug) =>
        string.Join(' ', slug.Split('-').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));

    /// <summary>Nombre de ítem para mostrar en los pickers de Mega/Z-Move, leído 100% de pokemon.db
    /// (no de PKHeX.Core) — evita sufijos de desambiguación como "(2)" que PKHeX agrega en su propia
    /// tabla de strings. "pikanium-z--held" → "Pikanium Z"; "charizardite-x" → "Charizardite X".</summary>
    private static string FormatDbItemName(string slug) => ToTitleCase(slug.Replace("--held", string.Empty));

    /// <summary>Carga las cintas aplicables a esta generación desde el PKM (vía RibbonReader),
    /// como estado inicial del ViewModel — no se re-lee automáticamente después (el usuario
    /// puede tildar/destildar libremente, igual que el resto del editor en esta fase).</summary>
    private void LoadRibbons(PKM pkm, int generation)
    {
        var items = new List<RibbonItemViewModel>();
        foreach (var def in RibbonDatabase.AllRibbons)
        {
            if (def.MinGeneration > generation) continue;
            var value = RibbonReader.TryGet(pkm, def.Index);
            if (value is null) continue; // este formato de PKM no soporta esta cinta puntual
            items.Add(new RibbonItemViewModel
            {
                Key = RibbonDatabase.KeyOf(def.Index),
                DisplayName = RibbonDatabase.GetDisplayName(def),
                Category = def.Category.ToString(),
                IsObtained = value.Value,
            });
        }
        _allRibbons = items;
        foreach (var item in items)
            item.OnUserToggle = OnRibbonToggled;

        var categoryKeys = new List<string> { "Todas" }
            .Concat(items.Select(i => i.Category).Distinct()
                .OrderBy(c =>
                {
                    var idx = Array.IndexOf(RibbonGroupViewModel.CategoryOrder, c);
                    return idx >= 0 ? idx : int.MaxValue;
                }));
        RibbonCategoryChips = categoryKeys
            .Select(c => new RibbonChipViewModel
            {
                Category    = c,
                DisplayName = RibbonCategoryColorPalette.LocalizedName(c),
                IsSelected  = c == "Todas",
            })
            .ToList();
        SelectedRibbonCategory = "Todas"; // dispara sync de IsSelected + RecalcRibbonGroups()
    }

    /// <summary>Se llama cuando el usuario tilda/destilda una cinta puntual (ver
    /// RibbonItemViewModel.OnUserToggle). Mismo criterio que Set&lt;T&gt;: solo se guarda si no
    /// estamos en medio de una carga.</summary>
    private void OnRibbonToggled(string ribbonKey, bool value)
    {
        // Conteo/barras en vivo (antes el "n/m" del encabezado quedaba viejo hasta cambiar el filtro).
        // Solo notificaciones de UI — no pasan por Set<T>, así que no se registran como ediciones.
        if (!_isLoading)
        {
            foreach (var g in _ribbonGroups) g.Refresh();
            UpdateRibbonSummary();
        }

        if (_isLoading || _currentSlotKey is not { } key) return;
        _editSession.RecordEdit(key, "Ribbon:" + ribbonKey, value);
    }

    /// <summary>Reaplica cintas editadas previamente en esta sesión para este slot (ver
    /// RestorePendingEdits). Se llama todavía con _isLoading en true.</summary>
    private void RestorePendingRibbonEdits(PokemonSlotKey slotKey)
    {
        var edits = _editSession.GetRawEdits(slotKey);
        if (edits == null || edits.Count == 0) return;

        var byKey = _allRibbons.ToDictionary(r => r.Key);
        foreach (var (key, value) in edits)
        {
            if (!key.StartsWith("Ribbon:")) continue;
            var ribbonKey = key["Ribbon:".Length..];
            if (byKey.TryGetValue(ribbonKey, out var item) && value is bool b)
                item.IsObtained = b;
        }
        RecalcRibbonGroups();
    }

    private void RecalcRibbonGroups()
    {
        IEnumerable<RibbonItemViewModel> filtered = _allRibbons;

        if (!string.IsNullOrWhiteSpace(_ribbonSearchText))
            filtered = filtered.Where(i => i.DisplayName.Contains(_ribbonSearchText, StringComparison.OrdinalIgnoreCase));

        if (_selectedRibbonCategory != "Todas")
            filtered = filtered.Where(i => i.Category == _selectedRibbonCategory);

        var groups = filtered
            .GroupBy(i => i.Category)
            .Select(g => new
            {
                Category = g.Key,
                // Alfabético dentro de la tarjeta — antes salían en el orden crudo de
                // RibbonDatabase.Entries, que no sigue ningún criterio visible para el usuario.
                Items = g.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
            })
            .OrderBy(g =>
            {
                var idx = Array.IndexOf(RibbonGroupViewModel.CategoryOrder, g.Category);
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ToList();

        // Si el filtro dejó una sola tarjeta (categoría específica seleccionada, no "Todas"),
        // se marca IsExpanded para que la vista la muestre más grande/espaciada.
        var isSingle  = groups.Count == 1;
        // Plegar solo tiene sentido en la vista "Todas" y sin búsqueda; con un filtro o texto,
        // las tarjetas se muestran siempre desplegadas.
        var canToggle = _selectedRibbonCategory == "Todas" && string.IsNullOrWhiteSpace(_ribbonSearchText);
        RibbonGroups = groups
            .Select(g => new RibbonGroupViewModel
            {
                Category       = g.Category,
                Items          = g.Items,
                IsExpanded     = isSingle,
                IsToggleable   = canToggle,
                StartCollapsed = canToggle && _ribbonCollapsed.GetValueOrDefault(g.Category),
                OnCollapsedChanged = (cat, collapsed) => _ribbonCollapsed[cat] = collapsed,
            })
            .ToList();

        UpdateRibbonSummary();
    }

    /// <summary>Texto "N de M obtenidas" + barra de total. Escribe el campo directo (no Set&lt;T&gt;):
    /// es un derivado de UI, no un dato del Pokémon, y no debe registrarse como edición.</summary>
    private void UpdateRibbonSummary()
    {
        var obtained = _allRibbons.Count(i => i.IsObtained);
        _ribbonSummary = string.Format(_loc["Ribbons_Summary"], obtained, _allRibbons.Count);
        OnPropertyChanged(nameof(RibbonSummary));
        OnPropertyChanged(nameof(RibbonProgressFill));
        OnPropertyChanged(nameof(RibbonProgressRest));
    }

    private void LoadItemLists(GameStrings strings, SaveCapabilities capabilities)
    {
        // Items válidos: solo hasta MaxItemID del save actual (PKHeX SaveFile.MaxItemID).
        // Esto excluye ítems de generaciones posteriores que no existen en este juego.
        int maxItem = Math.Min(capabilities.MaxItemID, strings.Item.Count - 1);
        var itemNameToId = new Dictionary<string, int>();
        var itemIdToName = new Dictionary<int, string>();
        var names = new List<string>();

        // Mega Stones y Z-Crystals se seleccionan EXCLUSIVAMENTE desde los pickers del tab
        // Special, nunca desde este combo general — evita duplicar el punto de entrada y la
        // desincronización que eso generaba entre ambos paneles.
        // Solo se ocultan en los juegos que realmente tienen la mecánica: en otras generaciones esos ids
        // son objetos normales distintos.
        _megaItemIds = capabilities.HasMega ? new HashSet<int>(MegaStoneDatabase.AllItemIds()) : new HashSet<int>();
        _zItemIds = capabilities.HasZMoves ? new HashSet<int>(ZCrystalDatabase.AllItemIds()) : new HashSet<int>();
        var specialItemIds = new HashSet<int>(_megaItemIds.Concat(_zItemIds));
        // Las versiones "objeto clave" de los Z-Crystals (mochila, nombre con sufijo " [Z]") tampoco son
        // equipables: equiparlas vuelve ilegal al Pokémon, así que se ocultan junto con las equipables.
        if (capabilities.HasZMoves)
            foreach (var keyId in ItemStorage7USUM.ZCrystalKey) specialItemIds.Add(keyId);

        for (int i = 1; i <= maxItem; i++)
        {
            var name = strings.Item[i];
            if (!string.IsNullOrWhiteSpace(name))
            {
                itemNameToId[name] = i; // último gana si hay nombres duplicados (raro, pero no crítico)
                itemIdToName[i] = name;
                if (!specialItemIds.Contains(i)) names.Add(name);
            }
        }
        names.Sort(StringComparer.CurrentCultureIgnoreCase); // el combo de Held Item ordenado A-Z
        var items = new List<string> { string.Empty };
        items.AddRange(names);
        AvailableItems = items;
        _itemNameToId = itemNameToId;
        _itemIdToName = itemIdToName;
        RecalcEquippedSpecialItem(); // HeldItem puede haberse cargado antes que estos diccionarios

        LoadMegaStoneOptions(capabilities);

        // Balls válidas: filtrar por categoría Y por el tope de ID correspondiente.
        //
        // IMPORTANTE: en Gen 1 y Gen 2, PKHeX.Core no define MaxBallID por generación
        // (solo existe MaxBallID_3 en adelante) — SaveFile.MaxBallID devuelve 0 para
        // esas generaciones, ya que las Poké Balls viven dentro del mismo rango general
        // de ítems (no son una lista separada). Usar MaxBallID=0 como tope deja la
        // lista de balls vacía en Gen 1/2. Para esas generaciones usamos MaxItemID.
        var ballCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "standard-balls", "special-balls", "apricorn-balls" };
        var ballItems = PokemonDatabase.Instance.GetItemsByCategory(ballCategories);
        // BUG encontrado: acá se guardaba b.Name tal cual ("Poké Ball", nombre de display de
        // nuestra tabla Items) y más abajo se comparaba contra `slug` (versión en minúsculas
        // con guiones de strings.Item[i], ej. "poké-ball") — nunca podían matchear, dejando
        // AvailableBalls con un solo elemento (el placeholder vacío) siempre. Slugificamos acá
        // con el mismo criterio que el loop para que ambos lados comparen el mismo formato.
        var ballSlugs = new HashSet<string>(
            ballItems.Select(b => b.Name.ToLowerInvariant().Replace(" ", "-")),
            StringComparer.OrdinalIgnoreCase);

        int ballTopId = capabilities.Generation <= 2
            ? capabilities.MaxItemID
            : capabilities.MaxBallID;
        int maxBall = Math.Min(ballTopId, strings.Item.Count - 1);
        var enItemNames = GameInfo.GetStrings("en").Item;
        var balls = new List<string> { string.Empty };
        for (int i = 1; i <= maxBall; i++)
        {
            var name = strings.Item[i];
            if (string.IsNullOrWhiteSpace(name)) continue;
            // El filtro compara contra nombres en inglés de pokemon.db: se usa el nombre inglés del MISMO
            // índice (las listas de PKHeX están alineadas entre idiomas) y se muestra el de la UI. Antes se
            // comparaba el nombre localizado y con la UI en otro idioma la lista de balls quedaba casi vacía.
            var enName = i < enItemNames.Count && !string.IsNullOrWhiteSpace(enItemNames[i]) ? enItemNames[i] : name;
            var slug = enName.ToLowerInvariant().Replace(" ", "-");
            if (ballSlugs.Contains(slug))
                balls.Add(name);
        }
        AvailableBalls = balls;
    }

    // ── EXP calculation ──────────────────────────────────────────────────────

    /// <summary>
    /// Recalcula _experience desde _level usando la curva de crecimiento del Pokémon.
    /// PKHeX EXPGrowth: 0=MediumFast, 1=Erratic, 2=Fluctuating, 3=MediumSlow, 4=Fast, 5=Slow
    /// </summary>
    // ── Ability / Item description recalculation ────────────────────────────

    /// <summary>Recalcula AbilityDescription cada vez que cambia la habilidad seleccionada.</summary>
    private void RecalcAbilityDescription()
    {
        if (string.IsNullOrWhiteSpace(_ability))
        { _abilityDesc = string.Empty; OnPropertyChanged(nameof(AbilityDescription)); return; }

        var slug = _ability.ToLowerInvariant().Replace(" ", "-");
        var entry = (_abilityId > 0 ? AbilityDatabase.Instance.GetById(_abilityId) : null)
                    ?? AbilityDatabase.Instance.GetByName(slug);
        _abilityDesc = entry?.Description ?? string.Empty;
        OnPropertyChanged(nameof(AbilityDescription));
    }

    /// <summary>Recalcula ItemDescription cada vez que cambia el ítem seleccionado.</summary>
    private void RecalcItemDescription()
    {
        if (string.IsNullOrWhiteSpace(_heldItem))
        { _itemDesc = string.Empty; OnPropertyChanged(nameof(ItemDescription)); return; }

        var slug = _heldItem.ToLowerInvariant().Replace(" ", "-");
        // El id sale del nombre actual (no de _heldItemId): al equipar desde el tab Special el setter de HeldItem
        // corre este método antes de que el selector fije HeldItemId.
        var descItemId = ResolveItemId(_heldItem);
        var entry = (descItemId > 0 ? ItemDatabase.Instance.GetById(descItemId) : null)
                    ?? ItemDatabase.Instance.GetByName(slug);
        // Idioma de los datos (pokemon.db solo trae inglés): mismo texto que la Mochila.
        _itemDesc = entry == null
            ? string.Empty
            : ItemDescriptionLocalizer.Get(entry.Id, entry.Description, entry.Category == "all-machines");
        OnPropertyChanged(nameof(ItemDescription));
    }

    private void RecalcMega()
    {
        MegaCompatible = false;
        MegaFormName = string.Empty;
        MegaSummary = string.Empty;
        MegaType1 = string.Empty;
        MegaType2 = string.Empty;

        if (_currentPkm == null || !TabMega) return;
        if (string.IsNullOrWhiteSpace(_heldItem)) return;
        if (!_itemNameToId.TryGetValue(_heldItem, out var itemId)) return;
        if (!MegaStoneDatabase.TryGet(itemId, out var stone)) return;
        if (stone.BaseSpeciesId != _currentPkm.Species) return;

        var megaSpecies = SpeciesDatabase.Instance.GetById(stone.MegaSpeciesId);
        if (megaSpecies == null) return;

        MegaCompatible = true;
        MegaFormName   = FormatMegaName(stone.MegaSlug, stone.BaseSpeciesId);
        MegaSummary    = string.Format(_loc["Special_MegaSummary"], string.Join("/", megaSpecies.Types.Select(TypeNames.Localize)), megaSpecies.BaseStatTotal);
        MegaType1 = megaSpecies.Types.ElementAtOrDefault(0) ?? string.Empty;
        MegaType2 = megaSpecies.Types.ElementAtOrDefault(1) ?? string.Empty;
    }

    /// <summary>"charizard-mega-x" → "Mega Charizard X" (en) / "Mega Charizard X" (es): la especie sale de
    /// PKHeX en el idioma de los datos y el "Mega {0}" de la plantilla <c>Form_Name_Mega</c> (idioma de la
    /// interfaz; sin clave en ese idioma cae a inglés). Sufijo X/Y desde el slug.</summary>
    private string FormatMegaName(string megaSlug, int baseSpeciesId)
    {
        var parts = megaSlug.Split('-'); // ej. ["charizard","mega","x"]
        var strings = DataLanguageService.Instance.Strings;
        var species = baseSpeciesId > 0 && baseSpeciesId < strings.Species.Count && !string.IsNullOrEmpty(strings.Species[baseSpeciesId])
            ? strings.Species[baseSpeciesId] : ToTitleCase(parts[0]);
        var suffix = parts.Length > 2 ? " " + parts[2].ToUpperInvariant() : string.Empty; // "x" / "y"
        var template = _loc["Form_Name_Mega"];
        if (template == "Form_Name_Mega") template = "Mega {0}"; // el indexador devuelve la clave si falta
        return string.Format(template, species) + suffix;
    }

    /// <summary>Arma la lista corta de Mega Stones compatibles con la especie actual (para el picker de la tarjeta Mega).
    /// Los nombres salen de la lista de objetos de PKHeX en el idioma de los datos (pokemon.db solo de respaldo).</summary>
    private void LoadMegaStoneOptions(SaveCapabilities capabilities)
    {
        var optionToItemId = new Dictionary<string, int>();
        var options = new List<string>();

        if (_currentPkm != null && capabilities.HasMega)
        {
            foreach (var stone in MegaStoneDatabase.GetForSpecies(_currentPkm.Species))
            {
                if (stone.ItemId > capabilities.MaxItemID) continue; // no válido en este juego puntual
                var dbEntry = ItemDatabase.Instance.GetById(stone.ItemId);
                if (dbEntry == null) continue;
                // Nombre de la lista de objetos de PKHeX (idioma de los datos, igual que los Z-Crystals);
                // pokemon.db (inglés) solo como respaldo si el juego no lo trae.
                var display = _itemIdToName.TryGetValue(stone.ItemId, out var stoneName)
                    ? stoneName : FormatDbItemName(dbEntry.Name);
                options.Add(display);
                optionToItemId[display] = stone.ItemId;
            }
        }
        _megaOptionToItemId = optionToItemId;
        MegaStoneOptions = options;
    }

    /// <summary>Arma la lista corta de Z-Crystals relevantes: por especie (exclusivos) o por tipo de sus 4 movimientos.
    /// Los nombres mostrados salen de pokemon.db (ItemDatabase), NO de PKHeX.Core.</summary>
    private void LoadZCrystalOptions()
    {
        var optionToItemId = new Dictionary<string, int>();
        var options = new List<string>();

        if (_currentPkm != null && TabZMoves)
        {
            var moveTypes = new[] { Move1Type, Move2Type, Move3Type, Move4Type }
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t.ToLowerInvariant())
                .ToHashSet();

            void AddOption(int itemId, string? requiredMove = null)
            {
                // Los ids de ZCrystalDatabase son los del objeto equipado de PKHeX: el nombre sale de la lista de
                // objetos del juego (localizado). Si el juego no tiene el objeto (ej. cristales de USUM en
                // Sun/Moon), no se ofrece.
                if (!_itemIdToName.TryGetValue(itemId, out var pkName)) return;
                // Los exclusivos solo sirven con un movimiento puntual: se muestra en el picker para que el
                // usuario sepa por qué el diagnóstico exige ese movimiento (ej. Snorlium Z → Giga Impact).
                var display = pkName + (requiredMove != null ? string.Format(_loc["Special_Requires"], MoveNames.Localize(requiredMove)) : string.Empty);
                options.Add(display);
                optionToItemId[display] = itemId;
            }

            foreach (var (itemId, requiredType, _) in ZCrystalDatabase.AllTypeCrystals())
            {
                if (moveTypes.Contains(requiredType.ToLowerInvariant())) AddOption(itemId);
            }
            foreach (var (itemId, requiredSpeciesId, requiredMove, _) in ZCrystalDatabase.AllSignatureCrystals())
            {
                if (requiredSpeciesId == _currentPkm.Species) AddOption(itemId, requiredMove);
            }
        }
        _zCrystalOptionToItemId = optionToItemId;
        ZCrystalOptions = options;
    }

    /// <summary>¿El movimiento del slot es el movimiento requerido? Compara por ID. El nombre del slot puede venir
    /// de dos lados: del save (strings.Move, idioma de la UI) o del selector de movimientos (nombre de pokemon.db,
    /// inglés); se resuelve con ambos. El requerido (ZCrystalDatabase) siempre está en inglés. Si algún lado no
    /// resuelve, se cae a comparar texto normalizado.</summary>
    private bool IsSameMove(string slotName, string requiredName)
    {
        if (string.IsNullOrWhiteSpace(slotName) || string.IsNullOrWhiteSpace(requiredName)) return false;
        int? slotId = _moveNameToId.TryGetValue(slotName, out var localizedId) ? localizedId : null;
        if (slotId == null) slotId = MoveDatabase.Instance.GetByName(slotName)?.Id;
        int? requiredId = MoveNames.IdFromEnglish(requiredName) is var rid and > 0 ? rid : MoveDatabase.Instance.GetByName(requiredName)?.Id;
        if (slotId != null && requiredId != null) return slotId == requiredId;
        static string Norm(string v) => new string(v.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return Norm(slotName) == Norm(requiredName);
    }

    private void RecalcZMove()
    {
        RecalcZMoveCore();
        RefreshSpecialAdvisories();
    }

    private void RecalcZMoveCore()
    {
        _zAdvisory = string.Empty;
        ZMoveCompatible = false;
        ZMoveResultName = string.Empty;
        ZMoveSourceMove = string.Empty;
        ZMoveNote = string.Empty;
        ZMoveType = string.Empty;
        ZMoveStatusText = _loc["Special_Z_NoCrystal"];

        if (_currentPkm == null || !TabZMoves) return;
        if (string.IsNullOrWhiteSpace(_heldItem)) return;
        if (!_itemNameToId.TryGetValue(_heldItem, out var itemId)) return;

        // Z-Crystal exclusivo por especie (Pikanium Z, Snorlium Z, etc.): solo sirve si la especie coincide
        // Y el Pokémon conoce el movimiento puntual. PKHeX no valida esto (equipar el cristal es legal con
        // cualquier moveset), así que el único aviso es esta tarjeta.
        if (ZCrystalDatabase.TryGetSignatureCrystal(itemId, out var sig))
        {
            if (sig.RequiredSpeciesId != _currentPkm.Species)
            {
                ZMoveStatusText = _loc["Special_Z_OtherSpecies"];
                _zAdvisory = string.Format(_loc["Special_Z_OtherSpecies_Advisory"], _heldItem);
                return;
            }

            var sigSlots = new[]
            {
                (Name: Move1, Type: Move1Type), (Name: Move2, Type: Move2Type),
                (Name: Move3, Type: Move3Type), (Name: Move4, Type: Move4Type),
            };
            foreach (var slot in sigSlots)
            {
                if (!IsSameMove(slot.Name, sig.RequiredMoveName)) continue;
                ZMoveCompatible = true;
                ZMoveResultName = MoveNames.Localize(sig.ZMoveName);
                ZMoveSourceMove = slot.Name;
                ZMoveType = slot.Type;
                return;
            }

            var sigRequired = MoveNames.Localize(sig.RequiredMoveName);
            ZMoveStatusText = string.Format(_loc["Special_Z_NotKnown"], sigRequired);
            ZMoveNote = _loc["Special_Z_NotKnown_Note"];
            _zAdvisory = string.Format(_loc["Special_Z_NotKnown_Advisory"], _heldItem, sigRequired, MoveNames.Localize(sig.ZMoveName));
            return;
        }

        // Z-Crystal genérico por tipo: buscar el primer movimiento dañino compatible
        if (!ZCrystalDatabase.TryGetTypeCrystal(itemId, out var typeCrystal)) return;
        ZMoveStatusText = _loc["Special_Z_NoDamaging"];

        var slots = new[]
        {
            (Name: Move1, Type: Move1Type, Class: Move1Class),
            (Name: Move2, Type: Move2Type, Class: Move2Class),
            (Name: Move3, Type: Move3Type, Class: Move3Class),
            (Name: Move4, Type: Move4Type, Class: Move4Class),
        };

        foreach (var slot in slots)
        {
            if (string.IsNullOrEmpty(slot.Name)) continue;
            if (!string.Equals(slot.Type, typeCrystal.RequiredType, StringComparison.OrdinalIgnoreCase)) continue;

            if (string.Equals(slot.Class, "status", StringComparison.OrdinalIgnoreCase))
            {
                // Los movimientos de estado no generan un Z-Move con nombre propio;
                // solo otorgan un boost garantizado. Seguimos buscando un dañino mejor.
                ZMoveNote = string.Format(_loc["Special_Z_StatusNote"], slot.Name, TypeNames.Localize(typeCrystal.RequiredType));
                continue;
            }

            ZMoveCompatible = true;
            ZMoveResultName = MoveNames.Localize(typeCrystal.ZMoveName);
            ZMoveSourceMove = slot.Name;
            ZMoveNote = string.Empty;
            ZMoveType = typeCrystal.RequiredType; // llave inglesa: el texto se localiza al pintar (TypeNameConverter)
            return;
        }

        // Cristal por tipo equipado, pero ningún movimiento dañino de ese tipo.
        _zAdvisory = string.Format(_loc["Special_Z_NoMatch_Advisory"], _heldItem, TypeNames.Localize(typeCrystal.RequiredType));
    }

    // ── Avisos del tab Special en el tab Diagnóstico ─────────────────────
    // PKHeX da por legal tener un Z-Crystal / Mega Stone equipado aunque no sirva (el juego no lo impide),
    // así que estos avisos NO son ilegalidades: van como Severity.Fishy, solo en la lista del tab
    // Diagnóstico, sin ficha roja en la cabecera. Los calcula la UI (no vienen de PKHeX) a partir del
    // estado ya calculado de las tarjetas Mega y Z-Moves.
    private string _zAdvisory = string.Empty;
    private DiagnosticStep? _specialAdvisoryStep;

    private void RefreshSpecialAdvisories()
    {
        if (_specialAdvisoryStep != null)
        {
            DiagnosticSteps.Remove(_specialAdvisoryStep);
            _specialAdvisoryStep = null;
        }

        var messages = new List<string>();
        if (TabZMoves && _zAdvisory.Length > 0) messages.Add(_zAdvisory);
        if (TabMega && HasEquippedMegaItem && !MegaCompatible)
            messages.Add(string.Format(_loc["Special_Mega_WrongSpecies"], _equippedMegaItemName));
        if (messages.Count == 0) return;

        _specialAdvisoryStep = new DiagnosticStep
        {
            Order           = 900,
            Category        = "Objeto",
            Severity        = Severity.Fishy,
            Summary         = string.Join(" ", messages),
            TechnicalDetail = _loc["Special_TechPrefix"] + string.Join(" ", messages),
        };
        DiagnosticSteps.Add(_specialAdvisoryStep);
    }

    private void RecalcExperience()
    {
        _experience = CalcMinExp(_level, _growthRate).ToString("N0");
        OnPropertyChanged(nameof(Experience));
    }

    private static uint CalcMinExp(int level, byte growthRate)
    {
        if (level <= 1) return 0;
        int n = level;
        return growthRate switch
        {
            0 => (uint)(n * n * n),                                          // MediumFast
            1 => CalcErratic(n),                                             // Erratic
            2 => CalcFluctuating(n),                                         // Fluctuating
            3 => (uint)Math.Max(0, (int)(1.2 * n*n*n - 15*n*n + 100*n - 140)), // MediumSlow
            4 => (uint)(0.8 * n * n * n),                                   // Fast
            5 => (uint)(1.25 * n * n * n),                                  // Slow
            _ => (uint)(n * n * n),
        };
    }

    private static uint CalcErratic(int n)
    {
        if (n <= 50)  return (uint)(Math.Pow(n, 3) * (100 - n) / 50);
        if (n <= 68)  return (uint)(Math.Pow(n, 3) * (150 - n) / 100);
        if (n <= 98)  return (uint)(Math.Pow(n, 3) * ((1911 - 10.0 * n) / 3) / 500);
        return (uint)(Math.Pow(n, 3) * (160 - n) / 100);
    }

    private static uint CalcFluctuating(int n)
    {
        double p = n <= 15 ? (n + 1.0) / 3 :
                   n <= 35 ? 14.0 :
                             n / 2.0;
        return (uint)(Math.Pow(n, 3) * (1 + p / n));
    }

    private void LoadLearnsets(PKM pkm, SaveCapabilities capabilities)
    {
        Learnsets.Clear();
        var db = PokemonDatabase.Instance;
        if (!db.IsAvailable) return;

        // IMPORTANTE: para Gen1 y Gen2, pkm.Version puede devolver Unknown o un valor
        // compuesto (RBY/GSC) que no coincide con los GameVersion individuales.
        // Usamos capabilities.GameVersion que viene del SaveFile y siempre tiene el
        // juego correcto (RD/BU/YW/GD/SI/C/etc).
        int gameId = MapVersionToGameId(capabilities.GameVersion);
        if (gameId <= 0) return;

        var entries = db.GetLearnsets(pkm.Species, gameId, PokemonService.LearnsetFormFor(pkm.Species, pkm.Form));
        foreach (var e in entries)
        {
            Learnsets.Add(new LearnsetEntry
            {
                LearnMethod = e.LearnMethod,
                LearnLevel  = e.LearnLevel,
                LearnItem   = e.LearnItem,
                MoveId      = e.MoveId,
                MoveName    = LocalizedMoveName(e.MoveId) ?? CapitalizeName(e.MoveName),
                TypeName    = CapitalizeName(e.TypeName),
                DamageClass = LegacyDamageClass.Resolve(e.DamageClass, e.TypeName),
                Power       = e.Power,
            });
        }
        NotifyLearnsetGroups();
    }

    private void NotifyLearnsetGroups() => OnPropertyChanged(nameof(LearnsetGroups));

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    private static string CapitalizeName(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return slug;
        // "ancient-power" → "Ancient Power"
        return string.Join(" ", slug.Split('-')
            .Select(w => w.Length == 0 ? w : char.ToUpper(w[0]) + w[1..]));
    }

    // ── Recalcular stats totales ──────────────────────────────────────
    private void RecalcStats()
    {
        if (!_hasBaseStats || _currentPkm == null) return;
        int lv = _level > 0 ? _level : 1;

        // Fórmula PS: floor((2*base + iv + aporteEv) * lv/100) + lv + 10
        TotalHp  = (int)Math.Floor((2.0 * _baseHp  + _ivHp  + EvContribution(_evHp)) * lv / 100) + lv + 10;

        // Fórmula otros stats: floor((floor((2*base + iv + aporteEv) * lv/100) + 5) * nature)
        double natureAtk = GetNatureMultiplier(0);
        double natureDef = GetNatureMultiplier(1);
        double natureSpa = GetNatureMultiplier(2);
        double natureSpd = GetNatureMultiplier(3);
        double natureSpe = GetNatureMultiplier(4);

        TotalAtk = (int)Math.Floor(((int)Math.Floor((2.0 * _baseAtk + _ivAtk + EvContribution(_evAtk)) * lv / 100) + 5) * natureAtk);
        TotalDef = (int)Math.Floor(((int)Math.Floor((2.0 * _baseDef + _ivDef + EvContribution(_evDef)) * lv / 100) + 5) * natureDef);
        TotalSpa = (int)Math.Floor(((int)Math.Floor((2.0 * _baseSpa + _ivSpa + EvContribution(_evSpa)) * lv / 100) + 5) * natureSpa);
        // Gen 1: Especial único → Def. Esp. se calcula con los mismos datos que Atq. Esp.
        // (IvSpd/EvSpd ya se espejan en sus setters; la base se espeja acá por si el orden de carga cambia).
        TotalSpd = IsGen1
            ? TotalSpa
            : (int)Math.Floor(((int)Math.Floor((2.0 * _baseSpd + _ivSpd + EvContribution(_evSpd)) * lv / 100) + 5) * natureSpd);
        TotalSpe = (int)Math.Floor(((int)Math.Floor((2.0 * _baseSpe + _ivSpe + EvContribution(_evSpe)) * lv / 100) + 5) * natureSpe);

        // Primera pasada de LoadExtraData: lo que el PKM trae del save = "original" para el delta
        if (_captureOriginal)
        {
            _origTotalHp = TotalHp;   _origTotalAtk = TotalAtk; _origTotalDef = TotalDef;
            _origTotalSpa = TotalSpa; _origTotalSpd = TotalSpd; _origTotalSpe = TotalSpe;
        }
        RebuildRadarVertices();

        // Refrescar barras segmentadas y radar (dependen de Total* recién calculados)
        foreach (var p in new[] { nameof(BarHp), nameof(BarAtk), nameof(BarDef),
                                   nameof(BarSpa), nameof(BarSpd), nameof(BarSpe),
                                   nameof(RadarHp), nameof(RadarAtk), nameof(RadarDef),
                                   nameof(RadarSpa), nameof(RadarSpd), nameof(RadarSpe),
                                   nameof(RadarBaseHp), nameof(RadarBaseAtk), nameof(RadarBaseDef),
                                   nameof(RadarBaseSpa), nameof(RadarBaseSpd), nameof(RadarBaseSpe),
                                   nameof(RadarOrigHp), nameof(RadarOrigAtk), nameof(RadarOrigDef),
                                   nameof(RadarOrigSpa), nameof(RadarOrigSpd), nameof(RadarOrigSpe),
                                   nameof(RowBarHp), nameof(RowBarAtk), nameof(RowBarDef),
                                   nameof(RowBarSpa), nameof(RowBarSpd), nameof(RowBarSpe),
                                   nameof(NatUpAtk), nameof(NatDownAtk), nameof(NatUpDef), nameof(NatDownDef),
                                   nameof(NatUpSpa), nameof(NatDownSpa), nameof(NatUpSpd), nameof(NatDownSpd),
                                   nameof(NatUpSpe), nameof(NatDownSpe), nameof(NatureModsDisplay),
                                   nameof(EvMeterFill), nameof(EvMeterRest), nameof(EvTotalOver),
                                   nameof(EvTotal), nameof(EvTotalDisplay) })
            OnPropertyChanged(p);
    }

    /// <summary>
    /// Stat de referencia "Base" a ESTE nivel: lo que daría la especie sin invertir nada (IV 0, EV 0,
    /// naturaleza neutra), con la misma fórmula que <see cref="RecalcStats"/>. Así la barra gris y el
    /// polígono punteado del radar son comparables con el stat actual (verde); la diferencia entre
    /// ambos es justamente lo que aportan IVs, EVs y naturaleza. Depende del nivel.
    /// </summary>
    private int BaseAtLevel(int baseStat, bool isHp)
    {
        int lv   = _level > 0 ? _level : 1;
        int core = (int)Math.Floor(2.0 * baseStat * lv / 100);
        return isHp ? core + lv + 10 : core + 5;
    }

    /// <summary>Aporte del EV/Stat-Exp a la fórmula de stats. En Gen3+ es floor(EV/4)
    /// (EV 0-252). En Gen1/2 el campo es en realidad "Stat Experience" (0-65.535) y el aporte
    /// real del juego es floor(sqrt(StatExp)/4) — no es la misma escala, dividir directo por 4
    /// daría un aporte ~250x mayor al real. En el techo de cada escala ambas fórmulas
    /// coinciden: floor(sqrt(65535)/4) = 63 = floor(252/4), por eso Gen3+ pudo simplificar el
    /// sistema sin cambiar el rango efectivo de stats.</summary>
    private double EvContribution(int rawEv) =>
        UsesLegacyIVs ? Math.Floor(Math.Sqrt(rawEv) / 4.0) : Math.Floor(rawEv / 4.0);

    private void RecalcHiddenPower()
    {
        if (!_showHiddenPower || _currentPkm == null) return;
        var lang    = GetPKHeXLang(_loc.CurrentLocale);
        var strings = GameInfo.GetStrings(lang);
        var ivs     = new[] { _ivHp, _ivAtk, _ivDef, _ivSpe, _ivSpa, _ivSpd };
        HiddenPowerType = HiddenPowerTypeName(strings, ivs, _currentPkm.Context);
    }

    /// <summary>Nombre del tipo de Hidden Power. HiddenPower.GetType devuelve 0 = Fighting … 15 = Dark
    /// (sin Normal), por eso se indexa en Types con +1; Gen 1-2 usa la fórmula de DVs (Ataque/Defensa),
    /// no la de bits de IV de Gen 3+ (con DVs convertidos daba siempre el mismo tipo).</summary>
    private static string HiddenPowerTypeName(GameStrings strings, ReadOnlySpan<int> ivs, EntityContext context)
        => strings.Types[HiddenPower.GetType(ivs, context) + 1];

    private static readonly string[] NatureStatOrder = { "Atk", "Def", "SpA", "SpD", "Spe" };

    private double GetNatureMultiplier(int statIndex)
    {
        if (!ShowNatures || _selectedNature is not { IsNeutral: false } selected) return 1.0;
        var stat = NatureStatOrder[statIndex];
        if (string.Equals(selected.Increased, stat, StringComparison.OrdinalIgnoreCase)) return 1.1;
        if (string.Equals(selected.Decreased, stat, StringComparison.OrdinalIgnoreCase)) return 0.9;
        return 1.0;
    }

    // ── Carga de movimiento ───────────────────────────────────────────
    private static void LoadMove(int moveId, int currentPP, int ppUps, GameStrings strings,
        out string name, out string type, out string damageClass,
        out int? power, out int? accuracy, out int basePP, out int pp, out int ups, out string effect)
    {
        if (moveId <= 0)
        {
            name = type = damageClass = effect = string.Empty;
            power = accuracy = null;
            basePP = pp = ups = 0;
            return;
        }

        name = strings.Move[moveId];
        ups  = ppUps;

        var entry = MoveDatabase.Instance.Get(moveId);
        if (entry != null)
        {
            type        = entry.Type;
            damageClass = LegacyDamageClass.Resolve(entry.DamageClass, entry.Type);
            power       = entry.Power;
            accuracy    = entry.Accuracy;
            basePP      = entry.Pp ?? 0;
            effect      = entry.EffectEntries;
        }
        else
        {
            type = MoveDatabase.Instance.GetType(moveId);
            damageClass = string.Empty;
            power = accuracy = null;
            basePP = 0;
            effect = string.Empty;
        }

        pp = currentPP;
    }

    // ── Helpers ───────────────────────────────────────────────────────
    private static int CalcPP(int basePP, int ppUps) =>
        basePP + (basePP / 5) * ppUps;

    private static int Clamp(int v, int min, int max) =>
        v < min ? min : v > max ? max : v;

    /// <summary>
    /// Mapea PKHeX GameVersion (int) al GameId de nuestra DB.
    ///
    /// GameIds actuales en pokemon.db (tabla Games):
    ///   1  = black2white2         2  = blackwhite
    ///   3  = brilliantdiamondshiningpearl   4  = crystal
    ///   5  = diamondpearl         6  = emerald
    ///   7  = fireredleafgreen     8  = galeofdarkness
    ///   9  = goldsilver           10 = heartgoldsoulsilver
    ///   11 = legendsarceus        12 = legendsza
    ///   13 = letsgopikachueevee   14 = omegarubyalphasapphire
    ///   15 = platinum             16 = redgreen
    ///   17 = rubysapphire         18 = scarletviolet
    ///   19 = sunmoon              20 = swordshield
    ///   21 = ultrasunultramoon    22 = xy
    ///   23 = yellow
    ///
    /// Valores exactos del enum PKHeX.Core.GameVersion:
    ///   S=1 R=2 E=3 FR=4 LG=5 CXD=15
    ///   HG=7 SS=8 D=10 P=11 Pt=12
    ///   W=20 B=21 W2=22 B2=23
    ///   X=24 Y=25 AS=26 OR=27
    ///   SN=30 MN=31 US=32 UM=33
    ///   RD=35 GN=36 BU=37 YW=38 GD=39 SI=40 C=41
    ///   GP=42 GE=43 SW=44 SH=45 PLA=47 BD=48 SP=49
    ///   SL=50 VL=51
    /// </summary>
    /// <summary>
    /// BUG encontrado en sesión de diagnóstico Gen9: esto comparaba contra números
    /// mágicos (35, 36, 37... 50, 51) copiados de una versión vieja de PKHeX.Core. En
    /// 25.11.7 el valor entero subyacente de <see cref="GameVersion"/> es completamente
    /// distinto (confirmado por reflection: GameVersion.E = 769, no 3 — ver
    /// conversación de sesión) y varios miembros individuales (S/W/W2, B2/GN/GP) comparten
    /// el mismo valor entre sí, lo cual los vuelve ambiguos para esto. Por eso ahora se
    /// compara contra los miembros del enum por nombre (que el compilador resuelve
    /// automáticamente al valor real de la versión de PKHeX.Core referenciada, sin
    /// necesidad de mantenerlos sincronizados a mano) y se usan los miembros "agrupados"
    /// (BW, ORAS, SWSH, etc.) en vez de las letras individuales ambiguas — coincide además
    /// con cómo está armada la tabla Games de pokemon.db (una fila por par de versiones).
    ///
    /// Incertidumbre conocida: Gen 1 (RB vs RBY) no se pudo confirmar con un save real
    /// todavía — si "redgreen" sigue sin traer Learnsets, avisar para revisarlo.
    /// </summary>
    /// <summary>
    /// Encontrado en sesión de diagnóstico de Learnsets Gen5-9: la corrección anterior
    /// (ver comentario más abajo, "PKHeX.Core 25.11.7") solo cubrió el miembro "agrupado"
    /// del enum (BW, XY, SWSH, etc.) para Gen5-8, asumiendo que era lo que devolvía
    /// SaveFile.Version — pero no se pudo confirmar con saves reales todavía. Por las dudas,
    /// ahora se cubre TAMBIÉN el miembro individual (B/W además de BW, X/Y además de XY,
    /// etc.) como respaldo — mismo criterio defensivo que ya se usó para Gen9 (SL/VL/SV).
    ///
    /// Excepción: S (Sapphire), W (White) y W2 (White2) — y por separado B2 (Black2), GN
    /// (Green) y GP (Let's Go Pikachu) — comparten el mismo valor entero subyacente entre
    /// sí (confirmado por reflection: S=W=W2=257, B2=GN=GP=1), así que agregarlos
    /// individualmente arriesgaría hacer que un save de una generación distinta matchee el
    /// bucket equivocado (el primer case que coincida gana). Para esos casos puntuales se
    /// confía únicamente en el miembro agrupado (RS, BW, B2W2, GG), que sí tiene un valor
    /// propio sin colisión conocida.
    /// </summary>
    private static int MapVersionToGameId(GameVersion version) => version switch
    {
        // Gen 1 — Red/Blue → redgreen (16), Yellow → yellow (23). GN (Green) no se agrega
        // individualmente por la colisión de valor explicada arriba.
        GameVersion.RB or GameVersion.RD or GameVersion.BU => 16,
        GameVersion.YW                     => 23,
        // Gen 2 — Gold/Silver → goldsilver (9), Crystal → crystal (4)
        GameVersion.GS or GameVersion.GD or GameVersion.SI => 9,
        GameVersion.C                      => 4,
        // Gen 3
        GameVersion.RS or GameVersion.R    => 17,  // Ruby/Sapphire → rubysapphire (S no se agrega, colisiona con W/W2)
        GameVersion.E                      => 6,   // Emerald → emerald
        GameVersion.FRLG or GameVersion.FR or GameVersion.LG => 7,  // FireRed/LeafGreen → fireredleafgreen
        GameVersion.CXD                    => 8,   // Colosseum/XD → galeofdarkness
        // Gen 4
        GameVersion.DP or GameVersion.D or GameVersion.P    => 5,   // Diamond/Pearl → diamondpearl
        GameVersion.Pt                     => 15,  // Platinum → platinum
        GameVersion.HGSS or GameVersion.HG or GameVersion.SS => 10, // HeartGold/SoulSilver → heartgoldsoulsilver
        // Gen 5 — W/W2/B2 no se agregan individualmente por la colisión de valor explicada arriba.
        GameVersion.BW                     => 2,   // Black/White → blackwhite
        GameVersion.B2W2                   => 1,   // Black2/White2 → black2white2
        // Gen 6
        GameVersion.XY or GameVersion.X or GameVersion.Y    => 22,  // X/Y → xy
        GameVersion.ORAS or GameVersion.OR or GameVersion.AS => 14, // OmegaRuby/AlphaSapphire → omegarubyalphasapphire
        // Gen 7
        GameVersion.SM or GameVersion.SN or GameVersion.MN  => 19,  // Sun/Moon → sunmoon
        GameVersion.USUM or GameVersion.US or GameVersion.UM => 21, // UltraSun/UltraMoon → ultrasunultramoon
        // GP (Let's Go Pikachu) no se agrega individualmente por la colisión de valor
        // explicada arriba — GE (Let's Go Eevee) sí es seguro.
        GameVersion.GG or GameVersion.GE   => 13,  // Let's Go Pikachu/Eevee → letsgopikachueevee
        // Gen 8
        GameVersion.SWSH or GameVersion.SW or GameVersion.SH => 20, // Sword/Shield → swordshield
        GameVersion.BDSP or GameVersion.BD or GameVersion.SP => 3,  // BrilliantDiamond/ShiningPearl → brilliantdiamondshiningpearl
        GameVersion.PLA                    => 11,  // Legends Arceus → legendsarceus
        GameVersion.ZA                     => 12,  // Legends Z-A → legendsza
        // Gen 9 — Scarlet/Violet → scarletviolet (18). Se agrega también el valor genérico
        // GameVersion.Gen9 (mismo motivo que XY/ORAS arriba — ver el comentario de
        // SaveCapabilities.Detect() donde se encontró y corrigió el mismo problema para el
        // tab Special; esto cubre el caso análogo para Learnsets/Sets).
        GameVersion.SL or GameVersion.VL or GameVersion.SV or GameVersion.Gen9 => 18,
        _                                   => 0
    };

    /// <summary>Código de idioma de PKHeX para los DATOS del juego (no para la UI): el parámetro
    /// se conserva por compatibilidad con los call sites existentes y se ignora — la fuente
    /// única es <see cref="DataLanguageService"/> (corrige zh → zh-Hans/zh-Hant, ver plan i18n).</summary>
    private static string GetPKHeXLang(string ignoredUiLocale) => DataLanguageService.Instance.PkhexCode;

    /// <summary>Recarga el Pokémon abierto tras cambiar el idioma de los datos. Los ids y las
    /// ediciones pendientes se conservan (LoadPokemon restaura <c>RestorePendingEdits</c> por
    /// slot): solo cambian los nombres que se calculan al mostrar.</summary>
    public void ReloadForDataLanguage()
    {
        if (_currentPkm is { } pkm && _currentSlotKey is { } key && Capabilities is { } caps)
        {
            MoveSelector = null; // sus filas ya traen los nombres de movimiento del idioma anterior: se cierra (se reabre en el nuevo)
            LoadPokemon(pkm, caps, key);
            // Los nombres de tipo se pintan con TypeNameConverter sobre una cadena que no cambia
            // (la llave sigue siendo "Fire"): sin esto los bindings no se reevalúan al cambiar de
            // idioma. PropertyName vacío = «todas las propiedades cambiaron».
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>Corre GetLegalitySummary en background (Task.Run) y aplica el resultado al
    /// ViewModel solo si nadie seleccionó otro Pokémon mientras tanto (chequeo de `token`
    /// contra `_loadToken`, que se incrementa en cada LoadPokemon/Close nuevo) — evita que un
    /// análisis viejo pise los datos de un Pokémon distinto ya cargado.</summary>
    /// <summary>Ubicaciones de encuentro reales (plantillas de PKHeX) para la especie/versión del PKM.
    /// Puro cálculo, sin tocar UI ni estado del ViewModel → seguro dentro de Task.Run.</summary>
    private static HashSet<int> ComputeValidMetLocationIds(PKM pkm)
    {
        var ids = new HashSet<int>();
        try
        {
            var candidates = EncounterMovesetGenerator.GenerateEncounters(
                pkm, ReadOnlyMemory<ushort>.Empty, new[] { pkm.Version });
            foreach (var enc in candidates)
                if (enc is ILocation loc && loc.GetLocation() is var id && id != 0)
                    ids.Add(id);
        }
        catch
        {
            // Igual que TryBuildFromLegitEncounter en PokemonService: versión sin datos de encuentro
            // u otra causa inesperada — queda vacío y el combo conserva la lista completa.
        }
        return ids;
    }

    /// <summary>
    /// Filtra el combo de "Ubicación" a las ubicaciones donde la especie realmente puede aparecer, sin
    /// bloquear la UI: el cálculo corre en Task.Run sobre un clon del PKM. El reemplazo de lista escribe
    /// los campos de respaldo + OnPropertyChanged directo (NO Set&lt;T&gt;) para no registrar una "edición"
    /// del usuario, y <see cref="_refiningMetLocations"/> frena el write-back del ComboBox. La ubicación
    /// actual del Pokémon (y la que el usuario haya elegido mientras tanto) se conservan siempre en la lista.
    /// </summary>
    private async Task RefineMetLocationsAsync(PKM clone, List<ComboItem> allLocations, int token)
    {
        HashSet<int> valid;
        try { valid = await Task.Run(() => ComputeValidMetLocationIds(clone)); }
        catch { return; }

        // Ya no es el Pokémon seleccionado, o no hay nada que filtrar (queda la lista completa).
        if (token != _loadToken || _currentPkm == null || valid.Count == 0) return;

        var refined = allLocations.Where(l => valid.Contains(l.Value)).ToList();
        // (Escrito para compilar igual si ComboItem es class o struct: sin comparar contra null el
        // resultado de FirstOrDefault ni usar .Value sobre un ComboItem? sin desenvolver.)
        if (refined.All(l => l.Value != clone.MetLocation) && allLocations.Any(l => l.Value == clone.MetLocation))
            refined.Add(allLocations.First(l => l.Value == clone.MetLocation));
        if (_selectedMetLocation is { } sel && refined.All(l => l.Value != sel.Value))
            refined.Add(sel);

        _refiningMetLocations = true;
        try
        {
            _availableMetLocations = refined;
            OnPropertyChanged(nameof(AvailableMetLocations));
            OnPropertyChanged(nameof(SelectedMetLocation));   // re-empuja la selección al combo
        }
        finally { _refiningMetLocations = false; }
    }

    private int _legalityScansInFlight;

    // Cierra el overlay solo cuando termina la ÚLTIMA verificación en vuelo: una carga vieja
    // que termina después de otra más nueva no debe apagarle el overlay a la nueva, y
    // tampoco puede quedar prendido si ninguna lo cierra (p. ej. Close() invalida el token).
    private void StopLegalityOverlay()
    {
        if (Interlocked.Decrement(ref _legalityScansInFlight) <= 0)
        {
            _legalityScansInFlight = 0;
            _busyState.Stop();
        }
    }

    private async Task LoadLegalityAsync(PKM pkm, int token)
    {
        Interlocked.Increment(ref _legalityScansInFlight);
        LegalitySummary summary;
        List<DiagnosticStep> steps;
        try
        {
            // Mismo criterio que en MainWindowViewModel: overlay compartido. Piso reducido a
            // 900ms (antes 1500ms, genérico) tras la sesión de mockup de la Pokédex — con el
            // checklist de 4 categorías (delay escalonado 0/0.3/0.6/0.9s + ~290ms para llegar
            // a opacidad plena, ver App.axaml), 900ms alcanza para ver ~3 de los 4 ítems
            // encenderse. No se persigue ver el ciclo completo (mismo criterio ya establecido
            // para Loader 1: la barra/checklist es "tinte", no hace falta garantizar que se
            // vea entero) — analizar UN Pokémon es una operación atómica, sin granularidad
            // real de progreso posible, a diferencia del caso de exportar (ver
            // ConfirmExportAsync, ese sí reporta avance real por Pokémon analizado).
            // GetLegalityDiagnosticSteps corre su propio LegalityAnalysis interno
            // (independiente del de GetLegalitySummary) — mismo trade-off de redundancia
            // documentado en RunLegalityRecalcAsync, aceptable acá también porque esto corre
            // una sola vez por Pokémon cargado, no en cada tecla.
            _busyState.Start(_loc["Busy_VerifyingLegality"], kind: BusyKind.VerifyLegality);
            _busyState.ReportScanSubject(pkm.Species);
            var summaryTask = Task.Run(() => _pokemonService.GetLegalitySummary(pkm));
            var stepsTask   = Task.Run(() => _pokemonService.GetLegalityDiagnosticSteps(pkm));
            await Task.WhenAll(summaryTask, stepsTask, Task.Delay(900));
            summary = summaryTask.Result;
            steps   = stepsTask.Result;
        }
        catch
        {
            // No rompemos la UI por esto — el badge se queda en "cargando" hasta la próxima
            // selección en vez de mostrar un estado incorrecto.
            StopLegalityOverlay();
            return;
        }

        StopLegalityOverlay();
        if (token != _loadToken) return; // ya no es el Pokémon seleccionado actualmente

        // Si el slot ya tiene ediciones pendientes que afectan la legalidad (típico: cambio de
        // idioma o reselección con correcciones aplicadas), el análisis de arriba es del PKM
        // ORIGINAL del save y mostraría de nuevo ilegalidades ya corregidas. Se descarta y se
        // recalcula con las ediciones (mismo camino que la edición en vivo y el modal de exportación).
        if (_currentSlotKey is { } pendingKey
            && _editSession.GetRawEdits(pendingKey) is { } pending
            && pending.Keys.Any(LegalityRelevantFields.Contains))
        {
            IsLegalityLoading = false;
            ScheduleLegalityRecalc(pendingKey);
            return;
        }

        IsLegal           = summary.IsLegal;
        LegalityText      = summary.IsLegal ? _loc["Pokemon_Legality_Legal"] : _loc["Pokemon_Legality_Illegal"];
        LegalityIssueTags = summary.CategoryTags;
        LegalityTooltip   = BuildLegalityTooltip(summary);
        LegalityIssues.Clear();
        foreach (var line in summary.TechnicalDetail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(10))
            LegalityIssues.Add(line);
        ApplyDiagnosticSteps(steps); // sin pulso acá — el pulso es solo para la transición en
                                      // vivo durante edición, no para la carga inicial
        IsLegalityLoading = false;
    }

    private void Close()
    {
        IsVisible    = false;
        _currentPkm  = null;
        Capabilities = null;
        _isLoading   = true;   // ClearFields() no debe grabarse como "edición" del slot anterior
        _loadToken++;          // invalida cualquier LoadLegalityAsync todavía en vuelo
        _legalityDebounceCts?.Cancel(); // ídem para el recálculo en vivo (Etapa 4.6.2)
        ClearFields();
        _currentSlotKey = null;
        _isLoading   = false;
    }

    /// <summary>Arma el tooltip del badge de legalidad: primero el mensaje amigable por
    /// categoría (LegalitySummary.FriendlySummaryText), y después el detalle técnico crudo
    /// de PKHeX.Core separado por un título — nunca se descarta, solo queda detrás del
    /// mensaje entendible (ver LegalityMessageMapper).</summary>
    private static string BuildLegalityTooltip(LegalitySummary summary)
    {
        if (summary.IsLegal) return string.Empty;
        return summary.FriendlySummaryText + "\n\n" + UiText.T("Legal_TechHeader") + "\n" + summary.TechnicalDetail;
    }

    private void ClearFields()
    {
        _isClearingFields = true;
        Species = Nickname = Gender = Nature = Ability = AbilityDescription =
        HeldItem = ItemDescription = OT = MetDate =
        TeraType = Type1 = Type2 =
        Move1 = Move2 = Move3 = Move4 =
        Move1Type = Move2Type = Move3Type = Move4Type =
        Move1Class = Move2Class = Move3Class = Move4Class =
        Move1Effect = Move2Effect = Move3Effect = Move4Effect =
        HiddenPowerType = LegalityText = LegalityIssueTags = LegalityTooltip = string.Empty;
        Move1Id = Move2Id = Move3Id = Move4Id = 0;
        IsNicknamedFlag = false;
        IsLegalityLoading = false;
        AvailableMetLocations = [];
        SelectedMetLocation = null;
        AvailableEggLocations = [];
        SelectedEggLocation = null;
        EggDate = string.Empty;
        HatchedFromEgg = false;
        RecalcMoveCreateSlots();

        Level = TID = MetLevel = Friendship =
        IvHp = IvAtk = IvDef = IvSpa = IvSpd = IvSpe =
        EvHp = EvAtk = EvDef = EvSpa = EvSpd = EvSpe =
        TotalHp = TotalAtk = TotalDef = TotalSpa = TotalSpd = TotalSpe =
        BaseHp = BaseAtk = BaseDef = BaseSpa = BaseSpd = BaseSpe = BaseTotal =
        Move1PP = Move2PP = Move3PP = Move4PP =
        Move1BasePP = Move2BasePP = Move3BasePP = Move4BasePP =
        Move1PPUps = Move2PPUps = Move3PPUps = Move4PPUps = 0;

        Move1Power = Move2Power = Move3Power = Move4Power = null;
        Move1Accuracy = Move2Accuracy = Move3Accuracy = Move4Accuracy = null;

        IsShiny = IsEgg = HasType2 = HasBaseStats =
        ShowHiddenPower = TabRibbons = TabMega = TabZMoves =
        TabDynamax = TabAlpha = TabTerastal = TabSets = TabSpecial = false;

        _allRibbons = new();
        RibbonGroups = new();
        RibbonCategoryChips = new();
        RibbonSummary = string.Empty;
        _selectedRibbonCategory = "Todas";
        _ribbonSearchText = string.Empty;

        DynamaxLevel = 0;
        CanGigantamax = false;
        _gmaxSpeciesCapable = false;

        _megaSwapNote = string.Empty;
        _zSwapNote = string.Empty;
        HeldItemId = 0;
        AbilityId = 0;
        BallId = 0;
        RecalcEquippedSpecialItem();

        IsAlpha = false;
        IsNoble = false;
        HasNoble = false;
        TeraOverrideOptions = [];
        TeraOriginalType = string.Empty;
        TeraOverrideEditable = false;
        TeraOverrideNote = string.Empty;

        MegaCompatible = false;
        MegaFormName = string.Empty;
        MegaSummary = string.Empty;
        MegaType1 = string.Empty;
        MegaType2 = string.Empty;

        ZMoveCompatible = false;
        ZMoveResultName = string.Empty;
        ZMoveSourceMove = string.Empty;
        ZMoveNote = string.Empty;
        ZMoveType = string.Empty;

        MegaStoneOptions = [];
        ZCrystalOptions = [];
        _megaOptionToItemId = new();
        _zCrystalOptionToItemId = new();

        TabIvsEvs = TabMoves = TabLearnsets = true;
        Sprite = null;

        Form = BallName = TimeOfDay = HandlerName = EggDate = string.Empty;
        _experience = string.Empty; OnPropertyChanged(nameof(Experience));
        HasForm = HasTimeOfDay = HasPokerus = PkrsActive = HasEggData = false;

        _growthRate = 0;
        AvailableItems = [];
        AvailableBalls = [];
        AvailableNatures = [];
        AvailableGenders = [];
        AvailableAbilities = [];
        AvailableForms = [];
        _selectedNature = null;
        _origTotalHp = _origTotalAtk = _origTotalDef = _origTotalSpa = _origTotalSpd = _origTotalSpe = 0;
        _hoveredStat = -1;


        LegalityIssues.Clear();
        LegalityChips.Clear();
        DiagnosticSteps.Clear();
        HighlightedFieldIds = new HashSet<string>();
        HasIvsEvsIssue = HasMovesIssue = HasRibbonsIssue = HasDiagnosticIssue = false;
        SetIvSolverPlan(null);
        ClearRecreateOffer();
        TypeWeaknesses.Clear();
        RecommendedSets.Clear();
        RecommendedSetGroups.Clear();
        Learnsets.Clear();
        NotifyLearnsetGroups();
        _isClearingFields = false;
    }
}

/// <summary>Entrada de learnset para binding en la vista.</summary>
public sealed class LearnsetEntry
{
    public string LearnMethod  { get; set; } = string.Empty;
    public int    LearnLevel   { get; set; }
    public string? LearnItem   { get; set; }
    public int    MoveId       { get; set; }
    public string MoveName     { get; set; } = string.Empty;
    public string TypeName     { get; set; } = string.Empty;
    public string DamageClass  { get; set; } = string.Empty;
    public int?   Power        { get; set; }

    public string PowerDisplay  => Power.HasValue ? Power.Value.ToString() : "—";

    /// <summary>Descripción del movimiento para el tooltip del nombre (lazy desde la DB; mismo
    /// formato que el tooltip del selector de movimientos).</summary>
    private string? _tooltip;
    public string Tooltip => _tooltip ??= LoadTooltip();

    private string LoadTooltip()
    {
        var entry = MoveId > 0 ? MoveDatabase.Instance.Get(MoveId) : MoveDatabase.Instance.GetByName(MoveName);
        if (entry == null) return MoveName;
        var parts = new List<string>();
        if (entry.Accuracy.HasValue) parts.Add($"{UiText.T("Moves_Acc_Short")} {entry.Accuracy}");
        if (entry.Pp.HasValue)       parts.Add($"PP {entry.Pp}");
        var header = string.Join(" · ", parts);
        return string.IsNullOrEmpty(entry.EffectEntries) ? (header.Length > 0 ? header : MoveName)
            : header.Length == 0 ? entry.EffectEntries
            : $"{header}\n{entry.EffectEntries}";
    }

    /// <summary>Qué mostrar en la primera columna de la tabla: nivel para "levelup", el
    /// código de MT/MO (ej. "TM06") para "tm"/"tr" si la DB lo tiene, vacío para el resto
    /// (huevo, tutor, evolución, etc. — no tiene sentido mostrar nivel ni MT ahí).</summary>
    public string LevelOrItemDisplay => LearnMethod switch
    {
        "levelup"          => LearnLevel.ToString(),
        "tm" or "tr" when !string.IsNullOrEmpty(LearnItem) => LearnItem,
        _                  => string.Empty,
    };

    public string MethodDisplay => LearnMethod switch
    {
        "levelup"   => UiText.T("Move_Method_LevelUp"),
        "egg"       => UiText.T("Move_Method_Egg"),
        "tm"        => UiText.T("Move_Method_TM"),
        "tr"        => UiText.T("Move_Method_TR"),
        "tutor"     => UiText.T("Move_Method_Tutor"),
        "reminder"  => UiText.T("Move_Method_Reminder"),
        "evolution" => UiText.T("Move_Method_Evolution"),
        "special"   => UiText.T("Move_Method_Special"),
        "oyabun"    => UiText.T("Move_Method_Alpha"),
        "zmove"     => UiText.T("Move_Method_ZMove"),
        _           => LearnMethod
    };
}

/// <summary>
/// Datos de una barra de stat segmentada: base (gris) + delta IV/EV/nivel/naturaleza (color).
/// Width* son fracciones 0-1 para ColumnDefinition Width con Star sizing.
/// </summary>
public sealed class StatBarInfo
{
    public int    BaseValue      { get; set; }
    public int    DeltaValue     { get; set; }
    public int    TotalValue     { get; set; }
    public double BaseWidth      { get; set; }
    public double DeltaWidth     { get; set; }
    public string DeltaLabel     { get; set; } = string.Empty;
    public bool   ShowDeltaLabel { get; set; }

    /// <summary>Ancho restante sin usar (fracción 0-1) para que Grid star-sizing complete a 1.0.</summary>
    public double RemainderWidth => Math.Max(0, 1 - BaseWidth - DeltaWidth);
}

/// <summary>
/// Una barra de fila del tab IVs/EVs. Todas las Width* son fracciones 0-1 (escala TotalBarMax)
/// para ColumnDefinition Star vía StarWidthConverter.
/// </summary>
public sealed class RowBarInfo
{
    public double FillWidth { get; init; }   // verde sólido: min(actual, original)
    public double GainWidth { get; init; }   // verde claro: lo ganado respecto del save
    public double LossWidth { get; init; }   // rojo: lo perdido respecto del save
    public double OrigWidth { get; init; }   // posición de la marca "original"
    public double BaseWidth { get; init; }   // barra gris aparte: stat base de la especie
    public string DeltaText { get; init; } = string.Empty;
    public bool   IsGain    { get; init; }
    public bool   IsLoss    { get; init; }

    public double RestWidth     => Math.Max(0, 1 - FillWidth - GainWidth - LossWidth);
    public double OrigRestWidth => Math.Max(0, 1 - OrigWidth);
    public double BaseRestWidth => Math.Max(0, 1 - BaseWidth);
}

/// <summary>Punto del radar con su zona de hover (24×24). IsHot lo prende el hover/foco cruzado.</summary>
public sealed class RadarVertexInfo : INotifyPropertyChanged
{
    private static readonly Avalonia.Media.IBrush Green = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1D9E75"));
    private static readonly Avalonia.Media.IBrush Red   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D6455D"));

    public RadarVertexInfo(int index, double left, double top, int direction, bool isHot)
    {
        Index     = index;
        BoxMargin = new Avalonia.Thickness(left, top, 0, 0);
        DotBrush  = direction < 0 ? Red : Green;   // rojo si bajó respecto del save, verde si no
        _isHot    = isHot;
    }

    public int Index { get; }
    public Avalonia.Thickness BoxMargin { get; }
    public Avalonia.Media.IBrush DotBrush { get; }

    private bool _isHot;
    public bool IsHot
    {
        get => _isHot;
        set
        {
            if (_isHot == value) return;
            _isHot = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHot)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
