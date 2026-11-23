namespace Exxeguttor.App.Services;

/// <summary>
/// Traductor de textos de UI para código de <c>Exxeguttor.App</c>, que no puede referenciar el
/// <c>LocalizationService</c> de la UI (mismo papel que <c>UiText</c> en Exxeguttor.UI). Lo asigna
/// <c>MainWindowViewModel</c> al arrancar; por defecto devuelve la clave (se nota enseguida si falta
/// y los tests de App no dependen de un idioma). Las claves viven en <c>Exxeguttor.UI/i18n/*.json</c>.
/// </summary>
public static class AppText
{
    public static Func<string, string> Translate { get; set; } = key => key;

    public static string T(string key) => Translate(key);

    public static string F(string key, params object?[] args) => string.Format(Translate(key), args);
}
