using System.ComponentModel;
using System.Runtime.CompilerServices;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Chip de filtro por tipo en el modal de selección de especie ("Todos", "Fire", "Water"...).
/// Cada chip lleva su propio `IsSelected` — mismo patrón ya probado en
/// <see cref="RibbonChipViewModel"/> (tab Ribbons), para no depender de un binding cruzado
/// al DataContext del ItemsControl padre.
/// </summary>
public sealed class TypeChipViewModel : INotifyPropertyChanged
{
    /// <summary>Clave cruda del tipo ("Fire", "Water", ... o "Todos") — la que espera el
    /// filtro y `TypeColorConverter`.</summary>
    public string TypeName { get; init; } = string.Empty;

    /// <summary>Clave del chip «Todos» (no es un tipo real). La llave es interna: el texto visible sale de <see cref="Label"/>.</summary>
    public const string AllKey = "Todos";

    /// <summary>Texto del chip: «Todos» en el idioma de la UI, o el nombre del tipo en el idioma de los datos.</summary>
    public string Label => TypeName == AllKey ? UiText.T("Picker_AllTypes") : TypeNames.Localize(TypeName);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));

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
