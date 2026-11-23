using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Views;

public partial class SpeciesPickerView : UserControl
{
    // Acumulador de scroll del mouse/trackpad sobre el cover flow — el .md pide
    // explícitamente "velocidad proporcional al gesto, no es un scroll = un resultado", así
    // que en vez de mover el índice en cada evento de rueda (demasiado sensible, sobre todo
    // en trackpads que emiten muchos eventos chicos por gesto), se acumula el delta y recién
    // se mueve el cover flow cuando cruza un umbral, después se resetea el acumulador.
    private double _wheelAccumulator;
    private const double WheelStepThreshold = 50;

    public SpeciesPickerView()
    {
        InitializeComponent();
        PointerWheelChanged += OnPointerWheelChanged;
    }

    private SpeciesPickerViewModel? VM => DataContext as SpeciesPickerViewModel;

    private void OnTypeChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TypeChipViewModel chip } && VM is { } vm)
            vm.SelectTypeChip(chip);
    }

    /// <summary>Tap/click sobre cualquier tarjeta del cover flow — SIEMPRE solo mueve el
    /// foco a esa tarjeta, nunca selecciona (el único camino real de selección es el botón
    /// "Crear", ver SpeciesPickerViewModel.CreateCommand). Cada tarjeta ahora ES un
    /// SpeciesPickerItemViewModel real (ItemsControl sobre Species, ya no son 5 slots fijos
    /// con un offset en el Tag), así que alcanza con leer su propio DataContext.</summary>
    private void SpeciesCard_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: SpeciesPickerItemViewModel item } && VM is { } vm)
            vm.FocusItem(item);
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e) => VM?.MoveBy(-1);
    private void OnNextClick(object? sender, RoutedEventArgs e) => VM?.MoveBy(1);

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (VM is not { } vm || !vm.IsVisible) return;

        // Delta.Y positivo = scroll "hacia arriba"/atrás en la convención de Avalonia —
        // usamos el eje que tenga más movimiento (algunos trackpads mandan el gesto
        // horizontal en X en vez de Y) para no depender de un solo eje.
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : -e.Delta.Y;
        _wheelAccumulator += delta * 20; // escala: la magnitud cruda de Delta suele ser ~1

        while (_wheelAccumulator >= WheelStepThreshold)
        {
            vm.MoveBy(1);
            _wheelAccumulator -= WheelStepThreshold;
        }
        while (_wheelAccumulator <= -WheelStepThreshold)
        {
            vm.MoveBy(-1);
            _wheelAccumulator += WheelStepThreshold;
        }

        e.Handled = true;
    }
}
