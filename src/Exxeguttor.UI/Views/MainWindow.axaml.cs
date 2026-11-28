using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Exxeguttor.UI.Services;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Una vez que la ventana está lista, inicializar el FileDialogService
        // con la referencia a esta ventana (necesaria para mostrar diálogos del sistema)
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Initialize(this);
                vm.ClipboardSetter = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
            }
        };

        // Tunnel (no Bubble/XAML KeyDown normal): así el evento le llega a la Window ANTES
        // que al control que tenga el foco en ese momento. Con el KeyDown declarado en el
        // XAML (fase Bubble de siempre) esto no andaba bien — si algún control de la ventana
        // (ej. un ítem del rail lateral) todavía tenía el foco de antes de abrir el modal,
        // Avalonia le daba las flechas a ESE control primero (navegación direccional propia
        // entre hermanos enfocables), y recién le llegaban acá si el usuario clickeaba varias
        // veces dentro del modal o usaba el scroll (eso movía el foco a algo sin
        // comportamiento propio de flechas). Con Tunnel, la Window intercepta primero
        // siempre, sin importar qué tenga el foco.
        AddHandler(KeyDownEvent, MainWindow_KeyDown, RoutingStrategies.Tunnel);

        // El DataContext se asigna DESPUÉS de construir la ventana (ver App.axaml.cs), así que
        // el enganche al BusyState tiene que esperar a DataContextChanged.
        DataContextChanged += OnDataContextChangedHookBusy;
        DataContextChanged += OnDataContextChangedHookScale;
    }

    // ── Escala de la interfaz (zoom) ──────────────────────────────────────────────────────
    // El contenido vive dentro de ScaleHost (LayoutTransformControl). Al cambiar la escala la ventana crece o se
    // achica en la misma proporción (el layout lógico se mantiene y todo se ve más grande), hasta el tamaño de la
    // pantalla; si ya está maximizada, no cambia de tamaño y el contenido se reacomoda al área lógica menor
    // (ventana / escala). El tamaño mínimo también sube con la escala (900×600 lógicos), acotado a la pantalla.
    // No se pudo compilar ni probar en el sandbox.
    private const double BaseWidth = 1280, BaseHeight = 750, BaseMinWidth = 900, BaseMinHeight = 600;
    private double _appliedScale = 1.0;
    private MainWindowViewModel? _scaleVm;

    private void OnDataContextChangedHookScale(object? sender, EventArgs e)
    {
        if (_scaleVm != null) _scaleVm.PropertyChanged -= OnScaleVmPropertyChanged;
        _scaleVm = DataContext as MainWindowViewModel;
        if (_scaleVm == null) return;
        _scaleVm.PropertyChanged += OnScaleVmPropertyChanged;
        ApplyUiScale(_scaleVm.UiScale);
    }

    private void OnScaleVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.UiScale) && _scaleVm != null)
            ApplyUiScale(_scaleVm.UiScale);
    }

    private void ApplyUiScale(double scale)
    {
        ScaleHost.LayoutTransform = Math.Abs(scale - 1.0) < 0.0001 ? null : new ScaleTransform(scale, scale);

        var maxW = double.PositiveInfinity;
        var maxH = double.PositiveInfinity;
        Avalonia.PixelRect? area = Screens?.ScreenFromVisual(this)?.WorkingArea;
        var rs = RenderScaling > 0 ? RenderScaling : 1.0;
        if (area is { } wa)
        {
            maxW = wa.Width / rs * 0.98;
            maxH = wa.Height / rs * 0.95;
        }
        MinWidth  = Math.Min(BaseMinWidth * scale, maxW);
        MinHeight = Math.Min(BaseMinHeight * scale, maxH);

        if (!IsVisible)
        {
            // Arranque: el tamaño inicial ya sale proporcional a la escala guardada.
            Width  = Math.Max(MinWidth,  Math.Min(BaseWidth * scale,  maxW));
            Height = Math.Max(MinHeight, Math.Min(BaseHeight * scale, maxH));
        }
        else if (WindowState == WindowState.Normal)
        {
            // Ventana abierta: crece/se achica en proporción al cambio de escala y se mantiene dentro de la pantalla.
            var ratio = scale / _appliedScale;
            var w = Math.Max(MinWidth,  Math.Min(ClientSize.Width * ratio,  maxW));
            var h = Math.Max(MinHeight, Math.Min(ClientSize.Height * ratio, maxH));
            Width = w;
            Height = h;
            if (area is { } r)
            {
                var px = (int)Math.Round(w * rs);
                var py = (int)Math.Round(h * rs);
                var x = Math.Max(r.X, Math.Min(Position.X, r.Right - px));
                var y = Math.Max(r.Y, Math.Min(Position.Y, r.Bottom - py));
                if (x != Position.X || y != Position.Y) Position = new Avalonia.PixelPoint(x, y);
            }
        }
        _appliedScale = scale;
    }

    // ── Repintado forzado al cerrar un loader ─────────────────────────────────────────────
    // Síntoma reportado: a veces un loader "queda pegado" en pantalla aunque la app ya lo
    // cerró (el estado es correcto: BusyState.IsBusy == false), y desaparece recién al
    // minimizar la ventana o sacar una captura de pantalla. Eso es un frame sin presentar, no
    // un error de lógica: al esconderse el overlay se detienen sus animaciones infinitas, el
    // ciclo de render queda ocioso, y en algunos backends de Linux el último frame (el que ya
    // NO tiene el overlay) no llega a mostrarse hasta que algo externo fuerza un redibujado
    // completo. Mismo síntoma que ya se vio en PokedexView (ver ForceRepaintAfterFlipAsync).
    // No se pudo reproducir en el sandbox (sin entorno gráfico): es una mitigación razonable,
    // no una causa raíz confirmada.
    private BusyStateService? _hookedBusy;
    private int _repaintGeneration;

    private void OnDataContextChangedHookBusy(object? sender, EventArgs e)
    {
        if (_hookedBusy != null) _hookedBusy.PropertyChanged -= OnBusyStateChanged;
        _hookedBusy = (DataContext as MainWindowViewModel)?.BusyState;
        if (_hookedBusy != null) _hookedBusy.PropertyChanged += OnBusyStateChanged;
    }

    private void OnBusyStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IsBusy y también los flags "Active" del loader: si el último frame con el overlay
        // quedó sin reemplazar, cualquiera de estos cambios es una buena ocasión de repintar.
        if (e.PropertyName != nameof(BusyStateService.IsBusy)
            && e.PropertyName != nameof(BusyStateService.IsVerifyLegalityLoaderActive)) return;
        if (sender is BusyStateService { IsBusy: false })
            Dispatcher.UIThread.Post(() => _ = ForceRepaintAfterLoaderAsync());
    }

    private async Task ForceRepaintAfterLoaderAsync()
    {
        // Varios empujes espaciados en vez de uno solo: el primero puede caer ANTES de que el
        // cambio de visibilidad se confirme al compositor; los siguientes cubren ese caso. Ni
        // el primero es inmediato: este evento se dispara con IsBusy=false ANTES de que Stop()
        // termine de actualizar el resto de las propiedades del loader.
        var generation = ++_repaintGeneration;
        foreach (var delayMs in new[] { 16, 80, 250, 600, 1500 })
        {
            await Task.Delay(delayMs);

            // Si arrancó otro loader o hay un cierre más nuevo en curso, no molestar.
            if (generation != _repaintGeneration || _hookedBusy is { IsBusy: true }) return;

            ForceFullRepaint();
        }
    }

    private void ForceFullRepaint()
    {
        // Invalidar solo no alcanzó en PokedexView; el cambio de opacidad de la raíz obliga a
        // recomponer toda la ventana (un salto de 0.1% es imperceptible) y se restaura enseguida.
        Opacity = 0.999;
        // Pide explícitamente un frame nuevo al compositor (el ciclo de render puede estar
        // ocioso porque se detuvieron las animaciones infinitas del loader).
        RequestAnimationFrame(_ => InvalidateVisual());
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        Dispatcher.UIThread.Post(() =>
        {
            Opacity = 1.0;
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    /// <summary>Flechas izquierda/derecha navegan el carrusel de revisión, arriba/abajo
    /// navegan el scroll de Tips, y el cover flow del selector de especie (Pantalla 4 /
    /// mockups/pokemon_picker) mientras ESE modal está visible — mismo mecanismo para los
    /// tres, ver nota sobre RoutingStrategies.Tunnel en el constructor (todos tienen el
    /// mismo problema de foco: pueden abrirse con el foco todavía en un ítem del rail
    /// lateral u otro control enfocable).</summary>
    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Zoom de la interfaz: Ctrl (o Cmd) + «+» / «−» / «0». Va primero: funciona aun con un modal abierto.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            if (e.Key is Key.OemPlus or Key.Add)          { vm.ZoomIn();    e.Handled = true; return; }
            if (e.Key is Key.OemMinus or Key.Subtract)    { vm.ZoomOut();   e.Handled = true; return; }
            if (e.Key is Key.D0 or Key.NumPad0)           { vm.ZoomReset(); e.Handled = true; return; }
        }

        // Menú Ayuda: Esc cierra el modal abierto; F1 abre la guía rápida (Tips) si no hay otro modal de Ayuda.
        if (vm.Help.IsAnyVisible)
        {
            if (e.Key == Key.Escape) { vm.Help.Close(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.F1 && !vm.IsTipsVisible)
        {
            vm.IsTipsVisible = true;
            e.Handled = true;
            return;
        }

        if (vm.IsReviewModalVisible)
        {
            if (e.Key == Key.Left)
            {
                vm.PrevReviewCardCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                vm.NextReviewCardCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (vm.IsTipsVisible)
        {
            if (e.Key == Key.Up)
            {
                vm.Tips.PrevCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                vm.Tips.NextCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (vm.SpeciesPicker.IsVisible)
        {
            var picker = vm.SpeciesPicker;
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            switch (e.Key)
            {
                case Key.Left:
                    if (shift) picker.JumpBackward(); else picker.MoveBy(-1);
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (shift) picker.JumpForward(); else picker.MoveBy(1);
                    e.Handled = true;
                    break;
                case Key.Home:
                    picker.MoveHome();
                    e.Handled = true;
                    break;
                case Key.End:
                    picker.MoveEnd();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (picker.CreateCommand.CanExecute(null))
                        picker.CreateCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }

    /// <summary>Click en una tarjeta del carrusel de revisión, o en un punto de paginación
    /// (ambos usan este mismo handler — Pantalla 3 / mockups/export_modal) — enfoca esa
    /// tarjeta vía SelectReviewCardCommand. El recentrado/reescalado en sí ya no necesita
    /// código de esta clase — lo resuelve el Canvas con posicionamiento absoluto
    /// (CardLeft/CardWidth/CardOpacity, ver MainWindowViewModel.UpdateCardLayout) más los
    /// Transitions declarados en MainWindow.axaml.</summary>
    private void ReviewCard_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not ExportReviewCardViewModel card) return;
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.SelectReviewCardCommand.CanExecute(card))
            vm.SelectReviewCardCommand.Execute(card);
    }

    private void TipDot_Tapped(object? sender, TappedEventArgs e)
    {
        // Mismo patrón que ReviewCard_Tapped de arriba — Border no tiene una
        // propiedad Command en Avalonia, así que el click se resuelve acá y se
        // delega al SelectCommand propio del punto (ver TipDotViewModel).
        if (sender is not Control control) return;
        if (control.DataContext is not TipDotViewModel dot) return;

        if (dot.SelectCommand.CanExecute(null))
            dot.SelectCommand.Execute(null);
    }
}
