namespace Exxeguttor.UI.Services;

/// <summary>
/// Traduce el slug de <c>Species.Habitat</c> (inglés, viene de PokeAPI sin traducir — ver
/// PokemonDatabase.GetSpeciesOrigin) a texto legible es/en. Tabla fija chica (9 valores
/// confirmados sobre los datos reales de pokemon.db) — mismo criterio que
/// NatureDatabase.cs: no vive en SQLite porque es un set cerrado que no cambia entre
/// juegos ni requiere query.
/// </summary>
public static class HabitatDatabase
{
    private static readonly Dictionary<string, (string Es, string En)> Labels = new()
    {
        ["cave"]          = ("Cueva",          "Cave"),
        ["forest"]        = ("Bosque",         "Forest"),
        ["grassland"]     = ("Pradera",        "Grassland"),
        ["mountain"]      = ("Montaña",        "Mountain"),
        ["rare"]          = ("Raro",           "Rare"),
        ["rough-terrain"] = ("Terreno Agreste", "Rough terrain"),
        ["sea"]           = ("Mar",            "Sea"),
        ["urban"]         = ("Urbano",         "Urban"),
        ["waters-edge"]   = ("Orilla del Agua", "Water's edge"),
    };

    /// <summary>Null si el slug es null/vacío o no está en la tabla (defensivo — no
    /// debería pasar salvo que PokeAPI agregue un hábitat nuevo en el futuro).</summary>
    public static string? GetLabel(string? slug, string locale)
    {
        if (string.IsNullOrEmpty(slug) || !Labels.TryGetValue(slug, out var pair)) return null;
        return locale == "es" ? pair.Es : pair.En;
    }
}
