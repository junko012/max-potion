using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Acceso a datos de movimientos.
/// Migrado de moves_database.json a SQLite.
/// Mantiene la API pública existente para no romper los ViewModels.
/// </summary>
public class MoveDatabase
{
    private static MoveDatabase? _instance;
    public static MoveDatabase Instance => _instance ??= new MoveDatabase();

    private MoveDatabase() { }

    // Cache en memoria por moveId, mismo criterio que SpeciesDatabase._cache (datos
    // estáticos dentro de una sesión, cache lazy, null también se cachea). Se suma acá
    // además de en PokemonDatabase.GetMove (ver ese archivo) porque acá también se
    // cachea el objeto YA proyectado a MoveEntry, evitando repetir el mapeo.
    private readonly Dictionary<int, MoveEntry?> _cache = new();

    /// <summary>Retorna la entrada completa de un movimiento por ID.</summary>
    public MoveEntry? Get(int moveId)
    {
        if (_cache.TryGetValue(moveId, out var cached)) return cached;

        var db = PokemonDatabase.Instance.GetMove(moveId);
        var entry = db == null ? null : new MoveEntry
        {
            Id            = db.Id,
            Name          = db.Name,
            Type          = db.TypeName,          // slug en minúsculas
            DamageClass   = db.DamageClass,
            Power         = db.Power,
            Accuracy      = db.Accuracy,
            Pp            = db.PP,
            EffectEntries = db.EffectText,
        };

        _cache[moveId] = entry;
        return entry;
    }

    /// <summary>
    /// Retorna el tipo de un movimiento capitalizado para coincidir
    /// con GameStrings de PKHeX (ej. "Electric").
    /// </summary>
    public string GetType(int moveId)
    {
        var db = PokemonDatabase.Instance.GetMove(moveId);
        return db?.TypeNameDisplay ?? string.Empty;
    }

    public bool HasMove(int moveId)
    {
        return PokemonDatabase.Instance.GetMove(moveId) != null;
    }

    /// <summary>
    /// Busca un movimiento por nombre display (ej. "Flare Blitz").
    /// Devuelve null si no se encuentra en la DB.
    /// </summary>
    public MoveEntry? GetByName(string displayName)
    {
        var db = PokemonDatabase.Instance.GetMoveByName(displayName);
        if (db == null) return null;
        return new MoveEntry
        {
            Id            = db.Id,
            Name          = db.Name,
            Type          = db.TypeName,
            DamageClass   = db.DamageClass,
            Power         = db.Power,
            Accuracy      = db.Accuracy,
            Pp            = db.PP,
            EffectEntries = db.EffectText,
        };
    }
}

/// <summary>DTO público — misma forma que la versión JSON anterior.</summary>
public class MoveEntry
{
    public int    Id            { get; set; }
    public string Name          { get; set; } = string.Empty;
    public int?   Accuracy      { get; set; }
    public int?   Power         { get; set; }
    public int?   Pp            { get; set; }
    public string DamageClass   { get; set; } = string.Empty;
    public string Type          { get; set; } = string.Empty;
    public string EffectEntries { get; set; } = string.Empty;

    public string DamageClassEs => DamageClass switch
    {
        "physical" => "Físico",
        "special"  => "Especial",
        "status"   => "Estado",
        _          => DamageClass,
    };
}
