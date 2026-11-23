using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Resuelve nombres de movimiento ESCRITOS EN INGLÉS en código (tablas de dominio como
/// <c>ZCrystalDatabase</c>: «Volt Tackle», «Breakneck Blitz»…) a su id de PKHeX y de ahí al nombre
/// en el idioma de los datos (plan i18n, Fase 3 tanda 4). El inglés se usa solo como llave
/// canónica de esas tablas — nunca como texto mostrado.
/// </summary>
public static class MoveNames
{
    private static Dictionary<string, int>? _byEnglish;

    // Misma normalización que GameNames (sin acentos, mayúsculas, espacios ni signos): "Nature's Madness"
    // == "natures madness"; un apóstrofo recto/curvo o un guion no rompen la coincidencia.
    private static string Norm(string v) => GameNames.Norm(v);

    // PKHeX distingue los 18 Z-Moves genéricos (ids 622-657, uno físico y otro especial por tipo) agregando " (P)" / " (S)"
    // al nombre en TODOS los idiomas. Para un cristal Z de tipo la categoría no importa («Breakneck Blitz»), así que el
    // sufijo se quita tanto al resolver el id como al mostrar el nombre.
    private const int FirstGenericZMove = 622, LastGenericZMove = 657;

    private static string WithoutZSuffix(int id, string name)
        => id is >= FirstGenericZMove and <= LastGenericZMove && (name.EndsWith(" (P)") || name.EndsWith(" (S)"))
            ? name[..^4]
            : name;

    /// <summary>Nombre del movimiento tal como se muestra: el de PKHeX, sin el sufijo (P)/(S) de los Z-Moves genéricos.</summary>
    internal static string DisplayName(int id, string name) => WithoutZSuffix(id, name);

    private static Dictionary<string, int> ByEnglish => _byEnglish ??= Build();

    private static Dictionary<string, int> Build()
    {
        var d = new Dictionary<string, int>();
        var en = GameInfo.GetStrings("en").Move;
        for (var id = 1; id < en.Count; id++)
        {
            if (string.IsNullOrWhiteSpace(en[id])) continue;
            d.TryAdd(Norm(WithoutZSuffix(id, en[id])), id); // primer id gana (los Z-Moves físico/especial comparten nombre)
        }
        return d;
    }

    /// <summary>Id de PKHeX del movimiento con ese nombre inglés, o 0 si no existe.</summary>
    public static int IdFromEnglish(string? englishName)
        => string.IsNullOrWhiteSpace(englishName) ? 0 : ByEnglish.GetValueOrDefault(Norm(englishName));

    /// <summary>Nombre en el idioma de los datos activo; si no se resuelve, el texto original.</summary>
    public static string Localize(string? englishName)
        => Localize(englishName, DataLanguageService.Instance.Strings.Move);

    /// <remarks>Overload con lista explícita — permite probar cada idioma sin tocar el singleton.</remarks>
    internal static string Localize(string? englishName, IReadOnlyList<string> targetMoves)
    {
        var id = IdFromEnglish(englishName);
        return id > 0 && id < targetMoves.Count && !string.IsNullOrEmpty(targetMoves[id])
            ? WithoutZSuffix(id, targetMoves[id])
            : englishName ?? string.Empty;
    }
}
