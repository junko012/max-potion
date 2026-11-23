namespace Exxeguttor.App.Services;

/// <summary>
/// Carpetas de la app. Configuración: la misma que ya usan <see cref="AppSettingsService"/> y
/// <see cref="RecentSavesService"/> (<c>~/.config/maxpotion</c> en Linux). Registros: <c>$XDG_STATE_HOME/maxpotion/logs</c>
/// (por defecto <c>~/.local/state/maxpotion/logs</c>) en Linux; en Windows y macOS, bajo datos locales del usuario.
/// </summary>
public static class AppPaths
{
    public const string AppFolder = "maxpotion";

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);

    public static string LogsDir => ResolveLogsDir(
        OperatingSystem.IsLinux(),
        Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Lógica pura (testeable): según plataforma y variables de entorno.</summary>
    public static string ResolveLogsDir(bool linux, string? xdgStateHome, string home, string localAppData)
    {
        if (linux)
        {
            var state = !string.IsNullOrWhiteSpace(xdgStateHome) && Path.IsPathRooted(xdgStateHome)
                ? xdgStateHome
                : Path.Combine(home, ".local", "state");
            return Path.Combine(state, AppFolder, "logs");
        }
        return Path.Combine(localAppData, AppFolder, "logs");
    }
}
