using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Acceso a datos de habilidades.
/// Migrado de abilities_database.json a SQLite.
/// </summary>
public class AbilityDatabase
{
    private static AbilityDatabase? _instance;
    public static AbilityDatabase Instance => _instance ??= new AbilityDatabase();

    private AbilityDatabase() { }

    public AbilityEntry? GetById(int id)
    {
        var db = PokemonDatabase.Instance.GetAbility(id);
        if (db == null) return null;
        return Map(db);
    }

    /// <summary>
    /// Búsqueda por nombre en inglés.
    /// Acepta tanto el slug "static" como la forma display "Static".
    /// </summary>
    public AbilityEntry? GetByName(string name)
    {
        var db = PokemonDatabase.Instance.GetAbilityByName(name);
        if (db == null) return null;
        return Map(db);
    }

    private static AbilityEntry Map(DbAbilityEntry db) => new()
    {
        Id              = db.Id,
        Name            = db.Name,
        Description     = db.Description,
        LongDescription = db.LongDescription,
    };
}

public class AbilityEntry
{
    public int    Id              { get; set; }
    public string Name            { get; set; } = string.Empty;
    public string Description     { get; set; } = string.Empty;
    public string LongDescription { get; set; } = string.Empty;
    public string FlavorText      { get; set; } = string.Empty; // no está en DB; se mantiene por compatibilidad
    public int?   Generation      { get; set; }
}
