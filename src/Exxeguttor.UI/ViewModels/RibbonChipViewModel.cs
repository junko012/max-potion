using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Un chip de filtro de categoría en el tab Ribbons. Lleva su propio `IsSelected` (en vez de
/// requerir que la vista compare contra `PokemonEditorViewModel.SelectedRibbonCategory` vía un
/// binding cruzado por ElementName) — mismo patrón que `RibbonItemViewModel.IsObtained`, más
/// simple y seguro con bindings compilados (ver pitfall #10 en context.md).
/// </summary>
public sealed class RibbonChipViewModel : INotifyPropertyChanged
{
    /// <summary>Clave cruda ("Champion", "Battle", ... o "Todas") — la que espera
    /// `SelectedRibbonCategory` y el resto del pipeline de filtrado.</summary>
    public string Category    { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
