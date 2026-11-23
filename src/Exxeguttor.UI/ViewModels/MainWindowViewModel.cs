using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Exxeguttor.App.Capabilities;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Modo general de la app — reemplaza panel central + derecho manteniendo el panel
/// Entrenador siempre visible. Pokédex reincorporada esta sesión (revierte la decisión de
/// alcance documentada en mockups/navigation_rail — el usuario confirmó que es un cambio
/// de rumbo intencional; el mockup de navigation_rail queda sin actualizar todavía a
/// propósito, ver screen-pokedex-album.md). A diferencia de Bag, Pokédex no tiene panel
/// derecho separado — ocupa todo el ancho (ver IsPokedexMode más abajo).</summary>
public enum AppMode { Pokemon, Bag, Pokedex }

public enum StatusIconKind { Idle, Opening, Opened, OpenError, Exporting, Exported }

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly SaveFileService     _saveService;
    private readonly TrainerService      _trainerService;
    private readonly PokemonService      _pokemonService;
    private readonly ItemInventoryService _itemInventoryService;
    private readonly LocalizationService _loc;
    private readonly EditSessionService  _editSession;
    private readonly PokemonReleaseService _release;
    private readonly EditApplyService    _editApply;
    private readonly BusyStateService    _busyState;
    private readonly RecentSavesService  _recentSavesService;
    private FileDialogService? _fileDialog;

    private string            _statusMessage = string.Empty;
    private string            _title         = string.Empty;
    private bool              _hasSaveLoaded;
    private SaveCapabilities? _capabilities;

    /// <summary>
    /// <paramref name="onProgress"/> es opcional (default null, no rompe ningún caller
    /// existente) — lo usa el splash de arranque (ver App.axaml.cs) para mostrar mensajes de
    /// "Cargando..." mientras se arma esta cadena de servicios/sub-viewmodels. Cada llamada
    /// es síncrona y corresponde a un grupo real de construcción, no a un delay artificial —
    /// el pacing/tiempo mínimo por mensaje lo decide quien pasa el callback, no acá.
    /// </summary>
    public MainWindowViewModel(Action<string>? onProgress = null)
    {
        _loc            = new LocalizationService();
        onProgress?.Invoke(_loc["Splash_Stage_Database"]);
        FormNameFormatter.Configure(_loc);

        onProgress?.Invoke(_loc["Splash_Stage_SaveModule"]);
        _saveService    = new SaveFileService();
        _trainerService = new TrainerService(_saveService);
        _pokemonService = new PokemonService(_saveService);
        _itemInventoryService = new ItemInventoryService(_saveService);

        onProgress?.Invoke(_loc["Splash_Stage_Editor"]);
        _editSession  = new EditSessionService();
        Trainer = new TrainerViewModel(_loc, _editSession);
        _busyState    = new BusyStateService();
        _release = new PokemonReleaseService(_saveService, _editSession);
        Box     = new BoxViewModel(_loc, _pokemonService, _editSession, _release);
        Party   = new PartyViewModel(_loc, _pokemonService, _editSession, _release);
        SpeciesPicker = new SpeciesPickerViewModel(_loc);
        Editor  = new PokemonEditorViewModel(_loc, _pokemonService, _editSession, _busyState);
        // Modal "Buscar IVs legales" (mockups/ivs_solver_modal/): el editor pide abrirlo cuando los IVs
        // editados rompen la semilla del encuentro; al aplicar una opción, ella vuelve al editor.
        IvSolver = new IvSolverViewModel(_loc, (plan, option) => Editor.ApplyIvSeedOption(plan, option));
        Editor.IvSolverRequested += plan => IvSolver.Open(plan);
        // Feedback de "Corregir automáticamente" (Rotom PC): procesando / corregido / no se pudo
        AutoFix = new AutoFixViewModel(_loc);
        Editor.AutoFixFeedback = AutoFix.RunAsync;
        Editor.AutoFixQuiet = AutoFix.RunQuietAsync;
        // Confirmación de las correcciones que reescriben el origen (Recrear con un encuentro válido,
        // catch rate y nivel de Gen1): el modal elige, el editor aplica con el feedback de arriba.
        Recreate = new RecreateViewModel(_loc, choice => Editor.ApplyRecreateChoiceAsync(choice));
        Editor.RecreateRequested += req => Recreate.Open(req);
        Bag     = new BagViewModel(_loc, _itemInventoryService, _editSession);
        Pokedex = new PokedexViewModel(new PokedexService(_pokemonService, PokemonDatabase.Instance), _loc);
        _editApply = new EditApplyService(_editSession, _saveService, _trainerService, _loc, _pokemonService);

        // Módulo Tips (mockups/tips_stack/) — TipsService carga el JSON embebido
        // (tips_ES.json, ver TipsService.cs), TipsViewModel arma el rail de
        // categorías + el scroll vertical con snap de la categoría activa.
        Tips = CreateTips();

        // Menú Ayuda (Acerca de / Atajos / Reportar). El portapapeles lo aporta la ventana (ClipboardSetter).
        Help = new HelpViewModel(_loc,
            () => DiagnosticsService.Collect(_loc.CurrentLocale, DataLanguageService.Instance.PkhexCode,
                _openFileName != null && Capabilities != null ? $"{Capabilities.GameName} · Gen {Capabilities.Generation}" : null),
            () => IsTipsVisible = true,
            text => ClipboardSetter?.Invoke(text) ?? Task.FromException(new InvalidOperationException()));

        // Pantalla 1 — Estado vacío / Carga de save: lista de "Recientes" + portada por
        // juego (sprite real si existe, o fallback procedural por generación).
        _recentSavesService = new RecentSavesService();
        RecentSaves = new RecentSavesViewModel(_recentSavesService, new GameCoverService(SpriteService.Instance));
        RecentSaves.OpenRequested += path => RequestOpenSaveFromPath(path);

        // Modal de error temático (mockups/error_empty_state/) — botones "Entendido" y
        // "Quitar de recientes" del modal necesitan ICommand, no alcanza con bindear el
        // método directo desde el Button.Command del XAML.
        DismissSaveErrorCommand = new RelayCommand(DismissSaveError);
        RemoveFailedRecentAndDismissCommand = new RelayCommand(RemoveFailedRecentAndDismiss);

        // Suscribir eventos de selección de Pokémon
        Box.PokemonSelected   += OnPokemonSelected;
        Party.PokemonSelected += OnPokemonSelected;

        // Crear Pokémon: click en "+" (Box/Party) → abre el modal → especie elegida → se
        // construye en memoria (PokemonService.BuildPokemon, sin tocar el SaveFile) → queda
        // "pendiente" en EditSessionService (mismo criterio que el resto de las ediciones:
        // nada se escribe de verdad hasta confirmarlo en el modal de revisión al exportar,
        // que todavía no aplica esta parte — queda para una sesión futura) → se recarga el
        // panel correspondiente (ahora Box/PartyViewModel saben mostrar lo pendiente) →
        // pulso visual breve → se selecciona automáticamente, como si se hubiera clickeado.
        Box.CreateRequested   += (boxIndex, slotIndex) => OpenSpeciesPicker(false, boxIndex, slotIndex);
        Party.CreateRequested += slotIndex => OpenSpeciesPicker(true, 0, slotIndex);
        Box.ReleaseRequested   += OnReleaseRequested;
        Party.ReleaseRequested += OnReleaseRequested;
        SpeciesPicker.SpeciesChosen += OnSpeciesChosen;

        Title         = _loc["App_Title"];
        SetStatus("Status_Ready");
        StartDirtyWatcher();
        // Idioma de los datos: arranca siguiendo a la UI y reacciona a cambios (plan i18n, Fase 1/2).
        NatureDatabase.Translate = key => _loc[key];
        AppText.Translate = key => _loc[key];
        UiText.Translate = key => _loc[key]; // «neutra» y abreviaturas de stat en el idioma de la UI
        DataLanguageService.Instance.SetUiLocale(_loc.CurrentLocale);
        DataLanguageService.Instance.ApplyToPkhexGlobals(); // ubicaciones, formatters de legalidad
        DataLanguageService.Instance.Changed += OnDataLanguageChanged;
        SwitchUiLocaleCommand   = new RelayCommand<string>(code => { if (code != null) SwitchUiLocale(code); });
        SwitchDataLocaleCommand = new RelayCommand<string>(code => { if (code != null) DataLanguageService.Instance.SetPreference(code); });
        RebuildLocaleEntries();

        OpenSaveFileCommand = new AsyncRelayCommand(OpenSaveFileAsync);
        SaveFileCommand     = new AsyncRelayCommand(SaveFileAsync);
        ExportFileCommand    = new AsyncRelayCommand(ExportFileAsync);
        ConfirmExportCommand = new AsyncRelayCommand(ConfirmExportAsync);
        CancelReviewCommand  = new RelayCommand(CancelReview);
        NextReviewCardCommand = new RelayCommand(() => ReviewCardIndex++);
        PrevReviewCardCommand = new RelayCommand(() => ReviewCardIndex--);
        SelectReviewCardCommand = new RelayCommand<ExportReviewCardViewModel>(
            card => ReviewCardIndex = _reviewCards.IndexOf(card));
        CheckpointCommand    = new AsyncRelayCommand(CheckpointAsync);
        ExitCommand         = new RelayCommand(Exit);
        ShowAboutCommand    = new RelayCommand(ShowAbout);
        ShowTipsCommand     = new RelayCommand(() => IsTipsVisible = true);
        CloseTipsCommand    = new RelayCommand(() => IsTipsVisible = false);
        CloseSaveCommand    = new RelayCommand(RequestCloseSave);
        ConfirmCloseCommand = new RelayCommand(ConfirmClose);
        CancelCloseCommand  = new RelayCommand(CancelClose);
        ClearEditsCommand   = new RelayCommand(RequestClearEdits);
        ConfirmReleaseCommand = new AsyncRelayCommand(ConfirmReleaseAsync);
        CancelReleaseCommand  = new RelayCommand(() => { IsConfirmingRelease = false; _pendingReleaseKey = null; });
        CloseReleaseBlockedCommand = new RelayCommand(() => IsReleaseBlocked = false);
        UndoReleaseCommand    = new RelayCommand(UndoRelease);
        DismissReleaseToastCommand = new RelayCommand(() => IsReleaseToastVisible = false);
        ConfirmClearCommand = new RelayCommand(ConfirmClear);
        CancelClearCommand  = new RelayCommand(CancelClear);
        ConfirmOpenNewCommand = new RelayCommand(ConfirmOpenNew);
        CancelOpenNewCommand  = new RelayCommand(CancelOpenNew);
        CloseExportSuccessCommand = new RelayCommand(() => IsExportSuccessVisible = false);

        SwitchToPokemonModeCommand = new RelayCommand(() => CurrentMode = AppMode.Pokemon);
        // Bag.Initialize() SACADO de acá (sesión "premisa de carga única al abrir el save") —
        // antes se re-ejecutaba en CADA click al rail de Mochila, re-consultando pokemon.db
        // aunque nada hubiera cambiado desde la última vez. Nada fuera de Mochila modifica
        // pouches/ítems (las ediciones de cantidad se trackean en EditSessionService y
        // BagViewModel ya las superpone en memoria sobre los datos base sin necesitar
        // releerlos de la DB), así que alcanza con la única llamada real en
        // OpenSaveFromPathAsync (y PerformClearEdits, que si necesita recalcular todo). Ver
        // docs/premisa-carga-unica.md para el detalle completo de esta sesión.
        SwitchToBagModeCommand     = new RelayCommand(() => CurrentMode = AppMode.Bag);
        SwitchToPokedexModeCommand = new RelayCommand(() => CurrentMode = AppMode.Pokedex);
    }

    // ── Idiomas: menú Preferencias + indicador (plan i18n, Fase 2) ───────────────────────

    private string? _openFileName;

    /// <summary>Título de la ventana: nombre de la app, o con save abierto "App — archivo [Juego · Gen N]".</summary>
    private void RefreshTitle()
    {
        Title = _openFileName != null && Capabilities != null
            ? string.Format("{0} — {1} [{2} · Gen {3}]", _loc["App_Title"], _openFileName,
                Capabilities.GameName, Capabilities.Generation)
            : _loc["App_Title"];
    }

    public ICommand SwitchUiLocaleCommand   { get; }
    public ICommand SwitchDataLocaleCommand { get; }

    private List<LocaleMenuEntry> _uiLocaleEntries = new();
    private List<LocaleMenuEntry> _dataLocaleEntries = new();
    public List<LocaleMenuEntry> UiLocaleEntries   { get => _uiLocaleEntries;   private set { _uiLocaleEntries = value;   OnPropertyChanged(); } }
    public List<LocaleMenuEntry> DataLocaleEntries { get => _dataLocaleEntries; private set { _dataLocaleEntries = value; OnPropertyChanged(); } }

    /// <summary>Texto de la píldora de la barra de estado: "UI ES · PK ES" (interfaz · datos).</summary>
    public string LanguageIndicator
        => $"UI {DataLanguageService.ShortCode(_loc.CurrentLocale)} · PK {DataLanguageService.ShortCode(DataLanguageService.Instance.PkhexCode)}";

    /// <summary>Ámbar solo cuando los datos caen a inglés por falta de idioma en PKHeX.</summary>
    public bool LanguageIndicatorIsWarning => DataLanguageService.Instance.FallsBackToEnglish;

    public string LanguageUiLine   => $"{_loc["Lang_Popover_Ui"]}: {DataLanguageService.NativeName(_loc.CurrentLocale)}";
    public string LanguageDataLine => $"{_loc["Lang_Popover_Data"]}: {DataLanguageService.NativeName(DataLanguageService.Instance.PkhexCode)}"
                                      + (DataLanguageService.Instance.IsAuto ? $" ({_loc["Lang_Auto_Short"]})" : "");

    /// <summary>Cambia el idioma de la interfaz en caliente y avisa al idioma de los datos (que, en
    /// modo «Igual que la interfaz», puede cambiar con él).</summary>
    private void SwitchUiLocale(string code)
    {
        if (!_loc.SetLocale(code)) return;
        DataLanguageService.Instance.SetUiLocale(code); // dispara OnDataLanguageChanged si cambia PkhexCode
        OnLocaleChanged();
    }

    /// <summary>Refresca todo lo que se calculó una vez con <c>_loc[...]</c>. Los bindings
    /// <c>{Binding Loc[...]}</c> se refrescan solos (LocalizationService notifica "Item"); acá van
    /// solo las propiedades de C# que envuelven textos. Regla del plan i18n, sección 6.</summary>
    /// <summary>Modales que pueden estar abiertos mientras se cambia de idioma (el menú queda accesible): los que
    /// guardan textos ya armados se rearman o, si no se puede sin perder el estado del usuario, se cierran.</summary>
    private void RefreshOpenModals()
    {
        IvSolver.OnLocaleChanged();
        Recreate.OnLocaleChanged();
        AutoFix.OnLocaleChanged();
        // El resumen de revisión trae filas/motivos ya resueltos y las casillas de «incluir» del usuario: se cierra
        // (las ediciones siguen pendientes) y se reabre con Exportar.
        if (IsReviewModalVisible) IsReviewModalVisible = false;
        // Modal de error al abrir un save: título y mensaje se resuelven en el constructor.
        if (ActiveSaveError is { } err)
            ActiveSaveError = new SaveOpenError(err.Kind, err.FilePath, SpriteService.Instance, _loc);
    }

    private void OnLocaleChanged()
    {
        RefreshTitle();
        RefreshOpenModals();
        RefreshStatus();
        OnPropertyChanged(nameof(ExportSuccessTitle));
        OnPropertyChanged(nameof(ExportSuccessDoneLabel));
        Trainer.OnLocaleChanged();
        Box.OnLocaleChanged();
        Bag.OnLocaleChanged();
        Pokedex.OnLocaleChanged();
        SpeciesPicker.OnLocaleChanged();
        Tips = CreateTips(); // los tips son contenido por idioma: se recargan (vuelve a la primera categoría)
        RecentSaves.Refresh(); // «Hoy · 14:30», «Ayer» y fechas cortas se calcularon al construir las filas
        RebuildLocaleEntries();
        // Los textos derivados del editor (avisos de Mega/Z-Moves, notas de Tera, métodos de learnset…)
        // se calculan al cargar el Pokémon: se recargan para tomar el idioma nuevo.
        if (HasSaveLoaded) { Editor.ReloadForDataLanguage(); SpeciesPicker.PreloadInBackground(); } // catálogo de especies nuevo, en segundo plano
    }

    /// <summary>El idioma de los datos cambió (selección explícita, o la UI cambió en modo auto):
    /// recarga lo que muestra nombres del juego. Los ids y las ediciones pendientes se conservan
    /// (los nombres se calculan al mostrar).</summary>
    private void OnDataLanguageChanged()
    {
        // Primero el idioma global de PKHeX: las listas de ubicaciones se vuelven a pedir al recargar.
        DataLanguageService.Instance.ApplyToPkhexGlobals();
        RebuildLocaleEntries();
        RefreshOpenModals();
        if (!HasSaveLoaded) return;
        Box.Refresh();
        Party.Refresh();
        Editor.ReloadForDataLanguage();
        Bag.ReloadForDataLanguage();
        SpeciesPicker.OnLocaleChanged(); // nombres de tipo de los chips + catálogo de especies (idioma de los datos)
        SpeciesPicker.PreloadInBackground();
        if (IsPokedexMode && _saveService.CurrentSave is { } save) Pokedex.Reload(save);
    }

    private void RebuildLocaleEntries()
    {
        var data = DataLanguageService.Instance;

        var ui = new List<LocaleMenuEntry>();
        foreach (var code in _loc.AvailableLocales.OrderBy(c => DataLanguageService.NativeName(c), StringComparer.CurrentCultureIgnoreCase))
        {
            var noData = DataLanguageService.MapUiToPkhex(code) is null && code != "en";
            ui.Add(new LocaleMenuEntry(code, DataLanguageService.NativeName(code), DataLanguageService.ShortCode(code),
                noData ? _loc["Lang_DataFallbackEnglish"] : "", code == _loc.CurrentLocale, new RelayCommand(() => SwitchUiLocale(code))));
        }
        UiLocaleEntries = ui;

        var dt = new List<LocaleMenuEntry>
        {
            new(DataLanguageService.Auto,
                $"{_loc["Lang_Auto"]} ({DataLanguageService.NativeName(data.IsAuto ? data.PkhexCode : (DataLanguageService.MapUiToPkhex(_loc.CurrentLocale) ?? DataLanguageService.Fallback))})",
                "", data.FallsBackToEnglish ? _loc["Lang_DataFallbackEnglish"] : "", data.IsAuto,
                new RelayCommand(() => data.SetPreference(DataLanguageService.Auto))),
        };
        foreach (var code in DataLanguageService.SelectableCodes)
            dt.Add(new LocaleMenuEntry(code, DataLanguageService.NativeName(code), DataLanguageService.ShortCode(code), "",
                !data.IsAuto && data.Preference == code, new RelayCommand(() => data.SetPreference(code))));
        DataLocaleEntries = dt;

        OnPropertyChanged(nameof(LanguageIndicator));
        OnPropertyChanged(nameof(LanguageIndicatorIsWarning));
        OnPropertyChanged(nameof(LanguageUiLine));
        OnPropertyChanged(nameof(LanguageDataLine));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    public LocalizationService    Loc     => _loc;
    public TrainerViewModel       Trainer { get; }
    public BoxViewModel           Box     { get; }
    public PartyViewModel         Party   { get; }
    public SpeciesPickerViewModel SpeciesPicker { get; }
    public IvSolverViewModel IvSolver { get; }
    public AutoFixViewModel AutoFix { get; }
    public RecreateViewModel Recreate { get; }
    public PokemonEditorViewModel Editor  { get; }
    public BagViewModel           Bag     { get; }
    public PokedexViewModel       Pokedex { get; }
    public RecentSavesViewModel   RecentSaves { get; }
    private TipsViewModel _tips = null!;
    public TipsViewModel          Tips    { get => _tips; private set { _tips = value; OnPropertyChanged(); } }

    // Tips en el idioma de la UI (tips_EN.json / tips_ES.json); TipsService cae a EN si no hay archivo del idioma.
    private TipsViewModel CreateTips() => new(new TipsService(_loc.CurrentLocale), _loc);

    // ── Selector de MODO general de la app (Pokémon / Mochila / Pokédex) ─────────────────
    // El panel Entrenador (columna izquierda, TrainerView) queda igual en cualquier modo —
    // solo cambian el panel central y el derecho. Pokédex reincorporada esta sesión (ver
    // comentario del enum AppMode arriba) — a diferencia de Bag, no usa panel derecho
    // separado, ocupa todo el ancho (ver MainWindow.axaml).
    private AppMode _currentMode = AppMode.Pokemon;
    public AppMode CurrentMode
    {
        get => _currentMode;
        set
        {
            Set(ref _currentMode, value);
            OnPropertyChanged(nameof(IsPokemonMode));
            OnPropertyChanged(nameof(IsBagMode));
            OnPropertyChanged(nameof(IsPokedexMode));
        }
    }
    public bool IsPokemonMode => CurrentMode == AppMode.Pokemon;
    public bool IsBagMode     => CurrentMode == AppMode.Bag;
    public bool IsPokedexMode => CurrentMode == AppMode.Pokedex;

    public ICommand SwitchToPokemonModeCommand { get; }
    public ICommand SwitchToBagModeCommand     { get; }
    public ICommand SwitchToPokedexModeCommand { get; }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    // Clave + argumentos del último mensaje de estado: al cambiar el idioma de la UI se vuelve a formatear
    // (antes «Archivo cargado: main» se quedaba en el idioma anterior hasta el próximo mensaje).
    private string? _statusKey;
    private object?[] _statusArgs = [];

    /// <summary>Mensaje de la barra de estado que no es de E/S de archivo: el ícono vuelve a reposo
    /// (sin save) o a "save cargado" (con save). Los mensajes de abrir/exportar usan <see cref="SetIoStatus"/>.</summary>
    /// <summary>Avisa en la barra de estado (y en el registro) si <c>pokemon.db</c> falta o no es compatible con este app.</summary>
    public void NotifyDbCompatibility()
    {
        var c = PokemonDatabase.Instance.Compatibility;
        if (c.IsOk) return;
        AppLog.Warn($"pokemon.db: {c.Status} (needs schema {DbCompatibility.RequiredSchema}, data >= {DbCompatibility.MinData}; found {c.Info?.Full ?? "none"})");
        SetStatus(DbCompatibility.StatusKey(c.Status));
    }

    private void SetStatus(string key, params object?[] args)
    {
        ApplyStatus(key, args);
        SetStatusIcon(HasSaveLoaded ? StatusIconKind.Opened : StatusIconKind.Idle);
    }

    /// <summary>Mensaje de E/S de archivo (abrir / exportar) con su ícono temático (Voltorb / Electrode).</summary>
    private void SetIoStatus(StatusIconKind kind, string key, params object?[] args)
    {
        ApplyStatus(key, args);
        SetStatusIcon(kind);
    }

    private void ApplyStatus(string key, object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = args.Length == 0 ? _loc[key] : string.Format(_loc[key], args);
    }

    private void RefreshStatus()
    {
        // Solo re-traduce el texto: el ícono no cambia al cambiar de idioma.
        if (_statusKey != null) ApplyStatus(_statusKey, _statusArgs);
        else if (!HasSaveLoaded) SetStatus("Status_Ready");
    }

    // ── Íconos de la barra de estado (mockups/status_bar/) ───────────────────────────────────
    private StatusIconKind _statusIcon = StatusIconKind.Idle;
    private int _statusIconToken;
    private bool _isDirty;

    public bool IsIconIdle      => _statusIcon == StatusIconKind.Idle;
    public bool IsIconOpening   => _statusIcon == StatusIconKind.Opening;
    public bool IsIconOpened    => _statusIcon == StatusIconKind.Opened;
    public bool IsIconError     => _statusIcon == StatusIconKind.OpenError;
    public bool IsIconExporting => _statusIcon == StatusIconKind.Exporting;
    public bool IsIconExported  => _statusIcon == StatusIconKind.Exported;

    /// <summary>Hay cambios sin exportar (ediciones, creaciones o liberaciones): Rotom a color con resplandor.</summary>
    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public Bitmap? StatusRotomColor { get; } = SpriteService.Instance.GetPokemonSprite(479);
    public Bitmap? StatusRotomGray  { get; } = SpriteService.Instance.GetStatusRotomGraySprite();

    private void SetStatusIcon(StatusIconKind kind)
    {
        var token = ++_statusIconToken;
        if (_statusIcon != kind)
        {
            _statusIcon = kind;
            OnPropertyChanged(nameof(IsIconIdle));
            OnPropertyChanged(nameof(IsIconOpening));
            OnPropertyChanged(nameof(IsIconOpened));
            OnPropertyChanged(nameof(IsIconError));
            OnPropertyChanged(nameof(IsIconExporting));
            OnPropertyChanged(nameof(IsIconExported));
        }
        // El parpadeo rojo es el estado que más llama la atención: se apaga solo a los 6 s (el mensaje queda).
        if (kind == StatusIconKind.OpenError) _ = CalmErrorIconAsync(token);
    }

    private async Task CalmErrorIconAsync(int token)
    {
        await Task.Delay(6000);
        if (token == _statusIconToken && _statusIcon == StatusIconKind.OpenError)
            SetStatusIcon(HasSaveLoaded ? StatusIconKind.Opened : StatusIconKind.Idle);
    }

    /// <summary>Sondea cada 0,4 s si hay cambios sin exportar (HasAnyEdits es barato; así no hay que
    /// enganchar cada punto donde la sesión de edición cambia).</summary>
    private void StartDirtyWatcher()
    {
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) => IsDirty = HasSaveLoaded && _editSession.HasAnyEdits;
        timer.Start();
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    public bool HasSaveLoaded
    {
        get => _hasSaveLoaded;
        set => Set(ref _hasSaveLoaded, value);
    }

    public SaveCapabilities? Capabilities
    {
        get => _capabilities;
        set => Set(ref _capabilities, value);
    }

    public ICommand OpenSaveFileCommand { get; }
    public ICommand SaveFileCommand     { get; }
    public ICommand ExportFileCommand    { get; }
    public ICommand ConfirmExportCommand { get; }
    public ICommand CancelReviewCommand  { get; }
    public ICommand CheckpointCommand    { get; }
    public ICommand ExitCommand         { get; }
    public ICommand ShowAboutCommand    { get; }
    public ICommand ShowTipsCommand     { get; }
    public ICommand CloseTipsCommand    { get; }
    public ICommand CloseSaveCommand    { get; }
    public ICommand ConfirmCloseCommand { get; }
    public ICommand CancelCloseCommand  { get; }
    public ICommand ClearEditsCommand   { get; }
    public ICommand ConfirmClearCommand { get; }
    public ICommand CancelClearCommand  { get; }
    public ICommand ConfirmOpenNewCommand { get; }
    public ICommand CancelOpenNewCommand  { get; }
    public ICommand CloseExportSuccessCommand { get; }

    // ── Confirmación "Cerrar save" (Punto 2 sesión "gestión de sesión de save") ── Mismo
    // patrón visual que ActiveSaveError/SaveOpenError (overlay condicionado por una propiedad
    // no-null/true) pero sin necesitar una clase aparte — acá no hay datos que llevar más allá
    // de "sí/no", un bool alcanza.
    // ── Modal "Tips" (ayuda general de la app) — mismo patrón bool que
    // IsConfirmingClose/IsConfirmingClear (overlay + tarjeta centrada), no necesita
    // datos aparte de sí/no, así que un bool alcanza acá también.
    private bool _isTipsVisible;
    /// <summary>True mientras se muestra el modal de Tips (ayuda general, abierto desde el
    /// nuevo botón de la barra de acciones — ver MainWindow.axaml).</summary>
    public bool IsTipsVisible
    {
        get => _isTipsVisible;
        set => Set(ref _isTipsVisible, value);
    }

    private bool _isConfirmingClose;
    /// <summary>True mientras se muestra el modal "¿Cerrar sin exportar?" — solo aparece si
    /// había algo pendiente (ver <see cref="RequestCloseSave"/>); si no había nada, cierra
    /// directo sin preguntar.</summary>
    public bool IsConfirmingClose
    {
        get => _isConfirmingClose;
        set => Set(ref _isConfirmingClose, value);
    }

    private void RequestCloseSave()
    {
        if (!HasSaveLoaded) return;
        if (_editSession.HasAnyEdits) { IsConfirmingClose = true; return; }
        PerformCloseSave();
    }

    private void ConfirmClose()
    {
        IsConfirmingClose = false;
        PerformCloseSave();
    }

    private void CancelClose() => IsConfirmingClose = false;

    /// <summary>Cierra el save actual y vuelve a Pantalla 1 (Recientes/drag&amp;drop) — mismo
    /// ResetSession que se usa al abrir un save nuevo (ver comentario ahí), tiene sentido acá
    /// también: después de esto no hay NINGÚN save cargado, no debería quedar nada pendiente
    /// en memoria.</summary>
    private void PerformCloseSave()
    {
        _saveService.Close();
        IsReleaseToastVisible = false;
        _editSession.ResetSession();
        NotebookSummary = new List<NotebookEntry>();
        Capabilities  = null;
        HasSaveLoaded = false;
        Editor.IsVisible = false;
        CurrentMode = AppMode.Pokemon;
        _openFileName = null;
        Title         = _loc["App_Title"];
        SetStatus("Status_Ready");
    }

    // ── "Limpiar" ediciones sin cerrar el save (Punto 3) — mismo patrón de confirmación que
    // Cerrar, ver comentarios ahí. La diferencia real está en PerformClearEdits: acá el save
    // sigue cargado, así que después de limpiar hay que RE-POBLAR los paneles desde el
    // SaveFile real (nunca se mutó — ver GAP CRÍTICO/EditApplyService, las ediciones son
    // "pendientes" hasta exportar) para que la UI refleje el estado original de verdad, no
    // solo que el modelo de ediciones quedó vacío.
    // ── Liberar Pokémon ("−" en cada slot) — ver PokemonReleaseService ───────────────────────────
    // Diálogo de confirmación con Rotom-Corte (corta el lazo y el Pokémon se va), aviso de "último del
    // equipo", y un aviso inferior con "Deshacer". El save en memoria se modifica al confirmar; lo
    // liberado se puede deshacer una por una o todo junto con "Descartar ediciones".
    private PokemonSlotKey? _pendingReleaseKey;
    private bool _isConfirmingRelease, _isReleaseBlocked, _isReleaseToastVisible;
    private string _releaseTitle = "", _releaseBody = "", _releaseWhere = "", _releaseItemWarning = "", _releaseToastText = "";
    private Bitmap? _releaseSprite;
    private int _releaseToastToken;

    public ICommand ConfirmReleaseCommand { get; }
    public ICommand CancelReleaseCommand { get; }
    public ICommand CloseReleaseBlockedCommand { get; }
    public ICommand UndoReleaseCommand { get; }
    public ICommand DismissReleaseToastCommand { get; }

    public bool IsConfirmingRelease { get => _isConfirmingRelease; set => Set(ref _isConfirmingRelease, value); }
    public bool IsReleaseBlocked { get => _isReleaseBlocked; set => Set(ref _isReleaseBlocked, value); }
    public bool IsReleaseToastVisible { get => _isReleaseToastVisible; set => Set(ref _isReleaseToastVisible, value); }
    public string ReleaseTitle { get => _releaseTitle; set => Set(ref _releaseTitle, value); }
    public string ReleaseBody { get => _releaseBody; set => Set(ref _releaseBody, value); }
    public string ReleaseWhere { get => _releaseWhere; set => Set(ref _releaseWhere, value); }
    public string ReleaseItemWarning { get => _releaseItemWarning; set { Set(ref _releaseItemWarning, value); OnPropertyChanged(nameof(HasReleaseItemWarning)); } }
    public bool HasReleaseItemWarning => !string.IsNullOrEmpty(_releaseItemWarning);
    public string ReleaseToastText { get => _releaseToastText; set => Set(ref _releaseToastText, value); }
    /// <summary>Sprite del Pokémon que se está por liberar (el que "se va" en la escena del diálogo).</summary>
    public Bitmap? ReleaseSprite { get => _releaseSprite; set => Set(ref _releaseSprite, value); }
    public Bitmap? RotomMowSprite => SpriteService.Instance.GetPokemonSprite(10012);  // "¿Liberar?" — Rotom-Corte

    private void OnReleaseRequested(PokemonSlotKey key)
    {
        var block = _release.GetBlock(key);
        if (block == PokemonReleaseService.Block.LastPartyMember)
        {
            IsReleaseBlocked = true;
            return;
        }
        if (block != PokemonReleaseService.Block.None || _release.Get(key) is not { } pkm) return;

        _pendingReleaseKey = key;
        var name = PokemonReleaseService.DisplayName(pkm);
        ReleaseTitle = pkm.IsEgg ? UiText.T("Release_Title_Egg") : UiText.F("Release_Title", name);
        ReleaseBody = UiText.T("Release_Body");
        ReleaseWhere = pkm.IsEgg
            ? PokemonReleaseService.Where(key) + " · " + UiText.T("Release_EggName")
            : PokemonReleaseService.Where(key) + " · " + UiText.F("Release_Level", pkm.CurrentLevel);
        ReleaseItemWarning = PokemonReleaseService.HeldItemName(pkm) is { Length: > 0 } item
            ? UiText.F("Release_ItemWarning", item) : "";
        ReleaseSprite = SpriteService.Instance.GetPokemonSprite(pkm.Species, pkm.IsShiny, form: pkm.Form);
        IsConfirmingRelease = true;
    }

    private async Task ConfirmReleaseAsync()
    {
        IsConfirmingRelease = false;
        if (_pendingReleaseKey is not { } key) return;
        _pendingReleaseKey = null;

        // Desvanecido del slot (el CSS del slot tarda ~0.35 s) y recién después se quita.
        if (key.IsParty) Party.FadeOutSlot(key.SlotIndex);
        else Box.FadeOutSlot(key.BoxIndex, key.SlotIndex);
        await Task.Delay(380);

        ReleaseRecord? record;
        try { record = _release.Release(key); }
        catch (Exception ex)
        {
            SetStatus("Error_Unknown", ex.Message);
            RefreshSlotPanels();
            return;
        }
        RefreshSlotPanels();
        if (record == null) return;

        ShowReleaseToast(UiText.F("Release_Toast", record.DisplayName));
    }

    /// <summary>Después de liberar o deshacer: el editor se cierra (el Pokémon mostrado pudo cambiar de lugar)
    /// y se repuebla el equipo y la caja visible, conservando el scroll de la caja.</summary>
    private void RefreshSlotPanels()
    {
        Editor.IsVisible = false;
        Box.SelectedSlot = null;
        Box.RefreshKeepingScroll();
        Party.Load();
        NotebookSummary = _editSession.GetSimpleSummary();
    }

    private async void ShowReleaseToast(string text)
    {
        var token = ++_releaseToastToken;
        ReleaseToastText = text;
        IsReleaseToastVisible = true;
        await Task.Delay(8000);
        if (token == _releaseToastToken) IsReleaseToastVisible = false;
    }

    private void UndoRelease()
    {
        IsReleaseToastVisible = false;
        if (_release.UndoLast(out var failKey))
        {
            RefreshSlotPanels();
            SetStatus("Release_Undone");
            // Si quedan más liberaciones, el aviso vuelve a ofrecer deshacer la anterior.
            if (_release.LastRelease is { } prev)
                ShowReleaseToast(UiText.F("Release_Toast", prev.DisplayName));
        }
        else
        {
            SetStatus(failKey ?? "Release_UndoNothing");
        }
    }

    private bool _isConfirmingClear;
    public bool IsConfirmingClear
    {
        get => _isConfirmingClear;
        set => Set(ref _isConfirmingClear, value);
    }

    private void RequestClearEdits()
    {
        if (!HasSaveLoaded) return;
        if (_editSession.HasAnyEdits) { IsConfirmingClear = true; return; }
        // Nada pendiente -- no hace falta preguntar ni re-popular nada, ya está en su estado
        // original.
        SetStatus("Status_NoPendingChanges");
    }

    private void ConfirmClear()
    {
        IsConfirmingClear = false;
        PerformClearEdits();
    }

    private void CancelClear() => IsConfirmingClear = false;

    private void PerformClearEdits()
    {
        if (_saveService.CurrentSave is not { } save) return;

        // Las liberaciones SÍ modificaron el save en memoria: hay que devolver esos Pokémon antes de
        // limpiar la sesión (el resto de las ediciones nunca tocan el save hasta exportar).
        _release.UndoAll();
        IsReleaseToastVisible = false;
        _editSession.ResetSession();
        NotebookSummary = new List<NotebookEntry>();

        // Mismo bloque de "repoblar todo desde el save" que OpenSaveFromPathAsync — el
        // SaveFile en memoria nunca se tocó, así que esto vuelve a mostrar exactamente lo que
        // había al cargar el archivo.
        var caps = new SaveCapabilities(save);
        var info = _trainerService.GetTrainerInfo();
        Capabilities = caps;
        Trainer.LoadFrom(info);
        Box.Initialize(save.BoxCount);
        Party.Load();
        Bag.Initialize();
        Pokedex.Reload(save);
        Editor.IsVisible = false;

        SetStatus("Status_EditsDiscarded");
    }

    // ── Pantalla de éxito de exportación (mockups/export_success) — overlay que se
    // dispara una sola vez al terminar de escribir el archivo con éxito (ver
    // PerformExportAsync), no bindeado a ningún progreso. Solo cubre el camino feliz —
    // si SaveAsAsync tira, PerformExportAsync va directo al catch existente
    // (StatusMessage con Error_Unknown) y esta pantalla ni se llega a mostrar; un
    // mensaje de error temático para ese caso queda pendiente (ver .md, "Pendiente #3"
    // — no definido en la sesión de diseño).
    private bool _isExportSuccessVisible;
    public bool IsExportSuccessVisible
    {
        get => _isExportSuccessVisible;
        set => Set(ref _isExportSuccessVisible, value);
    }

    private string _exportedFileName = "";
    /// <summary>Nombre del archivo (sin ruta) recién exportado — se muestra en la línea
    /// secundaria atenuada debajo de "Save exportado". Mismo dato que ya calcula
    /// PerformExportAsync para el diálogo de guardado, solo que ahí se descarta después de
    /// usarlo — acá se guarda porque esta pantalla lo necesita un rato más (hasta que el
    /// usuario la cierra).</summary>
    public string ExportedFileName
    {
        get => _exportedFileName;
        set => Set(ref _exportedFileName, value);
    }

    /// <summary>Setiados una sola vez en el constructor (mismo patrón que Title) — no
    /// cambian con el estado del save, así que no hace falta recalcularlos.</summary>
    // Computadas (no asignadas una vez): se refrescan al cambiar el idioma — ver OnLocaleChanged.
    public string ExportSuccessTitle => _loc["Dialog_ExportSuccess_Title"];
    public string ExportSuccessDoneLabel => _loc["Action_Done"];

    // Token de generación para el auto-cierre — se incrementa cada vez que se dispara la
    // pantalla (ver PerformExportAsync). Sin esto, exportar dos veces seguidas rápido podría
    // hacer que el auto-cierre programado por la PRIMERA exportación cierre de golpe la
    // pantalla de la SEGUNDA antes de tiempo — el delay captura su propio token y solo actúa
    // si sigue siendo el más reciente cuando termina de esperar.
    private int _exportSuccessToken;

    /// <summary>Cierra sola la pantalla de éxito ~2.5s después de que termina de asentarse la
    /// animación completa (3s) — le da tiempo al usuario a leer "Save exportado" +
    /// nombre de archivo (que recién aparecen a partir del 80% de esa animación, ver
    /// "StackPanel.xs-success-message.xsactive" en App.axaml) antes de desaparecer sola. El
    /// cierre manual (botón "Listo" / "✕") sigue funcionando igual — si el usuario ya cerró
    /// antes de que se cumpla el delay, este método simplemente no hace nada (el token ya no
    /// coincide, o IsExportSuccessVisible ya está en false y volver a ponerlo en false es
    /// inofensivo de cualquier forma).</summary>
    private async void ScheduleExportSuccessAutoClose(int token)
    {
        await Task.Delay(5500);
        if (token == _exportSuccessToken)
            IsExportSuccessVisible = false;
    }

    // entrada (diálogo del sistema / Recientes): antes pisaba el save actual sin avisar si
    // tenía ediciones pendientes. Mismo patrón/tono. ──
    private string? _pendingOpenPath;
    private bool _isConfirmingOpenNew;
    /// <summary>True mientras se muestra el modal "¿Abrir otro save?" — solo aparece si ya
    /// había un save cargado CON ediciones pendientes (ver
    /// <see cref="RequestOpenSaveFromPath"/>); si no hay nada que perder, abre directo.</summary>
    public bool IsConfirmingOpenNew
    {
        get => _isConfirmingOpenNew;
        set => Set(ref _isConfirmingOpenNew, value);
    }

    /// <summary>Punto de entrada único para las tres formas de "abrir un save" (diálogo del
    /// sistema, fila de Recientes, y a futuro drag&amp;drop) — decide si hace falta preguntar
    /// antes de pisar el save actual. Reemplaza las llamadas directas a
    /// <see cref="OpenSaveFromPathAsync"/> que había antes en los tres puntos de entrada.</summary>
    private void RequestOpenSaveFromPath(string path)
    {
        if (HasSaveLoaded && _editSession.HasAnyEdits)
        {
            _pendingOpenPath = path;
            IsConfirmingOpenNew = true;
            return;
        }
        _ = OpenSaveFromPathAsync(path);
    }

    private void ConfirmOpenNew()
    {
        IsConfirmingOpenNew = false;
        if (_pendingOpenPath is not { } path) return;
        _pendingOpenPath = null;
        _ = OpenSaveFromPathAsync(path);
    }

    private void CancelOpenNew()
    {
        IsConfirmingOpenNew = false;
        _pendingOpenPath = null;
    }

    // ── Indicador de carga compartido — ver BusyStateService. Cualquier operación que
    // llame a PKHeX.Core o haga I/O y pueda demorar (crear un Pokémon buscando un encuentro
    // legítimo, analizar legalidad, abrir/guardar el save) usa esta misma instancia, para que
    // un solo overlay grande y centrado cubra todos los casos en vez de indicadores sueltos
    // y chicos por cada ViewModel. ──
    public BusyStateService BusyState => _busyState;

    /// <summary>Ícono de Poké Ball para el overlay — mismo asset embebido que ya usa el
    /// badge de objeto equipado (SpriteService), no hace falta sumar nada nuevo.</summary>
    public Bitmap? BusySprite => SpriteService.Instance.GetItemSprite("poke-ball");


    // ── Diálogos de confirmación de cambios sin exportar (mockups/confirmation_dialogs) —
    // mismos dos Rotom que ya usa el ícono de la barra de estado inferior para "cambios sin
    // exportar", cada uno en una forma de aparato distinta para diferenciar la acción sin
    // cambiar de personaje. Sprites fijos (no dependen de ningún estado en runtime, a
    // diferencia del Loader3EggRevealSprite de arriba) — igual que los del Loader 1.
    public Bitmap? RotomFanSprite => SpriteService.Instance.GetPokemonSprite(10011);  // "¿Cerrar sin exportar?" — Rotom-Ventilador
    public Bitmap? RotomWashSprite => SpriteService.Instance.GetPokemonSprite(10009); // "¿Descartar ediciones?" — Rotom-Lavadora

    // ── Pantalla de éxito de exportación — Porygon (el único Pokémon canónicamente hecho
    // de datos) evolucionando a Porygon2 vía el objeto Mejora, que "viaja" hasta una Rotom
    // PC (aproximada con el Rotom base, 479 — no existe sprite oficial de "Rotom PC" como
    // tal, es escenografía del juego sin sprite propio, ver .md). Todos sprites fijos.
    public Bitmap? ExportSuccessPorygonSprite  => SpriteService.Instance.GetPokemonSprite(137);
    public Bitmap? ExportSuccessPorygon2Sprite => SpriteService.Instance.GetPokemonSprite(233);
    public Bitmap? ExportSuccessRotomSprite    => SpriteService.Instance.GetPokemonSprite(479);
    public Bitmap? ExportSuccessUpGradeSprite  => SpriteService.Instance.GetItemSprite("up-grade");

    // ── Loader 3 (BusyKind.CreatePokemon) — huevo que eclosiona y revela el sprite de la
    // especie recién elegida. A diferencia de los sprites fijos del Loader 1, este SÍ depende
    // de qué especie se está creando — se resuelve dinámicamente contra SpriteService en
    // cuanto se confirma la especie en el picker (ver OnSpeciesChosen), no como asset fijo.
    // Shiny/género por defecto (false/false, look "estándar") — no hace falta que coincida con
    // el resultado final del PKM real, es puramente el ícono decorativo de la revelación; el
    // editor que se abre después ya muestra el sprite exacto con todos sus detalles. Nace en
    // null (nada que revelar todavía) para que el binding no muestre nada raro antes de la
    // primera creación de la sesión.
    private int? _loader3SpeciesId;
    private byte _loader3Form; // Fase 2 de formas: 0 salvo al crear una forma regional
    public Bitmap? Loader3EggRevealSprite => _loader3SpeciesId.HasValue
        ? SpriteService.Instance.GetPokemonFormSprite(_loader3SpeciesId.Value, _loader3Form)
        : null;

    // ── Modal de revisión antes de exportar ────────────────────────────
    private bool _isReviewModalVisible;
    public bool IsReviewModalVisible { get => _isReviewModalVisible; set { _isReviewModalVisible = value; OnPropertyChanged(); } }

    private List<EditedPokemonSummary> _reviewSummary = new();
    public List<EditedPokemonSummary> ReviewSummary { get => _reviewSummary; set { _reviewSummary = value; OnPropertyChanged(); } }

    /// <summary>Tarjeta de entrenador del modal de revisión — separada de ReviewSummary
    /// (Pokémon) porque no es "un Pokémon más": sin legalidad, sin checkbox de inclusión,
    /// sprite de entrenador en vez de especie. Null si no hay ninguna edición pendiente del
    /// entrenador (ver EditSessionService.BuildTrainerSummary).</summary>
    private EditedTrainerSummary? _trainerSummary;
    public EditedTrainerSummary? TrainerSummary
    {
        get => _trainerSummary;
        set { _trainerSummary = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTrainerSummary)); }
    }
    public bool HasTrainerSummary => _trainerSummary != null;

    /// <summary>Tarjetas de pouches de mochila con ediciones pendientes — una por pouch
    /// tocado (ver EditSessionService.BuildBagSummaries). Mismo criterio que TrainerSummary:
    /// sin legalidad, sin checkbox de inclusión, se exporta siempre que haya alguna edición.</summary>
    private List<EditedBagPouchSummary> _bagReviewSummary = new();
    public List<EditedBagPouchSummary> BagReviewSummary
    {
        get => _bagReviewSummary;
        set { _bagReviewSummary = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBagReviewSummary)); }
    }
    public bool HasBagReviewSummary => _bagReviewSummary.Count > 0;

    // ── Carrusel unificado de revisión (Pantalla 3 / mockups/export_modal) — envuelve
    // TrainerSummary + BagReviewSummary + ReviewSummary en un solo ItemsSource ordenado
    // (ExportReviewCardViewModel), para que las 3 fuentes vivan en el mismo carrusel con
    // navegación por índice. Las propiedades de arriba (ReviewSummary, TrainerSummary,
    // BagReviewSummary) NO se eliminan — siguen siendo la fuente real que lee
    // EditApplyService.ApplyIncluded en ConfirmExportAsync; ReviewCards solo las envuelve
    // para mostrarlas, cada wrapper reenvía IsIncluded directo al objeto original. ──
    private List<ExportReviewCardViewModel> _reviewCards = new();
    public List<ExportReviewCardViewModel> ReviewCards
    {
        get => _reviewCards;
        private set
        {
            _reviewCards = value;
            OnPropertyChanged();
            RefreshReviewBadges();
            UpdateCardLayout();
        }
    }

    private int _reviewCardIndex;
    public int ReviewCardIndex
    {
        get => _reviewCardIndex;
        set
        {
            if (_reviewCards.Count == 0) return;
            _reviewCardIndex = Math.Clamp(value, 0, _reviewCards.Count - 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FocusedReviewCard));
            UpdateCardLayout();
        }
    }

    // Parámetros del cover flow — tarjeta enfocada más grande y arriba en Z, las vecinas se
    // achican/atenúan/superponen según qué tan lejos estén (no es binario "chica/grande").
    // CanvasCenterX/CanvasWidth deben coincidir con el Canvas del XAML (MainWindow.axaml,
    // ItemsPanel del carrusel) — si se cambia uno, cambiar el otro.
    private const double CanvasCenterX = 300;
    private const double CardSpacing   = 120;
    private const double FocusedWidth  = 200;

    /// <summary>Recalcula tamaño/opacidad/posición/Z de TODAS las tarjetas en base a su
    /// distancia al índice enfocado. La tarjeta enfocada SIEMPRE queda centrada en
    /// CanvasCenterX, sin importar si es la primera, la última o una del medio de la lista
    /// — a diferencia de un scroll horizontal común, que no garantiza eso. Los valores
    /// nuevos son solo doubles/int planos (Width/Opacity/CardLeft/CardZIndex) — el XAML los
    /// anima con Transitions normales, sin necesitar RenderTransform.</summary>
    private void UpdateCardLayout()
    {
        for (int i = 0; i < _reviewCards.Count; i++)
        {
            var card = _reviewCards[i];
            int distance = Math.Abs(i - _reviewCardIndex);

            card.IsFocused = distance == 0;

            double width = distance switch
            {
                0 => FocusedWidth,
                1 => 140,
                2 => 110,
                _ => 100
            };
            card.CardWidth = width;

            card.CardOpacity = distance switch
            {
                0 => 1.0,
                1 => 0.55,
                2 => 0.3,
                3 => 0.15,
                _ => 0.0
            };

            card.CardZIndex = 100 - distance;

            // Centrado real: la posición depende de la distancia AL ÍNDICE ENFOCADO, no de
            // la posición cruda en la lista — por eso "(i - _reviewCardIndex)" y no "i".
            card.CardLeft = CanvasCenterX + (i - _reviewCardIndex) * CardSpacing - width / 2;
        }
    }

    public ExportReviewCardViewModel? FocusedReviewCard =>
        _reviewCards.Count > 0 ? _reviewCards[_reviewCardIndex] : null;

    public int ReviewSelectedCount => _reviewCards.Count(c => c.IsIncluded);
    public int ReviewWarningCount   => _reviewCards.Count(c => c.HasWarnings);
    public bool HasReviewWarnings   => ReviewWarningCount > 0;

    /// <summary>Nombre del archivo destino para la línea "Se exportará a {archivo}" del
    /// header del modal — misma fuente que ya usa PerformExportAsync
    /// (_saveService.CurrentSave.Metadata.FilePath) para sugerir el nombre al exportar.</summary>
    public string ExportDestinationFileName =>
        System.IO.Path.GetFileName(_saveService.CurrentSave?.Metadata?.FilePath) ?? "";

    public ICommand NextReviewCardCommand { get; }
    public ICommand PrevReviewCardCommand { get; }
    public ICommand SelectReviewCardCommand { get; }

    /// <summary>Cada tarjeta puede cambiar su propio IsIncluded (checkbox) — hay que
    /// re-notificar los badges del header ("N de M seleccionados" / "N a revisar") cada vez,
    /// ya que no son parte de ninguna propiedad bindeada directamente sino contadores
    /// derivados de la lista completa.</summary>
    private void RefreshReviewBadges()
    {
        foreach (var card in _reviewCards)
            card.PropertyChanged -= OnReviewCardPropertyChanged; // por si se reconstruye la lista
        foreach (var card in _reviewCards)
            card.PropertyChanged += OnReviewCardPropertyChanged;

        OnPropertyChanged(nameof(ReviewSelectedCount));
        OnPropertyChanged(nameof(ReviewWarningCount));
        OnPropertyChanged(nameof(HasReviewWarnings));
    }

    private void OnReviewCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExportReviewCardViewModel.IsIncluded))
            OnPropertyChanged(nameof(ReviewSelectedCount));
    }

    // ── Libreta: checkpoint visual de la sesión (NO escribe a disco, coexiste con "Guardar" del menú) ──
    private List<NotebookEntry> _notebookSummary = new();
    public List<NotebookEntry> NotebookSummary
    {
        get => _notebookSummary;
        set { _notebookSummary = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotebookChangeCount)); OnPropertyChanged(nameof(HasNotebookChanges)); }
    }
    public int NotebookChangeCount => NotebookSummary.Count;
    public bool HasNotebookChanges => NotebookSummary.Count > 0;

    private bool _isNotebookPulsing;
    public bool IsNotebookPulsing { get => _isNotebookPulsing; set { _isNotebookPulsing = value; OnPropertyChanged(); } }

    public void Initialize(Window window)
    {
        _fileDialog = new FileDialogService(window);
    }

    private void OnPokemonSelected(PKM pkm, PokemonSlotKey key)
    {
        if (_capabilities != null)
            Editor.LoadPokemon(pkm, _capabilities, key);
    }

    private bool _isOpeningSpeciesPicker;

    private async void OpenSpeciesPicker(bool isParty, int boxIndex, int slotIndex)
    {
        var save = _saveService.CurrentSave;
        if (save == null) return;

        // Doble click en "+" mientras el loader está arriba: sin esto se lanzaban dos
        // aperturas en paralelo (dos Start/Open/Stop pisándose entre sí).
        if (_isOpeningSpeciesPicker) return;
        _isOpeningSpeciesPicker = true;

        var needsLoader = false;
        try
        {
            // Fase 2 de formas: qué formas regionales existen en ESTE juego (Alola en Gen7,
            // Galar/Hisui en Gen8...). Antes de WillRebuildOnOpen: el filtro del cover flow
            // depende de esto, y si cambió (otro save) tiene que contar como "hay que rearmar".
            SpeciesPicker.SetAvailableForms(RegionalFormCatalog.AvailableIn(save));
            SpeciesPicker.SetHiddenSpecies(CreatableSpecies.HiddenInCreationPicker(save));
            SpeciesPicker.SetAlternateForms(AlternateFormCatalog.AvailableIn(save.Version));

            // Hay DOS esperas posibles antes de que el modal aparezca:
            //  1) que el prefetch de especies/sprites (PreloadInBackground, ver
            //     OpenSaveFromPathAsync) todavía no haya terminado, y
            //  2) la construcción del cover flow en sí (SpeciesPicker.Open → RecalcFiltered),
            //     que corre sincrónica en el hilo de UI y es la parte más pesada en saves
            //     Gen 5+ (~500-1000 tarjetas, ItemsControl no virtualizado) — más el layout
            //     de esas tarjetas, que ocurre DESPUÉS de que Open() retorna.
            // El Loader 5 se muestra siempre que vaya a haber trabajo real
            // (WillRebuildOnOpen); en aperturas repetidas con el mismo filtro ni se ve.
            needsLoader = SpeciesPicker.WillRebuildOnOpen(save.MaxSpeciesID);

            // Barra de progreso única para las dos fases: si hubo que esperar el catálogo
            // ocupa el primer 40%, y el armado de tarjetas el resto; si el catálogo ya estaba
            // listo, el armado usa toda la barra.
            var waitedForCatalog = false;
            if (needsLoader)
            {
                _busyState.Start(_loc["Busy_SpeciesCatalog"], kind: BusyKind.SpeciesCatalog);

                if (!SpeciesPicker.IsPreloadReady)
                {
                    waitedForCatalog = true;
                    await SpeciesPicker.WaitForPreloadAsync(p => _busyState.ReportProgress(p * 0.4));
                }

                // Que Avalonia presente un frame con el overlay antes de empezar (el armado
                // ahora cede el hilo entre tandas, pero el primer frame igual tiene que estar
                // ya dibujado — mismo criterio que los demás loaders, con su Task.Delay).
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(80);
            }

            var baseProgress = waitedForCatalog ? 0.4 : 0.0;
            var sw = Perf.Start();
            // OpenAsync agrega las tarjetas por tandas cediendo el hilo de UI: la Pokédex del
            // loader se anima de verdad y la barra avanza con progreso real.
            await SpeciesPicker.OpenAsync(isParty, boxIndex, slotIndex, save.MaxSpeciesID,
                p => { if (needsLoader) _busyState.ReportProgress(baseProgress + p * (1.0 - baseProgress)); });
            Perf.Log("SpeciesPicker.OpenAsync (por tandas)", sw);

            // El layout final del modal ya visible se procesa en el próximo ciclo: se espera
            // acá con el loader todavía puesto, en vez de esconderlo antes y dejar la
            // pantalla sin nada que explique la espera.
            if (needsLoader)
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        finally
        {
            if (needsLoader) _busyState.Stop();
            _isOpeningSpeciesPicker = false;
        }
    }

    private async void OnSpeciesChosen(int speciesId, byte form, bool isParty, int boxIndex, int slotIndex)
    {
        try
        {
            // BuildPokemon ahora puede buscar un encuentro legítimo real vía PKHeX.Core
            // (huevo/estático/salvaje) antes de armar el PKM — eso corre búsquedas sobre las
            // tablas internas de PKHeX.Core, que pueden demorar lo suficiente como para
            // congelar la ventana si se corren en el hilo de UI. Task.Run lo pasa a
            // background; el Loader 3 (huevo que eclosiona) prende mientras tanto — ver
            // mockups/loading_screens/screen-05-loading-screens.md, sección "Resueltos" de
            // Loader 3, para la justificación completa de los 2.2s. Se fuerza ese mínimo
            // (Task.WhenAll con el delay) por dos razones a la vez: (1) que el usuario
            // alcance a ver el ciclo completo del huevo terminar en la revelación al menos
            // una vez, aun si la construcción real es más rápida, y (2) es la MISMA duración
            // que un ciclo completo de la animación decorativa (Style.Animations "loader3*"
            // en App.axaml, Duration="0:0:2.2") — no es casualidad, la barra de progreso de
            // abajo se rellena linealmente durante este mismo mínimo artificial en vez de
            // instrumentar pasos internos reales de la construcción por separado (no hace
            // falta: es la creación de un Pokémon, no la apertura de un save con pasos
            // reales que valga la pena exponer). Si la construcción real tarda más que 2.2s,
            // no se corta antes de tiempo — Task.WhenAll espera a la más lenta de las dos.
            _loader3SpeciesId = speciesId;
            _loader3Form      = form;
            OnPropertyChanged(nameof(Loader3EggRevealSprite));
            // Mismo patrón ya usado más abajo para el StatusMessage post-creación
            // Nombre de la especie en el idioma de los DATOS del juego (plan i18n, Fase 3 tanda 1).
            var speciesName = DataLanguageService.Instance.Strings.Species[speciesId];
            // "Marowak (Alola)" para una forma regional — el mismo texto que ve el usuario en
            // el selector, así el loader y el mensaje final dicen QUÉ variante se creó.
            speciesName = FormNameFormatter.Format(speciesName, speciesId, form);
            _busyState.Start(string.Format(_loc["Busy_Generating"], speciesName), kind: BusyKind.CreatePokemon);
            var buildTask = Task.Run(() => _pokemonService.BuildPokemon(speciesId, form));
            await Task.WhenAll(buildTask, Task.Delay(2200));
            var pkm = buildTask.Result;
            _busyState.Stop();

            var key = isParty ? PokemonSlotKey.ForParty(slotIndex) : PokemonSlotKey.ForBox(boxIndex, slotIndex);
            _editSession.RecordPendingCreation(key, pkm);
            // Marca especial: hace que la creación cuente para la libreta y el modal de
            // revisión (como "Creación", aparte de cualquier edición posterior que se le
            // haga a este mismo Pokémon — ver EditSessionService.BuildSummary/IsNewlyCreated).
            _editSession.RecordEdit(key, "__Created__", true);

            PokemonSlotViewModel? slotVm;
            if (isParty)
            {
                Party.Refresh();
                slotVm = Party.Slots.ElementAtOrDefault(slotIndex);
            }
            else
            {
                // Solo hace falta recargar la grilla si la caja creada es la que está
                // visible ahora mismo — si el usuario ya navegó a otra caja mientras el
                // modal estaba abierto, no tiene sentido pisarle la vista actual. La
                // creación queda igual guardada en EditSessionService y va a aparecer sola
                // la próxima vez que se navegue a esa caja.
                if (Box.CurrentBoxIndex == boxIndex)
                    Box.Refresh();
                slotVm = Box.Slots.FirstOrDefault(s => s.SlotIndex == slotIndex);
            }

            if (slotVm != null)
            {
                slotVm.IsJustCreated = true;
                await Task.Delay(450);
                slotVm.IsJustCreated = false;
            }

            // Seleccionar automáticamente — mismo efecto que clickear el slot a mano. Reusa
            // el slotVm real encontrado arriba (no uno nuevo) para que el resaltado visual
            // de "seleccionado" caiga sobre la tarjeta que efectivamente se está mostrando.
            if (isParty)
                Party.SelectSlot(slotIndex);
            else if (Box.CurrentBoxIndex == boxIndex && slotVm != null)
                Box.SelectSlot(slotVm);

            SetStatus("Status_PokemonCreated", speciesName);
        }
        catch (Exception ex)
        {
            _busyState.Stop();
            SetStatus("Status_CreateFailed", ex.Message);
        }
    }

    private async Task OpenSaveFileAsync()
    {
        if (_fileDialog == null) return;

        var path = await _fileDialog.OpenSaveFileAsync(
            _loc["Dialog_OpenSave_Title"],
            _loc["Dialog_OpenSave_Filter"]
        );

        if (path == null) return;

        RequestOpenSaveFromPath(path);
    }

    /// <summary>
    /// Núcleo real de "abrir un save", ya con el <c>path</c> resuelto — compartido por las
    /// tres formas de llegar acá: el botón "Abrir save..." (diálogo del sistema, ver
    /// <see cref="OpenSaveFileAsync"/>), clickear una fila de "Recientes"
    /// (<c>RecentSaves.OpenRequested</c>), y soltar un archivo por drag&amp;drop en la
    /// Pantalla 1. Antes esta lógica vivía solo dentro de <c>OpenSaveFileAsync</c> — se separó
    /// para no duplicarla en los tres casos. Las tres pasan primero por
    /// <see cref="RequestOpenSaveFromPath"/> (ver ahí), que es quien realmente decide si hay
    /// que preguntar antes de llegar hasta acá.
    /// </summary>
    private async Task OpenSaveFromPathAsync(string path)
    {
        try
        {
            SetIoStatus(StatusIconKind.Opening, "Status_Loading");

            // Loader 1 (BusyKind.OpenSave) — Pokédex escaneando + barra de progreso real +
            // etapa actual (ver mockups/open_save_pokedex/). Reemplaza la coreografía de
            // Hitmonlee/Voltorb/Dugtrio, que dependía de una ventana de tiempo precisa (la
            // patada) y se veía cortada cuando el hilo de UI se congelaba. La escena nueva es
            // continua y no depende de ningún instante del ciclo, así que tolera frames perdidos.
            // Tiempo mínimo en pantalla (1 s): con saves chicos la carga entera tarda menos que
            // eso y el modal casi no se llegaba a ver. El cronómetro arranca junto con el loader
            // y la espera de abajo (justo antes de Stop) solo completa lo que falte — si la carga
            // ya tardó 1 s o más, no agrega nada.
            var loaderTimer = System.Diagnostics.Stopwatch.StartNew();
            _busyState.Start(_loc["OpenSave_Title"], iconSize: 110, kind: BusyKind.OpenSave);
            _busyState.ReportStage(_loc["OpenSave_Stage_Reading"]);

            // Progreso REAL, no decorativo (ver docs/premisa-carga-unica.md): cada paso de abajo
            // reporta su avance y la etapa que está por correr, y cede el hilo al dispatcher en
            // prioridad Render — sin eso, como los pasos son sincrónicos, Avalonia coalescería
            // todos los cambios de Progress en un solo repintado final y la barra saltaría de 0
            // a 100 de golpe.
            async Task Advance(double progress, string stageKey)
            {
                _busyState.ReportProgress(progress);
                _busyState.ReportStage(_loc[stageKey]);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }

            // Pintar el primer frame del loader ANTES de empezar a trabajar: sin esto, si lo
            // primero que corre acá es trabajo sincrónico, el overlay puede no llegar a verse.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // Prefetch en background del modal "Crear Pokémon" (especies + sprites, ver
            // SpeciesPickerViewModel.PreloadInBackground) — no depende del save en sí, así
            // que arranca en paralelo con OpenAsync de una vez, sin esperar a que termine
            // nada más. Fire-and-forget deliberado: si el usuario llega a clickear "+" antes
            // de que termine, OpenSpeciesPicker ya se encarga de mostrar el Loader 5 mientras
            // espera (ver ahí) — este call es lo que hace que, la mayoría de las veces, ese
            // loader ni llegue a mostrarse. IMPORTANTE: esta línea se perdió una vez al
            // mezclar con cambios de otro módulo en esta misma zona — si el primer click
            // vuelve a sentirse lento SIN mostrar el Loader 5, lo primero a revisar es que
            // esta llamada siga acá.
            SpeciesPicker.PreloadInBackground();

            // OpenAsync corre en Task.Run (hilo de fondo real, ver SaveFileService), así que el
            // hilo de UI queda libre y la animación sigue corriendo mientras se lee el archivo.
            // Ya no hay piso artificial de 650 ms: existía solo para que alcanzara a verse la
            // patada de Hitmonlee, y la escena nueva no tiene ningún momento que garantizar.
            var save = await _saveService.OpenAsync(path);
            await Advance(0.45, "OpenSave_Stage_Trainer");

            // Reset de la sesión de edición — recién ACÁ, con el open ya confirmado exitoso
            // (si tirara excepción antes, el save viejo -si había uno- sigue cargado en
            // SaveFileService intacto, y sus ediciones pendientes no deberían perderse por un
            // intento de abrir OTRO archivo que falló). Bug encontrado en sesión "gestión de
            // sesión de save": esto nunca se llamaba, así que las ediciones pendientes de un
            // save anterior quedaban pegadas y se mostraban/aplicaban sobre el save recién
            // abierto (las PokemonSlotKey son estructurales — Box1/Slot1 del save viejo es la
            // MISMA key que Box1/Slot1 del save nuevo, no hay forma de distinguirlas sin este
            // reset explícito).
            IsReleaseToastVisible = false;
            _editSession.ResetSession();
            NotebookSummary = new List<NotebookEntry>();

            var caps = new SaveCapabilities(save);
            var info = _trainerService.GetTrainerInfo();

            Capabilities  = caps;
            HasSaveLoaded = true;
            Trainer.LoadFrom(info);
            await Advance(0.55, "OpenSave_Stage_Boxes");

            // Box/Party/Bag: acá es donde de verdad se cumple la "premisa de carga única" —
            // los tres quedan completamente prontos en memoria y ninguna navegación posterior
            // (cambiar de caja, entrar a Mochila, clickear un Pokémon) vuelve a tocar
            // pokemon.db para esto. Antes Bag.Initialize() se re-ejecutaba en cada click al
            // rail de Mochila (ver SwitchToBagModeCommand) — ya no.
            Box.Initialize(save.BoxCount);
            await Advance(0.75, "OpenSave_Stage_Party");

            // La Pokédex se recarga dentro de la etapa "equipo" (antes corría después de
            // reportar 85 %, con la etiqueta de la etapa siguiente ya mostrada).
            Party.Load();
            Pokedex.Reload(save);
            await Advance(0.85, "OpenSave_Stage_Bag");

            Editor.IsVisible = false;
            CurrentMode = AppMode.Pokemon; // un save nuevo siempre arranca en modo Pokémon

            // InitializeAsync (no Initialize) — el prewarming de sprites de Mochila escala
            // con el catálogo de ítems de la generación (Gen4+ tiene ~100 MTs/MOs vs ~50 de
            // Gen1-3, más objetos generales), así que puede ser el paso más largo de todos
            // acá. Reporta avance real categoría por categoría (85%→100%) en vez de un solo
            // salto al final — ver BagViewModel.InitializeAsync para el detalle completo.
            await Bag.InitializeAsync(p => _busyState.ReportProgress(0.85 + p * 0.15));
            await Advance(1.0, "Status_Ready");

            // Piso de 1 s de visibilidad del loader (ver loaderTimer arriba). La barra ya está
            // en 100 % y la etapa dice "Listo" mientras se completa el tiempo; la animación de
            // la Pokédex sigue corriendo porque acá el hilo de UI está libre (await, no bloqueo).
            var remainingMs = 1000 - (int)loaderTimer.ElapsedMilliseconds;
            if (remainingMs > 0)
                await Task.Delay(remainingMs);

            // ── Cierre del loader SIN que quede "pegado" ──
            // Bug reportado: a veces el overlay del Loader 1 quedaba en pantalla aunque la
            // app ya lo había cerrado (IsBusy == false), y desaparecía recién al sacar una
            // captura de pantalla o mover la ventana. Es un frame sin presentar: el trabajo
            // de arriba corre en el hilo de UI y, al terminar, el compositor no
            // necesariamente agenda solo un repintado — a veces hacía falta un evento
            // externo. Mitigaciones acá, en orden:
            //   1) Stop() apaga el overlay y sus animaciones infinitas.
            //   2) Un paso del dispatcher en prioridad Render (pinta el cambio de IsBusy/Kind).
            //   3) Un segundo paso en prioridad Background: garantiza que el layout y el render
            //      de la ventana principal, recién visible con Cajas/Equipo ya cargados,
            //      terminaron ANTES de seguir con el resto (Recientes, título).
            //   4) MainWindow.OnBusyStateChanged fuerza además un repintado completo de la
            //      ventana 16/80/250 ms después de cada cierre de loader (ya existía).
            _busyState.Stop();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // Registrar en "Recientes" (persiste a ~/.config/maxpotion/recent.json) — usa el
            // mismo código crudo de GameVersion que ya guarda TrainerInfo.GameName, así que
            // no hace falta volver a leer save.Version acá.
            RecentSaves.NotifyOpened(path, info.GameName);

            SetIoStatus(StatusIconKind.Opened, "Status_SaveLoaded", Path.GetFileName(path));
            _openFileName = Path.GetFileName(path);
            RefreshTitle();
        }
        catch (FileNotFoundException)
        {
            _busyState.Stop();
            SetIoStatus(StatusIconKind.OpenError, "Error_FileNotFound", path);
            ActiveSaveError = new SaveOpenError(SaveOpenErrorKind.NotFound, path, SpriteService.Instance, _loc);
        }
        catch (InvalidDataException)
        {
            _busyState.Stop();
            SetIoStatus(StatusIconKind.OpenError, "Error_InvalidSave");
            ActiveSaveError = new SaveOpenError(SaveOpenErrorKind.InvalidSave, path, SpriteService.Instance, _loc);
        }
        catch (UnauthorizedAccessException)
        {
            _busyState.Stop();
            SetIoStatus(StatusIconKind.OpenError, "Error_PermissionDenied");
            ActiveSaveError = new SaveOpenError(SaveOpenErrorKind.PermissionDenied, path, SpriteService.Instance, _loc);
        }
        catch (Exception ex)
        {
            // Cualquier excepción no controlada acá (fuera de las tres específicas de arriba)
            // es PKHeX.Core rompiendo a mitad de parseo con datos truncados/dañados — Caso 2
            // del modal ("Datos corruptos" / MissingNo.), no el genérico Error_Unknown que
            // usan otros métodos de este archivo (SaveFileAsync, etc. — esos no se tocan).
            _busyState.Stop();
            SetIoStatus(StatusIconKind.OpenError, "Error_Unknown", ex.Message);
            ActiveSaveError = new SaveOpenError(SaveOpenErrorKind.Corrupted, path, SpriteService.Instance, _loc);
        }
    }

    /// <summary>Error activo a mostrar en el modal de <c>EmptyStateView</c> (uno de los 4
    /// casos de <c>mockups/error_empty_state/</c>) — null cuando no hay ningún error para
    /// mostrar. Se limpia con <see cref="DismissSaveError"/> (botón "Entendido") o
    /// <see cref="RemoveFailedRecentAndDismiss"/> (botón "Quitar de recientes", solo en el
    /// caso NotFound).</summary>
    private SaveOpenError? _activeSaveError;
    public SaveOpenError? ActiveSaveError
    {
        get => _activeSaveError;
        set { _activeSaveError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasActiveSaveError)); }
    }

    public bool HasActiveSaveError => _activeSaveError != null;

    public ICommand DismissSaveErrorCommand { get; }
    public ICommand RemoveFailedRecentAndDismissCommand { get; }

    public void DismissSaveError() => ActiveSaveError = null;

    public void RemoveFailedRecentAndDismiss()
    {
        if (ActiveSaveError?.FilePath is { } path)
            RecentSaves.Remove(path);
        ActiveSaveError = null;
    }

    private async Task SaveFileAsync()
    {
        if (!HasSaveLoaded) return;
        try
        {
            SetIoStatus(StatusIconKind.Exporting, "Status_Saving");
            _busyState.Start(_loc["Busy_Saving"]);
            var saveTask = _saveService.SaveAsync();
            await Task.WhenAll(saveTask, Task.Delay(1500));
            _busyState.Stop();
            SetIoStatus(StatusIconKind.Exported, "Status_SaveSaved");
        }
        catch (Exception ex)
        {
            _busyState.Stop();
            SetIoStatus(StatusIconKind.OpenError, "Error_Unknown", ex.Message);
        }
    }

    /// <summary>Vuelve a leer el PKM prístino (sin ediciones de esta sesión) de un slot,
    /// para que EditSessionService pueda clonarlo + previsualizar legalidad sin tocar nada real.</summary>
    private PKM? FetchPristine(PokemonSlotKey key)
    {
        if (key.IsParty)
        {
            var party = _pokemonService.GetParty();
            return key.SlotIndex >= 0 && key.SlotIndex < party.Count ? party[key.SlotIndex] : null;
        }
        var box = _pokemonService.GetBox(key.BoxIndex);
        return key.SlotIndex >= 0 && key.SlotIndex < box.Count ? box[key.SlotIndex] : null;
    }

    /// <summary>"Exportar": si hay ediciones pendientes en la sesión, primero muestra el modal
    /// de revisión (una tarjeta por Pokémon editado, con checkbox de inclusión); si no hay
    /// nada pendiente, va directo al diálogo de guardar archivo.</summary>
    private async Task ExportFileAsync()
    {
        if (!HasSaveLoaded) return;

        // BuildSummary corre LegalityAnalysis por cada Pokémon editado en esta sesión — con
        // varios Pokémon tocados, esto puede demorar lo suficiente como para congelar la
        // ventana si se corre en el hilo de UI. Igual que en la creación, se pasa a
        // background con Task.Run.
        //
        // BUG REAL corregido tras feedback ("solo aparece el último Pokémon, no los demás"):
        // la primera versión reportaba progreso/especie EN VIVO desde dentro del loop de
        // BuildSummary, asumiendo que el análisis tomaría tiempo real perceptible por
        // Pokémon. En la práctica, analizar legalidad de unos pocos Pokémon es casi
        // instantáneo (microsegundos cada uno) — el loop entero terminaba en menos de lo que
        // tarda en pintarse UN frame, así que todos los ReportScanSubject/ReportProgress se
        // disparaban en ráfaga y la UI, cuando finalmente conseguía renderizar, solo llegaba
        // a mostrar el ÚLTIMO valor (el resto nunca se pintó). Mismo tipo de problema que el
        // freeze de Box.Initialize en Loader 1, pero al revés: ahí el reloj de la animación
        // seguía corriendo mientras no se pintaba nada; acá son las PROPIEDADES las que
        // cambian más rápido de lo que la UI puede pintar frames.
        //
        // Arreglo: se desacopla el cómputo real (rápido, sin pausas artificiales adentro —
        // BuildSummary sigue siendo una función pura, sin timing de UI mezclado) de la
        // "caminata visual" por cada especie, que ahora pasa ACÁ, después de que
        // BuildSummary ya terminó del todo. Se recolecta el orden real de especies vía
        // onScanSpecies (sin reportar nada en vivo, solo juntando la lista) y recién
        // entonces se reproduce a un ritmo legible con delays reales entre paso y paso.
        _busyState.Start(_loc["Busy_AnalyzingLegality"], kind: BusyKind.VerifyLegality);
        var scannedSpecies = new List<int>();
        var summary = await Task.Run(() => _editSession.BuildSummary(
            FetchPristine,
            onScanSpecies: speciesId => scannedSpecies.Add(speciesId)));

        // Ritmo legible pero acotado: 400ms por Pokémon hasta un total de ~3s, de ahí para
        // arriba se reparte proporcional para no hacer esperar una eternidad si son muchos
        // (ej. 20 Pokémon editados no debería tardar 8s solo para mostrar el escaneo).
        var n = scannedSpecies.Count;
        if (n == 0)
        {
            await Task.Delay(900); // nada que "caminar" (todo Entrenador/Mochila) — igual dar
                                    // tiempo a que se alcance a leer el mensaje un instante
        }
        else
        {
            var perItemMs = Math.Max(150, Math.Min(400, 3000 / n));
            for (var i = 0; i < n; i++)
            {
                _busyState.ReportScanSubject(scannedSpecies[i]);
                _busyState.ReportProgress((i + 1.0) / n);
                await Task.Delay(perItemMs);
            }
        }
        var trainerSummary = _editSession.BuildTrainerSummary(
            Trainer.Name, Trainer.IsFemale, _saveService.CurrentSave is { } curSave ? (int)curSave.Version : null);
        var bagSummary = _editSession.BuildBagSummaries();
        _busyState.Stop();

        var releasesToShow = _editSession.Releases.Where(r => r.Original != null).ToList();
        foreach (var r in releasesToShow) r.Include = true;   // revisión fresca cada vez
        if (summary.Count == 0 && trainerSummary == null && bagSummary.Count == 0 && releasesToShow.Count == 0)
        {
            await PerformExportAsync();
            return;
        }

        ReviewSummary = summary;
        TrainerSummary = trainerSummary;
        BagReviewSummary = bagSummary;
        ReviewCards = ExportReviewCardViewModel.BuildAll(trainerSummary, bagSummary, summary, releasesToShow, _release.CanExclude);
        ReviewCardIndex = 0;
        IsReviewModalVisible = true;
    }

    private async Task ConfirmExportAsync()
    {
        IsReviewModalVisible = false;
        // Pipeline de escritura real: aplica al SaveFile en memoria solo lo que quedó
        // tildado (IsIncluded=true) en cada tarjeta — Pokémon, Entrenador y Mochila. Antes
        // de esto, PerformExportAsync escribía el SaveFile tal cual estaba, sin aplicar
        // ninguna edición (ver GAP CRÍTICO, ya resuelto). No hace nada si no hay ediciones
        // pendientes en ninguna de las tres superficies.
        try
        {
            var releasesBefore = _editSession.Releases.Count;
            _release.ApplyExclusions();   // liberaciones destildadas: se reponen antes de aplicar el resto
            if (_editSession.Releases.Count != releasesBefore)
            {
                IsReleaseToastVisible = false;
                RefreshSlotPanels();      // los Pokémon repuestos vuelven a verse en su caja
            }
            _editApply.ApplyIncluded(ReviewSummary, TrainerSummary, BagReviewSummary);
        }
        catch (Exception ex)
        {
            // Antes una excepción acá (ej. formato de PKM distinto al del save) cerraba la app.
            SetStatus("Error_Unknown", ex.Message);
            return;
        }
        await PerformExportAsync();
    }

    private void CancelReview()
    {
        // Cerrar sin exportar no borra nada — las ediciones siguen en el editor tal cual,
        // y si se vuelve a apretar Exportar, se recalcula la revisión desde cero (todo
        // tildado de nuevo, no se recuerda qué se había destildado la vez anterior).
        IsReviewModalVisible = false;
    }

    /// <summary>Botón "Guardar" del toolbar (distinto del "Guardar" del menú, que sí escribe
    /// a disco): es un checkpoint puramente visual — las ediciones YA se trackean en tiempo
    /// real vía EditSessionService.RecordEdit en cada Set&lt;T&gt;, así que acá no se mueve
    /// ningún dato nuevo. Solo refresca el resumen de la libreta y dispara el pulso.</summary>
    private async Task CheckpointAsync()
    {
        NotebookSummary = _editSession.GetSimpleSummary();
        IsNotebookPulsing = true;
        await Task.Delay(450);
        IsNotebookPulsing = false;
    }

    private async Task PerformExportAsync()
    {
        if (_fileDialog == null) return;

        var save = _saveService.CurrentSave;
        var originalPath = save?.Metadata?.FilePath;
        string suggestedName;
        if (System.IO.Path.GetFileName(originalPath) is { Length: > 0 } originalName)
        {
            suggestedName = originalName;
        }
        else if (save != null)
        {
            // Sin archivo original (no debería pasar hoy, ya que siempre se parte de un save
            // abierto — por las dudas). save.Metadata.GetSuggestedExtension() da la extensión
            // correcta para el formato real de ESTE save (.sav, .dsv, sin extensión para
            // Switch, etc.) — confirmado por reflection, no una tabla propia por generación.
            suggestedName = $"export{save.Metadata.GetSuggestedExtension()}";
        }
        else
        {
            suggestedName = "export.sav";
        }

        var path = await _fileDialog.SaveFileAsAsync(_loc["Toolbar_Export_Tip"], suggestedName);
        if (path == null) return;

        try
        {
            SetIoStatus(StatusIconKind.Exporting, "Status_Saving");
            // Sin overlay genérico de pokebola acá — pedido explícito: la pantalla de éxito
            // (Porygon/Rotom) que se muestra justo después ya es la señal visual de que la
            // exportación terminó, así que un "Guardando…" genérico de por medio no aporta,
            // solo demora la aparición de la pantalla temática. Tampoco se fuerza ningún
            // mínimo artificial (antes había un Task.Delay(1500) acompañando el overlay que
            // se sacó) — escribir un save no suele tardar lo suficiente como para necesitar
            // uno, y si tardara, la pantalla de éxito simplemente aparece un poco después,
            // sin nada intermedio que mostrar mientras tanto.
            await _saveService.SaveAsAsync(path);
            var hadReleases = _editSession.Releases.Count > 0;
            _editSession.ClearAll(); // lo pendiente ya "se aplicó" a este export
            if (hadReleases)
            {
                // Las liberaciones ya están en el save exportado: se descartan los índices viejos y se relee todo.
                IsReleaseToastVisible = false;
                RefreshSlotPanels();
            }
            SetIoStatus(StatusIconKind.Exported, "Status_SaveSaved");
            ExportedFileName = System.IO.Path.GetFileName(path);
            IsExportSuccessVisible = true;
            ScheduleExportSuccessAutoClose(++_exportSuccessToken);
        }
        catch (Exception ex)
        {
            _busyState.Stop();
            SetIoStatus(StatusIconKind.OpenError, "Error_Unknown", ex.Message);
        }
    }

    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowAbout() => Help.OpenAboutCommand.Execute(null);

    /// <summary>Menú Ayuda: modales Acerca de / Atajos / Reportar.</summary>
    public HelpViewModel Help { get; }

    /// <summary>La ventana lo asigna al abrirse: copia texto al portapapeles del sistema.</summary>
    public Func<string, Task>? ClipboardSetter { get; set; }
}

// ── Comandos ──────────────────────────────────────────────────────────

public class RelayCommand : ICommand
{
    private readonly Action          _execute;
    private readonly Func<bool>?     _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

/// <summary>Comando con parámetro tipado.</summary>
public class RelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    public RelayCommand(Action<T> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        if (parameter is T typed)
            _execute(typed);
    }
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try   { await _execute(); }
        finally
        {
            _isExecuting = false;
            await Dispatcher.UIThread.InvokeAsync(
                () => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
