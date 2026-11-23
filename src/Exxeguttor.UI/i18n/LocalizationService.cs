using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Exxeguttor.App.Services;

namespace Exxeguttor.UI.i18n;

/// <summary>
/// Servicio de localización de Exxeguttor.
/// Carga traducciones desde archivos JSON embebidos en el ensamblado.
///
/// Idiomas incluidos en el binario base: es (español), en (inglés).
/// Idiomas adicionales (ej: zh-CN) se cargan desde paquetes lang externos.
///
/// Orden de resolución de una clave:
///   1. Idioma activo (ej: zh-CN)
///   2. Fallback a inglés (en)
///   3. La clave misma como último recurso
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private readonly AppSettingsService _settings;

    public event PropertyChangedEventHandler? PropertyChanged;
    private Dictionary<string, string> _active = new();
    private Dictionary<string, string> _fallback = new();

    // Idioma activo actualmente
    public string CurrentLocale { get; private set; } = "es";

    // Idiomas disponibles detectados
    public List<string> AvailableLocales { get; private set; } = new();

    public LocalizationService() : this(new AppSettingsService()) { }

    /// <remarks>Orden de arranque (plan i18n, sección 2): preferencia guardada (solo si ese
    /// idioma sigue disponible) → idioma del sistema (si hay JSON) → inglés.</remarks>
    public LocalizationService(AppSettingsService settings)
    {
        _settings = settings;
        DetectAvailableLocales();
        var saved = settings.GetUiLocale();
        Load(saved != null && AvailableLocales.Contains(saved) ? saved : DetectSystemLocale());
    }

    /// <summary>
    /// Cambia el idioma en caliente y guarda la preferencia. Los bindings <c>{Binding Loc[Clave]}</c>
    /// se refrescan solos (se notifica el indexador <c>"Item"</c>). Devuelve false si el idioma
    /// no está disponible (no cambia nada).
    /// </summary>
    public bool SetLocale(string locale)
    {
        if (!AvailableLocales.Contains(locale)) return false;
        if (locale == CurrentLocale) return true;
        Load(locale);
        if (CurrentLocale != locale) return false; // el JSON no cargó: se mantiene el anterior
        _settings.SetUiLocale(locale);
        return true;
    }

    /// <summary>
    /// El texto de <paramref name="key"/> en TODOS los idiomas disponibles (el activo primero). Sirve para
    /// reconocer un valor que se guardó (p. ej. una edición pendiente) con la UI en otro idioma: comparar
    /// solo con <c>this[key]</c> falla si el idioma cambió entre la edición y la comparación.
    /// </summary>
    public IReadOnlyList<string> AllTranslations(string key)
    {
        var result = new List<string> { this[key] };
        foreach (var locale in AvailableLocales)
        {
            try
            {
                if (TryLoadLocale(locale, out var dict) && dict != null && dict.TryGetValue(key, out var v) && !result.Contains(v))
                    result.Add(v);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { /* pack ilegible: se omite */ }
        }
        return result;
    }

    private void RaiseLocaleChanged()
    {
        // Avalonia 11.3: un binding con indexador ({Binding Loc[Clave]}) solo se reevalúa si el nombre de la
        // propiedad notificada es el de la propiedad del indexador en el tipo ("Item"; verificado en
        // ReflectionIndexerNode/ExpressionTreeIndexerNode.ShouldUpdate). "Item[]" es la convención de WPF y
        // Avalonia la ignora: con solo ese nombre el cambio de idioma no se veía hasta reiniciar.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); // WPF-style, por compatibilidad
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLocale)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
    }

    /// <summary>
    /// Obtiene el texto traducido para una clave.
    /// Usado como indexador: Loc["Menu_File_Open"]
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_active.TryGetValue(key, out var value)) return value;
            if (_fallback.TryGetValue(key, out var fallback)) return fallback;
            return key; // devuelve la clave si no hay traducción
        }
    }

    /// <summary>
    /// Carga el idioma especificado (ej: "es", "en", "zh-CN").
    /// Si no existe, mantiene el idioma actual.
    /// </summary>
    public void Load(string locale)
    {
        var loaded = TryLoadLocale(locale, out var dict);

        if (!loaded)
        {
            // Intentar con solo el código base (ej: "zh" si "zh-CN" no existe)
            var baseLocale = locale.Split('-')[0];
            loaded = TryLoadLocale(baseLocale, out dict);
        }

        if (loaded && dict != null)
        {
            _active = dict;
            CurrentLocale = locale;
        }

        // Siempre cargar inglés como fallback
        if (locale != "en" && TryLoadLocale("en", out var en) && en != null)
            _fallback = en;

        RaiseLocaleChanged();
    }

    private bool TryLoadLocale(string locale, out Dictionary<string, string>? dict)
    {
        dict = null;

        // 1. Buscar en recursos embebidos (es, en incluidos en el binario)
        var resourceName = $"Exxeguttor.UI.i18n.{locale}.json";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream != null)
        {
            dict = ParseStrings(stream);
            return dict != null;
        }

        // 2. Buscar en directorio de paquetes lang externos
        // Ruta: /usr/share/maxpotion/lang/{locale}.json  (instalado)
        //       ./lang/{locale}.json                        (portátil)
        var externalPaths = new[]
        {
            Path.Combine("/usr/share/maxpotion/lang", $"{locale}.json"),
            Path.Combine(AppContext.BaseDirectory, "lang", $"{locale}.json")
        };

        foreach (var path in externalPaths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var fs = File.OpenRead(path);
                dict = ParseStrings(fs);
                if (dict != null) return true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Un pack externo corrupto no debe tumbar la app: se ignora y se sigue con el siguiente.
            }
        }

        return false;
    }

    /// <summary>Lee un pack JSON tolerando metadatos: solo toma valores string y omite claves
    /// que empiezan con "_" (ej. "_rtl": true, "_note" en ar.json).</summary>
    private static Dictionary<string, string>? ParseStrings(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name.StartsWith('_') || prop.Value.ValueKind != JsonValueKind.String) continue;
            result[prop.Name] = prop.Value.GetString()!;
        }
        return result;
    }

    // ── Idiomas RTL (derecha a izquierda) ────────────────────────────────
    // Al cargar uno de estos el ViewModel debe activar FlowDirection.RightToLeft
    // en la ventana principal. Agregar aquí si se añaden más idiomas RTL.
    private static readonly HashSet<string> RtlLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar",       // Árabe
        "ar-SA",    // Árabe (Arabia Saudita)
        "ar-EG",    // Árabe (Egipto)
        "he",       // Hebreo (reservado para futuro)
        "fa",       // Persa/Farsi (reservado para futuro)
        "ur",       // Urdu (reservado para futuro)
    };

    // ── Idiomas conocidos del paquete lang ───────────────────────────────
    // Actualizar esta lista al añadir un nuevo idioma al paquete lang.
    // El código es el nombre del archivo JSON sin extensión (ej: "zh-CN" → zh-CN.json)
    private static readonly string[] KnownLangPackLocales =
    {
        "ja",       // Japonés
        "pt-BR",    // Portugués (Brasil)
        "ru",       // Ruso
        "de",       // Alemán
        "ko",       // Coreano
        "zh-CN",    // Chino simplificado
        "fr",       // Francés
        "id",       // Indonesio
        "hi",       // Hindi
        "ar",       // Árabe
        "it",       // Italiano
        // ── Espacio para idiomas futuros ──
        // "zh-TW",  // Chino tradicional (Taiwán)
        // "th",     // Tailandés
        // "pl",     // Polaco
        // "tr",     // Turco
        // "nl",     // Neerlandés
        // "sv",     // Sueco
        // "uk",     // Ucraniano
    };

    /// <summary>
    /// Indica si el idioma activo se escribe de derecha a izquierda.
    /// La UI debe usar FlowDirection.RightToLeft cuando esto sea true.
    /// </summary>
    public bool IsRtl => RtlLocales.Contains(CurrentLocale);

    private void DetectAvailableLocales()
    {
        // Siempre disponibles (embebidos en el binario)
        AvailableLocales = new List<string> { "es", "en" };

        // Buscar paquetes lang externos instalados
        var langDirs = new[]
        {
            "/usr/share/maxpotion/lang",
            Path.Combine(AppContext.BaseDirectory, "lang")
        };

        foreach (var dir in langDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var locale = Path.GetFileNameWithoutExtension(file);
                if (!AvailableLocales.Contains(locale))
                    AvailableLocales.Add(locale);
            }
        }
    }

    private string DetectSystemLocale()
    {
        // Detectar idioma del sistema operativo
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name; // ej: "es-PE", "en-US", "zh-CN"

        // Verificar si tenemos ese idioma exacto o su base
        if (AvailableLocales.Contains(name)) return name;
        var baseName = culture.TwoLetterISOLanguageName; // "es", "en", "zh"
        if (AvailableLocales.Contains(baseName)) return baseName;

        // Sin JSON para el idioma del sistema: inglés (plan i18n, sección 2)
        return "en";
    }
}
