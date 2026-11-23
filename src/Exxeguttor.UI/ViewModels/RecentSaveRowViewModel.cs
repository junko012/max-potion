using Avalonia.Media;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Una fila de la lista "Recientes" en la Pantalla 1 (Estado vacío / Carga de save).
/// Envuelve un <see cref="RecentSaveEntry"/> (dato crudo, Exxeguttor.App) con lo que la vista
/// necesita para dibujarla: subtítulo expandido, fecha relativa, y la portada (sprite real si
/// existe, o el estilo de fallback procedural si todavía no).
/// </summary>
public class RecentSaveRowViewModel
{
    public string FilePath { get; }
    public string FileName { get; }
    public string Subtitle { get; }
    public string RelativeDate { get; }

    /// <summary>Fila más reciente de la lista — la vista le da el fondo destacado del mockup.</summary>
    public bool IsLatest { get; }

    /// <summary>Sprite real de portada si ya existe el asset; null si hay que usar el fallback.</summary>
    public Bitmap? CoverSprite { get; }
    public bool HasCoverSprite => CoverSprite != null;

    // Fallback procedural (solo relevante cuando HasCoverSprite es false, pero siempre
    // calculado — es barato y evita que la vista tenga que manejar un estado "sin nada").
    public Color CoverTopColor { get; }
    public Color CoverBottomColor { get; }
    public string CoverGlyph { get; }

    public RecentSaveRowViewModel(RecentSaveEntry entry, bool isLatest, GameCoverService gameCovers)
    {
        FilePath = entry.FilePath;
        FileName = entry.FileName;
        IsLatest = isLatest;

        // ExpandGameName vive en TrainerViewModel (subida de private a internal para esto,
        // ver comentario ahí) — reusa el mismo mapeo código→nombre completo que ya usa el
        // panel de Entrenador, en vez de duplicar la tabla acá.
        var gameName = TrainerViewModel.ExpandGameName(entry.GameVersion);
        var generation = gameCovers.GetGeneration(entry.GameVersion);
        Subtitle = generation > 0 ? $"{gameName} · Gen {generation}" : gameName;

        RelativeDate = FormatRelativeDate(entry.LastOpenedAt);

        CoverSprite = gameCovers.GetSprite(entry.GameVersion);
        var fallback = gameCovers.GetFallbackStyle(entry.GameVersion);
        CoverTopColor = fallback.TopColor;
        CoverBottomColor = fallback.BottomColor;
        CoverGlyph = fallback.Glyph;
    }

    /// <summary>"Hoy · HH:mm", "Ayer", o fecha corta ("12 ago") si es más antigua — mismo
    /// criterio que especifica el mockup. Textos y cultura de la fecha salen del idioma de la UI
    /// (claves <c>Recent_*</c> y <c>Format_Culture</c>); la lista se reconstruye al cambiar de idioma.</summary>
    private static string FormatRelativeDate(DateTime dateTime)
    {
        var today = DateTime.Now.Date;
        var date = dateTime.Date;

        if (date == today)
            return UiText.F("Recent_Today", dateTime.ToString("HH:mm"));
        if (date == today.AddDays(-1))
            return UiText.T("Recent_Yesterday");

        // Fecha corta en la cultura del idioma de la UI, ej. "12 ago" / "Aug 12" — sin punto final.
        System.Globalization.CultureInfo culture;
        try { culture = System.Globalization.CultureInfo.GetCultureInfo(UiText.T("Format_Culture")); }
        catch (System.Globalization.CultureNotFoundException) { culture = System.Globalization.CultureInfo.InvariantCulture; }
        return dateTime.ToString("d MMM", culture).Replace(".", "");
    }
}
