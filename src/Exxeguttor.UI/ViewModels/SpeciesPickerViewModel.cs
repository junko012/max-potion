using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// ViewModel del modal "Elegir Pokémon" (crear Pokémon nuevo). Se abre desde el ícono "+" de
/// un slot vacío en Caja o Equipo — ver <see cref="BoxViewModel"/>/<see cref="PartyViewModel"/>
/// para el flag `IsCreateSlot` que decide en qué slots aparece ese "+".
/// </summary>
public class SpeciesPickerViewModel : INotifyPropertyChanged
{
    /// <summary>Los 18 tipos, en el mismo orden/capitalización que el resto de la app
    /// (TypeColorConverter, tabla de efectividad) — lista fija, mismo criterio que
    /// NatureDatabase/RibbonDatabase para conjuntos cerrados que no vienen de SQLite.</summary>
    private static readonly string[] AllTypeNames =
    {
        "Normal", "Fire", "Water", "Electric", "Grass", "Ice", "Fighting", "Poison", "Ground",
        "Flying", "Psychic", "Bug", "Rock", "Ghost", "Dragon", "Dark", "Steel", "Fairy",
    };

    private List<SpeciesPickerItemViewModel> _allSpecies = new();

    private bool   _isVisible;
    private string _searchText = string.Empty;
    private string _resultCountText = string.Empty;
    private bool   _isParty;
    private int    _targetBoxIndex;
    private int    _targetSlotIndex;

    /// <summary>Se dispara cuando el usuario elige una especie — (especie, forma, esEquipo, caja,
    /// slot). La forma es 0 salvo en las tarjetas de forma regional (Fase 2 de formas). El bool
    /// indica si el destino es el equipo (true) o una caja (false), replicando exactamente el
    /// slot que abrió el modal.</summary>
    public event Action<int, byte, bool, int, int>? SpeciesChosen;

    private readonly LocalizationService _loc;
    public LocalizationService Loc => _loc;
    private int _lastResultCount;

    public SpeciesPickerViewModel(LocalizationService loc)
    {
        _loc        = loc;
        Species     = new ObservableCollection<SpeciesPickerItemViewModel>();
        TypeChips   = new ObservableCollection<TypeChipViewModel>();
        CancelCommand = new RelayCommand(() => IsVisible = false);
        CreateCommand = new RelayCommand(() => Select(FocusedItem), () => FocusedItem != null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
        // El buscador dispara esto en CADA tecla (el binding de Avalonia para TextBox.Text
        // actualiza la fuente por cada cambio, no recién al perder foco), y RecalcFiltered()
        // termina siempre reconstruyendo el árbol visual completo del cover flow (no
        // virtualizado, ver nota grande en RecalcFiltered) — llamarlo sincrónico acá hacía que
        // escribir cualquier palabra se sintiera colgado, un rebuild pesado detrás de cada
        // letra. Debounce: en vez de recalcular ya mismo, se agenda para 250ms después del
        // último cambio, cancelando el pendiente anterior — mientras el usuario sigue
        // tipeando rápido no se dispara ningún rebuild, solo cuando hace una pausa real.
        if (name is nameof(SearchText)) ScheduleSearchRecalc();
    }

    private CancellationTokenSource? _searchDebounceCts;
    private const int SearchDebounceMs = 250;

    private void ScheduleSearchRecalc()
    {
        _searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchDebounceCts = cts;
        _ = DebouncedRecalcAsync(cts.Token);
    }

    private async Task DebouncedRecalcAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return; // llegó una tecla nueva antes de que venciera el delay — se cancela sin recalcular
        }

        if (token.IsCancellationRequested) return;

        // Post explícito al hilo de UI (en vez de confiar en que el SynchronizationContext de
        // Avalonia retome ahí solo tras el await) para que quede a prueba de cambios futuros —
        // mismo patrón que ya usa el resto del proyecto (ver SplashWindow.axaml.cs).
        Dispatcher.UIThread.Post(RecalcFiltered);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    /// <summary>Cambió el idioma de la UI o de los datos: refresca el conteo y las etiquetas de los chips.</summary>
    public void OnLocaleChanged()
    {
        InvalidateCatalogIfLanguageChanged();
        if (_resultCountText.Length > 0) ResultCountText = string.Format(_loc["Picker_Results"], _lastResultCount);
        foreach (var chip in TypeChips) chip.RefreshLabel();
    }

    // El catálogo (_allSpecies) trae textos ya resueltos al construirse (nombre de especie, nombres de forma,
    // texto de búsqueda) y se construye UNA vez por sesión: si cambia el idioma hay que tirarlo. «Firma» = idioma de la
    // UI + idioma de los datos con que se armó; si no cambió, no se hace nada (OnLocaleChanged se llama por ambos eventos).
    private string? _catalogLanguageSignature;
    private int _catalogGeneration;

    private string CurrentLanguageSignature() => $"{_loc.CurrentLocale}|{DataLanguageService.Instance.PkhexCode}";

    private void InvalidateCatalogIfLanguageChanged()
    {
        var signature = CurrentLanguageSignature();
        if (_catalogLanguageSignature == null || _catalogLanguageSignature == signature) return; // aún no se construyó, o mismo idioma

        lock (_lock)
        {
            _catalogGeneration++;      // un prefetch en curso con el idioma viejo ya no puede publicar su resultado
            _allSpecies = new();
            _preloadTask = null;       // IsPreloadReady vuelve a false → la próxima apertura espera/arma el catálogo nuevo
            _catalogLanguageSignature = null;
        }
        _lastRenderedList = null;      // fuerza el rebuild del cover flow en la próxima apertura
        if (IsVisible) IsVisible = false; // con el modal abierto mostraría tarjetas viejas: se cierra
    }

    public string ResultCountText
    {
        get => _resultCountText;
        set => Set(ref _resultCountText, value);
    }

    public ObservableCollection<SpeciesPickerItemViewModel> Species   { get; }
    public ObservableCollection<TypeChipViewModel>          TypeChips { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Botón "Crear" del footer — antes no existía y seleccionar un Pokémon
    /// dependía de hacer click DOS veces sobre la misma tarjeta (una para enfocarla, otra
    /// —ahora que ya estaba enfocada— para seleccionarla), lo que se sentía como un
    /// "doble click" no documentado. Ahora clickear una tarjeta SIEMPRE solo mueve el foco
    /// (ver <see cref="FocusItem"/>), nunca selecciona — la única forma de confirmar es este
    /// comando, explícito.</summary>
    public ICommand CreateCommand { get; }

    // ── Cover flow (Pantalla 4 / mockups/pokemon_picker) — SEGUNDA VERSIÓN tras feedback
    // visual: la primera entrega usaba 5 "slots" fijos que solo re-bindeaban qué especie
    // mostraban, sin posición propia — cambiar de foco era un salto instantáneo, sin
    // transición (a diferencia del carrusel de revisión de Pantalla 3, que sí desliza).
    // Ahora usa EXACTAMENTE el mismo mecanismo que ese carrusel: todos los ítems de
    // `Species` están en el árbol visual (ItemsControl sobre un Canvas en el XAML) y se
    // posicionan/dimensionan según su distancia al índice enfocado en UpdateCardLayout, con
    // Transitions en el XAML sobre esas propiedades para la animación. Puede haber hasta 386
    // ítems realizados a la vez (sin virtualizar) — mismo approach que ya usa el carrusel de
    // revisión sin problemas de rendimiento reportados, pero es lo primero a mirar si algún
    // filtro con muchos resultados se siente lento.
    private const double CanvasCenterX = 300;
    private const double CardSpacing   = 110;
    private const double FocusedWidth  = 170;

    private int _currentIndex;
    public int CurrentIndex
    {
        get => _currentIndex;
        private set
        {
            if (_currentIndex == value) return;
            _currentIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FocusedItem));
            OnPropertyChanged(nameof(ScrubberPositionText));
            OnPropertyChanged(nameof(ScrubberValue));
            UpdateCardLayout();
            (CreateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public SpeciesPickerItemViewModel? FocusedItem => ItemAt(CurrentIndex);

    private SpeciesPickerItemViewModel? ItemAt(int index) =>
        index >= 0 && index < Species.Count ? Species[index] : null;

    /// <summary>Calcula tamaño/opacidad/posición/Z de UNA tarjeta según su distancia al índice
    /// enfocado. Separado de <see cref="UpdateCardLayout"/> para poder aplicarlo también sobre
    /// tarjetas que TODAVÍA no están en <see cref="Species"/> — ver el comentario grande en
    /// RecalcFiltered sobre por qué importa hacerlo ANTES de agregarlas.</summary>
    private void ApplyCardLayout(SpeciesPickerItemViewModel card, int index, int focusIndex)
    {
        int distance = Math.Abs(index - focusIndex);

        card.IsFocused = distance == 0;

        card.CardWidth = distance switch
        {
            0 => FocusedWidth,
            1 => 130,
            2 => 105,
            _ => 95
        };

        card.CardOpacity = distance switch
        {
            0 => 1.0,
            1 => 0.7,
            2 => 0.4,
            3 => 0.2,
            _ => 0.0
        };

        card.CardZIndex = 100 - distance;
        card.CardLeft = CanvasCenterX + (index - focusIndex) * CardSpacing - card.CardWidth / 2;
    }

    /// <summary>Recalcula tamaño/opacidad/posición/Z de TODAS las tarjetas en base a su
    /// distancia al índice enfocado — mismo criterio y mismos números de tier que
    /// MainWindowViewModel.UpdateCardLayout (carrusel de revisión), adaptados a tarjetas más
    /// chicas. La tarjeta enfocada siempre queda centrada en CanvasCenterX.</summary>
    private void UpdateCardLayout()
    {
        for (int i = 0; i < Species.Count; i++)
            ApplyCardLayout(Species[i], i, CurrentIndex);
    }

    /// <summary>Texto del scrubber: <c>"{posición} / {total} · {nombre actual}"</c>. Vacío
    /// si no hay resultados (filtro sin matches).</summary>
    public string ScrubberPositionText => Species.Count == 0
        ? string.Empty
        : $"{CurrentIndex + 1} / {Species.Count} · {FocusedItem?.FullName}";

    public string FirstDexText => Species.Count > 0 ? Species[0].DexNumberText : string.Empty;
    public string LastDexText  => Species.Count > 0 ? Species[^1].DexNumberText : string.Empty;

    /// <summary>Rango del slider — Avalonia no acepta Maximum == Minimum, por eso el piso de
    /// 1 aunque no haya resultados (el slider queda deshabilitado en la vista en ese caso,
    /// ver <c>IsEnabled="{Binding HasResults}"</c>).</summary>
    public int ScrubberMax => Math.Max(1, Species.Count);
    public bool HasResults => Species.Count > 0;

    /// <summary>1-based para el <c>Slider</c> (más natural para mostrar en UI que un índice
    /// 0-based). Setter viene del arrastre del slider — reentra a <see cref="CurrentIndex"/>
    /// pero el guard de "if (_currentIndex == value) return" en el setter de CurrentIndex ya
    /// corta cualquier loop de binding.</summary>
    public int ScrubberValue
    {
        get => CurrentIndex + 1;
        set => FocusIndex(value - 1);
    }

    /// <summary>Paso de "salto rápido" (Shift+flecha) — proporcional al set filtrado actual
    /// (10%, mínimo 1) en vez de un número fijo, por pedido explícito: con un filtro de tipo
    /// activo el set puede ser mucho más chico que el total de 386, y saltar de a 10 fijo
    /// sería demasiado en ese caso.</summary>
    public int JumpStep => Math.Max(1, (int)Math.Round(Species.Count * 0.1));

    /// <summary>Mueve el foco del cover flow, clampeado al set filtrado actual. Usada por
    /// teclado (±1) y scroll del mouse (delta acumulado).</summary>
    public void MoveBy(int delta)
    {
        if (Species.Count == 0) return;
        FocusIndex(CurrentIndex + delta);
    }

    public void FocusIndex(int index)
    {
        if (Species.Count == 0) return;
        CurrentIndex = Math.Clamp(index, 0, Species.Count - 1);
    }

    /// <summary>Click sobre cualquier tarjeta del cover flow (enfocada o no) — SIEMPRE solo
    /// mueve el foco a esa tarjeta, nunca selecciona (ver <see cref="CreateCommand"/> para el
    /// único camino real de selección). Antes esto tomaba un offset relativo (-2..2) porque
    /// las tarjetas eran 5 slots fijos sin identidad propia; ahora cada tarjeta ES un
    /// SpeciesPickerItemViewModel real con su propio DataContext, así que alcanza con buscar
    /// su índice en <see cref="Species"/>.</summary>
    public void FocusItem(SpeciesPickerItemViewModel item)
    {
        var index = Species.IndexOf(item);
        if (index >= 0) FocusIndex(index);
    }

    public void JumpForward()  => MoveBy(JumpStep);
    public void JumpBackward() => MoveBy(-JumpStep);
    public void MoveHome()     => FocusIndex(0);
    public void MoveEnd()      => FocusIndex(Species.Count - 1);

    /// <summary>
    /// Abre el modal para el slot indicado. `maxSpeciesId` viene de
    /// `save.MaxSpeciesID` (PKHeX.Core) — así el listado queda acotado a las especies que
    /// existen en la generación del save cargado, sin necesitar una columna "Generation" en
    /// nuestra propia DB (ver `context.md` para el detalle de esta decisión).
    /// </summary>
    public void Open(bool isParty, int boxIndex, int slotIndex, int maxSpeciesId)
    {
        PrepareOpen(isParty, boxIndex, slotIndex, maxSpeciesId);
        RecalcFiltered();
        IsVisible = true;
    }

    private const int PopulateBatchSize = 30;

    // Mientras se arma el cover flow por tandas, la vista está "visible" (hace falta para que
    // Avalonia cree y mida las tarjetas de a poco, intercalado con el render) pero con opacidad
    // 0 — sin esto el usuario vería las tarjetas apareciendo a medias detrás del loader.
    private bool _isPopulating;
    public double ViewOpacity => _isPopulating ? 0.0 : 1.0;

    /// <summary>Igual que <see cref="Open"/>, pero agrega las tarjetas al cover flow POR TANDAS,
    /// cediendo el hilo de UI entre una y otra. Así la UI no se congela mientras se arma (el
    /// loader puede animarse de verdad y <paramref name="onProgress"/> reporta avance real
    /// 0.0–1.0) — el Open() sincrónico bloqueaba el hilo de UI de una sola vez, y cualquier
    /// animación XAML, que también corre en ese hilo, quedaba congelada. Si el contenido a
    /// mostrar es el mismo que ya está en pantalla (o es chico), no hay nada que repartir y
    /// cae al camino sincrónico de siempre.</summary>
    public async Task OpenAsync(bool isParty, int boxIndex, int slotIndex, int maxSpeciesId,
                                Action<double>? onProgress = null)
    {
        PrepareOpen(isParty, boxIndex, slotIndex, maxSpeciesId);

        var list = ComputeFiltered(_filteredMaxSpeciesId);
        var contentChanged = _lastRenderedList == null || !list.SequenceEqual(_lastRenderedList);

        if (!contentChanged || list.Count <= PopulateBatchSize)
        {
            RecalcFiltered();
            IsVisible = true;
            return;
        }

        _isPopulating = true;
        OnPropertyChanged(nameof(ViewOpacity));
        IsVisible = true;
        try
        {
            // Mismo motivo que en RecalcFiltered: layout final calculado ANTES de agregar cada
            // tarjeta, para que no nazcan animando de 0 al valor real.
            for (int i = 0; i < list.Count; i++)
                ApplyCardLayout(list[i], i, focusIndex: 0);

            Species.Clear();
            for (int i = 0; i < list.Count; i += PopulateBatchSize)
            {
                var end = Math.Min(i + PopulateBatchSize, list.Count);
                for (int j = i; j < end; j++)
                    Species.Add(list[j]);

                onProgress?.Invoke((double)end / list.Count);

                // Render: deja que se procese el layout de esta tanda y que la UI pinte (y
                // que las animaciones del loader avancen) antes de la siguiente.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            _lastRenderedList = list;
            FinishRecalc(list);
        }
        finally
        {
            _isPopulating = false;
            OnPropertyChanged(nameof(ViewOpacity));
        }
    }

    private void PrepareOpen(bool isParty, int boxIndex, int slotIndex, int maxSpeciesId)
    {
        _isParty         = isParty;
        _targetBoxIndex  = boxIndex;
        _targetSlotIndex = slotIndex;

        EnsureSpeciesLoaded();

        _filteredMaxSpeciesId = maxSpeciesId;

        if (TypeChips.Count == 0)
        {
            TypeChips.Add(new TypeChipViewModel { TypeName = TypeChipViewModel.AllKey, IsSelected = true });
            foreach (var t in AllTypeNames)
                TypeChips.Add(new TypeChipViewModel { TypeName = t });
        }
        // A propósito NO se resetea acá el chip de tipo seleccionado ni SearchText a sus
        // valores por default — se preserva el último filtro/búsqueda que el usuario haya
        // usado entre una apertura y la siguiente (decisión explícita, no un olvido: además
        // de ser más cómodo para crear varios Pokémon del mismo tipo seguidos, es lo que hace
        // que RecalcFiltered() de abajo detecte "mismo filtro que la vez pasada" y salte el
        // rebuild completo del cover flow — que es carísimo porque el ItemsControl no está
        // virtualizado. Si en algún momento se vuelve a resetear acá, hay que tener en cuenta
        // que se pierde ese salteo de rebuild en el caso común de reabrir sin tocar nada).
        // RecalcFiltered() igual hace falta llamarla siempre: _filteredMaxSpeciesId puede
        // haber cambiado (otro save, con otra generación) aunque el tipo/búsqueda sigan
        // iguales, y ella sola decide si el resultado cambió o no.
    }

    public void SelectTypeChip(TypeChipViewModel chip)
    {
        foreach (var c in TypeChips)
            c.IsSelected = c == chip;
        RecalcFiltered();
    }

    private int _filteredMaxSpeciesId = int.MaxValue;

    // Formas regionales (Fase 2 de formas) que existen en el juego del save abierto. A
    // diferencia de _allSpecies (válido para toda la sesión), esto SÍ depende del save: en Gen7
    // salen las de Alola, en Gen8 las de Galar/Hisui, en Gen4 ninguna. Vacío = no se muestra
    // ninguna tarjeta de forma. Lo setea MainWindowViewModel antes de abrir el modal.
    private HashSet<(int Species, byte Form)> _availableForms = new();

    /// <summary>Define qué formas regionales se ofrecen para el save abierto (ver
    /// <see cref="RegionalFormCatalog.AvailableIn"/>). Llamar ANTES de
    /// <see cref="WillRebuildOnOpen"/>/<see cref="OpenAsync"/>: el filtro del cover flow las
    /// tiene en cuenta, y si cambian (otro save) fuerza el rebuild solo.</summary>
    public void SetAvailableForms(IEnumerable<RegionalForm> forms) =>
        _availableForms = forms.Select(f => (f.Species, f.Form)).ToHashSet();

    // Formas NO regionales con encuentro legal propio para el juego del save (Zygarde 10%, Ash-Greninja,
    // Lycanroc Nocturno... ver AlternateFormCatalog): también son tarjetas propias. Igual que
    // _availableForms, dependen del save y se setean antes de abrir el modal.
    private HashSet<(int Species, byte Form)> _availableAltForms = new();

    /// <summary>Formas alternativas que se ofrecen para el save abierto (ver
    /// <see cref="AlternateFormCatalog.AvailableIn"/>). Llamar ANTES de <see cref="WillRebuildOnOpen"/>.</summary>
    public void SetAlternateForms(IEnumerable<(int Species, byte Form)> forms) =>
        _availableAltForms = forms.ToHashSet();

    // Especies que no existen en el juego del save abierto y no se pueden construir legales
    // (ver CreatableSpecies). También depende del save, así que, igual que _availableForms,
    // se setea antes de abrir el modal y cambiarlo fuerza el rebuild solo.
    private HashSet<int> _hiddenSpecies = new();

    /// <summary>Especies que no se ofrecen para el save abierto (ver
    /// <see cref="CreatableSpecies.HiddenIn"/>). Llamar ANTES de <see cref="WillRebuildOnOpen"/>.</summary>
    public void SetHiddenSpecies(IEnumerable<int> species) => _hiddenSpecies = species.ToHashSet();

    // ── Prefetch en background ──────────────────────────────────────────
    // BuildAllSpecies() hace ~1000 queries implícitas de sprite (una decodificación de PNG
    // embebido por especie, vía SpriteService) + la query a pokemon.db — con el pokedex ya
    // extendido a Gen 7 esto dejó de ser instantáneo. Antes se llamaba sincrónico desde
    // Open() (que corre en el hilo de UI, disparado por el click en el "+" de un slot
    // vacío), así que la PRIMERA vez que se abría el modal en la sesión, el modal tardaba en
    // aparecer porque el hilo de UI estaba ocupado decodificando sprites. _allSpecies y el
    // caché de SpriteService son válidos para toda la sesión de la app (no dependen del save
    // cargado), así que alcanza con hacerlo UNA vez — la solución es adelantar ese trabajo a
    // un hilo de background apenas termina de abrirse un save (ver
    // MainWindowViewModel.OpenSaveFromPathAsync), en paralelo con el resto de la carga, así
    // ya está listo (o casi) para cuando el usuario llega a clickear "+". _lock protege la
    // asignación de _allSpecies porque ahora puede escribirse desde el hilo de background
    // (PreloadInBackground) o desde el hilo de UI (fallback en EnsureSpeciesLoaded, por si
    // el usuario clickea "+" antes de que el prefetch haya terminado — ej. justo al abrir un
    // save muy chico).
    private readonly object _lock = new();
    private Task? _preloadTask;
    private volatile int _preloadDone;
    private volatile int _preloadTotal;

    /// <summary>True si no hace falta esperar nada para abrir el modal: el prefetch ya
    /// terminó. A propósito NO trata "_preloadTask == null" como listo — eso significaría
    /// "todavía no arrancó" (ej. si algún día se pierde la llamada a PreloadInBackground en
    /// OpenSaveFromPathAsync, como pasó una vez al mezclar con cambios de otro módulo — ver
    /// WaitForPreloadAsync) y en ese caso SÍ hay trabajo por delante, hay que mostrar el
    /// Loader 5 igual mientras arranca solo. Lo usa MainWindowViewModel.OpenSpeciesPicker
    /// para decidir si vale la pena mostrar el Loader 5 (Pokédex + progreso) — si ya está
    /// listo, ni se muestra, no hace falta ni un parpadeo.</summary>
    public bool IsPreloadReady => _preloadTask != null && _preloadTask.IsCompleted;

    /// <summary>Progreso (0.0–1.0) del prefetch en curso — actualizado desde el hilo de
    /// background dentro de <see cref="BuildAllSpecies"/> a medida que procesa cada especie.
    /// Simples ints <c>volatile</c> en vez de una propiedad con lock: esto es solo para
    /// mostrar una barra aproximada en el Loader 5, no hace falta exactitud transaccional —
    /// un valor leído a mitad de una escritura en el peor caso muestra un frame con el
    /// contador viejo, invisible en la práctica dado que se sondea cada ~80ms.</summary>
    public double PreloadProgress => _preloadTotal == 0 ? 0 : (double)_preloadDone / _preloadTotal;

    /// <summary>Dispara la carga de especies/sprites en background si todavía no arrancó.
    /// Seguro de llamar más de una vez (ej. al abrir varios saves en la misma sesión) —
    /// solo la primera llamada dispara trabajo real. Pensado para invocarse apenas se
    /// confirma la apertura de un save, en paralelo con el resto del loader, NO bloqueante.</summary>
    public void PreloadInBackground()
    {
        if (_preloadTask != null) return;
        var generation = _catalogGeneration;
        var signature = CurrentLanguageSignature();
        _preloadTask = Task.Run(() =>
        {
            var species = BuildAllSpecies(background: true);
            lock (_lock)
            {
                if (generation != _catalogGeneration) return; // el idioma cambió mientras se armaba: descartar
                _allSpecies = species;
                _catalogLanguageSignature = signature;
            }
        });
    }

    /// <summary>Espera a que el prefetch en background termine, sondeando su progreso cada
    /// ~80ms para que el caller (MainWindowViewModel.OpenSpeciesPicker) pueda actualizar el
    /// Loader 5 en vivo. A diferencia de EnsureSpeciesLoaded (fallback SINCRÓNICO, bloquea el
    /// hilo de UI), esto es awaitable — el hilo de UI queda libre para pintar el overlay
    /// mientras se espera. Vuelve inmediatamente si <see cref="IsPreloadReady"/> ya es true
    /// (nada que esperar, el caller ni debería haber llamado a esto en ese caso, pero es
    /// seguro igual). Si nadie llamó a PreloadInBackground antes (flujo distinto al normal),
    /// lo arranca acá mismo como red de seguridad.</summary>
    public async Task WaitForPreloadAsync(Action<double>? onProgress = null)
    {
        if (_preloadTask == null) PreloadInBackground();

        while (_preloadTask != null && !_preloadTask.IsCompleted)
        {
            onProgress?.Invoke(PreloadProgress);
            await Task.WhenAny(_preloadTask, Task.Delay(80));
        }

        onProgress?.Invoke(1.0);
    }

    /// <summary>Garantiza que _allSpecies esté poblado antes de abrir el modal. Camino feliz:
    /// el prefetch ya terminó (o ni hace falta esperar porque _preloadTask está completo) y
    /// esto es instantáneo. Camino de emergencia: nadie llamó a PreloadInBackground antes
    /// (ej. un flujo distinto que abra el picker sin pasar por la apertura de save) o el
    /// usuario ganó la carrera — ahí se hace la carga sincrónica de siempre, mismo costo que
    /// antes, nunca peor.</summary>
    private void EnsureSpeciesLoaded()
    {
        if (_preloadTask != null)
        {
            _preloadTask.Wait();
            return;
        }

        lock (_lock)
        {
            if (_allSpecies.Count == 0)
            {
                _allSpecies = BuildAllSpecies();
                _catalogLanguageSignature = CurrentLanguageSignature();
            }
        }
    }

    private List<SpeciesPickerItemViewModel> BuildAllSpecies(bool background = false)
    {
        // background=true (llamado desde PreloadInBackground, en Task.Run): usa una conexión
        // SQLite propia (ver SpeciesDatabase.GetAllSummariesForBackgroundPrefetch) en vez de
        // la compartida por toda la app, porque en paralelo el hilo de UI está usando esa
        // conexión compartida a fondo (Box/Party/Bag/Trainer durante la apertura del save) —
        // SqliteConnection no soporta uso concurrente desde dos hilos sobre la misma
        // instancia, así que sin esto la query de background terminaba esperando detrás de
        // todo lo demás y en la práctica nunca llegaba a tiempo (esto era la causa real de
        // "el primer click en crear sigue demorando" pese al prefetch).
        // background=false (fallback sincrónico en EnsureSpeciesLoaded, mismo hilo que llama
        // a Open()): no hay otro hilo compitiendo, así que usar la conexión compartida de
        // siempre es seguro y no hace falta pagar el costo de abrir una conexión aparte.
        var summaries = background
            ? SpeciesDatabase.Instance.GetAllSummariesForBackgroundPrefetch()
            : SpeciesDatabase.Instance.GetAllSummaries();

        // Antes esto era un one-liner .Select(...).ToList(); pasado a loop explícito para
        // poder reportar avance (_preloadDone/_preloadTotal) a medida que procesa cada
        // especie — únicamente relevante en el camino background (ver PreloadProgress /
        // WaitForPreloadAsync, consumidos por el Loader 5 en MainWindowViewModel). En el
        // camino sincrónico (background=false) no tiene efecto visible, nadie lo lee.
        if (background) { _preloadTotal = summaries.Count; _preloadDone = 0; }

        // Fase 2 de formas: cada forma regional del catálogo es una tarjeta propia, ubicada justo
        // después de su especie base (mismo número de Pokédex). Se construyen TODAS acá, una vez
        // por sesión (como el resto del catálogo); cuáles se muestran lo decide ComputeFiltered
        // según el juego del save abierto (SetAvailableForms).
        var formsBySpecies = RegionalFormCatalog.All
            .GroupBy(f => f.Species)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Form).ToList());

        var altBySpecies = AlternateFormCatalog.All()
            .Where(a => RegionalFormCatalog.Find(a.Species, a.Form) == null)
            .GroupBy(a => a.Species)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Form).OrderBy(f => f).ToList());

        // Nombre de la especie en el idioma de los datos (PKHeX); pokemon.db (inglés) solo si PKHeX no lo tiene.
        // El texto de búsqueda lleva AMBOS: se puede buscar por el nombre mostrado o por el inglés.
        var speciesNames = DataLanguageService.Instance.Strings.Species;
        string NameOf(int id, string dbName) =>
            id > 0 && id < speciesNames.Count && !string.IsNullOrWhiteSpace(speciesNames[id]) ? speciesNames[id] : Capitalize(dbName);

        var result = new List<SpeciesPickerItemViewModel>(summaries.Count + RegionalFormCatalog.All.Count + altBySpecies.Count);
        foreach (var s in summaries)
        {
            result.Add(new SpeciesPickerItemViewModel
            {
                Id            = s.Id,
                DisplayName   = NameOf(s.Id, s.Name),
                DexNumberText = $"#{s.Id:000}",
                Types         = s.Types.Select(Capitalize).ToList(),
                Sprite        = SpriteService.Instance.GetPokemonSprite(s.Id, false),
                SearchBlob    = $"{s.Name} {NameOf(s.Id, s.Name)} {s.Id}".ToLowerInvariant(),
            });

            if (formsBySpecies.TryGetValue(s.Id, out var forms))
            {
                foreach (var rf in forms)
                {
                    result.Add(new SpeciesPickerItemViewModel
                    {
                        Id            = s.Id,
                        Form          = rf.Form,
                        RegionLabel   = rf.Region,
                        DisplayName   = FormNameFormatter.Format(NameOf(s.Id, s.Name), s.Id, rf.Form),
                        DexNumberText = $"#{s.Id:000}",
                        // Tipos de ESTA forma (fila propia de la forma en pokemon.db).
                        Types         = RegionalFormCatalog.GetTypeNames(rf).ToList(),
                        Sprite        = SpriteService.Instance.GetPokemonFormSprite(s.Id, rf.Form),
                        // Buscar "alola" o "galar" encuentra todas las formas de esa región.
                        SearchBlob    = $"{s.Name} {NameOf(s.Id, s.Name)} {rf.Region} {s.Id}".ToLowerInvariant(),
                    });
                }
            }
            if (altBySpecies.TryGetValue(s.Id, out var alts))
            {
                foreach (var af in alts)
                {
                    var formName = AlternateFormCatalog.FormName(s.Id, af, FormNameFormatter.PkhexLang) ?? $"{af}";
                    var formTypes = SpeciesFormResolver.GetFormTypes(s.Id, af);
                    result.Add(new SpeciesPickerItemViewModel
                    {
                        Id            = s.Id,
                        Form          = af,
                        RegionLabel   = formName,
                        DisplayName   = FormNameFormatter.Format(NameOf(s.Id, s.Name), s.Id, af),
                        DexNumberText = $"#{s.Id:000}",
                        // Tipos de la forma si tiene fila propia en pokemon.db; si no, los de la especie.
                        Types         = (formTypes.Count > 0 ? formTypes : s.Types.Select(Capitalize).ToList()).ToList(),
                        Sprite        = SpriteService.Instance.GetPokemonFormSprite(s.Id, af),
                        SearchBlob    = $"{s.Name} {NameOf(s.Id, s.Name)} {formName} {s.Id}".ToLowerInvariant(),
                    });
                }
            }
            if (background) _preloadDone++;
        }
        return result;
    }

    // Última lista mostrada en el cover flow — para poder saltear el Clear()+Add() de abajo
    // cuando el filtro resultante es EXACTAMENTE el mismo que ya está en pantalla (ver nota
    // larga más abajo). Comparación por referencia (los SpeciesPickerItemViewModel de
    // _allSpecies son instancias estables, se crean una sola vez en BuildAllSpecies), no por
    // valor — más barato y es justo lo que queremos detectar (el MISMO objeto ya realizado
    // en el árbol visual, no una copia con los mismos datos).
    private List<SpeciesPickerItemViewModel>? _lastRenderedList;

    /// <summary>Set filtrado (especies hasta <paramref name="maxSpeciesId"/>, tipo y búsqueda
    /// actuales). Barato — LINQ sobre una lista en memoria; lo caro es siempre el rebuild
    /// visual del cover flow, no esto.</summary>
    private List<SpeciesPickerItemViewModel> ComputeFiltered(int maxSpeciesId)
    {
        var selectedType = TypeChips.FirstOrDefault(c => c.IsSelected)?.TypeName ?? TypeChipViewModel.AllKey;
        var search = SearchText.Trim().ToLowerInvariant();

        var filtered = _allSpecies.Where(s => s.Id <= maxSpeciesId
                                              && (s.Form == 0
                                                  ? !_hiddenSpecies.Contains(s.Id)
                                                  : _availableForms.Contains((s.Id, s.Form))
                                                    || _availableAltForms.Contains((s.Id, s.Form))));

        if (selectedType != TypeChipViewModel.AllKey)
            filtered = filtered.Where(s => s.Types.Contains(selectedType));

        if (search.Length > 0)
            filtered = filtered.Where(s => s.SearchBlob.Contains(search));

        return filtered.ToList();
    }

    /// <summary>True si abrir el modal AHORA obligaría a reconstruir el cover flow completo
    /// (la operación pesada, sincrónica en el hilo de UI) — o porque el catálogo todavía no
    /// terminó de cargarse, o porque el set a mostrar difiere del que ya está en pantalla.
    /// Lo usa MainWindowViewModel.OpenSpeciesPicker para decidir si mostrar el Loader 5: solo
    /// tiene sentido avisar cuando de verdad va a haber una espera.</summary>
    public bool WillRebuildOnOpen(int maxSpeciesId)
    {
        if (!IsPreloadReady || _lastRenderedList == null) return true;
        return !ComputeFiltered(maxSpeciesId).SequenceEqual(_lastRenderedList);
    }

    private void RecalcFiltered()
    {
        var list = ComputeFiltered(_filteredMaxSpeciesId);

        // El ItemsControl del cover flow es a propósito NO virtualizado (Canvas, para poder
        // animar posición/tamaño por distancia al foco — ver comentario arriba de
        // UpdateCardLayout), y cada tarjeta trae además su propio ItemsControl anidado para
        // los badges de tipo. Eso significa que un Species.Clear() + Add() de ~500-1000
        // ítems obliga a Avalonia a destruir y volver a realizar esa misma cantidad de
        // controles completos, sincrónico en el hilo de UI — y ANTES esto se hacía SIEMPRE,
        // en cada Open(), aunque el resultado filtrado fuera idéntico al que ya estaba
        // mostrado (typical: abrir el modal, cancelar, volver a abrir el mismo save sin
        // tocar el buscador ni los chips de tipo — Open() resetea ambos a sus valores por
        // default, así que el filtro que sale acá es el mismo "Todos" de siempre). Ese
        // Clear()+Add() innecesario era el grueso real de los ~4s reportados en el segundo
        // click en adelante (la carga de especies/sprites en sí ya está resuelta por
        // PreloadInBackground/EnsureSpeciesLoaded, eso es costo de UNA sola vez). Acá se
        // saltea ese trabajo cuando el set filtrado es exactamente el mismo (mismos objetos,
        // mismo orden) que ya está en Species — sigue haciendo falta reaplicar el resto
        // (foco al primer resultado, layout, textos), que es aritmética barata sobre una
        // lista que Avalonia no tiene que volver a materializar.
        var contentChanged = _lastRenderedList == null || !list.SequenceEqual(_lastRenderedList);
        if (contentChanged)
        {
            // El foco siempre vuelve a 0 tras un cambio de contenido (ver más abajo), así que
            // podemos calcular acá mismo el layout final (CardWidth/CardOpacity/CardLeft) de
            // cada tarjeta CON foco=0, ANTES de meterla en Species. Si no hiciéramos esto, cada
            // Border recién creado arrancaría bindeado a los valores default de
            // SpeciesPickerItemViewModel (CardWidth/CardOpacity en 0) y, un instante después,
            // UpdateCardLayout() de más abajo los pisaría con el valor real — y como el Border
            // tiene <Transitions> en Width/Opacity, ESE salto de 0 al valor real cuenta como un
            // cambio de propiedad y dispara la animación de 320ms, para las ~500-1000 tarjetas
            // nuevas A LA VEZ. Ese "storm" de transiciones simultáneas es trabajo de
            // composición/render real, aparte del costo de crear los controles en sí. Con el
            // valor final ya puesto ANTES de que el binding se evalúe por primera vez, no hay
            // cambio de propiedad que transicionar — la tarjeta aparece directo en su lugar.
            // (UpdateCardLayout() más abajo sigue corriendo igual — para estas tarjetas es un
            // no-op porque ya quedaron con el valor correcto, gracias al Equals() de Set().)
            for (int i = 0; i < list.Count; i++)
                ApplyCardLayout(list[i], i, focusIndex: 0);

            Species.Clear();
            foreach (var s in list)
                Species.Add(s);
            _lastRenderedList = list;
        }

        FinishRecalc(list);
    }

    /// <summary>Cola común de RecalcFiltered / OpenAsync: textos, foco al primer resultado y
    /// layout, una vez que Species ya tiene el contenido definitivo.</summary>
    private void FinishRecalc(List<SpeciesPickerItemViewModel> list)
    {
        _lastResultCount = list.Count;
        ResultCountText  = string.Format(_loc["Picker_Results"], list.Count);

        // Cambió el set filtrado (búsqueda o tipo) — el índice anterior ya no tiene sentido
        // (podría apuntar a una especie que el nuevo filtro excluye), así que vuelve siempre
        // al primer resultado. _currentIndex se asigna directo (no vía el setter de
        // CurrentIndex) para no pisar el "if (_currentIndex == value) return" cuando el
        // índice ya era 0 — acá SIEMPRE hace falta recalcular el layout porque el CONTENIDO
        // detrás del índice 0 cambió (son objetos SpeciesPickerItemViewModel distintos),
        // aunque el índice en sí no se haya movido.
        _currentIndex = 0;
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(FocusedItem));
        OnPropertyChanged(nameof(ScrubberPositionText));
        OnPropertyChanged(nameof(ScrubberValue));
        UpdateCardLayout();
        OnPropertyChanged(nameof(FirstDexText));
        OnPropertyChanged(nameof(LastDexText));
        OnPropertyChanged(nameof(ScrubberMax));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(JumpStep));
        (CreateCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void Select(SpeciesPickerItemViewModel? item)
    {
        if (item == null) return;
        IsVisible = false;
        SpeciesChosen?.Invoke(item.Id, item.Form, _isParty, _targetBoxIndex, _targetSlotIndex);
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
