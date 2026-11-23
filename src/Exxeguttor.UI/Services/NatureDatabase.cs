using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Tabla fija de las 25 naturalezas de Pokémon y sus modificadores de stat (±10%).
/// Esta tabla nunca cambia entre versiones de juego — es una regla fija del motor.
/// </summary>
/// <remarks>
/// Plan i18n (Fase 3): <see cref="NatureEntry.Name"/> es la LLAVE (nombre inglés = nombre del enum
/// <c>PKHeX.Core.Nature</c>, lo que <c>EditApplyService</c> parsea con <c>Enum.TryParse</c>) y NUNCA se
/// traduce. El texto mostrado (<see cref="NatureEntry.LocalizedName"/>) sale de PKHeX en el idioma de
/// los datos. El orden de la tabla coincide con el valor numérico del enum (Hardy = 0 … Quirky = 24).
/// </remarks>
public static class NatureDatabase
{
    public static readonly List<NatureEntry> All =
    [
        new(0,  "Hardy",   null,   null),
        new(1,  "Lonely",  "Atk",  "Def"),
        new(2,  "Brave",   "Atk",  "Spe"),
        new(3,  "Adamant", "Atk",  "SpA"),
        new(4,  "Naughty", "Atk",  "SpD"),
        new(5,  "Bold",    "Def",  "Atk"),
        new(6,  "Docile",  null,   null),
        new(7,  "Relaxed", "Def",  "Spe"),
        new(8,  "Impish",  "Def",  "SpA"),
        new(9,  "Lax",     "Def",  "SpD"),
        new(10, "Timid",   "Spe",  "Atk"),
        new(11, "Hasty",   "Spe",  "Def"),
        new(12, "Serious", null,   null),
        new(13, "Jolly",   "Spe",  "SpA"),
        new(14, "Naive",   "Spe",  "SpD"),
        new(15, "Modest",  "SpA",  "Atk"),
        new(16, "Mild",    "SpA",  "Def"),
        new(17, "Quiet",   "SpA",  "Spe"),
        new(18, "Bashful", null,   null),
        new(19, "Rash",    "SpA",  "SpD"),
        new(20, "Calm",    "SpD",  "Atk"),
        new(21, "Gentle",  "SpD",  "Def"),
        new(22, "Sassy",   "SpD",  "Spe"),
        new(23, "Careful", "SpD",  "SpA"),
        new(24, "Quirky",  null,   null),
    ];

    /// <summary>Lista NUEVA con las mismas 25 entradas: el ComboBox solo regenera sus ítems (y vuelve a
    /// pedir el texto) cuando cambia la referencia de la lista — al cambiar el idioma de los datos hace falta eso.</summary>
    public static List<NatureEntry> CreateList() => [.. All];

    /// <summary>Traductor de textos de UI (claves <c>Nature_Neutral</c>, <c>Stat_Short_*</c>). Lo asigna
    /// <c>MainWindowViewModel</c> al arrancar; por defecto devuelve la clave (se ve en seguida si falta).</summary>
    public static Func<string, string> Translate { get; set; } = key => key;

    /// <summary>Abreviatura de un stat ("Atk", "SpA"…) en el idioma de la interfaz.</summary>
    public static string StatLabel(string? stat)
        => string.IsNullOrEmpty(stat) ? string.Empty : Translate("Stat_Short_" + stat);

    public static NatureEntry? ById(int id) => id >= 0 && id < All.Count ? All[id] : null;

    /// <summary>Busca una naturaleza por su llave inglesa o por su nombre en el idioma de los datos activo
    /// (case-insensitive).</summary>
    public static NatureEntry? Find(string name) =>
        All.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? All.FirstOrDefault(n => string.Equals(n.LocalizedName, name, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>Una naturaleza con sus stats incrementado/reducido (null = neutral).</summary>
public sealed class NatureEntry(int id, string name, string? increased, string? decreased)
{
    /// <summary>Valor del enum <c>PKHeX.Core.Nature</c> (0–24) = índice en <c>GameStrings.Natures</c>.</summary>
    public int     Id        { get; } = id;

    /// <summary>LLAVE inglesa ("Adamant"): se guarda en las ediciones y se parsea al enum. No se traduce.</summary>
    public string  Name      { get; } = name;
    public string? Increased { get; } = increased;
    public string? Decreased { get; } = decreased;

    public bool IsNeutral => Increased == null;

    /// <summary>Nombre en el idioma de los datos activo (PKHeX); la llave inglesa si PKHeX no lo trae.</summary>
    public string LocalizedName
    {
        get
        {
            var list = DataLanguageService.Instance.Strings.Natures;
            return Id < list.Count && !string.IsNullOrEmpty(list[Id]) ? list[Id] : Name;
        }
    }

    /// <summary>"Audaz (+Atq, -AtqE)" / "Fuerte (neutra)": nombre en el idioma de los datos, stats y
    /// "neutra" en el idioma de la interfaz.</summary>
    public string Display => IsNeutral
        ? $"{LocalizedName} ({NatureDatabase.Translate("Nature_Neutral")})"
        : $"{LocalizedName} (+{NatureDatabase.StatLabel(Increased)}, -{NatureDatabase.StatLabel(Decreased)})";

    public override string ToString() => Display;
}
