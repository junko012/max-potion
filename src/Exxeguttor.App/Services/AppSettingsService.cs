using System.Text.Json;

namespace Exxeguttor.App.Services;

/// <summary>
/// Preferencias persistentes de la app en <c>~/.config/maxpotion/settings.json</c>
/// (Fase 0 de <c>docs/i18n_project/i18n-plan.md</c>). Guarda los dos idiomas y la escala de la interfaz (<c>uiScale</c>):
/// <c>uiLocale</c> (idioma de la interfaz; null = sin preferencia, se detecta el del sistema) y
/// <c>dataLocale</c> (idioma de los datos del juego; <c>"auto"</c> = igual que la interfaz).
/// </summary>
/// <remarks>
/// Mismo patrón que <see cref="RecentSavesService"/>: ruta base vía
/// <see cref="Environment.SpecialFolder.ApplicationData"/> (XDG en Linux), ctor interno para tests,
/// y nunca lanza — un archivo ausente, corrupto o de solo lectura se comporta como "sin
/// preferencias" para no romper el arranque. Cada escritura relee el archivo antes de modificar
/// SU clave, así dos instancias (p. ej. <c>LocalizationService</c> y <c>DataLanguageService</c>)
/// no se pisan entre sí, y las claves desconocidas de otras versiones se conservan.
/// </remarks>
public class AppSettingsService
{
    public const string AutoDataLocale = "auto";

    private readonly string _filePath;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public AppSettingsService() : this(ResolveDefaultPath()) { }

    private static string ResolveDefaultPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(baseDir, "maxpotion", "settings.json");
    }

    /// <remarks>Ctor interno para tests — permite apuntar a un archivo temporal.</remarks>
    internal AppSettingsService(string filePath) => _filePath = filePath;

    /// <summary>Idioma de la interfaz guardado, o null si el usuario nunca eligió uno.</summary>
    public string? GetUiLocale() => ReadString("uiLocale");

    public void SetUiLocale(string locale) => WriteString("uiLocale", locale);

    /// <summary>Idioma de los datos guardado: un código PKHeX o <see cref="AutoDataLocale"/>
    /// (valor por defecto si no hay nada guardado).</summary>
    public string GetDataLocale() => ReadString("dataLocale") ?? AutoDataLocale;

    public void SetDataLocale(string locale) => WriteString("dataLocale", locale);

    /// <summary>Escala de la interfaz guardada (<see cref="InterfaceScale"/>): 1.0 si no hay nada o el valor no es un número.</summary>
    public double GetUiScale()
    {
        var dict = ReadAll();
        return dict.TryGetValue("uiScale", out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var v)
            ? InterfaceScale.Snap(v)
            : InterfaceScale.Default;
    }

    public void SetUiScale(double scale) => WriteElement("uiScale", JsonSerializer.SerializeToElement(InterfaceScale.Snap(scale)));

    private string? ReadString(string key)
    {
        var dict = ReadAll();
        return dict.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()
            : null;
    }

    private void WriteString(string key, string value) => WriteElement(key, JsonSerializer.SerializeToElement(value));

    private void WriteElement(string key, JsonElement value)
    {
        try
        {
            var dict = ReadAll();
            dict[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(dict, WriteOptions));
        }
        catch
        {
            // Filesystem de solo lectura, permisos, etc.: la app sigue, solo no persiste.
        }
    }

    private Dictionary<string, JsonElement> ReadAll()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
        }
        catch
        {
            return new(); // JSON corrupto: se trata como vacío (la próxima escritura lo reemplaza)
        }
    }
}
