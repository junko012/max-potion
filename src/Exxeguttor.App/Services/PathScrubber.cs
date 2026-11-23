namespace Exxeguttor.App.Services;

/// <summary>
/// Saca datos personales de textos de diagnóstico: la carpeta personal pasa a <c>~</c> y el nombre de usuario
/// del sistema deja de aparecer suelto. Nunca se incluyen rutas con el nombre de usuario en un reporte.
/// </summary>
public static class PathScrubber
{
    public static string Scrub(string? text, string? homeDir = null, string? userName = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        homeDir ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        userName ??= Environment.UserName;

        var result = text;
        if (!string.IsNullOrWhiteSpace(homeDir) && homeDir.Length > 1)
        {
            var home = homeDir.TrimEnd('/', '\\');
            result = result.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
            // En Windows a veces la misma ruta aparece con la barra contraria.
            result = result.Replace(home.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
        }
        // Nombre de usuario suelto (p. ej. en una ruta que no era la carpeta personal): solo si es razonablemente
        // largo, para no destrozar palabras comunes de usuarios cortos como "al" o "me".
        if (!string.IsNullOrWhiteSpace(userName) && userName.Length >= 4)
            result = result.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        return result;
    }
}
