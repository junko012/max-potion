using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Qué almacén se está mirando — Mochila (pouches normales) o el almacén aparte que
/// usan las generaciones tempranas (InventoryType.PCItems/FreeSpace, ver
/// BagPouchDatabase.IsAlternateStorage). Cada uno tiene su propio set de tiles — no comparten
/// grid, la selección de tile de un storage no sobrevive al cambio al otro.</summary>
public enum BagStore { Bag, Pc }

/// <summary>
/// Orquestador del modo Mochila (panel central: grilla de categorías, navegando el "origen"
/// Mochila/PC igual que BoxViewModel navega Cajas; panel derecho: lista de ítems de la tile
/// elegida). Mismo patrón "edición pendiente hasta exportar" que
/// PokemonEditorViewModel/TrainerViewModel — cada cambio de cantidad pasa por
/// EditSessionService.RecordEdit vía la clave sentinel PokemonSlotKey.ForBag(InventoryType),
/// nunca se escribe al SaveFile real (ver GAP CRÍTICO en CLAUDE.md/context.md).
///
/// ⚠️ Corrección de sesión "categorización de Mochila": el diseño original tenía Mochila/PC
/// como un toggle y las categorías se navegaban una por una (◀ categoría ▶, como si CADA
/// categoría fuera una "caja"). Ahora es al revés, para calzar con el lenguaje visual de
/// BoxView: Mochila/PC es el "origen" que se navega con ◀▶ (equivalente a Caja 1/Caja 2), y
/// las categorías son la GRILLA central (equivalente a los Pokémon dentro de una caja) — se
/// eligen clickeando una tile, no navegando de a una.
/// </summary>
public class BagViewModel : INotifyPropertyChanged
{
    private readonly ItemInventoryService _inventoryService;
    private readonly EditSessionService   _editSession;

    private List<BagPouchTileViewModel> _bagPouches = new();
    private List<BagPouchTileViewModel> _pcPouches  = new();
    private List<BagItemRowViewModel>   _allItemsForPouch = new();

    private BagStore _currentStore = BagStore.Bag;
    private BagPouchTileViewModel? _selectedPouch;
    private string _searchText = string.Empty;

    private readonly LocalizationService _loc;
    public LocalizationService Loc => _loc;

    public BagViewModel(LocalizationService loc, ItemInventoryService inventoryService, EditSessionService editSession)
    {
        _loc              = loc;
        _inventoryService = inventoryService;
        _editSession      = editSession;

        Pouches = new ObservableCollection<BagPouchTileViewModel>();
        Items   = new ObservableCollection<BagItemRowViewModel>();

        SelectPouchCommand    = new RelayCommand<BagPouchTileViewModel>(tile => SelectPouch(tile));
        PreviousStoreCommand  = new RelayCommand(PreviousStore);
        NextStoreCommand      = new RelayCommand(NextStore);
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

    /// <summary>Grilla central — todas las categorías de ítems del origen actual (Mochila o
    /// PC). Equivalente a <c>BoxViewModel.VisibleSlots</c>.</summary>
    public ObservableCollection<BagPouchTileViewModel> Pouches { get; }
    public ObservableCollection<BagItemRowViewModel>   Items   { get; }

    public ICommand SelectPouchCommand   { get; }
    public ICommand PreviousStoreCommand { get; }
    public ICommand NextStoreCommand     { get; }

    /// <summary>True si el save actual tiene algún origen "de almacén" aparte de la mochila
    /// (InventoryType.PCItems/FreeSpace, ver BagPouchDatabase.IsAlternateStorage) — controla si
    /// tiene sentido mostrar la navegación ◀▶ de origen. Gen 3+ no debería traer ninguno (a
    /// confirmar con InventoryApiDiagnosticTests contra saves reales, no asumido).</summary>
    public bool ShowStoreNav => _pcPouches.Count > 0;

    public BagStore CurrentStore
    {
        get => _currentStore;
        private set
        {
            Set(ref _currentStore, value);
            OnPropertyChanged(nameof(CurrentStoreName));
            OnPropertyChanged(nameof(StoreNavText));
        }
    }

    /// <summary>Título del origen actual — equivalente a BoxViewModel.CurrentBoxName.</summary>
    public string CurrentStoreName => CurrentStore == BagStore.Bag ? _loc["Bag_Store_Bag"] : _loc["Bag_Store_Pc"];

    /// <summary>"Almacenamiento 1 / 2" — equivalente a BoxViewModel.BoxNavText. Vacío si no hay
    /// más de un origen (nada que navegar).</summary>
    public string StoreNavText =>
        ShowStoreNav ? string.Format(_loc["Bag_StoreNav"], CurrentStore == BagStore.Bag ? 1 : 2) : string.Empty;

    public string SearchText
    {
        get => _searchText;
        set { Set(ref _searchText, value); ApplySearchFilter(); }
    }

    public string PouchTitle => _selectedPouch?.DisplayName ?? string.Empty;

    public string PouchCountText
    {
        get
        {
            if (_selectedPouch == null) return string.Empty;
            var owned = _allItemsForPouch.Count(i => i.Owned);
            return string.Format(_loc["Bag_OwnedOf"], owned, _allItemsForPouch.Count);
        }
    }

    /// <summary>Releé todos los pouches del save actual y arma las tiles. Llamado desde
    /// <c>PerformClearEdits</c> ("Limpiar ediciones" — acción rápida y poco frecuente, sin
    /// loader visible, no necesita progreso granular). Para abrir un save usar
    /// <see cref="InitializeAsync"/> en su lugar, ver ese método para el porqué.</summary>
    public void Initialize()
    {
        InitializeCore();
        PrewarmSprites();
    }

    /// <summary>Versión para <c>MainWindowViewModel.OpenSaveFromPathAsync</c> — mismo trabajo
    /// que <see cref="Initialize"/>, pero el prewarming de sprites (la parte que escala con
    /// el tamaño del catálogo de ítems de la generación, ver <see cref="PrewarmSpritesAsync"/>)
    /// va reportando avance real categoría por categoría vía <paramref name="onProgress"/>
    /// (0.0-1.0) y cediendo el hilo al dispatcher entre cada una. Bug encontrado en sesión
    /// "aprovechar los loaders al máximo": para saves de generaciones con catálogos grandes
    /// (Gen4+: ~100 MTs/MOs vs ~50 de Gen1-3, más objetos generales) el prewarming eager de
    /// TODAS las categorías podía tardar perceptiblemente más — sin progreso granular, la
    /// barra se quedaba pegada en el valor del paso anterior (85%) todo ese rato y después
    /// saltaba a 100% de golpe, sintiéndose "trabado" en vez de progresando. Con esto la
    /// barra avanza de forma continua sin importar cuántas categorías/ítems tenga que
    /// procesar la generación del save — el tiempo total puede variar, pero la sensación de
    /// que "algo está pasando" ya no depende de eso.</summary>
    public async Task InitializeAsync(Action<double>? onProgress = null)
    {
        InitializeCore();
        await PrewarmSpritesAsync(onProgress);
    }

    private void InitializeCore()
    {
        _bagPouches = _inventoryService.GetPouches(useAlternateStorage: false)
            .Select(p => new BagPouchTileViewModel(p.Type, p.OwnedCount, p.LegalItemCount))
            .ToList();
        // Mismas categorías que Mochila (pokemon.db no distingue origen), pero con cantidades
        // leídas del pouch de PC real — puede quedar vacío si esta generación no tiene almacén
        // aparte (Gen4+, ver ItemInventoryService.GetPouches).
        _pcPouches = _inventoryService.GetPouches(useAlternateStorage: true)
            .Select(p => new BagPouchTileViewModel(p.Type, p.OwnedCount, p.LegalItemCount))
            .ToList();

        OnPropertyChanged(nameof(ShowStoreNav));
        SwitchStore(BagStore.Bag);
    }

    /// <summary>Decodifica y cachea (<c>SpriteService.GetItemSprite</c>) el ícono de TODOS
    /// los ítems de TODAS las categorías — Mochila y PC, no solo la que se ve primero.
    /// Sesión "premisa de carga única": ya movimos <c>Bag.Initialize()</c> a que corra una
    /// sola vez al abrir el save (no en cada click al rail), pero el usuario seguía viendo
    /// el PRIMER click a Mochila lento igual — la causa era <c>ItemNameToSpriteConverter</c>,
    /// que decodifica cada PNG de ítem recién cuando el <c>Image</c> de esa fila se renderiza
    /// por primera vez (costo de RENDER, no de datos). Acá se fuerza esa decodificación de
    /// antemano, durante el Loader 1.
    ///
    /// ⚠️ Sesión "análisis con calma" (revertido acá): una versión anterior de este método
    /// llamaba a <c>GetOrBuildRows</c> para armar y cachear las filas COMPLETAS
    /// (<c>BagItemRowViewModel</c>) de TODAS las categorías acá, no solo decodificar sprites
    /// — la intención era evitar también el costo de reconstruir filas al revisitar una
    /// categoría. Pero la causa real de "la primera vez tarda" resultó ser OTRA (el
    /// <c>ItemsControl</c> sin virtualizar de BagItemListView, ver ese archivo — ~10
    /// controles por fila × ~100 ítems = ~1000 controles realizados de una para catálogos
    /// grandes de Gen4+), y cachear TODAS las filas de TODAS las categorías de antemano
    /// —incluidas las que el usuario nunca visita— sumaba varios cientos de objetos y
    /// suscripciones a eventos retenidos en memoria sin necesidad, lo que parece haber
    /// generado presión de GC suficiente como para causar pausas esporádicas en OTRAS partes
    /// de la UI (reportado: "a veces también demora Pokémon", que no tiene relación alguna
    /// con Mochila). Con la virtualización ya resuelviendo el costo real, construir las filas
    /// de una categoría solo cuando el usuario la visita de verdad (ver
    /// <see cref="GetOrBuildRows"/>, todavía cachea para revisitas) alcanza y sobra — acá solo
    /// se precalienta el sprite, mucho más liviano y acotado.</summary>
    private void PrewarmSprites()
    {
        foreach (var tile in _bagPouches)
            foreach (var info in _inventoryService.GetItems(tile.Type, useAlternateStorage: false))
                SpriteService.Instance.GetItemSprite(info.Name);

        foreach (var tile in _pcPouches)
            foreach (var info in _inventoryService.GetItems(tile.Type, useAlternateStorage: true))
                SpriteService.Instance.GetItemSprite(info.Name);
    }

    /// <summary>Misma decodificación que <see cref="PrewarmSprites"/>, pero reportando avance
    /// real (0.0-1.0) categoría por categoría y cediendo el hilo al dispatcher en prioridad
    /// Render entre cada una — sin esto, Avalonia coalescería todos los cambios de progreso
    /// en un solo repintado final (mismo mecanismo ya usado para el resto de la barra en
    /// MainWindowViewModel.OpenSaveFromPathAsync). Recorre Mochila y PC como una sola lista
    /// para que el progreso sea proporcional al trabajo TOTAL, no a cada store por
    /// separado.</summary>
    private async Task PrewarmSpritesAsync(Action<double>? onProgress)
    {
        var allTiles = _bagPouches.Select(t => (Tile: t, Alt: false))
            .Concat(_pcPouches.Select(t => (Tile: t, Alt: true)))
            .ToList();

        if (allTiles.Count == 0) { onProgress?.Invoke(1.0); return; }

        for (int i = 0; i < allTiles.Count; i++)
        {
            var (tile, alt) = allTiles[i];
            foreach (var info in _inventoryService.GetItems(tile.Type, useAlternateStorage: alt))
                SpriteService.Instance.GetItemSprite(info.Name);

            onProgress?.Invoke((i + 1) / (double)allTiles.Count);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
    }

    // Clamp en los extremos, sin wrap — mismo criterio que BoxViewModel.PreviousBox/NextBox.
    private void PreviousStore()
    {
        if (CurrentStore == BagStore.Pc) SwitchStore(BagStore.Bag);
    }

    private void NextStore()
    {
        if (ShowStoreNav && CurrentStore == BagStore.Bag) SwitchStore(BagStore.Pc);
    }

    private void SwitchStore(BagStore store)
    {
        if (store == BagStore.Pc && _pcPouches.Count == 0) return; // no hay nada que mostrar
        CurrentStore = store;

        var list = store == BagStore.Bag ? _bagPouches : _pcPouches;
        Pouches.Clear();
        foreach (var tile in list) Pouches.Add(tile);

        // Primera tile de la grilla seleccionada por default, para que el panel derecho no
        // arranque vacío — mismo criterio que antes (BoxView, en cambio, no auto-selecciona
        // ningún Pokémon; la diferencia es a propósito: acá la lista de ítems es el contenido
        // principal del panel derecho, no un editor que solo tiene sentido tras elegir algo).
        SelectPouch(Pouches.FirstOrDefault());
    }

    public void SelectPouch(BagPouchTileViewModel? tile)
    {
        if (_selectedPouch != null) _selectedPouch.IsSelected = false;
        _selectedPouch = tile;
        if (_selectedPouch != null) _selectedPouch.IsSelected = true;

        OnPropertyChanged(nameof(PouchTitle));
        LoadItemsForSelectedPouch();
    }

    /// <summary>Cache de filas ya construidas por categoría, LAZY (se llena la primera vez
    /// que el usuario visita de verdad cada categoría, no de antemano) — evita reconstruir
    /// la lista entera (query + <c>BagItemRowViewModel</c> nuevos) cada vez que se revisita
    /// una categoría ya vista en la sesión. No se popula eager en el loader de apertura a
    /// propósito — ver el comentario largo en <see cref="PrewarmSprites"/> sobre por qué eso
    /// se revirtió (memoria/GC, y la causa real de "tarda" resultó ser la falta de
    /// virtualización en BagItemListView, no esto).</summary>
    private readonly Dictionary<(InventoryType Type, bool IsAlt), List<BagItemRowViewModel>> _rowsCache = new();

    /// <summary>Cambió el idioma de los datos: los nombres de ítem ya construidos quedaron en el idioma
    /// anterior, así que se descarta el cache de filas y se reconstruye la categoría visible (las demás
    /// se construyen en su primera visita, como siempre). Las cantidades editadas se reaplican desde
    /// EditSessionService al reconstruir.</summary>
    /// <summary>Cambió el idioma de la UI: refresca los textos calculados (origen, conteos, nombres de
    /// categoría de las tiles y el título del panel).</summary>
    public void OnLocaleChanged()
    {
        foreach (var t in _bagPouches) t.OnLocaleChanged();
        foreach (var t in _pcPouches)  t.OnLocaleChanged();
        ReloadForDataLanguage(); // las descripciones de MT/MO/DT usan una plantilla del idioma de la UI: se reconstruyen las filas
        OnPropertyChanged(nameof(CurrentStoreName));
        OnPropertyChanged(nameof(StoreNavText));
        OnPropertyChanged(nameof(PouchTitle));
        OnPropertyChanged(nameof(PouchCountText));
    }

    public void ReloadForDataLanguage()
    {
        _rowsCache.Clear();
        if (_selectedPouch != null) LoadItemsForSelectedPouch();
    }

    private void LoadItemsForSelectedPouch()
    {
        if (_selectedPouch == null)
        {
            _allItemsForPouch = new List<BagItemRowViewModel>();
            Items.Clear();
            OnPropertyChanged(nameof(PouchCountText));
            return;
        }

        var type = _selectedPouch.Type;
        var isAlt = CurrentStore == BagStore.Pc;
        _allItemsForPouch = GetOrBuildRows(type, isAlt);

        ApplySearchFilter();
        OnPropertyChanged(nameof(PouchCountText));
    }

    private List<BagItemRowViewModel> GetOrBuildRows(InventoryType type, bool isAlt)
    {
        var cacheKey = (type, isAlt);
        if (_rowsCache.TryGetValue(cacheKey, out var cached)) return cached;

        var rows = BuildRowsForCategory(type, isAlt);
        _rowsCache[cacheKey] = rows;
        return rows;
    }

    /// <summary>Construye la lista de filas de UNA categoría — llamado solo bajo demanda,
    /// desde <see cref="GetOrBuildRows"/>, la primera vez que el usuario visita esa categoría
    /// (no eager en el loader, ver <see cref="PrewarmSprites"/>).</summary>
    private List<BagItemRowViewModel> BuildRowsForCategory(InventoryType type, bool isAlt)
    {
        var rows = new List<BagItemRowViewModel>();
        var key = PokemonSlotKey.ForBag(type, isAlt);
        var pendingEdits = _editSession.GetRawEdits(key);

        foreach (var info in _inventoryService.GetItems(type, useAlternateStorage: isAlt))
        {
            // Valor "real" del save primero, para que CapturePristine registre lo que
            // efectivamente hay guardado — igual que PokemonEditorViewModel.Set<T> durante la
            // carga (ver EditSessionService.CapturePristine, solo graba la primera vez que ve
            // la propiedad, así que reabrir el mismo pouch en la sesión no pisa el prístino).
            // BagItemEditValue (no int plano) desde la corrección de sesión "categorización de
            // Mochila": el nombre se captura acá, en el único lugar donde ya lo tenemos posta
            // (info.Name, de ItemInventoryService) — así el modal de revisión nunca más necesita
            // reconstruirlo a partir del ItemId (que ahora es un RawItemId de PKHeX.Core, no
            // sirve para buscar en pokemon.db).
            var shownName = info.DisplayName ?? info.Name;
            _editSession.CapturePristine(key, info.ItemId.ToString(), new BagItemEditValue(shownName, info.Quantity));

            // Reaplicar cualquier edición pendiente de esta sesión — mismo criterio que
            // RestorePendingRibbonEdits en PokemonEditorViewModel.
            var qty = info.Quantity;
            if (pendingEdits != null
                && pendingEdits.TryGetValue(info.ItemId.ToString(), out var pending)
                && pending is BagItemEditValue pendingVal)
            {
                qty = pendingVal.Quantity;
                // El nombre guardado en la edición quedó en el idioma en que se hizo: se refresca al
                // idioma actual para que el modal de revisión no mezcle idiomas (la igualdad de
                // BagItemEditValue ignora el nombre, así que no cambia si cuenta como edición).
                if (pendingVal.ItemName != shownName)
                    _editSession.RecordEdit(key, info.ItemId.ToString(), new BagItemEditValue(shownName, qty));
            }

            var row = new BagItemRowViewModel(
                info.ItemId, info.Name, info.Description, qty, info.PouchMaxCount,
                (itemId, newQty) => _editSession.RecordEdit(key, itemId.ToString(), new BagItemEditValue(shownName, newQty)),
                info.DisplayName);

            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BagItemRowViewModel.Owned))
                    OnPropertyChanged(nameof(PouchCountText));
            };

            rows.Add(row);
        }

        return rows;
    }

    private void ApplySearchFilter()
    {
        Items.Clear();
        foreach (var row in _allItemsForPouch.Where(r => r.MatchesSearch(SearchText)))
            Items.Add(row);
    }
}
