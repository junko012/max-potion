using System.Text;

namespace Exxeguttor.App.Services;

/// <summary>
/// Arma la URL de «Nuevo issue» de GitHub con el formulario (<c>.github/ISSUE_TEMPLATE/bug_report.yml</c>)
/// ya prellenado. Max Potion no envía nada: solo abre esa URL en el navegador. Los parámetros usan los
/// <c>id</c> del formulario: <c>version</c>, <c>os</c>, <c>game</c>, <c>diagnostics</c>.
/// </summary>
public static class IssueUrlBuilder
{
    /// <summary>Límite práctico de una URL en navegadores/GitHub (~8 KB); se deja margen.</summary>
    public const int MaxUrlLength = 7500;
    public const int MaxTitleLength = 200;

    /// <param name="errorDetail">Detalle de una excepción (modal «Algo salió mal»); va al final del diagnóstico, así se recorta primero.</param>
    public static string BuildBugReport(string? title, DiagnosticsInfo info, string? homeDir = null, string? userName = null, string? errorDetail = null)
    {
        var cleanTitle = Truncate(Normalize(PathScrubber.Scrub(title, homeDir, userName)), MaxTitleLength);
        var diagnostics = PathScrubber.Scrub(info.ToText(), homeDir, userName);
        if (!string.IsNullOrWhiteSpace(errorDetail))
            diagnostics += "\n\nError:\n" + PathScrubber.Scrub(errorDetail, homeDir, userName);

        string Url(string diag) => Compose(cleanTitle, info, diag);

        var url = Url(diagnostics);
        // Si no entra, se recorta primero el diagnóstico (el título y los datos cortos se conservan).
        if (url.Length > MaxUrlLength)
        {
            var lines = diagnostics.Split('\n');
            for (int keep = lines.Length - 1; keep >= 1 && url.Length > MaxUrlLength; keep--)
                url = Url(string.Join('\n', lines.Take(keep)) + "\n… (trimmed)");
        }
        if (url.Length > MaxUrlLength)
            url = Url("(omitted: too long)");
        return url;
    }

    private static string Compose(string title, DiagnosticsInfo info, string diagnostics)
    {
        var sb = new StringBuilder(AppLinks.NewIssueUrl);
        sb.Append("?template=").Append(AppLinks.BugTemplate);
        if (title.Length > 0) sb.Append("&title=").Append(Uri.EscapeDataString(title));
        sb.Append("&version=").Append(Uri.EscapeDataString(Truncate(info.VersionLine, 80)));
        sb.Append("&os=").Append(Uri.EscapeDataString(Truncate(info.OsLine, 160)));
        if (!string.IsNullOrEmpty(info.SaveGame)) sb.Append("&game=").Append(Uri.EscapeDataString(Truncate(info.SaveGame, 80)));
        sb.Append("&diagnostics=").Append(Uri.EscapeDataString(diagnostics));
        return sb.ToString();
    }

    private static string Normalize(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
