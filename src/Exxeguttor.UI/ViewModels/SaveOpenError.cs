using Avalonia.Media.Imaging;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Datos ya resueltos para el modal de error de <c>EmptyStateView</c>: qué sprite mostrar, qué
/// texto, y si corresponde el botón secundario "Quitar de recientes" (solo
/// <see cref="SaveOpenErrorKind.NotFound"/>). Ver <c>mockups/error_empty_state/</c> para el
/// spec visual completo.
/// </summary>
public class SaveOpenError
{
    public SaveOpenErrorKind Kind { get; }

    /// <summary>Ruta del archivo que falló — solo se usa para <c>NotFound</c>, para poder
    /// sacar la entrada correspondiente de "Recientes" desde el botón secundario.</summary>
    public string? FilePath { get; }

    public Bitmap? Sprite { get; }
    public string Title { get; }
    public string Message { get; }

    public bool ShowRemoveFromRecents => Kind == SaveOpenErrorKind.NotFound;

    // Booleans de conveniencia para bindear la clase de animación correcta en el XAML sin
    // necesitar un IValueConverter para el enum (Classes.ditto="{Binding IsDitto}", etc.).
    public bool IsDitto     => Kind == SaveOpenErrorKind.InvalidSave;
    public bool IsMissingNo => Kind == SaveOpenErrorKind.Corrupted;
    public bool IsAbra      => Kind == SaveOpenErrorKind.NotFound;
    public bool IsSnorlax   => Kind == SaveOpenErrorKind.PermissionDenied;

    public SaveOpenError(SaveOpenErrorKind kind, string? filePath, SpriteService sprites, LocalizationService loc)
    {
        Kind = kind;
        FilePath = filePath;

        // IDs de Pokédex del mockup: Ditto #132, Abra #63, Snorlax #143. MissingNo. no tiene
        // un ID real explotable (no existe post-Gen 1) — sprite dedicado vía
        // SpriteService.GetMissingNoSprite(), ver nota ahí.
        Sprite = kind switch
        {
            SaveOpenErrorKind.InvalidSave      => sprites.GetPokemonSprite(132),
            SaveOpenErrorKind.Corrupted        => sprites.GetMissingNoSprite(),
            SaveOpenErrorKind.NotFound         => sprites.GetPokemonSprite(63),
            SaveOpenErrorKind.PermissionDenied => sprites.GetPokemonSprite(143),
            _ => null
        };

        var (titleKey, messageKey) = kind switch
        {
            SaveOpenErrorKind.InvalidSave      => ("SaveError_InvalidSave_Title", "SaveError_InvalidSave_Message"),
            SaveOpenErrorKind.Corrupted        => ("SaveError_Corrupted_Title", "SaveError_Corrupted_Message"),
            SaveOpenErrorKind.NotFound         => ("SaveError_NotFound_Title", "SaveError_NotFound_Message"),
            SaveOpenErrorKind.PermissionDenied => ("SaveError_PermissionDenied_Title", "SaveError_PermissionDenied_Message"),
            _ => ("SaveError_Generic_Title", "SaveError_Generic_Message")
        };
        Title   = loc[titleKey];
        Message = loc[messageKey];
    }
}
