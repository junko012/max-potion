using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Una cinta individual para binding en la vista del tab Ribbons. IsObtained solo actualiza
/// este ViewModel (no escribe en el PKM todavía — mismo GAP CRÍTICO que el resto del editor).
/// </summary>
public sealed class RibbonItemViewModel : INotifyPropertyChanged
{
    /// <summary>Llave estable (nombre de <c>RibbonIndex</c>): identifica la cinta en ediciones y exportación.</summary>
    public string Key         { get; init; } = string.Empty;
    /// <summary>Nombre visible en el idioma de los datos; solo para mostrar, buscar y ordenar.</summary>
    public string DisplayName { get; init; } = string.Empty;
    public string Category    { get; init; } = string.Empty;

    /// <summary>Texto de la tarjeta. Los nombres vienen de PKHeX y cada idioma formula las Marcas
    /// distinto («Jumbo Mark», «Emblema Enormidad», «でっかいあかし»), así que no se recorta ningún prefijo.</summary>
    public string ShortName => DisplayName;

    private bool _isObtained;
    public bool IsObtained
    {
        get => _isObtained;
        set
        {
            if (_isObtained == value) return;
            _isObtained = value;
            OnPropertyChanged();
            OnUserToggle?.Invoke(Key, value);
        }
    }

    /// <summary>Notifica al ViewModel padre (PokemonEditorViewModel) que esta cinta cambió,
    /// para que la guarde en la caché de ediciones de sesión. Null durante la carga inicial
    /// no importa — el padre igual gatea por su propio flag _isLoading.</summary>
    public Action<string, bool>? OnUserToggle { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
