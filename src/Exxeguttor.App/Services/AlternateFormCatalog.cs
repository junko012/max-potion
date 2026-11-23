using System.Reflection;
using System.Text.Json;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Formas NO regionales que el selector de creación ofrece como tarjeta propia porque el combo Forma
/// del editor no puede llegar a ellas (cambiarlas desde otro encuentro rompe el origen del Pokémon) pero
/// SÍ tienen un encuentro legal propio: Zygarde 10% y Ash-Greninja (Battle Bond) y Lycanroc Nocturno en
/// Gen 7, los gorros de Pikachu, Vivillon Fancy/Poké Ball, Pichu Spiky-eared... Se crean con
/// <c>PokemonService.BuildPokemon(especie, forma)</c>, que ya busca el encuentro de esa forma.
///
/// Fuente principal: tabla <c>FormGameAvailability</c> de pokemon.db (Status = 'own_encounter', hotfix
/// fix_form_availability); respaldo: <c>Assets/creatable_forms.json</c> (clave = GameId). Generado con
/// PKHeX.Core 25.11.07: forma oculta del combo + <c>BuildPokemon</c> la deja legal con esa forma.
/// </summary>
public static class AlternateFormCatalog
{
    private static Dictionary<int, HashSet<(int, int)>>? _json;
    private static readonly Dictionary<int, HashSet<(int, int)>?> DbCache = new();
    private static IReadOnlyCollection<(int Species, byte Form)>? _all;
    private static readonly object Lock = new();

    /// <summary>Todas las formas alternativas de todos los juegos (para armar las tarjetas una sola vez).</summary>
    public static IReadOnlyCollection<(int Species, byte Form)> All()
    {
        lock (Lock)
        {
            if (_all != null) return _all;
            // DB primero (tabla FormGameAvailability, hotfix fix_form_availability); el JSON embebido
            // solo es respaldo cuando la DB no trae ninguna fila own_encounter.
            var db = PokemonDatabase.Instance.GetFormsWithStatus("own_encounter", null);
            var set = db is { Count: > 0 }
                ? new HashSet<(int, int)>(db)
                : new HashSet<(int, int)>(LoadJson().Values.SelectMany(v => v));
            return _all = set.Select(x => (x.Item1, (byte)x.Item2)).OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToList();
        }
    }

    /// <summary>True si (especie, forma) es una forma alternativa de algún juego (tiene tarjeta propia).</summary>
    public static bool Contains(int species, byte form) => All().Contains((species, form));

    /// <summary>Formas alternativas con encuentro propio para el juego del save abierto.</summary>
    public static IReadOnlySet<(int Species, byte Form)> AvailableIn(GameVersion version)
    {
        var gameId = GameVersionMappings.TryGetGameId(version);
        if (gameId <= 0) return new HashSet<(int, byte)>();
        HashSet<(int, int)>? set;
        lock (Lock)
        {
            if (!DbCache.TryGetValue(gameId, out set))
                DbCache[gameId] = set = PokemonDatabase.Instance.GetFormsWithStatus("own_encounter", gameId);
        }
        // Respaldo: sin filas own_encounter en la DB (tabla ausente o sin cargar) se usa el JSON embebido.
        var dbAll = PokemonDatabase.Instance.GetFormsWithStatus("own_encounter", null);
        if (dbAll is not { Count: > 0 })
        {
            var merged = new HashSet<(int, int)>();
            if (LoadJson().TryGetValue(gameId, out var j)) merged.UnionWith(j);
            return merged.Select(x => (x.Item1, (byte)x.Item2)).ToHashSet();
        }
        return (set ?? []).Select(x => (x.Item1, (byte)x.Item2)).ToHashSet();
    }

    /// <summary>Nombre de la forma ("10%", "Ash", "Midnight") en el idioma de PKHeX pedido; null si no hay.</summary>
    public static string? FormName(int species, byte form, string lang = "en")
    {
        try
        {
            var strings = GameInfo.GetStrings(lang);
            var names = FormConverter.GetFormList((ushort)species, strings.Types, strings.forms,
                GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
            return form < names.Length && names[form].Length > 0 ? names[form] : null;
        }
        catch { return null; }
    }

    private static Dictionary<int, HashSet<(int, int)>> LoadJson()
    {
        lock (Lock)
        {
            if (_json != null) return _json;
            var map = new Dictionary<int, HashSet<(int, int)>>();
            try
            {
                using var s = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("Exxeguttor.App.Assets.creatable_forms.json");
                if (s != null)
                    foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string[]>>(s) ?? [])
                    {
                        if (!int.TryParse(kv.Key, out var gid)) continue;
                        var set = new HashSet<(int, int)>();
                        foreach (var e in kv.Value)
                        {
                            var p = e.Split(':');
                            if (p.Length == 2 && int.TryParse(p[0], out var sp) && int.TryParse(p[1], out var fo))
                                set.Add((sp, fo));
                        }
                        map[gid] = set;
                    }
            }
            catch { /* sin lista: no se ofrece ninguna forma alternativa */ }
            return _json = map;
        }
    }
}
