namespace Exxeguttor.UI.Services;

/// <summary>
/// Traductor de textos de UI para código que NO tiene acceso a un <c>LocalizationService</c> (clases
/// estáticas, entidades de lista como <c>LearnsetEntry</c>). Lo asigna <c>MainWindowViewModel</c> al
/// arrancar; por defecto devuelve la clave (se nota enseguida si falta). Los ViewModels con <c>_loc</c>
/// siguen usándolo directamente.
/// </summary>
public static class UiText
{
    public static Func<string, string> Translate { get; set; } = key => key;

    public static string T(string key) => Translate(key);

    public static string F(string key, params object?[] args) => string.Format(Translate(key), args);
}
