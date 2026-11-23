using Avalonia.Controls;

namespace Exxeguttor.UI.Views;

/// <summary>Modales del menú Ayuda: Acerca de, Atajos y Reportar un error. Toda la lógica está en HelpViewModel.</summary>
public partial class HelpOverlay : UserControl
{
    public HelpOverlay() => InitializeComponent();

    /// <summary>Clic en el fondo oscurecido cierra el modal (igual que Esc).</summary>
    private void OnDimPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is ViewModels.HelpViewModel { IsCrashVisible: false } vm) vm.Close(); // el modal de error no se cierra por accidente
    }
}
