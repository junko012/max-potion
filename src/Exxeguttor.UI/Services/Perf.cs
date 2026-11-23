using System.Diagnostics;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Medición de tiempos opcional para diagnosticar lentitud en la UI. Apagada por defecto (no
/// cuesta nada): se activa lanzando la app con la variable de entorno MAXPOTION_PROFILE=1 y
/// escribe líneas "[perf] etiqueta: N ms" a stderr.
/// </summary>
internal static class Perf
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("MAXPOTION_PROFILE") == "1";

    public static Stopwatch? Start() => Enabled ? Stopwatch.StartNew() : null;

    public static void Log(string label, Stopwatch? sw)
    {
        if (sw != null) Console.Error.WriteLine($"[perf] {label}: {sw.ElapsedMilliseconds} ms");
    }
}
