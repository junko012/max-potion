using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Una tarjeta del modal de selección de especie (crear Pokémon): sprite + nombre +
/// número de Pokédex + tipos, para el buscador de texto y el filtro por tipo.
///
/// SEGUNDA VERSIÓN tras feedback visual sobre la primera entrega del cover flow: ahora
/// implementa INotifyPropertyChanged y expone propiedades de layout mutables
/// (CardLeft/CardWidth/CardOpacity/CardZIndex/IsFocused), mismo patrón que
/// <see cref="ExportReviewCardViewModel"/> — ANTES el cover flow eran 5 slots fijos que solo
/// re-bindeaban qué especie mostraban (sin posición propia), lo que hacía que cambiar de
/// foco fuera un salto instantáneo sin transición. Ahora TODOS los ítems de
/// SpeciesPickerViewModel.Species están en el árbol visual (un ItemsControl sobre un Canvas,
/// igual que el carrusel de revisión de Pantalla 3) y se posicionan/dimensionan en base a su
/// distancia al índice focalizado — recalculado en SpeciesPickerViewModel.UpdateCardLayout,
/// con Transitions en el XAML sobre estas propiedades para la animación.</summary>
public sealed class SpeciesPickerItemViewModel : INotifyPropertyChanged
{
    public int    Id            { get; init; }
    public string DisplayName   { get; init; } = string.Empty;
    public string DexNumberText { get; init; } = string.Empty;
    public List<string> Types   { get; init; } = new();
    public Bitmap? Sprite       { get; init; }

    /// <summary>Índice de forma de PKHeX.Core (<c>PKM.Form</c>). 0 = la especie base; &gt; 0 =
    /// una forma regional del <c>RegionalFormCatalog</c> (Fase 2 de formas), que tiene su propia
    /// tarjeta en el cover flow con el mismo <see cref="Id"/> que la especie base.</summary>
    public byte    Form        { get; init; }

    /// <summary>"Alola", "Galar", "Hisui", "Paldea"... — null en las tarjetas de especie base.</summary>
    public string? RegionLabel { get; init; }

    /// <summary>Línea chica de la tarjeta: el número de Pokédex. (La región ya va en
    /// <see cref="DisplayName"/> — "Exeggutor de Alola" — para que las dos tarjetas no se vean
    /// con el mismo nombre.)</summary>
    public string SubText => DexNumberText;

    /// <summary>Nombre completo sin ambigüedad ("Marowak (Alola)") para el scrubber y los
    /// mensajes de estado — en la tarjeta se muestra solo <see cref="DisplayName"/>.</summary>
    public string FullName => DisplayName;

    // Un Pokémon tiene como mucho 2 tipos — exponerlos como 2 propiedades fijas (en vez de que
    // la vista bindee `Types` a un ItemsControl anidado) evita que Avalonia tenga que generar
    // OTRO nivel completo de contenedores (Border+TextBlock por tipo) para cada una de las
    // ~500-1000 tarjetas del cover flow. `Types` se deja igual para el resto del código
    // (filtro por tipo en RecalcFiltered), esto es puramente para la vista.
    public string? Type1 => Types.Count > 0 ? Types[0] : null;
    public string? Type2 => Types.Count > 1 ? Types[1] : null;
    public bool HasType2 => Types.Count > 1;

    /// <summary>Texto plano usado por el buscador (nombre + número, todo en minúsculas).</summary>
    public string SearchBlob { get; init; } = string.Empty;

    private bool _isFocused;
    public bool IsFocused
    {
        get => _isFocused;
        set { if (_isFocused == value) return; _isFocused = value; OnPropertyChanged(); }
    }

    private double _cardWidth = 100;
    public double CardWidth
    {
        get => _cardWidth;
        set { if (_cardWidth.Equals(value)) return; _cardWidth = value; OnPropertyChanged(); }
    }

    private double _cardOpacity;
    public double CardOpacity
    {
        get => _cardOpacity;
        set { if (_cardOpacity.Equals(value)) return; _cardOpacity = value; OnPropertyChanged(); }
    }

    private double _cardLeft;
    public double CardLeft
    {
        get => _cardLeft;
        set { if (_cardLeft.Equals(value)) return; _cardLeft = value; OnPropertyChanged(); }
    }

    private int _cardZIndex;
    public int CardZIndex
    {
        get => _cardZIndex;
        set { if (_cardZIndex == value) return; _cardZIndex = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
