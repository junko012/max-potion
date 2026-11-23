using System.Text;

namespace Exxeguttor.App.Services;

/// <summary>Texto legible de una excepción para el modal de error, el registro y el reporte (sin rutas personales).</summary>
public static class CrashReport
{
    public const int MaxFrames = 12;

    /// <summary>«Tipo: mensaje» (con las excepciones internas) y las primeras líneas de la pila.</summary>
    public static string Format(Exception ex, int maxFrames = MaxFrames)
    {
        var sb = new StringBuilder();
        var depth = 0;
        for (var e = ex is AggregateException agg ? agg.Flatten().InnerExceptions.FirstOrDefault() ?? ex : ex;
             e != null && depth < 4; e = e.InnerException, depth++)
        {
            if (depth > 0) sb.AppendLine().Append("--> ");
            sb.Append(e.GetType().Name).Append(": ").Append(e.Message.Replace('\r', ' ').Replace('\n', ' '));
            foreach (var line in (e.StackTrace ?? "").Split('\n').Take(maxFrames))
            {
                var l = line.TrimEnd('\r');
                if (l.Length > 0) sb.AppendLine().Append(l);
            }
        }
        return PathScrubber.Scrub(sb.ToString());
    }

    /// <summary>Título corto para el reporte: «Tipo: mensaje» de la excepción más externa.</summary>
    public static string Summary(Exception ex)
    {
        var e = ex is AggregateException agg ? agg.Flatten().InnerExceptions.FirstOrDefault() ?? ex : ex;
        var msg = e.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return PathScrubber.Scrub($"{e.GetType().Name}: {msg}");
    }
}
