using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Exxeguttor.UI.ViewModels;
using Avalonia;

namespace Exxeguttor.UI.Views;

public partial class BoxView : UserControl
{
    public BoxView()
    {
        InitializeComponent();
        // Manejar clic en cualquier punto de la grilla
        AddHandler(PointerPressedEvent, OnSlotPointerPressed, handledEventsToo: true);
    }

    private void OnSlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not BoxViewModel vm) return;
        // Buscar el PokemonSlotView que fue clickeado
        var hit = e.Source as Visual;
        var onRelease = IsOnReleaseButton(hit);
        while (hit != null)
        {
            if (hit is UserControl uc && uc.DataContext is PokemonSlotViewModel slot)
            {
                // Botón "−" del slot: pide liberar el Pokémon, sin seleccionarlo.
                if (onRelease) { vm.RequestRelease(slot); return; }

                // Slots vacíos con el "+" abren el modal de crear Pokémon en vez de
                // intentar seleccionar (que igual sería un no-op, GetBox()[i].Species == 0).
                if (slot.IsCreateSlot)
                    vm.CreateSlotCommand.Execute(slot);
                else
                    vm.SelectSlot(slot);
                return;
            }
            hit = hit.GetVisualParent();
        }
    }

    /// <summary>True si el clic cayó sobre el "−" de liberar (Border con clase "rel-btn") o dentro de él.</summary>
    internal static bool IsOnReleaseButton(Visual? v)
    {
        while (v != null)
        {
            if (v is Border b && b.Classes.Contains("rel-btn")) return true;
            v = v.GetVisualParent();
        }
        return false;
    }
}
