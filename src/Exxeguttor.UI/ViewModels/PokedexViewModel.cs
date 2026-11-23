using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Converters;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Una tarjeta de la grilla — envuelve PokedexAlbumEntry (App layer) con lo
/// mínimo que la vista necesita para pintar el frente. El reverso (evolución/origen) se
/// carga aparte, bajo demanda, en PokedexViewModel.OpenCard — no tiene sentido consultar
/// eso para las ~1000 tarjetas de la grilla si el usuario nunca las toca.</summary>
public class PokedexCardViewModel
{
    public int          SpeciesId { get; }
    /// <summary>0 = especie base; &gt; 0 = forma regional que muestra la tarjeta (ver PokedexAlbumEntry).</summary>
    public byte          Form      { get; }
    public string        Name      { get; }
    public List<string>  Types     { get; }
    public bool           Owned     { get; }

    public PokedexCardViewModel(PokedexAlbumEntry entry)
    {
        SpeciesId = entry.SpeciesId;
        Form      = entry.Form;
        Name      = PokedexNames.Display(entry.SpeciesId, entry.Form, entry.Name);
        Types     = entry.Types;
        Owned     = entry.Owned;
        FrameBrush = BuildFrameBrush(Types, Owned);
    }

    public string DexNumberDisplay => $"#{SpeciesId:000}";

    /// <summary>Sin shiny/female para esta vista — mismo criterio que el spec de diseño
    /// (screen-pokedex-album.md, sección "Tarjeta — frente").</summary>
    public Bitmap? Sprite => SpriteService.Instance.GetPokemonSprite(SpeciesId, form: Form);

    /// <summary>Marco doble estilo trading-card del boceto: degradé con el/los color(es)
    /// de tipo (reusa TypeColorConverter.GetBaseColor, mismos hex en toda la app — no
    /// duplicar la tabla acá) si se posee, gris neutro si no. Calculado una sola vez en el
    /// constructor porque Types/Owned no cambian en la vida de la tarjeta.</summary>
    public IBrush FrameBrush { get; }

    private static IBrush BuildFrameBrush(List<string> types, bool owned)
    {
        if (!owned)
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint   = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#C7C7CC"), 0),
                    new GradientStop(Color.Parse("#E2E2E6"), 1),
                },
            };

        var c1 = TypeColorConverter.GetBaseColor(types.Count > 0 ? types[0] : "normal");
        var c2 = TypeColorConverter.GetBaseColor(types.Count > 1 ? types[1] : types.Count > 0 ? types[0] : "normal");
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(c1, 0), new GradientStop(c2, 1) },
        };
    }
}

/// <summary>Un nodo de evolución ya listo para bindear (sprite + nombre + condición) —
/// espejo de PokedexEvolutionNode (App layer) del lado UI.</summary>
public class PokedexEvolutionNodeViewModel
{
    /// <summary>N.º de Pokédex nacional de la especie base (también para los nodos de forma regional).</summary>
    public int    SpeciesId     { get; }
    public byte   Form          { get; }
    public string Name          { get; }
    public string ConditionText { get; }
    public Bitmap? Sprite => SpriteService.Instance.GetPokemonSprite(SpeciesId, form: Form);

    public PokedexEvolutionNodeViewModel(PokedexEvolutionNode node)
    {
        // Los nodos de la API antigua (GetEvolutionFamily) no traen NationalId: ahí SpeciesId ya es el n.º nacional.
        SpeciesId     = node.NationalId > 0 ? node.NationalId : node.SpeciesId;
        Form          = node.Form;
        Name          = PokedexNames.Display(SpeciesId, Form, node.Name);
        ConditionText = node.ConditionText;
    }
}

/// <summary>Nombre mostrado en la Pokédex: el de PKHeX por id en el idioma de la UI (pokemon.db solo
/// tiene nombres en inglés, y en minúscula) más la etiqueta de forma regional ("Exeggutor de Alola" /
/// "Alolan Exeggutor") vía <see cref="FormNameFormatter"/>. <paramref name="fallbackName"/> solo se usa si
/// PKHeX no tiene nombre para ese id.</summary>
internal static class PokedexNames
{
    public static string Display(int speciesId, byte form, string fallbackName)
    {
        var names = GameInfo.GetStrings(FormNameFormatter.PkhexLang).Species;
        var baseName = speciesId > 0 && speciesId < names.Count && !string.IsNullOrWhiteSpace(names[speciesId])
            ? names[speciesId]
            : fallbackName;
        return FormNameFormatter.Format(baseName, speciesId, form);
    }
}

/// <summary>Un ítem del "flujo" de evolución — WrapPanel de un solo nivel que mezcla
/// nodos (previa / esta especie / evoluciones directas) y flechas entre ellos, en vez de
/// apilar todo en columna. Reflowa solo si hace falta, sin geometría de círculo calculada
/// a mano (eso quedó en el boceto HTML, ver nota en PokedexView.axaml). Dos tipos de ítem
/// (nodo/flecha) elegidos por DataType en PokedexView.axaml vía ItemsControl.DataTemplates
/// — no hace falta una jerarquía de clases más elaborada para esto.</summary>
public sealed class PokedexEvolutionFlowNode
{
    public string  Name          { get; init; } = string.Empty;
    public string? ConditionText { get; init; }
    public Bitmap? Sprite        { get; init; }
    public bool    IsSelf        { get; init; }
}
public sealed class PokedexEvolutionFlowArrow { }
/// <summary>Separador entre ramas alternativas ("Exeggutor / Exeggutor de Alola"): distinto de la flecha,
/// que indica "evoluciona a".</summary>
public sealed class PokedexEvolutionFlowAlt { }

/// <summary>
/// Orquestador del modo Pokédex (grilla de álbum de figuritas, ancho completo — sin
/// separación centro/derecha como en Mochila, ver spec de diseño
/// screen-pokedex-album.md en exxeguttor-context). Álbum se recalcula por completo cada
/// vez que cambia el save cargado (Reload) — sin nada persistente entre saves, decisión
/// ya tomada en el spec.
/// </summary>
public class PokedexViewModel : INotifyPropertyChanged
{
    // Arranca en 24 como estimación razonable antes del primer layout — la vista
    // llama SetPageSize() con el valor real apenas mide el espacio disponible (ver
    // PokedexView.axaml.cs, GridArea.SizeChanged). Nunca hay scroll: lo que no entra
    // pasa a la página siguiente, no se recorta a la fuerza.
    private int _pageSize = 24;

    // Timings de la coreografía de apertura: crece+aparece (debe coincidir con la
    // duración de TransformOperationsTransition/DoubleTransition en PokedexView.axaml) →
    // se queda mostrando el frente un rato → gira al reverso (debe coincidir con la
    // duración de Rotate3DTransition). Mientras _isAnimating es true, ni cerrar ni abrir
    // otra tarjeta hacen nada — recién se puede interactuar de nuevo cuando termina.
    private static readonly TimeSpan GrowDuration      = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan HoldFrontDuration = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan FlipDuration      = TimeSpan.FromMilliseconds(400);

    private bool _isAnimating;

    private readonly PokedexService _service;
    private readonly LocalizationService _loc;
    public LocalizationService Loc => _loc;

    private List<PokedexAlbumEntry> _allEntries = new();
    private GameVersion _currentVersion;
    private SaveFile? _currentSave;

    private int _page;
    private int _ownedCount;
    private int _totalCount;

    private PokedexCardViewModel? _expandedCard;
    private bool _isShowingBack;
    private bool _isPanelVisible;
    private PokedexEvolutionNodeViewModel? _evolutionPrev;
    private string? _originGenus;
    private string? _originHabitat;
    private string? _originFlavorText;

    public PokedexViewModel(PokedexService service, LocalizationService loc)
    {
        _service = service;
        _loc     = loc;

        VisibleCards   = new ObservableCollection<PokedexCardViewModel>();
        EvolutionNext  = new ObservableCollection<PokedexEvolutionNodeViewModel>();
        EvolutionFlow  = new ObservableCollection<object>();

        PreviousPageCommand = new RelayCommand(() => { if (CanGoPrevious) { Page--; RenderPage(); } });
        NextPageCommand     = new RelayCommand(() => { if (CanGoNext) { Page++; RenderPage(); } });
        OpenCardCommand     = new RelayCommand<PokedexCardViewModel>(card => { if (card is { Owned: true }) OpenCard(card); });
        CloseDetailCommand  = new RelayCommand(() => _ = CloseDetailAsync());
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

    // ── Grilla / paginado ────────────────────────────────────────────
    public ObservableCollection<PokedexCardViewModel> VisibleCards { get; }

    public int Page
    {
        get => _page;
        private set => Set(ref _page, value);
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(_totalCount / (double)_pageSize));
    public bool CanGoPrevious => _page > 0;
    public bool CanGoNext => _page < TotalPages - 1;
    public string PageLabel => string.Format(_loc["Pokedex_Page"], _page + 1, TotalPages);
    public string OwnedCountLabel => string.Format(_loc["Pokedex_OwnedCount"], _ownedCount, _totalCount);

    /// <summary>Cambió el idioma de la UI: los rótulos calculados quedaron en el idioma anterior.</summary>
    public void OnLocaleChanged()
    {
        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(OwnedCountLabel));
    }

    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand OpenCardCommand { get; }
    public ICommand CloseDetailCommand { get; }

    // ── Detalle expandido (reverso) ──────────────────────────────────
    public PokedexCardViewModel? ExpandedCard
    {
        get => _expandedCard;
        private set { Set(ref _expandedCard, value); OnPropertyChanged(nameof(CurrentFace)); }
    }
    public bool IsDetailOpen => ExpandedCard != null;

    /// <summary>true = mostrando el reverso, false = mostrando el frente — controla qué
    /// lado gira a la vista con TransitioningContentControl.Content en PokedexView.axaml.
    /// El nombre no es "IsFlipped" a propósito: no describe la animación, describe el
    /// estado lógico (qué cara se está mostrando), la vista decide cómo animarlo.</summary>
    public bool IsShowingBack
    {
        get => _isShowingBack;
        private set { Set(ref _isShowingBack, value); OnPropertyChanged(nameof(CurrentFace)); }
    }

    /// <summary>Contenido a mostrar en el TransitioningContentControl — el frente
    /// (ExpandedCard, tipo PokedexCardViewModel) o el reverso (this mismo, ya que todos
    /// los datos del reverso — EvolutionFlow, OriginGenus, etc. — ya viven directo en esta
    /// clase). PokedexView.axaml resuelve cuál plantilla usar por DataType.</summary>
    public object? CurrentFace => IsShowingBack ? this : ExpandedCard;

    /// <summary>Controla la escala/opacidad de ENTRADA y SALIDA del panel completo (no del
    /// contenido interno, eso lo maneja IsShowingBack) — crece+aparece al abrir, se
    /// achica+desaparece al cerrar. Bindeado en PokedexView.axaml con
    /// TransformOperationsTransition + DoubleTransition.</summary>
    public bool IsPanelVisible
    {
        get => _isPanelVisible;
        private set { Set(ref _isPanelVisible, value); OnPropertyChanged(nameof(PanelOpacity)); OnPropertyChanged(nameof(PanelScale)); }
    }
    public double PanelOpacity => IsPanelVisible ? 1.0 : 0.0;
    public string PanelScale   => IsPanelVisible ? "scale(1)" : "scale(0.85)";

    public PokedexEvolutionNodeViewModel? EvolutionPrev
    {
        get => _evolutionPrev;
        private set => Set(ref _evolutionPrev, value);
    }
    public ObservableCollection<PokedexEvolutionNodeViewModel> EvolutionNext { get; }
    public bool HasNoEvolutionData => EvolutionPrev == null && EvolutionNext.Count == 0;

    /// <summary>Secuencia mixta nodo/flecha lista para el WrapPanel del reverso — ver
    /// PokedexEvolutionFlowNode/Arrow arriba. Reconstruida en BuildEvolutionFlow() cada
    /// vez que cambian EvolutionPrev/EvolutionNext/ExpandedCard.</summary>
    public ObservableCollection<object> EvolutionFlow { get; }

    public string? OriginGenus
    {
        get => _originGenus;
        private set => Set(ref _originGenus, value);
    }
    public string? OriginHabitat
    {
        get => _originHabitat;
        private set => Set(ref _originHabitat, value);
    }
    public string? OriginFlavorText
    {
        get => _originFlavorText;
        private set => Set(ref _originFlavorText, value);
    }
    public bool HasOriginData => !string.IsNullOrEmpty(OriginFlavorText);

    // ── Ciclo de vida ─────────────────────────────────────────────────

    /// <summary>Recalcula el álbum entero para el save recién cargado. Llamar desde
    /// MainWindowViewModel al abrir un save, mismo momento que TrainerViewModel.LoadFrom /
    /// BoxViewModel.Initialize.</summary>
    public void Reload(SaveFile save)
    {
        _currentSave = save;
        _currentVersion = save.Version;
        _allEntries = _service.GetAlbumEntries(save);
        _totalCount = _allEntries.Count;
        _ownedCount = _allEntries.Count(e => e.Owned);
        Page = 0;
        HardCloseDetail();
        RenderPage();
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(OwnedCountLabel));
    }

    private void RenderPage()
    {
        VisibleCards.Clear();
        foreach (var entry in _allEntries.Skip(_page * _pageSize).Take(_pageSize))
            VisibleCards.Add(new PokedexCardViewModel(entry));

        OnPropertyChanged(nameof(PageLabel));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>Llamado desde PokedexView cada vez que cambia el tamaño real disponible
    /// para la grilla (SizeChanged de GridArea) — recalcula cuántas tarjetas entran sin
    /// necesitar scroll. Resetea a la página 0 al cambiar de tamaño (más simple que
    /// intentar preservar la posición exacta entre distintos tamaños de página, y un
    /// resize en medio de hojear el álbum es un caso raro).</summary>
    public void SetPageSize(int size)
    {
        size = Math.Max(1, size);
        if (size == _pageSize) return;
        _pageSize = size;
        Page = 0;
        RenderPage();
        OnPropertyChanged(nameof(TotalPages));
    }

    private void OpenCard(PokedexCardViewModel card)
    {
        // Bloqueado mientras hay una coreografía en curso (abriendo O cerrando otra
        // tarjeta) — evita pisar una secuencia de animación a mitad de camino.
        if (_isAnimating) return;
        _isAnimating = true;

        ExpandedCard = card;
        IsShowingBack = false;
        IsPanelVisible = true; // dispara crecer+aparecer, ver GrowDuration arriba

        // Todas las ramas, con formas regionales y condiciones en el idioma de la UI (diseño del 11 oct).
        var family = _currentSave != null
            ? _service.GetAlbumEvolutionFamily(card.SpeciesId, card.Form, _currentSave, _loc.CurrentLocale)
            : _service.GetEvolutionFamily(card.SpeciesId);
        EvolutionPrev = family.Prev != null ? new PokedexEvolutionNodeViewModel(family.Prev) : null;
        EvolutionNext.Clear();
        foreach (var n in family.Next)
            EvolutionNext.Add(new PokedexEvolutionNodeViewModel(n));
        BuildEvolutionFlow(card, family.Siblings.Select(n => new PokedexEvolutionNodeViewModel(n)).ToList());

        var origin = _service.GetOriginInfo(card.SpeciesId, _currentVersion, _loc.CurrentLocale);
        OriginGenus      = origin.Genus;
        OriginHabitat    = HabitatDatabase.GetLabel(origin.Habitat, _loc.CurrentLocale);
        OriginFlavorText = origin.FlavorText;

        OnPropertyChanged(nameof(IsDetailOpen));
        OnPropertyChanged(nameof(HasNoEvolutionData));
        OnPropertyChanged(nameof(HasOriginData));

        _ = OpenSequenceAsync();
    }

    /// <summary>Coreografía completa de apertura: 1) crece y aparece mostrando el frente
    /// (ya disparado por IsPanelVisible=true en OpenCard) — se espera a que termine esa
    /// transición + el tiempo que el frente queda quieto en pantalla; 2) gira al reverso,
    /// que se reajusta de tamaño solo a su contenido (Border sin Height fijo, mismo
    /// mecanismo que ya evita el scroll en la grilla); 3) recién ahí se libera el bloqueo
    /// y el usuario puede volver a interactuar (cerrar, o abrir otra tarjeta).</summary>
    private async Task OpenSequenceAsync()
    {
        await Task.Delay(GrowDuration + HoldFrontDuration);
        IsShowingBack = true;
        await Task.Delay(FlipDuration);
        _isAnimating = false;
    }

    /// <summary>Cierre con animación: gira de vuelta al frente + achica/desvanece el
    /// panel en paralelo, y recién cuando terminan (FlipDuration, que ya cubre la
    /// duración más corta del achique) limpia los datos y saca el panel del todo
    /// (IsDetailOpen pasa a false ahí, no antes — así el panel sigue renderizado
    /// mientras se ve la animación de salida). Bloqueado mientras haya cualquier
    /// coreografía en curso, mismo _isAnimating que OpenCard.</summary>
    private async Task CloseDetailAsync()
    {
        if (_isAnimating || ExpandedCard == null) return;
        _isAnimating = true;
        IsShowingBack = false;
        IsPanelVisible = false;
        await Task.Delay(FlipDuration);
        HardCloseDetail();
        _isAnimating = false;
    }

    /// <summary>Cierre inmediato sin animación — usado al cambiar de save (Reload), donde
    /// no tiene sentido animar nada porque toda la pantalla se está recalculando. También
    /// resetea el bloqueo de animación por las dudas (si el save cambia a mitad de una
    /// coreografía, no debería quedar todo trabado después).</summary>
    private void HardCloseDetail()
    {
        _isAnimating = false;
        ExpandedCard = null;
        IsShowingBack = false;
        IsPanelVisible = false;
        EvolutionPrev = null;
        EvolutionNext.Clear();
        EvolutionFlow.Clear();
        OriginGenus = OriginHabitat = OriginFlavorText = null;
        OnPropertyChanged(nameof(IsDetailOpen));
    }

    private void BuildEvolutionFlow(PokedexCardViewModel self, IReadOnlyList<PokedexEvolutionNodeViewModel> siblings)
    {
        EvolutionFlow.Clear();
        if (EvolutionPrev == null && EvolutionNext.Count == 0) return;

        if (EvolutionPrev != null)
        {
            EvolutionFlow.Add(new PokedexEvolutionFlowNode
            {
                Name = EvolutionPrev.Name, ConditionText = EvolutionPrev.ConditionText,
                Sprite = EvolutionPrev.Sprite, IsSelf = false,
            });
            EvolutionFlow.Add(new PokedexEvolutionFlowArrow());
        }

        EvolutionFlow.Add(new PokedexEvolutionFlowNode
        {
            Name = self.Name, ConditionText = null, Sprite = self.Sprite, IsSelf = true,
        });

        // Ramas alternativas de la misma previa (p. ej. Exeggutor junto a Exeggutor de Alola): se separan
        // con "/" y no con flecha. Sin layout de árbol: las evoluciones de cada hermana no se muestran.
        foreach (var sibling in siblings)
        {
            EvolutionFlow.Add(new PokedexEvolutionFlowAlt());
            EvolutionFlow.Add(new PokedexEvolutionFlowNode
            {
                Name = sibling.Name, ConditionText = sibling.ConditionText,
                Sprite = sibling.Sprite, IsSelf = false,
            });
        }

        // Evoluciones de esta especie: la primera con flecha, las demás son ramas alternativas ("/").
        for (var i = 0; i < EvolutionNext.Count; i++)
        {
            var next = EvolutionNext[i];
            EvolutionFlow.Add(i == 0 ? new PokedexEvolutionFlowArrow() : new PokedexEvolutionFlowAlt());
            EvolutionFlow.Add(new PokedexEvolutionFlowNode
            {
                Name = next.Name, ConditionText = next.ConditionText,
                Sprite = next.Sprite, IsSelf = false,
            });
        }
    }
}
