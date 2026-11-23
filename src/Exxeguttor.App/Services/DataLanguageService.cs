using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Idioma de los DATOS del juego (especies, movimientos, habilidades, objetos, tipos,
/// naturalezas, balls, lugares…), separado del idioma de la interfaz
/// (<c>docs/i18n_project/i18n-plan.md</c>, secciones 1–3). Fuente única del código de idioma de
/// PKHeX: reemplaza a los <c>GetPKHeXLang</c>/<c>PkhexLang</c> dispersos y a los
/// <c>GameInfo.GetStrings("en")</c> fijos.
/// </summary>
/// <remarks>
/// Resolución: preferencia <c>"auto"</c> → sigue al idioma de la interfaz (si PKHeX no lo tiene:
/// inglés); preferencia explícita → fija. Una preferencia guardada solo vale si PKHeX sigue
/// ofreciéndola. Singleton (como <c>SpriteService.Instance</c>) porque lo consultan
/// ViewModels construidos sin acceso a servicios inyectados.
/// <c>GameInfo.GetStrings(código)</c> ya cachea las listas por idioma (carga perezosa, una vez),
/// por eso <see cref="Strings"/> no duplica diccionarios: nunca llamar a GetStrings con otro
/// código ni "en" fijo.
/// </remarks>
public sealed class DataLanguageService
{
    public const string Auto = AppSettingsService.AutoDataLocale;
    public const string Fallback = "en";

    /// <summary>Códigos de idioma de datos que ofrece el selector (los de PKHeX con soporte
    /// completo). <c>es-419</c> existe en PKHeX pero no se ofrece (la UI en es-419 usa «es»).</summary>
    public static readonly IReadOnlyList<string> SelectableCodes =
        new[] { "en", "es", "fr", "de", "it", "ja", "ko", "zh-Hans", "zh-Hant" };

    // OJO con el orden: Instance va DESPUÉS de SelectableCodes — los inicializadores estáticos
    // corren en orden textual y el ctor valida la preferencia guardada contra esa lista.
    public static DataLanguageService Instance { get; } = new(new AppSettingsService());

    private readonly AppSettingsService _settings;
    private string _uiLocale = Fallback;
    private string _preference;

    /// <summary>Se dispara al cambiar el código PKHeX activo o la preferencia. La UI recarga los
    /// datos mostrados (los ids no cambian: solo el nombre que se calcula al mostrar).</summary>
    public event Action? Changed;

    internal DataLanguageService(AppSettingsService settings)
    {
        _settings = settings;
        var saved = settings.GetDataLocale();
        _preference = IsValidPreference(saved) ? saved : Auto;
    }

    /// <summary>Preferencia guardada: <see cref="Auto"/> o un código de <see cref="SelectableCodes"/>.</summary>
    public string Preference => _preference;

    public bool IsAuto => _preference == Auto;

    /// <summary>Idioma de la interfaz tal como lo informó la UI (<see cref="SetUiLocale"/>; "en" por defecto).</summary>
    public string UiLocale => _uiLocale;

    /// <summary>Código de PKHeX activo ("en", "es", "zh-Hans"…).</summary>
    public string PkhexCode => IsAuto ? (MapUiToPkhex(_uiLocale) ?? Fallback) : _preference;

    /// <summary>True cuando el modo es "Igual que la interfaz" pero la interfaz está en un idioma
    /// que PKHeX no tiene (ar, hi, id, pt-BR, ru): los datos salen en inglés (píldora ámbar).</summary>
    public bool FallsBackToEnglish => IsAuto && MapUiToPkhex(_uiLocale) is null && _uiLocale != Fallback;

    /// <summary>Listas de nombres de PKHeX en el idioma de los datos activo (cacheadas por PKHeX).</summary>
    public GameStrings Strings => GameInfo.GetStrings(PkhexCode);

    /// <summary>
    /// Alinea el idioma GLOBAL de PKHeX (<c>GameInfo.Strings</c>/<c>CurrentLanguage</c>) con el idioma de los
    /// datos activo. Varias APIs de PKHeX no reciben idioma y leen ese global: <c>GameInfo.GetLocationList</c>
    /// (ubicaciones de captura/huevo del panel Origen), <c>ILocation.GetEncounterLocation</c> y los nombres
    /// que arman los formatters de legalidad. Sin esto siempre salían en inglés. La llama la UI (no el ctor ni
    /// <see cref="SetUiLocale"/>) porque es estado global del proceso: los tests que crean instancias propias
    /// no deben tocarlo. Barato si el idioma ya coincide.
    /// </summary>
    public void ApplyToPkhexGlobals()
    {
        var code = PkhexCode;
        if (GameInfo.CurrentLanguage == code) return;
        GameInfo.CurrentLanguage = code;
        GameInfo.Strings = GameInfo.GetStrings(code); // el setter reconstruye GameInfo.Sources (listas de lugares, etc.)
    }

    /// <summary>El idioma de la interfaz cambió (lo llama la UI). No persiste nada.</summary>
    public void SetUiLocale(string uiLocale)
    {
        var before = PkhexCode;
        _uiLocale = string.IsNullOrWhiteSpace(uiLocale) ? Fallback : uiLocale;
        if (before != PkhexCode) Changed?.Invoke();
    }

    /// <summary>Elige el idioma de los datos (<see cref="Auto"/> o un código de
    /// <see cref="SelectableCodes"/>) y lo guarda. Un valor inválido se ignora.</summary>
    public void SetPreference(string preference)
    {
        if (!IsValidPreference(preference) || preference == _preference) return;
        _preference = preference;
        _settings.SetDataLocale(preference);
        Changed?.Invoke();
    }

    private static bool IsValidPreference(string? p)
        => p == Auto || (p is not null && SelectableCodes.Contains(p));

    /// <summary>Tabla UI → PKHeX (única en el proyecto). Devuelve null si PKHeX no tiene el
    /// idioma (ar, hi, id, pt-BR, ru…). <c>zh-CN</c>/<c>zh-TW</c> van a <c>zh-Hans</c>/<c>zh-Hant</c>:
    /// el código "zh" suelto NO existe en PKHeX y caía a inglés.</summary>
    public static string? MapUiToPkhex(string uiLocale)
    {
        if (string.IsNullOrWhiteSpace(uiLocale)) return null;
        switch (uiLocale)
        {
            case "zh-CN" or "zh-Hans" or "zh": return "zh-Hans";
            case "zh-TW" or "zh-Hant" or "zh-HK": return "zh-Hant";
            case "es-419": return "es-419";
        }
        var baseCode = uiLocale.Split('-')[0].ToLowerInvariant();
        return baseCode switch
        {
            "en" or "es" or "fr" or "de" or "it" or "ja" or "ko" => baseCode,
            _ => null,
        };
    }

    /// <summary>Autoglotónimo para los selectores (nunca el nombre en otro idioma).</summary>
    public static string NativeName(string code) => code switch
    {
        "en" => "English", "es" => "Español", "fr" => "Français", "de" => "Deutsch",
        "it" => "Italiano", "ja" => "日本語", "ko" => "한국어",
        "zh-Hans" or "zh-CN" => "简体中文", "zh-Hant" or "zh-TW" => "繁體中文",
        "pt-BR" => "Português (Brasil)", "ru" => "Русский", "ar" => "العربية",
        "hi" => "हिन्दी", "id" => "Bahasa Indonesia", "es-419" => "Español (Latinoamérica)",
        _ => code,
    };

    /// <summary>Código corto en mayúsculas para la píldora/menú ("ES", "ZH-HANS"→"ZH").</summary>
    public static string ShortCode(string code)
        => code.Split('-')[0].ToUpperInvariant() + (code is "zh-Hans" or "zh-CN" ? "·S" : code is "zh-Hant" or "zh-TW" ? "·T" : "");
}
