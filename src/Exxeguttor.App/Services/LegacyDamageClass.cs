namespace Exxeguttor.App.Services;

/// <summary>
/// Clase de daño de un movimiento según la generación del save.
/// En Gen 1-3 la clase NO era propia del movimiento: dependía del TIPO (los de tipo Normal,
/// Lucha, Volador, Veneno, Tierra, Roca, Bicho, Fantasma y Acero eran siempre físicos; Fuego,
/// Agua, Planta, Eléctrico, Psíquico, Hielo, Dragón y Siniestro, siempre especiales). Desde Gen 4
/// cada movimiento tiene su propia clase (la que trae la DB, basada en las generaciones modernas).
/// Los movimientos de estado siguen siendo de estado en todas las generaciones.
/// Ej.: Hyper Beam es Especial en la DB (Gen 4+), pero en Gen 1-3 es Normal → Físico.
/// </summary>
public static class LegacyDamageClass
{
    /// <summary>Generación del save abierto actualmente (la fija PokemonEditorViewModel al cargar un Pokémon).</summary>
    public static int Generation { get; set; }

    private static readonly HashSet<string> PhysicalTypes = new(StringComparer.OrdinalIgnoreCase)
        { "normal", "fighting", "flying", "poison", "ground", "rock", "bug", "ghost", "steel" };

    private static readonly HashSet<string> SpecialTypes = new(StringComparer.OrdinalIgnoreCase)
        { "fire", "water", "grass", "electric", "psychic", "ice", "dragon", "dark" };

    public static string Resolve(string? dbClass, string? typeName) => Resolve(dbClass, typeName, Generation);

    public static string Resolve(string? dbClass, string? typeName, int generation)
    {
        var cls = dbClass ?? string.Empty;
        if (generation < 1 || generation > 3) return cls;
        if (string.Equals(cls, "status", StringComparison.OrdinalIgnoreCase) || cls.Length == 0) return cls;
        var type = (typeName ?? string.Empty).Trim();
        if (PhysicalTypes.Contains(type)) return "physical";
        if (SpecialTypes.Contains(type))  return "special";
        return cls;
    }
}
