using System.Text.Json;

namespace Exxeguttor.App.Services;

/// <summary>
/// Lee y escribe la lista de "saves recientes" (Pantalla 1 — Estado vacío) en
/// <c>~/.config/maxpotion/recent.json</c>. Máximo 15 entradas, más reciente primero
/// (el panel "Recientes" de la pantalla de inicio hace scroll si la lista no entra).
/// </summary>
/// <remarks>
/// Usa <see cref="Environment.SpecialFolder.ApplicationData"/> para resolver la ruta base
/// en vez de hardcodear <c>"~/.config"</c> — en .NET (Core) sobre Linux, ese special folder
/// ya resuelve a <c>$XDG_CONFIG_HOME</c> (o <c>~/.config</c> si no está seteada la variable),
/// que es exactamente la convención XDG que pidió el usuario, sin necesidad de leer la
/// variable de entorno a mano.
///
/// Nunca lanza si el archivo no existe todavía (primera vez que se abre la app) o si el JSON
/// está corrupto — en ambos casos se comporta como si la lista estuviera vacía, para no
/// romper el arranque de la app por un archivo de config dañado.
/// </remarks>
public class RecentSavesService
{
    private const int MaxEntries = 15;

    private readonly string _filePath;

    public RecentSavesService() : this(ResolveDefaultPath()) { }

    private static string ResolveDefaultPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var current = Path.Combine(baseDir, "maxpotion", "recent.json");
        var legacy  = Path.Combine(baseDir, "exxeguttor", "recent.json"); // legacy: nombre previo al rebranding
        try
        {
            if (!File.Exists(current) && File.Exists(legacy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                File.Move(legacy, current);
            }
        }
        catch { /* mejor esfuerzo: si falla, arranca sin recientes */ }
        return current;
    }

    /// <remarks>Ctor interno para tests — permite apuntar a un archivo temporal.</remarks>
    internal RecentSavesService(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Devuelve las entradas guardadas, más reciente primero. Lista vacía si el archivo
    /// no existe todavía o si no se pudo leer/parsear.
    /// </summary>
    public List<RecentSaveEntry> GetAll()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new List<RecentSaveEntry>();

            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<RecentSaveEntry>>(json);
            if (entries == null) return new List<RecentSaveEntry>();
            // Un recent.json escrito por otra versión (o editado a mano) con más entradas que el tope.
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            return entries;
        }
        catch
        {
            // Archivo corrupto, permisos, etc. — no bloquear el arranque de la app por esto.
            return new List<RecentSaveEntry>();
        }
    }

    /// <summary>
    /// Agrega (o mueve al tope si ya existía) una entrada para <paramref name="filePath"/>.
    /// Trunca la lista a <see cref="MaxEntries"/>. No lanza si falla la escritura a disco
    /// (ej. filesystem read-only) — la app sigue funcionando, solo no persiste el historial.
    /// </summary>
    public void Add(string filePath, string gameVersion)
    {
        var entries = GetAll();

        // Si ya estaba (mismo path), se remueve la entrada vieja para reinsertarla al tope
        // con la fecha nueva, en vez de dejar un duplicado.
        entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.Ordinal));

        entries.Insert(0, new RecentSaveEntry
        {
            FilePath     = filePath,
            FileName     = Path.GetFileName(filePath),
            GameVersion  = gameVersion,
            LastOpenedAt = DateTime.Now
        });

        if (entries.Count > MaxEntries)
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Best-effort: no persistir no debería tumbar el flujo de "abrir save".
        }
    }

    /// <summary>
    /// Saca una entrada de la lista (ej. "Quitar de recientes" cuando el archivo ya no existe
    /// — Caso 3 de <c>mockups/error_empty_state/</c>). No lanza si el path no estaba en la
    /// lista o si falla la escritura — mismo criterio best-effort de arriba.
    /// </summary>
    public void Remove(string filePath)
    {
        var entries = GetAll();
        var removed = entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.Ordinal));
        if (removed == 0) return;

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Best-effort — mismo criterio de arriba.
        }
    }
}
