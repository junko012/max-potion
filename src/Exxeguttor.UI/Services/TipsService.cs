using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Exxeguttor.UI.Services;

/// <summary>Un tip individual — igual forma que una fila de tips.{locale}.json.</summary>
public record TipEntry
{
    [JsonPropertyName("category")] public string Category { get; init; } = "";
    /// <summary>Id estable de la categoría (general/creation/editing/legality), igual en todos los idiomas:
    /// agrupa los tips y elige el ícono del rail. <see cref="Category"/> es solo el texto a mostrar.</summary>
    [JsonPropertyName("categoryId")] public string CategoryId { get; init; } = "";
    public string CategoryKey => string.IsNullOrEmpty(CategoryId) ? Category : CategoryId;
    [JsonPropertyName("icon")]     public string Icon     { get; init; } = "";
    [JsonPropertyName("title")]    public string Title    { get; init; } = "";
    [JsonPropertyName("text")]     public string Text     { get; init; } = "";
    /// <summary>Opcional: id de la miniatura de interfaz que ilustra el tip (ver Views/TipSampleView.axaml).
    /// Vacío = solo ícono. Un id desconocido simplemente no muestra nada.</summary>
    [JsonPropertyName("sample")]   public string Sample   { get; init; } = "";
}

/// <summary>
/// Carga el contenido de Tips desde JSON embebido, sufijado por locale
/// (tips_ES.json, tips_EN.json...) — mismo mecanismo que LocalizationService
/// (Assembly.GetManifestResourceStream) y que movesets_all_combined.json: el archivo
/// vive en Assets/Tips/ dentro del proyecto (ver Exxeguttor.UI.csproj).
///
/// OJO — el separador antes del locale es GUION BAJO ("tips_ES.json"), no punto
/// ("tips.ES.json") como se probó primero. Con punto, el nombre queda con 3 partes
/// separadas por puntos (tips / ES / json), y el SDK de .NET detecta automáticamente
/// ese patrón "nombre.cultura.extensión" (AssignCulture) cuando el segmento del medio
/// coincide con un código de idioma válido — lo saca del ensamblado principal y lo
/// empaqueta como recurso satélite de localización en un sub-ensamblado aparte, así
/// que GetManifestResourceStream en el ensamblado principal nunca lo encuentra (sin
/// ningún error de compilación que lo delate). Poner WithCulture="false" en el
/// EmbeddedResource del .csproj no fue suficiente en la práctica, así que se optó por
/// evitar el patrón de raíz cambiando el separador — más robusto que depender de esa
/// propiedad. La fuente "de trabajo" sigue en pokemon-database/resources/Tips/ (con
/// el nombre que Juan prefiera ahí) — hay que copiarla a mano a Assets/Tips/ con este
/// nombre exacto (guion bajo) después de cada edición, antes de compilar.
///
/// OJO: el sufijo de locale para tips es en MAYÚSCULA ("ES", no "es" como el resto
/// de i18n/LocalizationService) — decisión explícita de Juan, pero distinta del resto
/// del proyecto. Como los nombres de recurso embebido son case-sensitive, este
/// servicio arma el nombre de recurso con el locale tal cual viene (sin forzar
/// mayúscula/minúscula) — si en algún momento se llama con "es" en vez de "ES" no va
/// a encontrar el archivo. Documentado acá para que no sorprenda.
/// </summary>
public class TipsService
{
    private List<TipEntry> _tips = new();
    public IReadOnlyList<TipEntry> Tips => _tips;

    /// <param name="locale">Código de la UI ("es", "en", "es-419"…); se normaliza a la mayúscula del archivo
    /// (tips_ES.json, tips_EN.json). Si no hay archivo para ese idioma se usa el inglés.</param>
    public TipsService(string locale = "ES")
    {
        var code = locale.Split('-', '_')[0].ToUpperInvariant();
        Load(code);
        if (_tips.Count == 0 && code != "EN") Load("EN");
    }

    private void Load(string locale)
    {
        try
        {
            var resourceName = $"Exxeguttor.UI.Assets.Tips.tips_{locale}.json";
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                _tips = new List<TipEntry>();
                return;
            }

            var loaded = JsonSerializer.Deserialize<List<TipEntry>>(stream);
            _tips = loaded ?? new List<TipEntry>();
        }
        catch
        {
            // Igual que PokemonDatabase/LocalizationService: un problema acá nunca
            // debe romper el arranque de la app — el modal de Tips simplemente
            // queda sin contenido (ver TipsViewModel.HasTips) en vez de explotar.
            _tips = new List<TipEntry>();
        }
    }
}
