using System.Reflection;
using System.Text.Json;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Formas que <c>PokemonService.BuildPokemon</c> no consigue dejar legales en un juego (p. ej.
/// Zygarde 10%/50% y Ash-Greninja en Legends: Z-A). Fuente principal: tabla
/// <c>FormGameAvailability</c> de pokemon.db (hotfix fix_form_availability, Status = 'uncreatable');
/// respaldo si la DB no la tiene: <c>Assets/uncreatable_forms.json</c> (clave "ZA"/"SL"/"SW"/"PLA",
/// valores "especie:forma"), generado con PKHeX.Core 25.11.07.
/// Por sí sola NO oculta nada: <c>GetSelectableFormsCore</c> exige además que el cambio de forma
/// rompa el origen del Pokémon, para no esconder formas corregibles a mano.
/// </summary>
public static class UncreatableForms
{
    private static Dictionary<string, HashSet<(int, int)>>? _json;
    private static readonly Dictionary<int, HashSet<(int, int)>?> DbCache = new();
    private static readonly object Lock = new();

    /// <summary>True si el juego tiene lista de formas (Legends: Arceus/Z-A, Espada/Escudo, Escarlata/Púrpura).</summary>
    public static bool HasList(GameVersion version)
        => GameVersionMappings.TryGetGameId(version) is 11 or 12 or 18 or 20;

    public static bool Contains(GameVersion version, ushort species, byte form)
    {
        var gameId = GameVersionMappings.TryGetGameId(version);
        if (gameId is not (11 or 12 or 18 or 20)) return false;

        HashSet<(int, int)>? fromDb;
        lock (Lock)
        {
            if (!DbCache.TryGetValue(gameId, out fromDb))
                DbCache[gameId] = fromDb = PokemonDatabase.Instance.GetUncreatableForms(gameId);
        }
        if (fromDb != null) return fromDb.Contains((species, form));

        var key = gameId switch { 12 => "ZA", 11 => "PLA", 20 => "SW", _ => "SL" };
        return LoadJson().TryGetValue(key, out var set) && set.Contains((species, form));
    }

    private static Dictionary<string, HashSet<(int, int)>> LoadJson()
    {
        lock (Lock)
        {
            if (_json != null) return _json;
            var map = new Dictionary<string, HashSet<(int, int)>>();
            try
            {
                using var s = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("Exxeguttor.App.Assets.uncreatable_forms.json");
                if (s != null)
                    foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string[]>>(s) ?? [])
                    {
                        var set = new HashSet<(int, int)>();
                        foreach (var e in kv.Value)
                        {
                            var p = e.Split(':');
                            if (p.Length == 2 && int.TryParse(p[0], out var sp) && int.TryParse(p[1], out var fo))
                                set.Add((sp, fo));
                        }
                        map[kv.Key] = set;
                    }
            }
            catch { /* sin lista: no se oculta nada */ }
            return _json = map;
        }
    }
}
