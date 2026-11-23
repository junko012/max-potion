using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Una fila de la lista de ítems del panel derecho de Mochila. Se muestran TODOS los ítems
/// legales del pouch (poseídos y no) — ver ItemInventoryService.GetItems — para poder agregar
/// un ítem nuevo, no solo editar cantidad de los existentes.
///
/// Igual que el resto del editor, cada cambio de cantidad pasa por
/// EditSessionService.RecordEdit (vía el callback <c>onQuantityChanged</c> que inyecta
/// BagViewModel) — nada se escribe al SaveFile real todavía (ver GAP CRÍTICO).
/// </summary>
public class BagItemRowViewModel : INotifyPropertyChanged
{
    private readonly Action<int, int> _onQuantityChanged;
    private int _quantity;

    public int ItemId { get; }
    public string Name { get; }
    public string Description { get; }
    public int MaxCount { get; }
    public Bitmap? Sprite { get; }

    /// <param name="name">Llave de pokemon.db (inglés): solo para el sprite.</param>
    /// <param name="displayName">Texto a mostrar en el idioma de los datos (null = usar <paramref name="name"/>).</param>
    public BagItemRowViewModel(int itemId, string name, string description, int quantity, int maxCount,
        Action<int, int> onQuantityChanged, string? displayName = null)
    {
        ItemId = itemId;
        Name = displayName ?? name;
        Description = description;
        MaxCount = maxCount <= 0 ? 999 : maxCount; // defensivo: un MaxCount de 0 dejaría todo
                                                     // el pouch inutilizable si algún save raro
                                                     // lo reporta así
        _quantity = quantity;
        _onQuantityChanged = onQuantityChanged;
        Sprite = SpriteService.Instance.GetItemSprite(name);

        IncrementCommand = new RelayCommand(() => Quantity = Math.Min(Quantity + 1, MaxCount));
        DecrementCommand = new RelayCommand(() => Quantity = Math.Max(Quantity - 1, 0));
        MaxCommand        = new RelayCommand(() => Quantity = MaxCount);
        ToggleOwnedCommand = new RelayCommand(() => Quantity = Owned ? 0 : 1);
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxCount);
            if (_quantity == clamped) return;
            _quantity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Owned));
            _onQuantityChanged(ItemId, _quantity);
        }
    }

    public bool Owned => Quantity > 0;

    public ICommand IncrementCommand { get; }
    public ICommand DecrementCommand { get; }
    public ICommand MaxCommand { get; }
    public ICommand ToggleOwnedCommand { get; }

    /// <summary>Filtro de búsqueda (ver BagViewModel.SearchText) — coincide por nombre o
    /// descripción, igual que el buscador de MoveSelectorViewModel.</summary>
    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
