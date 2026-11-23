using Avalonia.Threading;
using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Captura de excepciones no controladas: las escribe en el registro y, si la ventana principal ya existe,
/// las muestra en el modal «Algo salió mal». Sin ventana (error durante el arranque) solo registra.
/// </summary>
public static class CrashHandler
{
    private static Action<Exception>? _sink;
    private static bool _installed;
    private static bool _uiHooked;

    /// <summary>Se llama una vez al arrancar (antes del splash).</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        AppLog.Initialize();
        AppLog.Info($"Max Potion {AppInfo.Version} ({AppInfo.Commit ?? "no commit"}) started on {SystemInfoReader.DescribeOs()}");

        // Tareas sin await: no tumban el proceso, pero tampoco deben pasar en silencio.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            // Servicios DBus opcionales del escritorio ausentes (menú global, etc.): no es un fallo de la app.
            if (e.Exception.Flatten().InnerExceptions.All(x => x.GetType().Name == "DBusException"))
            {
                AppLog.Info("Ignored DBus error (optional desktop service missing): " + e.Exception.GetBaseException().Message);
                return;
            }
            AppLog.Error("Unobserved task exception", e.Exception);
            Show(e.Exception);
        };
        // Cualquier otro hilo: el runtime cierra el proceso igual; al menos queda registrado.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Error(e.IsTerminating ? "Fatal exception" : "Unhandled exception", ex);
        };
    }

    /// <summary>Engancha el hilo de UI. Debe llamarse DESPUÉS de que Avalonia inicialice su plataforma (desde
    /// <c>App.OnFrameworkInitializationCompleted</c>): tocar <c>Dispatcher.UIThread</c> antes, desde Main, crea un
    /// dispatcher sin plataforma y la app muere con PlatformNotSupportedException al iniciar el bucle principal.</summary>
    public static void HookUiThread()
    {
        if (_uiHooked) return;
        _uiHooked = true;
        // Hilo de UI: se puede seguir (Handled) si ya hay dónde mostrar el error.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled exception (UI thread)", e.Exception);
            if (_sink != null) { e.Handled = true; Show(e.Exception); }
        };
    }

    /// <summary>La ventana principal registra aquí cómo mostrar el modal (siempre se invoca en el hilo de UI).</summary>
    public static void SetSink(Action<Exception> sink) => _sink = sink;

    private static void Show(Exception ex)
    {
        var sink = _sink;
        if (sink == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            try { sink(ex); } catch (Exception inner) { AppLog.Error("Failed to show the error dialog", inner); }
        });
    }
}
