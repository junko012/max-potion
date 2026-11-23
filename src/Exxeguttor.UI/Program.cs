using Avalonia;
using Exxeguttor.UI;
using Exxeguttor.UI.Services;

// Punto de entrada de Exxeguttor
// Avalonia requiere que este sea el único método en la clase de entrada
// Registro de errores y captura de excepciones no controladas (ver CrashHandler).
CrashHandler.Install();

return AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    // Sin menú global DBus: la app no usa menú nativo y, en escritorios sin registrador
    // (com.canonical.AppMenu.Registrar), Avalonia lanza una DBusException en segundo plano.
    .With(new X11PlatformOptions { UseDBusMenu = false })
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
