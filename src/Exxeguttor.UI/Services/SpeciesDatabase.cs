using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Acceso a datos de especies Pokémon.
/// Delegado completamente a PokemonDatabase (SQLite).
/// Mantiene la misma API pública que la versión JSON para no romper
/// los ViewModels existentes.
/// </summary>
public class SpeciesDatabase
{
    private static SpeciesDatabase? _instance;
    public static SpeciesDatabase Instance => _instance ??= new SpeciesDatabase();

    private SpeciesDatabase() { }

    // Cache en memoria por speciesId — sesión "premisa de carga única al abrir el save".
    // GetById se llama en CADA click sobre un Pokémon (PokemonEditorViewModel.LoadPokemon),
    // y sin cache eso son 3 queries SQLite (fila base + tipos + habilidades, ver
    // PokemonDatabase.GetSpecies) repetidas cada vez que el usuario vuelve a abrir el MISMO
    // Pokémon/especie — muy común (entrar/salir del mismo slot varias veces). Los datos de
    // especie son estáticos dentro de una sesión de la app (no dependen de qué save esté
    // cargado, ni cambian entre aperturas), así que cachear por id para siempre es seguro —
    // a diferencia de BagViewModel, acá no hace falta invalidar nunca. Es un cache LAZY (se
    // llena en el primer click de cada especie, no se precarga entero en el loader de
    // apertura) porque una sesión típica solo toca un subconjunto chico de las ~1000
    // especies — precargarlas todas de una sería trabajo desperdiciado en la mayoría de los
    // casos. <c>null</c> también se cachea (especie inexistente) para no re-consultar de
    // nuevo un ID inválido.
    private readonly Dictionary<int, SpeciesEntry?> _cache = new();

    /// <summary>Retorna la entrada de una especie por ID nacional.</summary>
    public SpeciesEntry? GetById(int id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;

        var db = PokemonDatabase.Instance.GetSpecies(id);
        var entry = db == null ? null : new SpeciesEntry
        {
            Id          = db.Id,
            Name        = db.Name,
            Types       = db.Types,
            BaseStats   = new BaseStats
            {
                Hp  = db.BaseHp,
                Atk = db.BaseAtk,
                Def = db.BaseDef,
                Spa = db.BaseSpa,
                Spd = db.BaseSpd,
                Spe = db.BaseSpe,
                Gen1Special = db.BaseSpecialGen1,
            },
            // SpeciesAbilities almacena slugs; los exponemos como lista de nombres
            Abilities   = db.Abilities.Select(a => a.Name).ToList(),
            GrowthRate  = db.GrowthRate,
            CaptureRate = db.CaptureRate,
            Height      = db.Height,
            Weight      = db.Weight,
        };

        _cache[id] = entry;
        return entry;
    }

    /// <summary>Datos de la especie para un Pokémon concreto: si es de forma regional (Exeggutor de
    /// Alola) devuelve los de SU forma (tipos, stats base, habilidades); si no, los de la especie.</summary>
    public SpeciesEntry? GetByForm(int species, byte form) =>
        GetById(SpeciesFormResolver.ResolveDbSpeciesId(species, form));

    /// <summary>Lista liviana de todas las especies base (Id/Name/Types) para el modal de
    /// selección de especie al crear un Pokémon.</summary>
    public List<SpeciesSummary> GetAllSummaries() =>
        Map(PokemonDatabase.Instance.GetAllSpeciesSummaries());

    /// <summary>Misma lista que <see cref="GetAllSummaries"/>, pero pensada para llamarse
    /// desde un hilo de BACKGROUND (ver SpeciesPickerViewModel.PreloadInBackground) — abre su
    /// propia conexión SQLite (<see cref="PokemonDatabase.OpenSecondaryConnection"/>) en vez
    /// de compartir la conexión principal de la app, que en ese momento suele estar ocupada
    /// con la apertura del save (Box/Party/Bag/Trainer, todo en el hilo de UI). Sin esto, la
    /// query de background termina contendiendo con esas otras queries sobre la MISMA
    /// conexión (SqliteConnection no soporta uso concurrente desde dos hilos) y en la
    /// práctica nunca llega a tiempo — el modal de crear seguía lento pese al prefetch por
    /// esto exactamente. Si la DB no está disponible, cae al mismo camino que
    /// GetAllSummaries() (ambos devuelven listo vacío en ese caso, ver PokemonDatabase).</summary>
    public List<SpeciesSummary> GetAllSummariesForBackgroundPrefetch()
    {
        using var conn = PokemonDatabase.Instance.OpenSecondaryConnection();
        if (conn == null) return GetAllSummaries();
        return Map(PokemonDatabase.Instance.GetAllSpeciesSummaries(conn));
    }

    private static List<SpeciesSummary> Map(List<SpeciesSummaryDbEntry> entries) =>
        entries.Select(s => new SpeciesSummary { Id = s.Id, Name = s.Name, Types = s.Types }).ToList();
}

// ── DTOs públicos (sin cambios de API) ─────────────────────────────
public class SpeciesSummary
{
    public int          Id    { get; set; }
    public string        Name  { get; set; } = string.Empty;
    public List<string>  Types { get; set; } = new();
}
public class SpeciesEntry
{
    public int          Id          { get; set; }
    public string       Name        { get; set; } = string.Empty;
    public List<string> Types       { get; set; } = new();
    public BaseStats    BaseStats   { get; set; } = new();
    public List<string> Abilities   { get; set; } = new();
    public string       GrowthRate  { get; set; } = string.Empty;
    public int?         CaptureRate { get; set; }
    public double?      Height      { get; set; }
    public double?      Weight      { get; set; }
    public int?         Generation  { get; set; }

    public int BaseStatTotal => BaseStats.Hp + BaseStats.Atk + BaseStats.Def +
                                BaseStats.Spa + BaseStats.Spd + BaseStats.Spe;

    /// <summary>Total base de Gen 1: el stat Especial cuenta UNA vez (no Ataque Esp. + Defensa Esp.).
    /// Si la especie no tiene el dato de Gen 1, el total normal.</summary>
    public int BaseStatTotalGen1 => BaseStats.Gen1Special is { } sp
        ? BaseStats.Hp + BaseStats.Atk + BaseStats.Def + BaseStats.Spe + sp
        : BaseStatTotal;
}

public class BaseStats
{
    public int Hp  { get; set; }
    public int Atk { get; set; }
    public int Def { get; set; }
    public int Spa { get; set; }
    public int Spd { get; set; }
    public int Spe { get; set; }
    /// <summary>Stat Especial único de Gen 1 (solo Kanto); null si no aplica.</summary>
    public int? Gen1Special { get; set; }
}
