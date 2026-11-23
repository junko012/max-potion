using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Servicio para abrir, leer y guardar archivos de saves Pokémon.
/// Usa PKHeX.Core como motor; esta clase es el único punto de contacto
/// entre Exxeguttor y PKHeX.Core para operaciones de archivo.
/// </summary>
public class SaveFileService
{
    private SaveFile? _currentSave;

    // El save actualmente cargado (null si no hay ninguno)
    public SaveFile? CurrentSave => _currentSave;

    public bool HasSaveLoaded => _currentSave != null;

    /// <summary>
    /// Abre un archivo de guardado desde la ruta indicada.
    /// PKHeX.Core detecta automáticamente el juego y la región.
    /// </summary>
    /// <remarks>
    /// Encontrado en sesión de indicadores de carga: esto antes era "fake-async" —
    /// devolvía <c>Task.FromResult(save)</c> sin haber corrido nada en background, así que
    /// el <c>await</c> del lado de la UI nunca cedía el hilo (una tarea ya completada no
    /// yieldea). Toda la lectura del archivo + detección de formato de PKHeX.Core corría
    /// bloqueando la ventana. <c>Task.Run</c> lo pasa a un hilo de background de verdad.
    /// </remarks>
    public Task<SaveFile> OpenAsync(string filePath)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Save file not found: {filePath}");

            var data = File.ReadAllBytes(filePath);

            // PKHeX.Core detecta el tipo de save automáticamente.
            // 25.11.7 renombró SaveUtil.GetVariantSAV(byte[]) -> SaveUtil.GetSaveFile(Memory<byte>, string? path = null).
            // byte[] convierte implícito a Memory<byte>, así que la llamada es prácticamente igual.
            var save = SaveUtil.GetSaveFile(data)
                ?? throw new InvalidDataException("Not a valid Pokémon save file.");

            _currentSave = save;
            return save;
        });
    }

    /// <summary>
    /// Guarda el save actual en la ruta original.
    /// Crea un backup automático antes de sobreescribir.
    /// </summary>
    public Task SaveAsync()
    {
        if (_currentSave == null)
            throw new InvalidOperationException("No save file is loaded.");

        if (string.IsNullOrEmpty(_currentSave.Metadata?.FilePath))
            throw new InvalidOperationException("Save file has no associated path. Use SaveAsAsync.");

        return SaveAsAsync(_currentSave.Metadata.FilePath);
    }

    /// <summary>
    /// Guarda el save en una ruta específica con backup previo.
    /// </summary>
    /// <remarks>
    /// Mismo problema que <see cref="OpenAsync"/>: antes era "fake-async"
    /// (<c>Task.CompletedTask</c> sin haber corrido nada en background). El backup (copia de
    /// archivo) + <c>SaveFile.Write()</c> (recalcula checksums de todo el save) ahora corren
    /// en <c>Task.Run</c>.
    /// </remarks>
    public Task SaveAsAsync(string filePath)
    {
        if (_currentSave == null)
            throw new InvalidOperationException("No save file is loaded.");

        var save = _currentSave;
        return Task.Run(() =>
        {
            // Crear backup antes de sobreescribir — usa la convención de nombre propia de
            // PKHeX.Core (save.Metadata.GetBackupFileName), no un timestamp inventado. Espera
            // el DIRECTORIO donde va el backup, no la ruta completa del archivo (confirmado
            // por reflection: pasarle la ruta completa lo trata como si fuera una carpeta).
            // Fallback al timestamp manual de antes si esto tira — confirmado que puede pasar
            // con un save recién creado en blanco sin fecha jugada válida (no debería darse
            // con un save real de un usuario, pero mejor no romper el guardado entero por el
            // nombre del backup).
            if (File.Exists(filePath))
            {
                string backupPath;
                try
                {
                    var dir = Path.GetDirectoryName(filePath);
                    // GetBackupFileName ya devuelve la ruta completa combinada (confirmado:
                    // GetBackupFileName("/tmp") -> "/tmp/ [TEST (E) - 0ː00ː00].bak") — no hay
                    // que recombinarla con el directorio de nuevo.
                    backupPath = save.Metadata.GetBackupFileName(string.IsNullOrEmpty(dir) ? "." : dir);
                }
                catch
                {
                    backupPath = filePath + $".bak.{DateTime.Now:yyyyMMdd_HHmmss}";
                }
                File.Copy(filePath, backupPath, overwrite: true);
            }

            // SaveFile.Write() ahora devuelve Memory<byte> en vez de byte[] (25.11.7) — .ToArray()
            // lo convierte para File.WriteAllBytes, que sigue pidiendo byte[].
            var data = save.Write();
            File.WriteAllBytes(filePath, data.ToArray());
        });
    }

    /// <summary>
    /// Descarga el save de memoria sin guardar.
    /// </summary>
    public void Close()
    {
        _currentSave = null;
    }
}
