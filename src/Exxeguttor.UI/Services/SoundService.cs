using System.Diagnostics;
using System.Reflection;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Reproductor de sonido mínimo — el proyecto no tenía ninguna dependencia de audio
/// hasta ahora, y sumar una librería completa (NAudio, OpenAL, etc.) para UN sonido
/// cortito de UI no se justifica. En vez de eso: extraemos el .wav embebido a un
/// archivo temporal una sola vez (cacheado en disco entre reproducciones) y lo tiramos
/// por un reproductor de línea de comandos ya presente en casi cualquier escritorio
/// Linux — <c>paplay</c> (PulseAudio/PipeWire, lo más común hoy) con <c>aplay</c>
/// (ALSA) como respaldo si el primero no está instalado.
///
/// Si ninguno de los dos existe en el sistema, falla en silencio (try/catch) — un
/// sonido decorativo de UI nunca debería poder romper la app ni bloquear el hilo de UI.
/// </summary>
public static class SoundService
{
    private static string? _cachedWavPath;
    private static readonly object _lock = new();

    /// <summary>Reproduce Assets/sounds/spark_chime.wav (fire-and-forget, no bloquea).
    /// Pensado para el estallido de chispas del módulo Pokédex, pero es genérico —
    /// cualquier otro efecto de UI corto puede reusar este mismo mecanismo.</summary>
    public static void PlaySparkChime() => Play("Exxeguttor.UI.Assets.sounds.spark_chime.wav");

    private static void Play(string embeddedResourceName)
    {
        try
        {
            var path = GetOrExtractWav(embeddedResourceName);
            if (path == null) return;

            if (!TryStart("paplay", path))
                TryStart("aplay", path);
        }
        catch
        {
            // sonido decorativo — nunca debe tirar abajo la app ni mostrar un error al
            // usuario si el sistema no tiene reproductor de línea de comandos instalado.
        }
    }

    private static bool TryStart(string executable, string wavPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"\"{wavPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            // típicamente Win32Exception "No such file or directory" si el ejecutable
            // no existe en el sistema — se prueba el siguiente reproductor de la lista.
            return false;
        }
    }

    private static string? GetOrExtractWav(string embeddedResourceName)
    {
        lock (_lock)
        {
            if (_cachedWavPath != null && File.Exists(_cachedWavPath))
                return _cachedWavPath;

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(embeddedResourceName);
            if (stream == null) return null;

            var tempPath = Path.Combine(Path.GetTempPath(), $"maxpotion_{Path.GetFileName(embeddedResourceName)}");
            using (var fileStream = File.Create(tempPath))
                stream.CopyTo(fileStream);

            _cachedWavPath = tempPath;
            return tempPath;
        }
    }
}
