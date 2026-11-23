using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Texto legible de un <see cref="CheckResult"/> para el «detalle técnico» del diagnóstico, con los
/// mensajes oficiales y traducidos de PKHeX (<c>LegalityLocalizationContext.Humanize</c>, 10 idiomas).
/// Antes se usaba <c>CheckResult.ToString()</c>: <c>CheckResult</c> es un <c>record struct</c> sin
/// <c>ToString</c> propio (PKHeX.Core 25.11.07), así que eso volcaba los campos del registro, no una oración.
/// </summary>
/// <remarks>
/// El idioma es el de la INTERFAZ (es texto para leer; los nombres de movimientos/especies que PKHeX
/// incrusta en la frase salen en ese mismo idioma). Si PKHeX no tiene el idioma (ar, hi, id, pt-BR, ru)
/// o falla la carga, se usa inglés; si todo falla, el <c>ToString()</c> de siempre.
/// <c>LegalityLocalizationSet.GetLocalization</c> cachea en un diccionario sin lock y esto se llama desde
/// <c>Task.Run</c> (análisis en paralelo), por eso el acceso a los sets pasa por <see cref="Gate"/>.
/// </remarks>
public static class LegalityHumanizer
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, LegalityLocalizationSet?> Sets = new();

    /// <summary>Código de PKHeX del idioma de la interfaz ("en" si PKHeX no lo tiene).</summary>
    public static string CurrentCode =>
        DataLanguageService.MapUiToPkhex(DataLanguageService.Instance.UiLocale) ?? DataLanguageService.Fallback;

    public static string Humanize(LegalityAnalysis analysis, in CheckResult result) =>
        Humanize(analysis, result, CurrentCode);

    public static string Humanize(LegalityAnalysis analysis, in CheckResult result, string code)
    {
        try
        {
            var set = GetSet(code) ?? GetSet(DataLanguageService.Fallback);
            if (set is not null)
                return LegalityLocalizationContext.Create(analysis, set).Humanize(result);
        }
        catch
        {
            // código sin plantilla o argumento fuera de rango: se cae al texto de siempre
        }
        return result.ToString();
    }

    private static LegalityLocalizationSet? GetSet(string code)
    {
        lock (Gate)
        {
            if (Sets.TryGetValue(code, out var cached)) return cached;
            LegalityLocalizationSet? set;
            try { set = LegalityLocalizationSet.GetLocalization(code); }
            catch { set = null; }
            Sets[code] = set;
            return set;
        }
    }
}
