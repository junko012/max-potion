namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Una fila de la tabla de efectividad de tipos.
/// DefenseTypeList expone Receives como lista plana de badges (sin cambios).
/// Hits ahora viene YA separado por tipo propio desde <see cref="Exxeguttor.UI.Services.TypeEffectivenessDatabase"/>
/// — OffenseTargets1 para el tipo propio 1, OffenseTargets2 para el tipo propio 2 (vacía si el
/// Pokémon es de un solo tipo). Antes esto se armaba como un string combinado
/// ("Fire: Grass, Bug\nFlying: ...") y se reparseaba acá con un Split — se sacó esa capa
/// intermedia: ahora la base de datos ya sabe qué tipo propio es cada lista, no hace falta
/// reconstruirlo por nombre en la vista.
/// </summary>
public record TypeEffectivenessEntry(
    string Multiplier,
    string DefenseTypes,
    IReadOnlyList<string> OffenseTargets1,
    IReadOnlyList<string> OffenseTargets2,
    bool IsDualType
)
{
    public bool HasDefense => !string.IsNullOrEmpty(DefenseTypes);
    public bool HasOffense => OffenseTargets1.Count > 0 || OffenseTargets2.Count > 0;

    /// <summary>True cuando esta fila debe mostrar Hits como una sola lista plana (Pokémon de
    /// un solo tipo) — mismo aspecto visual que tenía la columna Hits antes del rediseño.</summary>
    public bool ShowFlatOffense => !IsDualType;

    /// <summary>Un solo tipo propio y este factor no tiene ningún tipo → la vista muestra "—".</summary>
    public bool ShowFlatOffenseEmpty => !IsDualType && !HasOffense;

    /// <summary>True cuando esta fila debe mostrar Hits como 2 subcolumnas (una por tipo
    /// propio) — el rediseño pedido para Pokémon de doble tipo.</summary>
    public bool ShowSplitOffense => IsDualType;

    /// <summary>Solo relevante cuando IsDualType — si el tipo propio 1 no pega nada en esta
    /// fila, la subcolumna correspondiente muestra un "—" en vez de quedar vacía.</summary>
    public bool HasOffense1 => OffenseTargets1.Count > 0;
    public bool HasOffense2 => OffenseTargets2.Count > 0;

    /// <summary>
    /// Tipos individuales para la columna Receives.
    /// "Fire, Ice, Rock" → ["Fire","Ice","Rock"]
    /// </summary>
    public IReadOnlyList<string> DefenseTypeList =>
        string.IsNullOrEmpty(DefenseTypes)
            ? Array.Empty<string>()
            : DefenseTypes
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
