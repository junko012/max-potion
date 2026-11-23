using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Acceso a datos de ítems.
/// Migrado de items_database.json a SQLite.
/// </summary>
public class ItemDatabase
{
    private static ItemDatabase? _instance;
    public static ItemDatabase Instance => _instance ??= new ItemDatabase();

    private ItemDatabase() { }

    public ItemEntry? GetById(int id)
    {
        var db = PokemonDatabase.Instance.GetItem(id);
        if (db == null) return null;
        return Map(db);
    }

    /// <summary>
    /// Búsqueda por nombre en inglés.
    /// Acepta tanto el slug "amulet-coin" como la forma display "Amulet Coin".
    /// </summary>
    public ItemEntry? GetByName(string name)
    {
        var db = PokemonDatabase.Instance.GetItemByName(name);
        if (db == null) return null;
        return Map(db);
    }

    private static ItemEntry Map(DbItemEntry db) => new()
    {
        Id          = db.Id,
        Name        = db.Name,
        Description = db.Description,
        Category    = db.Category,
        Cost        = db.Cost,
    };
}

public class ItemEntry
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category    { get; set; } = string.Empty;
    public int?   Cost        { get; set; }
    public string Attributes  { get; set; } = string.Empty; // compatibilidad
}
