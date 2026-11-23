using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Exxeguttor.UI.ViewModels;
using Avalonia;

namespace Exxeguttor.UI.Views;

public partial class BagPouchGridView : UserControl
{
    public BagPouchGridView()
    {
        InitializeComponent();
        // Manejar clic en cualquier punto de la grilla — mismo patrón que BoxView.axaml.cs.
        AddHandler(PointerPressedEvent, OnTilePointerPressed, handledEventsToo: true);
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not BagViewModel vm) return;
        // Buscar el BagPouchTileView que fue clickeado.
        var hit = e.Source as Visual;
        while (hit != null)
        {
            if (hit is UserControl uc && uc.DataContext is BagPouchTileViewModel tile)
            {
                vm.SelectPouch(tile);
                return;
            }
            hit = hit.GetVisualParent();
        }
    }
}
