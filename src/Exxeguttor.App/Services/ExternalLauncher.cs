using System.Diagnostics;

namespace Exxeguttor.App.Services;

/// <summary>
/// Abre un enlace o una carpeta con la aplicación del sistema: <c>xdg-open</c> en Linux, el shell en Windows,
/// <c>open</c> en macOS. Devuelve false (nunca lanza) si no se pudo; quien llama copia el enlace al portapapeles.
/// </summary>
public static class ExternalLauncher
{
    public static bool Open(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return false;
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
                psi = new ProcessStartInfo(urlOrPath) { UseShellExecute = true };
            else
            {
                psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(urlOrPath);
            }
            using var proc = Process.Start(psi);
            return proc != null;
        }
        catch { return false; }
    }

    /// <summary>Abre una carpeta, creándola si todavía no existe (p. ej. la de registros antes del primer error).</summary>
    public static bool OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); } catch { return false; }
        return Open(path);
    }
}
