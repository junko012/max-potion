using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Estilo de fallback procedural para la portada de un juego (Pantalla 1 — Estado vacío),
/// usado cuando todavía no existe el sprite real en <c>Assets/sprites/games/</c>. Mismo
/// criterio que el mockup original: gradiente vertical + glyph, uno por generación.
/// </summary>
public sealed class GameCoverStyle
{
    public required Color TopColor    { get; init; }
    public required Color BottomColor { get; init; }
    public required string Glyph       { get; init; }
}

/// <summary>
/// Resuelve la portada a mostrar para una fila de "Recientes": intenta el sprite real por
/// juego primero (<see cref="SpriteService.GetGameCoverSprite"/>), y si no existe, expone el
/// estilo de fallback procedural (color + glyph derivados de la generación) para que la vista
/// dibuje un gradiente en vez de dejar la fila sin portada.
/// </summary>
/// <remarks>
/// El mapeo código→generación usa los mismos códigos crudos de <c>GameVersion.ToString()</c>
/// que ya documenta <c>TrainerViewModel.ExpandGameName</c> — mantenerlos sincronizados si se
/// agrega soporte a un juego nuevo ahí.
///
/// Paleta: los 4 primeros valores (Gen 1-4) y el de Gen 9 vienen literalmente del mockup
/// original (<c>screen-01-empty-state.html</c>, filas Yellow/Crystal/Emerald/Platinum/Violet).
/// Gen 5-8 no estaban definidos en el mockup ("la sesión de implementación puede definir una
/// paleta fija") — elegidos acá para completar el set sin repetir tono/glyph con los ya
/// confirmados. Ajustar libremente si no convencen.
/// </remarks>
public class GameCoverService
{
    private static readonly Dictionary<string, int> GenerationByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        // Gen 1 (+ Virtual Console)
        ["RD"] = 1, ["BU"] = 1, ["GN"] = 1, ["YW"] = 1,
        ["RD2"] = 1, ["BU2"] = 1, ["YW2"] = 1,
        // Gen 2 (+ Virtual Console)
        ["GD"] = 2, ["SI"] = 2, ["C"] = 2,
        ["GD2"] = 2, ["SI2"] = 2, ["C2"] = 2,
        // Gen 3
        ["S"] = 3, ["R"] = 3, ["E"] = 3, ["FR"] = 3, ["LG"] = 3, ["CXD"] = 3,
        // Gen 4
        ["D"] = 4, ["P"] = 4, ["Pt"] = 4, ["HG"] = 4, ["SS"] = 4,
        // Gen 5
        ["B"] = 5, ["W"] = 5, ["B2"] = 5, ["W2"] = 5,
        // Gen 6
        ["X"] = 6, ["Y"] = 6, ["OR"] = 6, ["AS"] = 6,
        // Gen 7
        ["SN"] = 7, ["MN"] = 7, ["US"] = 7, ["UM"] = 7, ["GP"] = 7, ["GE"] = 7,
        // Gen 8
        ["SW"] = 8, ["SH"] = 8, ["BD"] = 8, ["SP"] = 8, ["PLA"] = 8,
        // Gen 9
        ["SL"] = 9, ["VL"] = 9, ["SV"] = 9, ["ZA"] = 9,
        // "GO" y "SWSH" (valor agrupado) quedan deliberadamente afuera — sin generación clara
        // para un único glyph, caen en el fallback genérico de abajo.
    };

    private static readonly Dictionary<int, GameCoverStyle> PaletteByGeneration = new()
    {
        // Confirmados por el mockup original (screen-01-empty-state.html):
        [1] = new GameCoverStyle { TopColor = Color.Parse("#F4D03F"), BottomColor = Color.Parse("#F7DC6F"), Glyph = "⚡" }, // Yellow
        [2] = new GameCoverStyle { TopColor = Color.Parse("#5DADE2"), BottomColor = Color.Parse("#85C1E9"), Glyph = "◆" },  // Crystal
        [3] = new GameCoverStyle { TopColor = Color.Parse("#58D68D"), BottomColor = Color.Parse("#82E0AA"), Glyph = "🍃" }, // Emerald
        [4] = new GameCoverStyle { TopColor = Color.Parse("#A569BD"), BottomColor = Color.Parse("#BB8FCE"), Glyph = "▲" },  // Platinum
        [9] = new GameCoverStyle { TopColor = Color.Parse("#EC7063"), BottomColor = Color.Parse("#F1948A"), Glyph = "✦" },  // Violet
        // Nuevos, definidos en esta sesión para completar 5-8 (ver nota en <remarks>):
        [5] = new GameCoverStyle { TopColor = Color.Parse("#85929E"), BottomColor = Color.Parse("#AEB6BF"), Glyph = "❄" },  // B/W
        [6] = new GameCoverStyle { TopColor = Color.Parse("#F06292"), BottomColor = Color.Parse("#F48FB1"), Glyph = "✧" },  // X/Y
        [7] = new GameCoverStyle { TopColor = Color.Parse("#F5B041"), BottomColor = Color.Parse("#F8C471"), Glyph = "☀" },  // Sun/Moon
        [8] = new GameCoverStyle { TopColor = Color.Parse("#16A085"), BottomColor = Color.Parse("#48C9B0"), Glyph = "⚔" },  // Sword/Shield
    };

    /// <summary>Fallback genérico para códigos sin generación mapeada (ej. "GO", "SWSH").</summary>
    private static readonly GameCoverStyle UnknownStyle =
        new() { TopColor = Color.Parse("#9AA0A6"), BottomColor = Color.Parse("#BDC3C7"), Glyph = "•" };

    private readonly SpriteService _sprites;

    public GameCoverService(SpriteService sprites)
    {
        _sprites = sprites;
    }

    /// <summary>
    /// Sprite real de portada si ya existe en <c>Assets/sprites/games/{codigo}.png</c>, o
    /// <c>null</c> si todavía no fue agregado — en ese caso usar <see cref="GetFallbackStyle"/>.
    /// </summary>
    public Bitmap? GetSprite(string gameVersion) => _sprites.GetGameCoverSprite(gameVersion);

    /// <summary>Número de generación (1-9) para el subtítulo "{Juego} · Gen {N}", o 0 si el
    /// código no tiene generación mapeada (ej. "GO", "SWSH").</summary>
    public int GetGeneration(string gameVersion)
    {
        if (!string.IsNullOrEmpty(gameVersion) && GenerationByCode.TryGetValue(gameVersion, out var generation))
            return generation;
        return 0;
    }

    /// <summary>
    /// Estilo procedural (gradiente + glyph) para cuando no hay sprite real todavía. Nunca
    /// devuelve <c>null</c> — un código no reconocido cae en <see cref="UnknownStyle"/>.
    /// </summary>
    public GameCoverStyle GetFallbackStyle(string gameVersion)
    {
        if (!string.IsNullOrEmpty(gameVersion)
            && GenerationByCode.TryGetValue(gameVersion, out var generation)
            && PaletteByGeneration.TryGetValue(generation, out var style))
        {
            return style;
        }

        return UnknownStyle;
    }
}
