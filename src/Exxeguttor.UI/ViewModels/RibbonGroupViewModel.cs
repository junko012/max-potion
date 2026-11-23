using System.ComponentModel;
using Avalonia.Media;
using Exxeguttor.UI.Converters;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Grupo de cintas por categoría para el tab Ribbons: una tarjeta por categoría, con color de
/// fondo propio para Campeón/Batalla/Concursos/Marcas y gris neutro (3 tonos distintos, para
/// que sigan siendo distinguibles entre sí) para Memoria/Evento/General.
/// </summary>
public sealed class RibbonGroupViewModel : INotifyPropertyChanged
{
    /// <summary>Orden fijo de categorías en el tab: primero las "trofeo" con color propio,
    /// después las neutras — no alfabético. Si `Category` no está en esta lista (no debería
    /// pasar salvo que se agregue una categoría nueva a RibbonDatabase sin actualizar esto),
    /// se ordena al final.</summary>
    public static readonly string[] CategoryOrder =
        { "Champion", "Battle", "Contest", "Mark", "Common", "Event", "Memory" };

    public string                     Category { get; init; } = string.Empty;
    public List<RibbonItemViewModel>  Items    { get; init; } = new();

    /// <summary>True cuando esta es la única tarjeta visible (filtro por chip de categoría
    /// específica, no "Todas") — la vista la muestra más grande/espaciada.</summary>
    public bool IsExpanded { get; init; }

    /// <summary>True cuando la tarjeta puede plegarse desde su encabezado: solo en el filtro "Todas"
    /// y sin búsqueda activa (con una categoría elegida o buscando, siempre se ve desplegada).</summary>
    public bool IsToggleable { get; init; }

    /// <summary>Estado de plegado inicial (lo decide el ViewModel padre, que lo recuerda entre
    /// reconstrucciones de la lista).</summary>
    public bool StartCollapsed { init => _isCollapsed = value; }

    private bool _isCollapsed;
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed == value) return;
            _isCollapsed = value;
            OnCollapsedChanged?.Invoke(Category, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCollapsed)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBodyVisible)));
        }
    }
    public bool IsBodyVisible => !_isCollapsed;

    /// <summary>El padre guarda acá el plegado por categoría para que sobreviva a la reconstrucción.</summary>
    public Action<string, bool>? OnCollapsedChanged { get; set; }

    /// <summary>Glifo del encabezado (ver <see cref="RibbonIcons"/>).</summary>
    public Geometry IconGeometry => RibbonIcons.ForCategory(Category);

    /// <summary>Progreso 0-1 de la categoría, para la barra del encabezado (columnas Star).</summary>
    public double ProgressFill => Total == 0 ? 0 : (double)Obtained / Total;
    public double ProgressRest => 1 - ProgressFill;

    /// <summary>Se llama cuando el usuario tilda/destilda una cinta, para refrescar conteo y barra.</summary>
    public void Refresh()
    {
        foreach (var p in new[] { nameof(Obtained), nameof(CountLabel), nameof(Header),
                                  nameof(ProgressFill), nameof(ProgressRest) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Obtained => Items.Count(i => i.IsObtained);
    public int Total    => Items.Count;
    public string CountLabel => $"{Obtained}/{Total}";
    public string Header => $"{CategoryDisplay} · {CountLabel}";

    /// <summary>Única fuente de verdad del nombre en español — la comparte con
    /// <see cref="RibbonChipViewModel"/> (vía <see cref="RibbonCategoryColorPalette"/>) para no
    /// repetir el mapeo español↔categoría en dos lugares distintos.</summary>
    public string CategoryDisplay => RibbonCategoryColorPalette.LocalizedName(Category);
}
