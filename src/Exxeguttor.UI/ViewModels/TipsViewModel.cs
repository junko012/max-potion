using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Un ítem del rail de categorías (General/Creación/Edición/Legalidad).</summary>
public class TipCategoryViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Name { get; }
    public string Key { get; }
    public string IconGeometry { get; }
    public int Count { get; }
    public ICommand SelectCommand { get; }

    public TipCategoryViewModel(string key, string name, string iconGeometry, int count, ICommand selectCommand)
    {
        Key = key;
        Name = name;
        IconGeometry = iconGeometry;
        Count = count;
        SelectCommand = selectCommand;
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; OnPropertyChanged(nameof(IsActive)); }
    }
}

/// <summary>Una "página" del scroll vertical — un tip completo, renderizado a todo el
/// alto del panel (ver TipsViewModel.PageHeight). A diferencia del viejo
/// TipCardSlotViewModel, esto NO tiene posición/rotación propia — es contenido
/// estático, lo único que se anima es TipsViewModel.ScrollOffset por encima de
/// todas las páginas apiladas.</summary>
public class TipPageViewModel
{
    public string IconGeometry { get; }
    public string Title { get; }
    public string Text { get; }
    /// <summary>Id de la miniatura de UI (TipSampleView); "" si el tip no tiene.</summary>
    public string Sample { get; }
    public bool HasSample => Sample.Length > 0;
    /// <summary>Textos traducidos de la app: las miniaturas reutilizan los de los botones reales.</summary>
    public Exxeguttor.UI.i18n.LocalizationService? Loc { get; }

    public TipPageViewModel(string iconGeometry, string title, string text, string sample = "",
        Exxeguttor.UI.i18n.LocalizationService? loc = null)
    {
        IconGeometry = iconGeometry;
        Title = title;
        Text = text;
        Sample = sample;
        Loc = loc;
    }
}

/// <summary>Punto del navegador lateral — uno por tip de la categoría activa.</summary>
public class TipDotViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SelectCommand { get; }

    public TipDotViewModel(ICommand selectCommand)
    {
        SelectCommand = selectCommand;
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }
}

/// <summary>
/// Orquesta el rail de categorías + el scroll vertical con snap de la categoría
/// activa. Portado del boceto interactivo validado
/// (mockups/tips_stack/screen-06-tips-scrollsnap-propuesta.html) — reemplaza por
/// completo el diseño anterior de stack 3D (4 tarjetas rotando con Position/ZIndex),
/// que se descartó por los bugs de superposición/opacidad que arrastraba.
///
/// A diferencia del stack viejo, acá NO hay identidad de tarjeta que reciclar ni
/// posición que rotar — cada categoría simplemente repuebla VisiblePages
/// con sus tips (todas páginas reales, sin trucos), y navegar es mover
/// ScrollOffset a -(índice x PageHeight). La animación la hace
/// Avalonia solo, vía Transitions sobre esa propiedad en el XAML — no hace falta
/// ningún Task.Delay ni coreografía manual como en la versión vieja.
/// </summary>
public class TipsViewModel : INotifyPropertyChanged
{
    /// <summary>Alto de cada "página" en píxeles — DEBE coincidir con el Height
    /// fijado en MainWindow.axaml (el Border que recorta + cada item del
    /// ItemsControl). Si se cambia acá, cambiar también ahí.</summary>
    public const double PageHeight = 260;
    private readonly Exxeguttor.UI.i18n.LocalizationService? _loc;

    private readonly IReadOnlyList<TipEntry> _allTips;
    private List<TipEntry> _categoryTips = new();
    private int _activeCategoryIndex;
    private int _currentIndex;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    public bool HasTips => _allTips.Count > 0;

    public List<TipCategoryViewModel> Categories { get; } = new();

    private List<TipPageViewModel> _visiblePages = new();
    /// <summary>Tips de la categoría activa, uno por página — se REEMPLAZA entero
    /// (no se muta en el lugar) cada vez que cambia la categoría, así el
    /// ItemsControl bindeado regenera sus contenedores solo.</summary>
    public List<TipPageViewModel> VisiblePages
    {
        get => _visiblePages;
        private set { _visiblePages = value; OnPropertyChanged(nameof(VisiblePages)); }
    }

    private List<TipDotViewModel> _dots = new();
    public List<TipDotViewModel> Dots
    {
        get => _dots;
        private set { _dots = value; OnPropertyChanged(nameof(Dots)); }
    }

    private double _scrollOffset;
    /// <summary>Bindeado al TranslateTransform.Y del contenido apilado en
    /// MainWindow.axaml — Avalonia anima la transición sola vía Transitions,
    /// no hace falta lógica de animación acá.</summary>
    public double ScrollOffset { get => _scrollOffset; private set => Set(ref _scrollOffset, value); }

    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }

    public TipsViewModel(TipsService tipsService, Exxeguttor.UI.i18n.LocalizationService? loc = null)
    {
        _allTips = tipsService.Tips;
        _loc = loc;

        NextCommand = new RelayCommand(() => GoTo(_currentIndex + 1));
        PrevCommand = new RelayCommand(() => GoTo(_currentIndex - 1));

        if (!HasTips) return; // ver TipsService — nunca lanza, el modal queda vacío

        // Se agrupa por id estable (categoryId) y se muestra el nombre traducido de esa categoría.
        var seen = new List<(string Key, string Name)>();
        foreach (var tip in _allTips)
        {
            if (seen.Any(c => c.Key == tip.CategoryKey)) continue;
            seen.Add((tip.CategoryKey, tip.Category));
        }

        for (int i = 0; i < seen.Count; i++)
        {
            var (key, name) = seen[i];
            var count = _allTips.Count(t => t.CategoryKey == key);
            var categoryIndex = i; // capturar por valor para el closure de abajo
            Categories.Add(new TipCategoryViewModel(
                key,
                name,
                TipIconCatalog.ResolveCategoryIcon(key),
                count,
                new RelayCommand(() => SelectCategory(categoryIndex))));
        }

        Categories[0].IsActive = true;
        LoadCategory(0);
    }

    private void SelectCategory(int index)
    {
        if (index == _activeCategoryIndex) return;
        foreach (var c in Categories) c.IsActive = false;
        Categories[index].IsActive = true;
        _activeCategoryIndex = index;
        LoadCategory(index);
    }

    private void LoadCategory(int index)
    {
        var key = Categories[index].Key;
        _categoryTips = _allTips.Where(t => t.CategoryKey == key).ToList();

        VisiblePages = _categoryTips
            .Select(t => new TipPageViewModel(TipIconCatalog.Resolve(t.Icon), t.Title, t.Text, t.Sample, _loc))
            .ToList();

        var newDots = new List<TipDotViewModel>();
        for (int i = 0; i < _categoryTips.Count; i++)
        {
            var tipIndex = i; // capturar por valor
            newDots.Add(new TipDotViewModel(new RelayCommand(() => GoTo(tipIndex))));
        }
        Dots = newDots;

        _currentIndex = 0;
        if (Dots.Count > 0) Dots[0].IsActive = true;
        ScrollOffset = 0;
    }

    /// <summary>Navega al tip <paramref name="target"/> DENTRO de la categoría
    /// activa (cicla si se pasa de los extremos, mismo criterio que las flechas
    /// arriba/abajo del boceto). No hay animación manual acá — solo se cambia
    /// ScrollOffset, y el XAML (Transitions sobre TranslateTransform.Y) hace el
    /// resto.</summary>
    public void GoTo(int target)
    {
        int total = _categoryTips.Count;
        if (total == 0) return;
        target = ((target % total) + total) % total;
        if (target == _currentIndex) return;

        _currentIndex = target;
        for (int i = 0; i < Dots.Count; i++)
            Dots[i].IsActive = i == target;
        ScrollOffset = -(target * PageHeight);
    }
}
