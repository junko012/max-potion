using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Nombre localizado de un objeto / movimiento / especie a partir del slug en inglés que trae
/// pokemon.db en las condiciones de evolución (<c>leaf-stone</c>, <c>mimic</c>, <c>shelmet</c>).
/// pokemon.db solo tiene nombres en inglés, así que el nombre se toma de las listas de PKHeX
/// (<c>GameInfo.GetStrings</c>): se busca el índice por el nombre inglés normalizado y se devuelve
/// el nombre del mismo índice en el idioma pedido (las listas de PKHeX están alineadas entre idiomas).
/// Devuelve null si el slug no se reconoce — el llamador usa su respaldo.
/// </summary>
public static class EvolutionNameLocalizer
{
    private static readonly object Lock = new();
    private static Dictionary<string, int>? _items;
    private static Dictionary<string, int>? _moves;
    private static Dictionary<string, int>? _species;

    /// <summary>Código de idioma de PKHeX para el idioma de la UI ("es", "en", "fr"...). Cualquier
    /// idioma sin lista propia en PKHeX cae a inglés.</summary>
    public static string PkhexLang(string lang)
        => DataLanguageService.MapUiToPkhex(lang) ?? DataLanguageService.Fallback;

    public static string? Item(string slug, string lang)    => Localize(Index(ref _items,   s => s.Item),    slug, lang, s => s.Item);
    public static string? Move(string slug, string lang)    => Localize(Index(ref _moves,   s => s.Move),    slug, lang, s => s.Move);
    public static string? Species(string slug, string lang) => Localize(Index(ref _species, s => s.Species), slug, lang, s => s.Species);

    /// <summary>Solo letras y dígitos en minúscula: "leaf-stone" y "Leaf Stone", "mr-mime" y "Mr. Mime" coinciden.</summary>
    internal static string Normalize(string s)
    {
        Span<char> buffer = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) buffer[n++] = char.ToLowerInvariant(ch);
        return new string(buffer[..n]);
    }

    private static Dictionary<string, int> Index(ref Dictionary<string, int>? cache, Func<GameStrings, IReadOnlyList<string>> pick)
    {
        if (cache != null) return cache;
        lock (Lock)
        {
            if (cache != null) return cache;
            var names = pick(GameInfo.GetStrings("en"));
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 1; i < names.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i])) continue;
                map.TryAdd(Normalize(names[i]), i); // con nombres repetidos gana el primero
            }
            cache = map;
            return map;
        }
    }

    private static string? Localize(Dictionary<string, int> index, string slug, string lang, Func<GameStrings, IReadOnlyList<string>> pick)
    {
        if (string.IsNullOrWhiteSpace(slug) || !index.TryGetValue(Normalize(slug), out var id)) return null;
        try
        {
            var names = pick(GameInfo.GetStrings(PkhexLang(lang)));
            return id < names.Count && !string.IsNullOrWhiteSpace(names[id]) ? names[id] : null;
        }
        catch (Exception)
        {
            return null; // idioma sin lista en esta versión de PKHeX: el llamador usa su respaldo
        }
    }
}
