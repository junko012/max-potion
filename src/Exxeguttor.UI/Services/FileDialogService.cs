using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Servicio de diálogos de archivo.
/// Abstrae el StorageProvider de Avalonia para abrir y guardar archivos.
///
/// Usa el portal XDG en Linux — compatible con Flatpak v2 sin cambios.
/// Nunca usa rutas hardcodeadas; siempre pasa por el diálogo del sistema.
/// </summary>
public class FileDialogService
{
    private readonly Window _window;

    /// <summary>"Todos los archivos" real. No se usa <c>FilePickerFileTypes.All</c> de Avalonia: en Linux se envía como <c>*.*</c> y
    /// oculta los saves sin extensión (los <c>main</c> de Gen 6 en adelante). Ver <see cref="SaveFilePatterns"/>.</summary>
    private static FilePickerFileType AllFiles() => new("All") { Patterns = SaveFilePatterns.AllFiles() };

    public FileDialogService(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Abre el diálogo del sistema para seleccionar un archivo de save.
    /// Retorna la ruta seleccionada o null si el usuario canceló.
    /// </summary>
    public async Task<string?> OpenSaveFileAsync(string title, string filterName)
    {
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(filterName)
                {
                    Patterns = SaveFilePatterns.Saves()
                },
                AllFiles()
            }
        };

        var result = await _window.StorageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    /// <summary>
    /// Abre el diálogo del sistema para guardar un archivo.
    /// Retorna la ruta elegida o null si el usuario canceló.
    /// </summary>
    public async Task<string?> SaveFileAsAsync(string title, string suggestedName)
    {
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Pokémon Save")
                {
                    Patterns = SaveFilePatterns.Saves()
                },
                AllFiles()
            }
        };

        var result = await _window.StorageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }
}
