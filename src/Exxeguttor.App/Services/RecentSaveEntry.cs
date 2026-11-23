namespace Exxeguttor.App.Services;

/// <summary>
/// Una entrada de la lista de "saves recientes" (Pantalla 1 — Estado vacío).
/// </summary>
/// <remarks>
/// <c>GameVersion</c> guarda el código CRUDO tal cual lo devuelve
/// <c>SaveFile.Version.ToString()</c> en PKHeX.Core (ej. "RD", "SI", "SV", "ZA") —
/// el mismo valor que <c>TrainerViewModel.ExpandGameName()</c> ya usa como clave de
/// entrada, y el mismo criterio de nombre de archivo acordado para
/// <c>Assets/sprites/games/{codigo-en-minuscula}.png</c>. Nunca guardar acá el nombre
/// expandido ("Scarlet", "Legends: Z-A", etc.) — evita duplicar la tabla de mapeo y
/// evita problemas de caracteres inválidos para nombre de archivo (ej. "Colosseum/XD").
/// </remarks>
public sealed class RecentSaveEntry
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    /// <summary>Código crudo de <c>GameVersion.ToString()</c> (ej. "RD", "SV", "ZA").</summary>
    public required string GameVersion { get; init; }

    public required DateTime LastOpenedAt { get; init; }
}
