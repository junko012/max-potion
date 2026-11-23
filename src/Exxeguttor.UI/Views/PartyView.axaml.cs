using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Views;

public partial class PartyView : UserControl
{
    public PartyView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnSlotPointerPressed, handledEventsToo: true);
    }

    private void OnSlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PartyViewModel vm) return;

        var hit = e.Source as Visual;
        var onRelease = BoxView.IsOnReleaseButton(hit);
        while (hit != null)
        {
            if (hit is Control c && c.DataContext is PokemonSlotViewModel slot)
            {
                // Botón "−": pide liberar el Pokémon, sin seleccionarlo.
                if (onRelease) { vm.RequestRelease(slot); return; }
                if (slot.IsCreateSlot)
                    vm.CreateSlotCommand.Execute(slot.SlotIndex);
                else
                    vm.SelectSlot(slot.SlotIndex);
                return;
            }
            hit = hit.GetVisualParent();
        }
    }
}
