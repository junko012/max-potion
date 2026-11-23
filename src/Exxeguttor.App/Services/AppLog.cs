using System.Text;

namespace Exxeguttor.App.Services;

/// <summary>
/// Registro de errores y eventos importantes en un archivo de texto rotativo: <c>maxpotion.log</c> + 2 anteriores
/// (<c>.1</c>, <c>.2</c>), de hasta 1 MB cada uno (≈3 MB como máximo). Sin datos personales: cada línea pasa por
/// <see cref="PathScrubber"/>. Nunca lanza: si no se puede escribir (disco lleno, permisos), simplemente no registra.
/// </summary>
public static class AppLog
{
    public const string FileName = "maxpotion.log";
    public const int Keep = 3;                       // archivo actual + 2 rotados
    public const long DefaultMaxBytes = 1_000_000;   // ~1 MB por archivo

    private static readonly object Gate = new();
    private static string? _dir;
    private static long _maxBytes = DefaultMaxBytes;

    /// <summary>Carpeta del registro (null hasta <see cref="Initialize"/>).</summary>
    public static string? Directory => _dir;

    public static void Initialize(string? dir = null, long maxBytes = DefaultMaxBytes)
    {
        lock (Gate)
        {
            _dir = dir ?? AppPaths.LogsDir;
            _maxBytes = maxBytes;
            try { System.IO.Directory.CreateDirectory(_dir); } catch { /* se intentará de nuevo al escribir */ }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string context, Exception ex) =>
        Write("ERROR", context + Environment.NewLine + CrashReport.Format(ex));

    public static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                if (_dir == null) return;
                System.IO.Directory.CreateDirectory(_dir);
                var path = Path.Combine(_dir, FileName);
                RotateIfNeeded(path);
                var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z [{level}] {PathScrubber.Scrub(message)}{Environment.NewLine}";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch { /* el registro nunca debe romper la app */ }
    }

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < _maxBytes) return;
        // .log.(Keep-1) se borra; los demás suben un número; el actual pasa a .1
        var oldest = $"{path}.{Keep - 1}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = Keep - 2; i >= 1; i--)
            if (File.Exists($"{path}.{i}")) File.Move($"{path}.{i}", $"{path}.{i + 1}");
        File.Move(path, $"{path}.1");
    }
}
