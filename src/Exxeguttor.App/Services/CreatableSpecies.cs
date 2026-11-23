using System.Reflection;
using System.Text.Json;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Especies que NO tiene sentido ofrecer en "Crear Pokémon" para un juego, porque no existen en
/// él y ninguna forma de construirlas pasa la validación de PKHeX (Unown y Arceus en Legends: Z-A,
/// casi todo lo que no es del Pokédex de Hisui en Legends: Arceus, las especies fuera del Pokédex de
/// Galar/Paldea en Espada/Escudo y Escarlata/Púrpura...). Sin este filtro el selector las ofrecía
/// y el resultado era un Pokémon sin movimientos y con varias alertas de ilegalidad.
///
/// La lista sale de la tabla <c>SpeciesGameAvailability</c> de pokemon.db (hotfix
/// fix_species_availability) y, como respaldo, de <c>Assets/uncreatable_species.json</c>,
/// generada con PKHeX.Core 25.11.07: una especie se oculta solo si NO figura en la tabla personal
/// del juego (<c>IsPresentInGame</c>) Y <c>PokemonService.BuildPokemon</c> no logró una versión
/// legal en 4 intentos. Las especies ausentes de la tabla pero que sí pueden ser legales (las que
/// llegan por transferencia desde HOME, p. ej. Sandshrew en Z-A) se siguen ofreciendo. Las que sí
/// están en el juego pero salen con alertas por una limitación de BuildPokemon NO se ocultan.
/// Si PKHeX.Core se actualiza hay que regenerar el JSON (ver README de <c>scripts/</c>).
/// </summary>
public static class CreatableSpecies
{
    private static Dictionary<string, HashSet<int>>? _hidden;
    private static readonly object Lock = new();
    private static readonly HashSet<int> None = [];

    /// <summary>Especies ocultas del selector para el juego del save (vacío si ninguna). Fuente
    /// principal: tabla <c>SpeciesGameAvailability</c> de pokemon.db (Status = 'uncreatable');
    /// si la DB todavía no la tiene, respaldo en el JSON embebido.</summary>
    public static IReadOnlySet<int> HiddenIn(GameVersion version)
    {
        var gameId = GameVersionMappings.TryGetGameId(version);
        IReadOnlySet<int>? result = null;
        if (gameId > 0)
        {
            var fromDb = PokemonDatabase.Instance.GetUncreatableSpecies(gameId);
            if (fromDb != null) result = fromDb;
        }
        result ??= HiddenFromJson(version);
        return result;
    }

    /// <summary>Especies ocultas del selector de "Crear Pokémon" para ESTE save: las de
    /// <see cref="HiddenIn"/> más TODA especie que la tabla personal del propio juego
    /// (<c>save.Personal</c>) no marca como presente en ninguna de sus formas — así solo se
    /// ofrecen Pokémon que existen en el juego, en cualquier generación. Una forma regional sola
    /// alcanza (Sandshrew de Alola en Z-A cuenta como presente). Un encuentro de otro juego solo
    /// llega a un formato como PA9/PA8/PB8/PB7 por la conversión vía HOME de PKHeX, y esa
    /// conversión devuelve null si la especie no está en el juego destino: el resultado era un
    /// Pokémon en blanco, sin movimientos e ilegal. Se evalúa hasta el máximo global de especies
    /// (no solo el del juego) para ocultar también las posteriores a la generación del save.
    /// No se usa en el Pokédex.</summary>
    public static IReadOnlySet<int> HiddenForCreation(SaveFile save)
    {
        var hidden = new HashSet<int>(HiddenIn(save.Version));
        var table = save.Personal;
        var max = Math.Max((int)save.MaxSpeciesID, (int)PersonalTable.ZA.MaxSpeciesID);
        for (ushort species = 1; species <= max; species++)
            if (!table.IsSpeciesInGame(species))
                hidden.Add(species);
        return hidden;
    }

    /// <summary>Especies de Z-A que existen en su tabla personal pero que PKHeX 25.11.07 no puede
    /// validar como legales con ninguna construcción (investigado una por una):
    /// - Mewtwo (150), Hoopa (720), Volcanion (721): sin encuentro propio de Z-A, solo llegan
    ///   como PA9 de origen no-Z-A, y PKHeX marca TransferBad a todo PA9 así.
    /// - Espeon (196), Umbreon (197), Pupitar (247): salen de Eevee/Larvitar de Z-A, pero PKHeX
    ///   descarta de la cadena de evolución a la pre-evolución cuya habilidad (mismo índice) no
    ///   coincide (EvolutionUtil.Discard) y queda EncounterInvalid.
    /// Revisar al actualizar PKHeX; el test CreationLegalitySweepTests avisa si alguna pasa a legal.</summary>
    internal static readonly int[] ZaUnlegalizable = [150, 196, 197, 247, 720, 721];

    /// <summary>Lo que oculta el selector de "Crear Pokémon" para este save: <see cref="HiddenForCreation"/>
    /// más las especies que existen en el juego pero que PKHeX no puede validar como legales
    /// (hoy, solo en Z-A: <see cref="ZaUnlegalizable"/>). El Pokédex NO usa esta lista — esas especies
    /// sí existen en el juego y siguen siendo entradas del álbum.</summary>
    public static IReadOnlySet<int> HiddenInCreationPicker(SaveFile save)
    {
        var hidden = new HashSet<int>(HiddenForCreation(save));
        if (save.Version == GameVersion.ZA)
            hidden.UnionWith(ZaUnlegalizable);
        return hidden;
    }

    /// <summary>Respaldo: lista embebida (clave = <c>GameVersion.ToString()</c>).</summary>
    internal static IReadOnlySet<int> HiddenFromJson(GameVersion version)
    {
        var map = Load();
        // Valores agregados de PKHeX: se resuelven al primer juego concreto del grupo.
        var key = version switch
        {
            GameVersion.SWSH => "SW",
            GameVersion.SV or GameVersion.Gen9 => "SL",
            GameVersion.GG => "GP",
            _ => version.ToString(),
        };
        return map.TryGetValue(key, out var set) ? set : None;
    }

    private static Dictionary<string, HashSet<int>> Load()
    {
        if (_hidden != null) return _hidden;
        lock (Lock)
        {
            if (_hidden != null) return _hidden;
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Exxeguttor.App.Assets.uncreatable_species.json");
            if (stream == null) { _hidden = new(); return _hidden; }   // sin recurso: no se oculta nada

            var raw = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(stream)
                      ?? new Dictionary<string, List<int>>();
            _hidden = raw.ToDictionary(kv => kv.Key, kv => new HashSet<int>(kv.Value));
            return _hidden;
        }
    }
}
