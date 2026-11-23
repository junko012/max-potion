using System;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Views;

/// <summary>
/// Ventana de carga mínima mostrada al arrancar la app, mientras <c>MainWindowViewModel</c>
/// (y sus ~14 servicios/sub-viewmodels — ver App.axaml.cs) todavía se está construyendo.
///
/// REDISEÑO — ver comentario largo en SplashWindow.axaml sobre por qué se reemplazó la
/// versión anterior (fondo ilustrado de Exeggutor) por esta estética "pantalla LCD vieja".
///
/// CORRECCIÓN DE SINCRONIZACIÓN (segunda vuelta): la primera versión de este archivo
/// asumía que App.axaml.cs seguía sosteniendo cada etapa con Thread.Sleep sobre el hilo de
/// UI (como hacía el splash viejo) — pero eso bloquea el reloj de animación de Avalonia por
/// completo, así que ninguna Transition ni ningún Task.Delay interno (el flicker, el reveal
/// del wordmark) llegaban a correr. Ahora SetStage/SetStatus solo ENCOLAN el mensaje
/// (instantáneo, no bloqueante, seguro de llamar desde cualquier hilo) y un consumidor
/// async interno (<see cref="ConsumeStagesAsync"/>) es quien decide cuánto tiempo mínimo
/// sostener cada uno — la carga real puede ir todo lo rápido que quiera, la experiencia
/// visual ya no depende de eso.
/// </summary>
public partial class SplashWindow : Window
{
    // Alto de una fila del carrusel de etapas (viewport de 100 px en SplashWindow.axaml: fila activa
    // a 34 px, la anterior a 6 px, el resto fuera de vista).
    private const double StageRowHeight = 28;
    private const double StageViewportCenterTop = 34; // fila activa dentro del viewport de 100 px (splash pokébola)
    private const double WordmarkRowHeight = 30;       // debe calzar con el Canvas del wordmark en el XAML

    // Tiempo mínimo que cada mensaje de carga real queda visible antes de pasar al
    // siguiente, aunque la carga de ese módulo haya sido instantánea — mismo espíritu que
    // GIMP. Tiene que ser claramente más largo que la Transition de 0.75s del carrusel
    // (ver SplashWindow.axaml) — con un valor apenas mayor, el siguiente mensaje empieza a
    // moverse antes de que el anterior termine de asentarse, y se ve todo pegoteado/
    // artificial en vez de una sucesión de pasos prolijos.
    private const int MinStageVisibleMs = 1300;

    // El último módulo real ("Preparando ventana principal...") se sostiene un poco más
    // que el resto — da la sensación de que ahí hay algo más sustancial cargándose, en vez
    // de que todos los pasos se sientan intercambiables.
    private const int LastModuleVisibleMs = 1900;

    // Los dos mensajes "marco" (arranque/cierre) se sostienen todavía un poco más.
    private const int MilestoneVisibleMs = 1600;
    /// <summary>Lo que se espera tras agregar el primer renglón (entrada de 0,75 s) antes de ceder el hilo de UI.</summary>
    private const int FirstStageEntryMs = 900;

    // Mismos tonos que la paleta "LCD" acordada en la sesión de diseño (mockup HTML) — si
    // se retoca la paleta alguna vez, estos dos (más los hex sueltos del XAML) son los
    // únicos valores a tocar.
    private static readonly IBrush InkBrush = new SolidColorBrush(Color.Parse("#3C4235"));
    private static readonly IBrush InkFaintBrush = new SolidColorBrush(Color.Parse("#7D8371"));

    private readonly ObservableCollection<SplashStageItem> _stages = new();
    private readonly Channel<(string Text, bool IsMilestone, bool ExtraHold, bool Emphasize, bool Complete)> _stageChannel =
        Channel.CreateUnbounded<(string, bool, bool, bool, bool)>();

    // Aro de progreso del botón central: cuántos renglones se mostraron sobre los que se esperan en el
    // arranque normal (entorno + 4 módulos + cierre). El mensaje de cierre lo completa siempre.
    private const int ExpectedStages = 6;
    private int _stagesAdded;
    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.Parse("#1D9E75"));
    // Primer renglón: en el idioma de la UI (se lee en el constructor, antes de que exista MainWindowViewModel).
    private string _firstStageText = "Preparing environment...";
    private readonly TaskCompletionSource _wordmarkReadyTcs = new();
    private readonly TaskCompletionSource _stagesDrainedTcs = new();

    public SplashWindow()
    {
        InitializeComponent();
        // El splash corre antes de que exista MainWindowViewModel: lee el mismo idioma guardado/del sistema.
        try
        {
            var loc = new Exxeguttor.UI.i18n.LocalizationService();
            Subtitle.Text = loc["Splash_Subtitle"];
            _firstStageText = loc["Splash_Stage_Start"];
        }
        catch { /* queda el texto del XAML / el texto por defecto */ }
        StageItemsControl.ItemsSource = _stages;

        Opened += (_, _) =>
        {
            // El círculo necesita ventana transparente (Linux con compositor, Windows 10/11). Si el sistema
            // no la da (sin compositor, escritorio remoto...), se pinta un fondo liso y sin sombra para
            // que no queden esquinas negras alrededor del círculo.
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
            {
                Background = new SolidColorBrush(Color.Parse("#C9CDBF"));
                SplashCard.BoxShadow = default;
            }
            RunPowerOnSequence();
        };

        // Fire-and-forget intencional: es una corrutina de UI puramente cosmética, nunca
        // debería poder tirar abajo el arranque real de la app. Si algo saliera mal acá
        // adentro, que el splash se vea raro es preferible a que una excepción no
        // observada rompa cualquier otra cosa.
        _ = ConsumeStagesAsync();
    }

    /// <summary>Compat con código existente que llama <c>splash.SetStatus(...)</c> — misma
    /// firma pública que la versión anterior del splash. Ya no bloquea nada: solo encola.</summary>
    public void SetStatus(string text) => _stageChannel.Writer.TryWrite((text, false, false, true, false));

    /// <summary><paramref name="fraction"/> se mantiene en la firma por compatibilidad con
    /// los callers existentes en App.axaml.cs (antes alimentaba la ProgressBar que sacamos
    /// del diseño nuevo) pero ya no se usa para nada visual — el carrusel de etapas ya
    /// comunica avance por sí solo. <paramref name="isMilestone"/> es para los mensajes
    /// "marco" (arranque/cierre: negrita + sombra + un poco más de tiempo en pantalla).
    /// <paramref name="extraHold"/> es para un mensaje de carga real puntual que debería
    /// quedar un poco más de tiempo que el resto SIN llevar la negrita/sombra de los
    /// mensajes marco — pensado para el último paso real antes del cierre (da la sensación
    /// de que ahí hay algo más sustancial cargándose). Seguro de llamar desde cualquier
    /// hilo, incluido sincrónicamente desde adentro de un constructor.</summary>
    public void SetStage(string text, double fraction, bool isMilestone = false, bool extraHold = false, bool emphasize = true) =>
        _stageChannel.Writer.TryWrite((text, isMilestone, extraHold, emphasize, fraction >= 1.0));

    /// <summary>Avisa que no van a llegar más etapas — App.axaml.cs la llama justo después
    /// de encolar el mensaje final de cierre. Deja que <see cref="StagesDrainedTask"/> se
    /// resuelva apenas ese último mensaje termine de sostenerse en pantalla, en vez de
    /// necesitar otro buffer/Sleep a ciegas desde afuera.</summary>
    public void CompleteStages() => _stageChannel.Writer.TryComplete();

    /// <summary>Se resuelve cuando ya se procesó y se sostuvo en pantalla el último mensaje
    /// encolado (después de llamar <see cref="CompleteStages"/>) — App.axaml.cs debería
    /// esperar esto (await) antes de cerrar la ventana, en vez de un Thread.Sleep fijo.</summary>
    public Task StagesDrainedTask => _stagesDrainedTcs.Task;

    /// <summary>Se resuelve cuando terminó la animación de apertura (flicker + reveal del
    /// wordmark) y ya se habilitó el carrusel de etapas. App.axaml.cs espera esto ANTES de
    /// arrancar la construcción pesada y síncrona de MainWindowViewModel/MainWindow — esa
    /// construcción no tiene ningún await adentro, así que monopoliza el hilo de UI de
    /// punta a punta; si arrancara antes de este punto, la animación de apertura quedaría
    /// congelada a mitad de camino hasta que la construcción termine (exactamente el bug
    /// que causaba que el splash apareciera en blanco y recién "reviviera" después de que
    /// ya se hubiera mostrado la ventana principal).</summary>
    public Task IntroCompletedTask => _wordmarkReadyTcs.Task;

    /// <summary>Consumidor único de la cola de etapas — corre en el hilo de UI (gracias al
    /// contexto de sincronización, las continuaciones de los await de acá vuelven solas al
    /// hilo de UI) pero SIN bloquearlo nunca: usa await Task.Delay en vez de Thread.Sleep,
    /// así que el reloj de animación de Avalonia sigue corriendo mientras tanto.</summary>
    private async Task ConsumeStagesAsync()
    {
        try
        {
            // El primer mensaje ("Preparando entorno") espera a que el wordmark ya haya
            // terminado de revelarse (ver RunPowerOnSequence) — si llegaran etapas reales
            // encoladas ANTES de eso, se procesan recién después, en orden, nunca se
            // pierden ni se desordenan.
            await _wordmarkReadyTcs.Task;

            // El primer renglón ("Preparando entorno") ya lo agregó RunPowerOnSequence y se dejó entrar con
            // su animación ANTES de liberar el hilo; acá solo se completa su pausa de hito.
            await Task.Delay(MilestoneVisibleMs - FirstStageEntryMs);

            await foreach (var (text, isMilestone, extraHold, emphasize, complete) in _stageChannel.Reader.ReadAllAsync())
            {
                AddStageLine(text, isMilestone, emphasize, complete);
                var holdMs = isMilestone ? MilestoneVisibleMs
                    : extraHold ? LastModuleVisibleMs
                    : MinStageVisibleMs;
                await Task.Delay(holdMs);
            }
        }
        finally
        {
            _stagesDrainedTcs.TrySetResult();
        }
    }

    private void AddStageLine(string text, bool isMilestone, bool emphasize = true, bool complete = false)
    {
        var item = new SplashStageItem(text, isMilestone, emphasize)
        {
            // Arranca oculto un renglón por debajo del centro (fuera del viewport
            // recortado) — el salto a su posición final recién se dispara en
            // RecalculateStageLayout(), pospuesto un tick de layout más abajo. Si se
            // asignara la posición final en el mismo momento en que se agrega el ítem al
            // ItemsControl, la Transition no llega a "ver" el estado inicial oculto y el
            // renglón aparece ya asentado, sin ninguna animación de entrada.
            Top = StageViewportCenterTop + StageRowHeight,
            Opacity = 0,
            FontSize = 10,
            Foreground = InkFaintBrush,
        };
        _stages.Add(item);
        UpdateProgress(complete);

        Dispatcher.UIThread.Post(RecalculateStageLayout, DispatcherPriority.Loaded);
    }

    /// <summary>Aro de progreso del botón central (0 → 359,9°) y punto verde al completar.</summary>
    private void UpdateProgress(bool complete)
    {
        _stagesAdded++;
        var fraction = complete ? 1.0 : Math.Min(0.95, (_stagesAdded - 1) / (double)(ExpectedStages - 1));
        ProgressArc.SweepAngle = fraction >= 1.0 ? 359.9 : 360.0 * fraction;
        if (complete) ButtonDot.Fill = DoneBrush;
    }

    private void RecalculateStageLayout()
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            var item = _stages[i];
            int distanceFromEnd = _stages.Count - 1 - i; // 0 = renglón actual (el más nuevo)

            item.Top = StageViewportCenterTop - distanceFromEnd * StageRowHeight;
            item.IsMilestoneActive = item.IsMilestone && item.Emphasize && distanceFromEnd == 0;

            switch (distanceFromEnd)
            {
                case 0:
                    item.Opacity = 1;
                    item.FontSize = item.IsMilestone && item.Emphasize ? 13 : 12;
                    item.Foreground = InkBrush;
                    break;
                case 1:
                    item.Opacity = 0.4;
                    item.FontSize = 11;
                    item.Foreground = InkFaintBrush;
                    break;
                default:
                    item.Opacity = 0;
                    item.FontSize = 10;
                    item.Foreground = InkFaintBrush;
                    break;
            }
        }
    }

    /// <summary>Secuencia de arranque, un único disparo (nunca en loop):
    /// flicker de encendido (~0.5s) → reveal del wordmark vía barrido (~1.1s) →
    /// aparece el subtítulo y se habilita el carrusel de etapas (ver
    /// <see cref="ConsumeStagesAsync"/>, que espera <see cref="_wordmarkReadyTcs"/> antes
    /// de mostrar el primer mensaje). Los nombres de elemento (PowerFlicker, WordmarkCover,
    /// ScanBar, Subtitle, StageCarouselContainer) están declarados en SplashWindow.axaml
    /// con sus Transitions ya puestas; acá solo se dispara el cambio de valores que las
    /// activa.</summary>
    private async void RunPowerOnSequence()
    {
        // Flicker: unos pocos saltos de opacidad cortos en vez de una animación
        // declarativa de keyframes — más simple de verificar y da una sensación de
        // parpadeo equivalente en ~0.5s. Cada salto anima suave gracias a la Transition
        // de 0.06s puesta sobre PowerFlicker en el XAML.
        (double opacity, int holdMs)[] flickerSteps =
        {
            (0.15, 60), (0.85, 60), (0.05, 60), (1.0, 60), (0.0, 260),
        };
        foreach (var (opacity, holdMs) in flickerSteps)
        {
            PowerFlicker.Opacity = opacity;
            await Task.Delay(holdMs);
        }
        PowerFlicker.IsVisible = false;

        // Reveal del wordmark: la "tapa" se encoge de 30 a 0 (ver WordmarkCover en el
        // XAML, anclada por abajo para que el reveal avance de arriba hacia abajo) y la
        // barra de escaneo baja en sincro — misma duración/easing en ambas Transitions
        // del XAML, así que aunque son dos propiedades independientes se mueven juntas.
        WordmarkCover.Height = 0;
        ScanBar.Opacity = 0;
        Canvas.SetTop(ScanBar, WordmarkRowHeight);

        await Task.Delay(1100);

        // El subtítulo se dibuja primero y se sostiene solo — recién después de un
        // respiro se habilita el carrusel de etapas. Antes las dos cosas pasaban al mismo
        // tiempo, así que el primer mensaje aparecía compitiendo visualmente con el propio
        // subtítulo en vez de sentirse como el siguiente paso de la secuencia.
        Subtitle.Opacity = 1;
        await Task.Delay(500);

        StageCarouselContainer.Opacity = 1;
        await Task.Delay(450); // fade-in del carrusel (0.4 s)

        // El primer mensaje se agrega y se espera a que termine de entrar (0,75 s) ANTES de liberar
        // _wordmarkReadyTcs. Antes se agregaba justo después de liberarlo: la continuación de
        // App.axaml.cs corre inline y arranca la construcción pesada y síncrona de la ventana principal,
        // que bloquea el hilo de UI antes de que el renglón llegara a pintarse en su estado inicial
        // (oculto, un renglón más abajo) — por eso aparecía "de la nada", sin animar como los demás.
        // Hito para los tiempos (entrada con pausa), pero sin negrita ni sombra: igual que el resto de renglones.
        AddStageLine(_firstStageText, isMilestone: true, emphasize: false);
        await Task.Delay(FirstStageEntryMs);
        _wordmarkReadyTcs.TrySetResult();
    }
}
