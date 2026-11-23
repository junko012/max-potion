using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Exxeguttor.UI.Services;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Views;

public partial class PokedexView : UserControl
{
    // Debe coincidir con el tamaño real de la tarjeta en el DataTemplate de abajo:
    // Width=130 + Margin=6 en cada lado (WrapPanel) = 142 de ancho efectivo por celda,
    // mismo cálculo en alto (150+12=162). Si se cambia el tamaño de tarjeta en el AXAML,
    // hay que actualizar estos dos números también.
    private const double CardCellWidth = 142;
    private const double CardCellHeight = 162;

    // Debe coincidir con Rotate3DTransition.Duration en PokedexView.axaml — se usa acá
    // para saber cuándo forzar el repintado de abajo, un ratito después de que el giro
    // debería haber terminado.
    private static readonly TimeSpan FlipDuration = TimeSpan.FromMilliseconds(450);

    public PokedexView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnCardPointerPressed, handledEventsToo: true);
        GridArea.SizeChanged += OnGridAreaSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PokedexViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Mitigación para un remanente visual del frente que a veces queda "pegado"
    /// después del giro hasta que algo más (ej. un click) fuerza un repintado — no se
    /// pudo reproducir ni depurar en este sandbox (sin entorno gráfico para correr la
    /// app), así que esto es una mitigación razonable, no una causa raíz confirmada.
    /// Fuerza un InvalidateVisual del contenido que gira apenas termina la transición,
    /// en vez de esperar a que lo dispare una interacción del usuario.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PokedexViewModel.IsShowingBack)) return;
        _ = ForceRepaintAfterFlipAsync();
    }

    private async Task ForceRepaintAfterFlipAsync()
    {
        await Task.Delay(FlipDuration);

        // Repintado agresivo: medida+arreglo+visual del contenido que gira, Y subiendo
        // hasta la ventana raíz — un InvalidateVisual() de un solo control no alcanzó
        // (ver aviso de la sesión: un screenshot del sistema operativo lo hace
        // desaparecer, lo cual fuerza redibujado a nivel de compositor/ventana, más
        // fuerte que invalidar un control individual).
        FaceContent.InvalidateMeasure();
        FaceContent.InvalidateArrange();
        FaceContent.InvalidateVisual();
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();

        var topLevel = TopLevel.GetTopLevel(this);
        topLevel?.InvalidateVisual();
    }

    /// <summary>Nunca hay scroll en el álbum — en vez de eso, cada vez que cambia el
    /// espacio disponible se recalcula cuántas tarjetas entran (columnas × filas) y se
    /// le informa al ViewModel, que repagina con ese tamaño. Lo que no entra pasa a la
    /// página siguiente en vez de recortarse o requerir scroll.</summary>
    private void OnGridAreaSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not PokedexViewModel vm) return;
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;

        int cols = Math.Max(1, (int)(e.NewSize.Width / CardCellWidth));
        int rows = Math.Max(1, (int)(e.NewSize.Height / CardCellHeight));
        vm.SetPageSize(cols * rows);
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PokedexViewModel vm) return;

        var hit = e.Source as Visual;
        while (hit != null)
        {
            if (hit is Border { DataContext: PokedexCardViewModel card })
            {
                if (card.Owned)
                {
                    vm.OpenCardCommand.Execute(card);
                    SoundService.PlaySparkChime();
                    _ = RunSparkBurstAsync();
                }
                return;
            }
            hit = hit.GetVisualParent();
        }
    }

    /// <summary>Estallido de chispas + flash, estilo "pack reveal" — Avalonia no tiene
    /// sistema de partículas, así que esto son 9 animaciones de keyframes ya definidas en
    /// PokedexView.axaml (Resources) corriendo en paralelo contra elementos fijos en
    /// anillo (ver Canvas en el overlay). Nota: el ViewModel puede terminar ignorando el
    /// OpenCard si ya hay una coreografía en curso (_isAnimating) — en ese caso el
    /// estallido igual se ve pero sin cambio de estado; caso raro (click muy rápido
    /// repetido) que se aceptó para no complicar la sincronización entre vista y VM.</summary>
    private async Task RunSparkBurstAsync()
    {
        if (Resources["FlashAnimation"] is not Animation flash) return;
        if (Resources["SparkBurstAnimation"] is not Animation spark) return;

        var targets = new Control[] { Spark0, Spark1, Spark2, Spark3, Spark4, Spark5, Spark6, Spark7 };
        var tasks = new List<Task> { flash.RunAsync(FlashEllipse) };
        tasks.AddRange(targets.Select(t => spark.RunAsync(t)));
        await Task.WhenAll(tasks);
    }
}
