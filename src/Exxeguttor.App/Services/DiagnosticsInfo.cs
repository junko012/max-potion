using System.Text;

namespace Exxeguttor.App.Services;

/// <summary>
/// Datos de diagnóstico para «Sistema» y «Reportar un error». Solo datos técnicos: nunca el nombre del
/// entrenador, el ID, rutas con el nombre de usuario ni el contenido del save (sí el juego y la generación).
/// </summary>
public sealed record DiagnosticsInfo(
    string AppVersion,
    string? Commit,
    string? BuildDate,
    string DotNet,
    string Pkhex,
    string Avalonia,
    string Os,
    string? Desktop,
    string? Session,
    string UiLanguage,
    string DataLanguage,
    string? SaveGame,
    string? DbVersion = null)
{
    /// <summary>Bloque de texto (etiquetas fijas en inglés, igual en todos los idiomas: es para quien atiende el reporte).</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        var build = string.Join(", ", new[] { Commit is { Length: > 0 } c ? "commit " + c : null, BuildDate }.Where(x => !string.IsNullOrEmpty(x)));
        sb.AppendLine($"Max Potion     {AppVersion}{(build.Length > 0 ? $" ({build})" : "")}");
        sb.AppendLine($".NET           {DotNet}");
        sb.AppendLine($"PKHeX.Core     {Pkhex}");
        sb.AppendLine($"Avalonia       {Avalonia}");
        sb.AppendLine($"Database       {(string.IsNullOrEmpty(DbVersion) ? "(unknown)" : DbVersion)}");
        sb.AppendLine($"OS             {Os}");
        if (!string.IsNullOrEmpty(Desktop) || !string.IsNullOrEmpty(Session))
            sb.AppendLine($"Desktop        {string.Join(" · ", new[] { Desktop, Session }.Where(x => !string.IsNullOrEmpty(x)))}");
        sb.AppendLine($"Language       UI {UiLanguage} · data {DataLanguage}");
        sb.Append($"Open save      {(string.IsNullOrEmpty(SaveGame) ? "(none)" : SaveGame)}");
        return sb.ToString();
    }

    /// <summary>Versión corta para el campo «version» del formulario de GitHub.</summary>
    public string VersionLine => Commit is { Length: > 0 } c ? $"{AppVersion} ({c})" : AppVersion;

    /// <summary>Sistema, escritorio y sesión en una línea (campo «os» del formulario).</summary>
    public string OsLine => string.Join(" · ", new[] { Os, Desktop, Session }.Where(x => !string.IsNullOrEmpty(x)));
}
