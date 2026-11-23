using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Un renglón del carrusel vertical de etapas del splash (estilo cover flow, pero
/// vertical y solo texto) — mismo patrón de "propiedades de layout mutables + Transitions
/// en el XAML" que ya usa <see cref="SpeciesPickerItemViewModel"/> para su cover flow
/// horizontal, acá aplicado en vertical: SplashWindow.RecalculateStageLayout() recalcula
/// Top/Opacity/FontSize/Foreground de TODOS los renglones cada vez que se agrega uno nuevo,
/// según su distancia al más reciente, y el XAML anima esos cambios vía Transitions.
///
/// <see cref="IsMilestoneActive"/> solo es true mientras este renglón es el actual (el más
/// reciente, al centro del viewport) Y fue creado como mensaje "marco" (arranque/cierre) —
/// dispara la negrita + sombra vía el selector de estilo "TextBlock.milestone-active" en
/// SplashWindow.axaml. A propósito NO se anima (el salto entre negrita y normal es
/// instantáneo, igual que en el boceto HTML del que viene este diseño).</summary>
public sealed class SplashStageItem : INotifyPropertyChanged
{
    public string Text { get; }

    /// <summary>Marca fija al crear el renglón — true solo para el primer mensaje
    /// ("Preparando entorno") y el mensaje final de cierre ("Entorno completo y
    /// funcional"), nunca para los de carga de módulos intermedios.</summary>
    public bool IsMilestone { get; }

    /// <summary>False = se comporta como hito en tiempos (pausa larga) pero se dibuja como un renglón normal,
    /// sin negrita ni sombra (el mensaje final de cierre).</summary>
    public bool Emphasize { get; }

    public SplashStageItem(string text, bool isMilestone, bool emphasize = true)
    {
        Text = text;
        IsMilestone = isMilestone;
        Emphasize = emphasize;
    }

    private double _top;
    public double Top
    {
        get => _top;
        set { if (_top.Equals(value)) return; _top = value; OnPropertyChanged(); }
    }

    private double _opacity;
    public double Opacity
    {
        get => _opacity;
        set { if (_opacity.Equals(value)) return; _opacity = value; OnPropertyChanged(); }
    }

    private double _fontSize = 10;
    public double FontSize
    {
        get => _fontSize;
        set { if (_fontSize.Equals(value)) return; _fontSize = value; OnPropertyChanged(); }
    }

    private IBrush _foreground = Brushes.Transparent;
    public IBrush Foreground
    {
        get => _foreground;
        set { _foreground = value; OnPropertyChanged(); }
    }

    private bool _isMilestoneActive;
    public bool IsMilestoneActive
    {
        get => _isMilestoneActive;
        set { if (_isMilestoneActive == value) return; _isMilestoneActive = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
