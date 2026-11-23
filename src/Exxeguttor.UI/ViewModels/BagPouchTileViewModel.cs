using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Una tile del grid central de Mochila — mismo lenguaje visual que las tiles de
/// BoxView, pero para categorías de ítems en vez de Pokémon.</summary>
public class BagPouchTileViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public InventoryType Type { get; }
    /// <summary>Nombre de la categoría en el idioma de la UI (se calcula al leerlo; ver <see cref="OnLocaleChanged"/>).</summary>
    public string DisplayName => BagPouchDatabase.DisplayName(Type);

    /// <summary>Sprite real de ítem representativo de la categoría (ej. Poké Ball para
    /// "Bolas") — antes era un emoji (<c>Icon</c>, string) que se veía inconsistente entre
    /// sistemas y obligaba un FontSize grande en la tile. Mismo patrón que
    /// <see cref="BagItemRowViewModel.Sprite"/>: se resuelve una sola vez acá, en el
    /// constructor, vía SpriteService (que ya cachea el Bitmap decodificado).</summary>
    public Bitmap? Sprite { get; }

    public int OwnedCount { get; }
    public int LegalItemCount { get; }

    /// <summary>"3/12" — poseídos sobre legales, para el pie de la tile en la grilla central
    /// (espacio chico, no entra "3 poseídos de 12" como en el header del panel derecho).</summary>
    public string CountText => $"{OwnedCount}/{LegalItemCount}";

    public BagPouchTileViewModel(InventoryType type, int ownedCount, int legalItemCount)
    {
        Type = type;
        Sprite = SpriteService.Instance.GetItemSprite(BagPouchDatabase.IconSpriteKey(type));
        OwnedCount = ownedCount;
        LegalItemCount = legalItemCount;
    }

    public void OnLocaleChanged() => OnPropertyChanged(nameof(DisplayName));

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
