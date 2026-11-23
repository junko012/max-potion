namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Los 4 casos con tratamiento visual propio en <c>mockups/error_empty_state/</c>. El caso 5
/// del hand-off (panel de Recientes vacío) no es un error puntual — no tiene entrada acá,
/// se maneja aparte con <c>RecentSavesViewModel.HasEntries</c>.
/// </summary>
public enum SaveOpenErrorKind
{
    /// <summary>SaveUtil.GetSaveFile() devolvió null — Ditto (#132).</summary>
    InvalidSave,

    /// <summary>Excepción no controlada de PKHeX.Core al parsear — MissingNo. (sprite glitch,
    /// no es un Pokédex real).</summary>
    Corrupted,

    /// <summary>FileNotFoundException — Abra (#63). Único caso con acción secundaria
    /// ("Quitar de recientes").</summary>
    NotFound,

    /// <summary>UnauthorizedAccessException — Snorlax (#143).</summary>
    PermissionDenied,
}
