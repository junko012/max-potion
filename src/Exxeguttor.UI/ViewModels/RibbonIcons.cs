using Avalonia.Media;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Glifos vectoriales (caja 24×24, solo trazo) de los encabezados del tab Ribbons — uno por
/// categoría. Sin assets: se dibujan con Path. La medalla de cada tarjeta, el ✓ y el chevron
/// viven como StreamGeometry en PokemonStatsView.axaml.
/// </summary>
public static class RibbonIcons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Trofeo
        ["Champion"] = "M8,4 H16 V9 A4,4 0 0 1 8,9 Z M8,6 H5 V8 A3,3 0 0 0 8,11 M16,6 H19 V8 A3,3 0 0 1 16,11 M12,13 V17 M9,20 H15 M10,17 H14",
        // Espada
        ["Battle"]   = "M5,19 L19,5 M15,5 H19 V9 M8,13 L11,16 M4,20 L7,17",
        // Estrella
        ["Contest"]  = "M12,3 L14.7,8.6 L20.8,9.5 L16.4,13.8 L17.4,19.9 L12,17 L6.6,19.9 L7.6,13.8 L3.2,9.5 L9.3,8.6 Z",
        // Sello
        ["Mark"]     = "M7,10 A5,5 0 1 0 17,10 A5,5 0 1 0 7,10 M9,14 L7,21 L12,18 L17,21 L15,14",
        // Marcador
        ["Common"]   = "M7,4 H17 V20 L12,17 L7,20 Z",
        // Regalo
        ["Event"]    = "M3,8 H21 V12 H3 Z M5,12 H19 V20 H5 Z M12,8 V20 M12,8 C10,4 6,5 8,8 M12,8 C14,4 18,5 16,8",
        // Corazón
        ["Memory"]   = "M12,20 C12,20 5,15.6 5,10 A4,4 0 0 1 12,7.4 A4,4 0 0 1 19,10 C19,15.6 12,20 12,20 Z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Glifo de la categoría (marcador si la categoría no está mapeada).</summary>
    public static Geometry ForCategory(string? category)
    {
        var key = category != null && Paths.ContainsKey(category) ? category : "Common";
        if (!Cache.TryGetValue(key, out var g))
            Cache[key] = g = Geometry.Parse(Paths[key]);
        return g;
    }
}
