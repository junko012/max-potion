using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Qué overlay dibujar mientras <c>IsBusy</c> es true. <c>Generic</c> es el spinner de Poké
/// Ball de siempre (sigue cubriendo guardar — verificar/analizar legalidad y crear Pokémon
/// ya tienen loader temático propio, ver <c>VerifyLegality</c>/<c>CreatePokemon</c> abajo).
/// Los demás valores son loaders con identidad propia de <c>mockups/loading_screens/</c> (y
/// <c>mockups/legality_scan/</c> para <c>VerifyLegality</c>) — <c>MainWindow.axaml</c> tiene
/// un <c>Border</c> de overlay por cada uno, mutuamente excluyentes por <c>Kind</c>, mismo
/// fondo oscurecido compartido. Agregar acá + el Border correspondiente cuando llegue
/// Loader 2 (cargar Pokémon) — no generalizar antes de tenerlo, cada uno tiene contenido
/// bastante distinto (sprite dinámico + barra EXP para Loader 2, vs. huevo que eclosiona /
/// Pokédex escaneando ya implementados acá).
/// </summary>
public enum BusyKind
{
    Generic,
    OpenSave,
    CreatePokemon,
    VerifyLegality,
    SpeciesCatalog,
}

/// <summary>
/// Estado de "operación pesada en curso" compartido entre <c>MainWindowViewModel</c> (crear
/// Pokémon, abrir/guardar el save, analizar legalidad al exportar) y
/// <c>PokemonEditorViewModel</c> (verificar legalidad al seleccionar un Pokémon) — mismo
/// patrón de instancia única inyectada por constructor que ya usa <c>EditSessionService</c>
/// entre ambos ViewModels. Antes cada ViewModel tenía su propio <c>IsBusy</c> desconectado,
/// lo que resultaba en dos indicadores chicos y poco visibles en vez de uno solo grande y
/// centrado en <c>MainWindow.axaml</c>.
/// </summary>
public class BusyStateService : INotifyPropertyChanged
{
    private bool _isBusy;
    private string _message = string.Empty;
    private double _iconSize = 72;
    private BusyKind _kind = BusyKind.Generic;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGenericOverlay));
            OnPropertyChanged(nameof(ShowOpenSaveLoader));
            OnPropertyChanged(nameof(ShowCreatePokemonLoader));
            OnPropertyChanged(nameof(ShowVerifyLegalityLoader));
            OnPropertyChanged(nameof(ShowSpeciesCatalogLoader));
        }
    }

    /// <summary>Combina <see cref="IsBusy"/> + <see cref="Kind"/> en un solo booleano — evita
    /// tener que encadenar dos bindings en <c>IsVisible</c> del XAML (Avalonia no soporta
    /// AND de bindings sin un MultiBinding/converter aparte).</summary>
    public bool ShowGenericOverlay => IsBusy && Kind == BusyKind.Generic;

    /// <summary>Ídem, para el Loader 1 temático (abrir save — Pokédex escaneando).</summary>
    public bool ShowOpenSaveLoader => IsBusy && Kind == BusyKind.OpenSave;

    /// <summary>Ídem, para el Loader 3 temático (crear/elegir Pokémon — huevo que eclosiona).</summary>
    public bool ShowCreatePokemonLoader => IsBusy && Kind == BusyKind.CreatePokemon;

    /// <summary>Ídem, para el Loader 4 temático (verificar/analizar legalidad — Pokédex
    /// escaneando). Un solo Kind para DOS disparadores distintos (ver
    /// PokemonEditorViewModel.LoadLegalityAsync y MainWindowViewModel.ConfirmExportAsync) —
    /// mismo criterio que Rotom-Ventilador reusado en tres diálogos: conceptualmente es la
    /// misma acción (correr LegalityAnalysis), solo cambia el disparador.</summary>
    public bool ShowVerifyLegalityLoader => IsBusy && Kind == BusyKind.VerifyLegality;

    /// <summary>Loader 5 — Pokédex quieta (sin haz de escaneo ni checklist, esos son propios
    /// del análisis de legalidad) + barra de progreso, para el ÚNICO caso en que abrir el
    /// modal de crear Pokémon todavía tiene que esperar el prefetch en background de
    /// SpeciesPickerViewModel (ver MainWindowViewModel.OpenSpeciesPicker) — típicamente el
    /// primer click tras abrir un save de Gen 5+ (más especies = más tiempo de prefetch). En
    /// clicks siguientes el prefetch ya terminó, así que este overlay ni llega a mostrarse
    /// (ver SpeciesPickerViewModel.IsPreloadReady).</summary>
    public bool ShowSpeciesCatalogLoader => IsBusy && Kind == BusyKind.SpeciesCatalog;

    /// <summary>Bindeado en <c>MainWindow.axaml</c> a <c>Classes.opensaveactive</c> (y <c>Classes.pokedexactive</c>
    /// en el ícono de la Pokédex) en cada elemento animado del Loader 1 (mismo mecanismo ya probado en producción que
    /// <c>Border.notebook-icon.pulse</c> + <c>Classes.pulse="{Binding IsNotebookPulsing}"</c>,
    /// ver <c>CheckpointAsync</c>). Los <c>Style.Animations IterationCount="Infinite"</c> de
    /// App.axaml (selectores <c>.opensaveactive</c>) arrancan/re-arrancan desde el cue 0% cada
    /// vez que esta propiedad pasa de false a true — necesario porque antes el Border del
    /// Loader 1 estaba SIEMPRE en el árbol visual (solo con <c>IsVisible</c> togglado), así
    /// que sus animaciones infinitas corrían de fondo desde que abría la ventana sin importar
    /// si el overlay se mostraba o no; el usuario podía terminar viendo el overlay recién a
    /// mitad del ciclo de 2.8s, salteándose los fantasmas y la patada. Bug reportado tras la
    /// primera entrega de Loader 1.</summary>
    public bool IsOpenSaveLoaderActive
    {
        get => _isOpenSaveLoaderActive;
        private set { if (_isOpenSaveLoaderActive == value) return; _isOpenSaveLoaderActive = value; OnPropertyChanged(); }
    }
    private bool _isOpenSaveLoaderActive;

    /// <summary>Mismo mecanismo que <see cref="IsOpenSaveLoaderActive"/> (ver el comentario
    /// largo ahí para el porqué) — bindeado en <c>MainWindow.axaml</c> a
    /// <c>Classes.loader3active</c> en cada elemento animado del huevo, así el ciclo de
    /// eclosión arranca siempre desde cue 0% (huevo entero, sin grietas) cada vez que se crea
    /// un Pokémon nuevo, en vez de heredar el punto del ciclo en el que quedó la vez
    /// anterior.</summary>
    public bool IsCreatePokemonLoaderActive
    {
        get => _isCreatePokemonLoaderActive;
        private set { if (_isCreatePokemonLoaderActive == value) return; _isCreatePokemonLoaderActive = value; OnPropertyChanged(); }
    }
    private bool _isCreatePokemonLoaderActive;

    /// <summary>Mismo mecanismo que <see cref="IsOpenSaveLoaderActive"/> — bindeado en
    /// <c>MainWindow.axaml</c> a <c>Classes.pokedexactive</c> en cada elemento animado de la
    /// escena de la Pokédex, así el ciclo de escaneo arranca siempre desde cue 0% (Pokédex
    /// quieta, checklist apagado) en cada verificación nueva.</summary>
    public bool IsVerifyLegalityLoaderActive
    {
        get => _isVerifyLegalityLoaderActive;
        private set { if (_isVerifyLegalityLoaderActive == value) return; _isVerifyLegalityLoaderActive = value; OnPropertyChanged(); }
    }
    private bool _isVerifyLegalityLoaderActive;

    /// <summary>Mismo mecanismo que <see cref="IsOpenSaveLoaderActive"/> — bindeado a
    /// <c>Classes.pokedexactive</c> en el ícono de Pokédex del Loader 5 (catálogo de
    /// especies). Reusa las mismas animaciones <c>pkdx-bob-y</c>/<c>pkdx-bob-rotate</c> que
    /// el Loader 4 (son selectores por clase, no atados a un Kind — ver App.axaml), sin el
    /// haz de escaneo ni el checklist, que son propios del análisis de legalidad.</summary>
    public bool IsSpeciesCatalogLoaderActive
    {
        get => _isSpeciesCatalogLoaderActive;
        private set { if (_isSpeciesCatalogLoaderActive == value) return; _isSpeciesCatalogLoaderActive = value; OnPropertyChanged(); }
    }
    private bool _isSpeciesCatalogLoaderActive;

    public string Message
    {
        get => _message;
        private set { if (_message == value) return; _message = value; OnPropertyChanged(); }
    }

    /// <summary>Progreso REAL (0.0-1.0) de la operación en curso — sesión "premisa de carga
    /// única al abrir el save". Antes la barra del Loader 1 (<c>l1-progress-fill</c>, ya eliminado, en
    /// App.axaml) era puramente decorativa: un <c>Style.Animation IterationCount="Infinite"</c>
    /// que se rellenaba de 0 a 100% cada 2.8s fijos, sin relación real con cuánto faltaba.
    /// Ahora que <c>OpenSaveFromPathAsync</c> reporta avance real en cada paso (parsear el
    /// archivo, Cajas, Equipo, Mochila), la barra puede reflejarlo de verdad — ver
    /// <see cref="ReportProgress"/> y el binding directo (no Style.Animation) en
    /// MainWindow.axaml. Sigue en 0 para cualquier operación que no reporte progreso
    /// explícito (todo lo que no sea BusyKind.OpenSave, ver <see cref="Start"/>) — esas
    /// siguen con su overlay genérico, que no tiene barra.</summary>
    public double Progress
    {
        get => _progress;
        private set
        {
            if (_progress.Equals(value)) return;
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InverseProgress));
            OnPropertyChanged(nameof(ProgressText));
        }
    }
    private double _progress;

    /// <summary><see cref="Progress"/> como texto entero con espacio antes del símbolo (ej.
    /// "45 %") — lo muestra el Loader 1 (Pokédex escaneando al abrir un save) a la derecha de
    /// la barra. Mismo valor real que la barra, no un número aparte.</summary>
    public string ProgressText => $"{(int)Math.Round(Progress * 100)} %";

    /// <summary>Etapa actual del trabajo en curso (ej. "Preparando cajas…") — la muestra el
    /// Loader 1 a la izquierda de la barra. Ya viene localizada desde quien la reporta
    /// (<see cref="ReportStage"/>); este servicio no conoce <c>LocalizationService</c>. Vacía
    /// para cualquier operación que no reporte etapas.</summary>
    public string Stage
    {
        get => _stage;
        private set { if (_stage == value) return; _stage = value; OnPropertyChanged(); }
    }
    private string _stage = string.Empty;

    /// <summary>Actualiza <see cref="Stage"/> — texto ya localizado.</summary>
    public void ReportStage(string stage) => Stage = stage;

    /// <summary>1 - <see cref="Progress"/> — el "resto" de la barra. Existe solo para poder
    /// armar la barra real del Loader 1 con el mismo patrón de Grid de dos columnas Star que
    /// ya usan las barras de stats en PokemonStatsView.axaml (BaseWidth/RemainderWidth +
    /// StarWidthConverter) en vez de sumar un control nuevo (ProgressBar nativo) que habría
    /// que restylear a mano — ver comentario largo en MainWindow.axaml, bloque del Loader 1,
    /// para el porqué de este approach en vez de un ProgressBar.</summary>
    public double InverseProgress => 1 - Progress;

    /// <summary>Actualiza <see cref="Progress"/>, clampeado a [0,1] — el llamante no necesita
    /// preocuparse por pasarse de 100% por errores de redondeo entre pasos.</summary>
    public void ReportProgress(double value) => Progress = Math.Clamp(value, 0, 1);

    /// <summary>Especie que se está mostrando ahora mismo en la escena del Loader 4 (Pokédex
    /// escaneando) — null hasta el primer <see cref="ReportScanSubject"/>. A diferencia de
    /// <see cref="Progress"/>, esto NO tiene un caso "sin granularidad": tanto
    /// LoadLegalityAsync (un Pokémon) como BuildSummary (varios) siempre saben qué especie
    /// están analizando en cada momento, a diferencia del progreso real (que si es un solo
    /// Pokémon, no tiene un "porcentaje" que reportar).</summary>
    public int? CurrentScanSpeciesId { get; private set; }

    /// <summary>Sprite real de <see cref="CurrentScanSpeciesId"/> — reemplaza la silueta
    /// genérica que tenía la escena antes de esta sesión (feedback real: "debería mostrar el
    /// sprite del Pokémon siendo escaneado"). Shiny/género por defecto (false/false) — mismo
    /// criterio que el resto de los sprites decorativos de este archivo (Loader 3):
    /// no hace falta que coincida exacto con el PKM real, es el ícono de la escena, no el
    /// editor.</summary>
    public Bitmap? CurrentScanSprite =>
        CurrentScanSpeciesId.HasValue ? SpriteService.Instance.GetPokemonSprite(CurrentScanSpeciesId.Value) : null;

    /// <summary>Bindeado en <c>MainWindow.axaml</c> a <c>Classes.pkdxrevealactive</c> en el
    /// sprite escaneado — a diferencia de <see cref="IsOpenSaveLoaderActive"/> y compañía
    /// (que solo se togglean UNA vez, al arrancar el loader), esto se pulsa una vez POR CADA
    /// Pokémon nuevo que entra a escena durante la misma sesión del loader (mismo mecanismo
    /// ya probado en producción que <c>IsNotebookPulsing</c> en <c>CheckpointAsync</c>: un
    /// booleano que se prende, se espera la duración exacta de la animación de un solo tiro,
    /// y se apaga) — así se ve una transición corta cada vez que cambia el sprite, no una
    /// vez sola al principio.</summary>
    public bool IsScanSpriteRevealing { get; private set; }

    /// <summary>Reporta qué especie se está analizando ahora mismo — llamado una vez por
    /// LoadLegalityAsync (un solo Pokémon, sin transición necesaria más que la revelación
    /// inicial) y una vez POR CADA Pokémon dentro del loop real de BuildSummary (ver el
    /// parámetro <c>onScanSpecies</c> ahí). Si la especie no cambió respecto a la ya
    /// mostrada, no hace nada — evita relanzar la animación de revelación sin necesidad
    /// (ej. si <c>ReportProgress</c> y esto se llaman con valores redundantes).</summary>
    public void ReportScanSubject(int speciesId)
    {
        if (CurrentScanSpeciesId == speciesId) return;
        CurrentScanSpeciesId = speciesId;
        OnPropertyChanged(nameof(CurrentScanSpeciesId));
        OnPropertyChanged(nameof(CurrentScanSprite));
        PulseScanReveal();
    }

    private async void PulseScanReveal()
    {
        IsScanSpriteRevealing = true;
        OnPropertyChanged(nameof(IsScanSpriteRevealing));
        await Task.Delay(320);
        IsScanSpriteRevealing = false;
        OnPropertyChanged(nameof(IsScanSpriteRevealing));
    }

    /// <summary>Tamaño del ícono de Poké Ball del overlay — 72px por defecto, más grande
    /// (ej. 110) para operaciones pesadas y poco frecuentes como abrir un save.</summary>
    public double IconSize
    {
        get => _iconSize;
        private set { if (_iconSize == value) return; _iconSize = value; OnPropertyChanged(); }
    }

    /// <summary>Qué overlay mostrar — ver <see cref="BusyKind"/>. Vuelve a <c>Generic</c> en
    /// cada <see cref="Stop"/> para que el siguiente <see cref="Start(string,double)"/> sin
    /// especificar <c>kind</c> (la mayoría de los call sites) no herede por error el loader
    /// temático de la operación anterior.</summary>
    public BusyKind Kind
    {
        get => _kind;
        private set
        {
            if (_kind == value) return;
            _kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGenericOverlay));
            OnPropertyChanged(nameof(ShowOpenSaveLoader));
            OnPropertyChanged(nameof(ShowCreatePokemonLoader));
            OnPropertyChanged(nameof(ShowVerifyLegalityLoader));
            OnPropertyChanged(nameof(ShowSpeciesCatalogLoader));
        }
    }

    /// <summary>Prende el overlay. <paramref name="iconSize"/> permite destacar operaciones
    /// más pesadas (ej. abrir save) con un ícono más grande que el resto — solo aplica al
    /// overlay <c>Generic</c>, los loaders temáticos definen su propio tamaño en XAML.</summary>
    public void Start(string message, double iconSize = 72, BusyKind kind = BusyKind.Generic)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Start(message, iconSize, kind));
            return;
        }
        Kind = kind;
        IconSize = iconSize;
        Message = message;
        Progress = 0;
        Stage = string.Empty;
        CurrentScanSpeciesId = null;
        OnPropertyChanged(nameof(CurrentScanSpeciesId));
        OnPropertyChanged(nameof(CurrentScanSprite));
        IsBusy = true;
        // false→true acá es lo que re-dispara los Style.Animations infinitos del Loader 1
        // (y del Loader 3) desde cue 0% — ver comentario largo en IsOpenSaveLoaderActive. Si
        // kind no coincide con el loader temático correspondiente, se deja en false
        // explícitamente (no hace nada visible, pero mantiene el estado consistente por si el
        // mismo BusyStateService se reusa para esa operación más tarde en la sesión).
        IsOpenSaveLoaderActive = kind == BusyKind.OpenSave;
        IsCreatePokemonLoaderActive = kind == BusyKind.CreatePokemon;
        IsVerifyLegalityLoaderActive = kind == BusyKind.VerifyLegality;
        IsSpeciesCatalogLoaderActive = kind == BusyKind.SpeciesCatalog;
    }

    public void Stop()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Stop);
            return;
        }
        IsBusy = false;
        Kind = BusyKind.Generic;
        IsOpenSaveLoaderActive = false;
        IsCreatePokemonLoaderActive = false;
        IsVerifyLegalityLoaderActive = false;
        IsSpeciesCatalogLoaderActive = false;
        Progress = 0;
        Stage = string.Empty;
    }
}
