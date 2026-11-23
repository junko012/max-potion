using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;
using Exxeguttor.UI.ViewModels;
using Exxeguttor.UI.Views;

namespace Exxeguttor.UI;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        NumericInput.Register();     // filtro de tipo/rango de los campos numéricos y de fecha
        CrashHandler.HookUiThread(); // ya con la plataforma de Avalonia lista (ver CrashHandler)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Pantalla de carga estilo GIMP: se muestra ANTES de construir
            // MainWindowViewModel (que encadena ~14 servicios/sub-viewmodels — Box, Party,
            // Editor, Bag, EditApplyService, RecentSaves, etc. — todo síncrono en el hilo de
            // UI) y antes de parsear el XAML de MainWindow (60K+, el árbol visual más grande
            // del proyecto). Ninguna de las dos cosas se puede evitar, pero ahora corren
            // DESPUÉS de que el splash ya esté pintado en pantalla, en vez de bloquear la
            // aparición de la primera ventana.
            //
            // ShutdownMode explícito: por defecto la lifetime cierra la app cuando se cierra
            // la MainWindow "actual". Como acá cerramos el splash y recién DESPUÉS asignamos
            // la ventana real, necesitamos apagado manual (ver MainWindow.Closed más abajo)
            // para que cerrar el splash no tumbe la app antes de que aparezca la real.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashWindow();
            splash.Show();

            // HISTORIAL DE ESTA SECCIÓN (dos bugs de sincronización distintos, en dos
            // vueltas):
            //
            // 1) La primera versión sostenía cada etapa bloqueando el hilo de UI con
            //    Thread.Sleep (heredado del splash viejo, que solo hacía saltos instantáneos
            //    de texto/barra sin animación). Eso bloqueaba el reloj de animación de
            //    Avalonia por completo — se sacó, y SetStage/SetStatus pasaron a solo
            //    ENCOLAR el mensaje en un Channel (instantáneo, no bloqueante), con
            //    SplashWindow.ConsumeStagesAsync decidiendo cuánto sostener cada uno vía
            //    await Task.Delay.
            //
            // 2) Sacar el Thread.Sleep no alcanzó: la construcción de MainWindowViewModel/
            //    MainWindow de acá abajo es código síncrono sin ningún await adentro, así
            //    que igual monopoliza el hilo de UI de punta a punta mientras corre — si
            //    arrancaba antes de que la animación de apertura del splash (flicker +
            //    reveal del wordmark) llegara a completarse, esa animación quedaba
            //    congelada a mitad de camino hasta que esta construcción terminara. Por eso
            //    el splash aparecía en blanco, la ventana principal aparecía primero, y
            //    recién DESPUÉS el splash "revivía" mostrando los mensajes de golpe.
            //
            //    La corrección es el await de acá abajo (IntroCompletedTask): asegura que
            //    la animación de apertura del splash haya terminado ANTES de arrancar esta
            //    construcción, en vez de confiar en que las prioridades del dispatcher se
            //    acomoden solas (no lo hacían).
            //
            // 3) Con eso solucionado, apareció el mismo problema un escalón más abajo:
            //    main.Show() se llamaba apenas terminaba la construcción (síncrona, rápida),
            //    sin esperar a que el carrusel de etapas llegara a reproducir los mensajes
            //    ya encolados — la ventana principal aparecía primero, y recién cuando el
            //    hilo de UI volvía a quedar libre el splash mostraba todo de golpe. No hay
            //    forma real de solapar la animación del splash con trabajo síncrono en un
            //    único hilo de UI sin mover ese trabajo a otro hilo (cambio de arquitectura
            //    más riesgoso de tocar sin poder compilar acá). La solución adoptada es más
            //    simple y más fiel a lo pedido desde el arranque (estilo GIMP): dejar que el
            //    splash reproduzca TODA su secuencia de punta a punta — incluido el mensaje
            //    final — y recién ahí mostrar la ventana principal. Ver el await de
            //    StagesDrainedTask más abajo, movido a ANTES de main.Show().
            Dispatcher.UIThread.Post(async () =>
            {
                // Espera a que el splash termine su propia animación de apertura (flicker +
                // reveal del wordmark) ANTES de arrancar la construcción pesada y síncrona
                // de MainWindowViewModel/MainWindow — ver el comentario largo sobre
                // IntroCompletedTask en SplashWindow.axaml.cs. Esa construcción no tiene
                // ningún await adentro, así que monopoliza el hilo de UI de punta a punta;
                // sin este await, competía con la animación del splash y ganaba siempre,
                // dejándola congelada hasta que la ventana principal ya estaba mostrada.
                await splash.IntroCompletedTask;

                void ShowStage(string message) => splash.SetStage(message, 0);

                var vm = new MainWindowViewModel(onProgress: ShowStage);
                var main = new MainWindow { DataContext = vm };

                // Última etapa real, ya fuera de MainWindowViewModel (ese constructor ya
                // terminó con sus propios mensajes) — cubre el parseo del XAML de
                // MainWindow que está a punto de ocurrir en el Show() de más abajo.
                // extraHold: true porque es el último módulo real antes del mensaje de
                // cierre — se sostiene un poco más que el resto para que se sienta que ahí
                // hay algo más sustancial cargándose, en vez de que todos los pasos se
                // sientan intercambiables.
                splash.SetStage(vm.Loc["Splash_Stage_MainWindow"], 0, extraHold: true);

                main.Closed += (_, _) => { AppLog.Info("Max Potion closed."); desktop.Shutdown(); };

                // Desde acá los errores no controlados se muestran en «Algo salió mal» (además del registro).
                CrashHandler.SetSink(ex => vm.Help.ShowCrash(ex));
                vm.NotifyDbCompatibility();

                // Encolamos el mensaje final ACÁ, antes de mostrar nada — así el carrusel
                // ya tiene toda la secuencia completa en cola (los 4 mensajes de carga real
                // + el de cierre) y la reproduce de punta a punta sin que main.Show()
                // compita por el hilo de UI en el medio.
                splash.SetStage(vm.Loc["Splash_Done"], 1.0, isMilestone: true, emphasize: false);
                splash.CompleteStages();

                // VUELTA 3 de este mismo problema, ver historial arriba: acá el bug era que
                // main.Show() se llamaba apenas terminaba la construcción (rápida), sin
                // esperar a que el carrusel llegara a reproducir los mensajes ya encolados
                // — la ventana principal aparecía, y RECIÉN AHÍ (cuando el hilo de UI volvía
                // a quedar libre) el splash mostraba todo de golpe.
                //
                // La solución no es tratar de solapar animación con trabajo real — en un
                // único hilo de UI eso no se puede lograr de verdad sin mover la
                // construcción a otro hilo (cambio de arquitectura mucho más riesgoso). Es
                // más simple, y más fiel a lo pedido desde el arranque (estilo GIMP): el
                // splash reproduce TODA su secuencia de punta a punta, sin solaparse con
                // nada, y recién después aparece la ventana principal.
                await splash.StagesDrainedTask;

                desktop.MainWindow = main;
                main.Show();
                splash.Close();
            }, DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
