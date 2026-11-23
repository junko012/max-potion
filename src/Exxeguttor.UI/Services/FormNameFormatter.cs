using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Nombre para mostrar de un Pokémon con forma regional: "Exeggutor de Alola" (es) /
/// "Alolan Exeggutor" (en), con las plantillas en i18n (<c>Form_Name_*</c>). Una forma regional es
/// otro Pokémon, no una variante de la misma ficha, así que se distingue por nombre en el selector,
/// la caja, el equipo y el editor. Para una especie sin forma regional devuelve el nombre tal cual.
///
/// Estático porque lo usan ViewModels que se construyen sin acceso al LocalizationService
/// (casillas de caja, tarjetas del selector); MainWindowViewModel lo configura al arrancar.
/// </summary>
public static class FormNameFormatter
{
    private static LocalizationService? _loc;

    public static void Configure(LocalizationService loc) => _loc = loc;

    /// <summary>Código de idioma de PKHeX para los datos del juego activos (nombres de forma).</summary>
    public static string PkhexLang => DataLanguageService.Instance.PkhexCode;

    public static string Format(string baseName, int species, byte form)
    {
        if (RegionalFormCatalog.Find(species, form) is not { } rf)
        {
            // Forma no regional con encuentro propio (Zygarde 10%, Ash-Greninja...): "Zygarde (10%)".
            return form != 0 && AlternateFormCatalog.Contains(species, form)
                && AlternateFormCatalog.FormName(species, form, PkhexLang) is { } formName
                ? $"{baseName} ({formName})"
                : baseName;
        }

        // "Paldea Combat" -> clave "Form_Name_PaldeaCombat"
        var key = "Form_Name_" + rf.Region.Replace(" ", string.Empty);
        var template = _loc?[key];
        // Sin servicio de idiomas (o clave faltante: el indexador devuelve la clave misma).
        if (template is null || template == key) return $"{baseName} ({rf.Region})";
        return string.Format(template, baseName);
    }
}
