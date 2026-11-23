using System.Linq;
using Microsoft.Data.Sqlite;

namespace Exxeguttor.App.Services;

/// <summary>
/// Punto de acceso central a pokemon.db (SQLite).
/// La DB se busca en el mismo directorio que el ejecutable; si no existe
/// ahí, se busca en las rutas de desarrollo habituales del repositorio.
/// 
/// Todos los métodos de consulta son síncronos e internamente usan
/// una única conexión con WAL habilitado.  Los DTOs que retorna son
/// structs o records simples para evitar allocations innecesarios.
/// </summary>
public sealed class PokemonDatabase : IDisposable
{
    // ── Singleton ────────────────────────────────────────────────────
    private static PokemonDatabase? _instance;
    public  static PokemonDatabase  Instance => _instance ??= new PokemonDatabase();

    // ── Estado interno ────────────────────────────────────────────────
    private readonly SqliteConnection _conn;
    private readonly string? _dbPath;
    private readonly string? _connString;
    public  bool IsAvailable { get; }

    /// <summary>Versión declarada por la DB y su compatibilidad con este app (ver <see cref="DbCompatibility"/>).</summary>
    public DbCompatibilityResult Compatibility { get; }

    // Cache en memoria por id — GetSpecies/GetMove son consultados repetidamente con
    // los mismos IDs una y otra vez dentro de una sesión (especies/movimientos son
    // datos estáticos, no dependen de qué save esté cargado). GetSpecies en particular
    // hace 3 queries por llamada (fila base + tipos + habilidades) — cachearla acá
    // evita repetir las 3 cada vez, no solo la principal. Sumado ahora puntualmente
    // porque LegalityArgumentFormatter (diagnosticador de legalidad) va a llamar a
    // GetMove/GetSpecies en cada recálculo en vivo mientras el usuario edita, no solo
    // una vez por apertura de Pokémon como antes. <c>null</c> también se cachea
    // (id inexistente) para no re-consultar de nuevo un ID inválido.
    private readonly Dictionary<int, DbSpeciesEntry?> _speciesCache = new();
    private readonly Dictionary<int, DbMoveEntry?> _moveCache = new();

    // ── Constructor ───────────────────────────────────────────────────
    private PokemonDatabase()
    {
        var path = ResolveDbPath();
        if (path == null)
        {
            IsAvailable = false;
            // Conexión en memoria vacía para que los métodos nunca lancen NullRef
            _conn = new SqliteConnection("Data Source=:memory:");
            _conn.Open();
            Compatibility = new DbCompatibilityResult(DbCompatibilityStatus.Missing, null);
            return;
        }

        // pokemon.db es SOLO LECTURA para el app: nunca se escribe en ella. Se abre como INMUTABLE
        // (`immutable=1`): SQLite no toma bloqueos ni crea archivos auxiliares (-wal / -shm), así que
        // funciona igual desde /usr/share (instalación del sistema, dueño root), Program Files o
        // /Applications, y también si el archivo fue guardado en modo WAL. No se ejecuta ningún PRAGMA
        // que escriba (antes `journal_mode=WAL` fallaba con "SQLite Error 8: attempt to write a
        // readonly database" en el .rpm). Varios lectores concurrentes (cada uno con su conexión, ver
        // OpenSecondaryConnection) siguen siendo posibles: una DB inmutable no necesita WAL.
        _connString = $"Data Source=file:{new Uri(Path.GetFullPath(path)).AbsolutePath}?immutable=1;Mode=ReadOnly";
        try
        {
            _conn = OpenReadOnly(_connString);
        }
        catch (Exception ex) when (ex is SqliteException or ArgumentException)
        {
            // Respaldo: apertura de solo lectura normal (por si el URI inmutable no se acepta)
            _connString = $"Data Source={path};Mode=ReadOnly";
            _conn = OpenReadOnly(_connString);
        }
        _dbPath = path;
        IsAvailable = true;
        Compatibility = DbCompatibility.Evaluate(DbCompatibility.Read(_conn));
    }

    /// <summary>Abre la conexión y comprueba con una lectura que la DB es legible (nunca escribe).</summary>
    private static SqliteConnection OpenReadOnly(string connectionString)
    {
        var c = new SqliteConnection(connectionString);
        try
        {
            c.Open();
            using var probe = c.CreateCommand();
            probe.CommandText = "SELECT 1 FROM sqlite_master LIMIT 1";
            probe.ExecuteScalar();
            return c;
        }
        catch
        {
            c.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Abre una conexión de solo lectura INDEPENDIENTE a pokemon.db — pensada para consultas
    /// que corren en un hilo de background EN PARALELO con el uso normal de <see cref="_conn"/>
    /// (que es la conexión compartida por toda la app y está muy ocupada durante la apertura
    /// de un save: Box.Initialize, Party.Load, Bag.InitializeAsync, TrainerService, etc.).
    /// <see cref="SqliteConnection"/> NO es segura para usarse desde dos hilos a la vez sobre
    /// la MISMA instancia — hacerlo no tira una excepción prolija, sino que serializa/contiene
    /// internamente, así que una query de background termina esperando detrás de todo lo que
    /// ya está corriendo en el hilo de UI y en la práctica nunca llega a tiempo (bug real
    /// encontrado en la sesión de "modal de crear Pokémon sigue lento pese al prefetch" — ver
    /// SpeciesPickerViewModel.PreloadInBackground). La DB ya se abre en modo WAL (ver
    /// constructor), que está diseñado justo para esto: soportar múltiples lectores
    /// concurrentes, siempre que cada uno use su PROPIA conexión — por eso alcanza con abrir
    /// una conexión nueva acá en vez de un lock sobre <see cref="_conn"/> (que anularía el
    /// paralelismo que se busca). El caller es responsable de Dispose() (usar `using`).
    /// Devuelve null si la DB no está disponible, mismo criterio que el resto de la clase.
    /// </summary>
    public SqliteConnection? OpenSecondaryConnection()
    {
        if (!IsAvailable || _dbPath == null) return null;
        var conn = new SqliteConnection(_connString);
        conn.Open();
        return conn;
    }

    /// <summary>Fila de <c>Species</c> (especie base o forma, id &gt; 10000 estilo PokeAPI:
    /// "exeggutor-alola", "giratina-origin") con sus tipos (slugs en minúscula), stats base
    /// [HP, Atk, Def, SpA, SpD, Spe] y habilidades (slugs).</summary>
    public sealed record DbFormRow(int Id, string Name, List<string> Types, int[] Stats, List<string> Abilities);

    /// <summary>Datos que necesita <see cref="SpeciesFormResolver"/>: las especies base (ids
    /// 1–1025) y todas las filas de forma (ids &gt; 10000), cada una con tipos y stats base. Usa su
    /// PROPIA conexión de solo lectura, así que es seguro llamarlo desde el hilo de background del
    /// prefetch del selector sin contender con la conexión principal. null si la DB no está.</summary>
    public (Dictionary<int, DbFormRow> Bases, List<DbFormRow> Forms)? LoadFormSource()
    {
        using var conn = OpenSecondaryConnection();
        if (conn == null) return null;

        var all = new Dictionary<int, DbFormRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT SpeciesId, Name, BaseHP, BaseAttack, BaseDefense,
                       BaseSpAttack, BaseSpDefense, BaseSpeed
                FROM   Species
                WHERE  SpeciesId <= 1025 OR SpeciesId > 10000
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int V(int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);
                all[r.GetInt32(0)] = new DbFormRow(r.GetInt32(0), r.GetString(1), new List<string>(2),
                    [V(2), V(3), V(4), V(5), V(6), V(7)], new List<string>(3));
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT SpeciesId, TypeName FROM SpeciesTypes
                WHERE  SpeciesId <= 1025 OR SpeciesId > 10000
                ORDER  BY SpeciesId, Slot
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (all.TryGetValue(r.GetInt32(0), out var row)) row.Types.Add(r.GetString(1));
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT SpeciesId, AbilityName FROM SpeciesAbilities
                WHERE  SpeciesId <= 1025 OR SpeciesId > 10000
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (all.TryGetValue(r.GetInt32(0), out var row)) row.Abilities.Add(r.GetString(1));
        }

        var bases = all.Values.Where(x => x.Id <= 1025).ToDictionary(x => x.Id);
        var forms = all.Values.Where(x => x.Id > 10000).ToList();
        return (bases, forms);
    }

    // ── Resolución de ruta ────────────────────────────────────────────
    /// <summary>
    /// Busca pokemon.db en (por orden de prioridad):
    ///   1. Directorio del ejecutable          (distribución instalada / AppImage)
    ///   2. ./pokemon-database/database/        (desde raíz del repo)
    ///   3. Hasta 5 niveles arriba buscando pokemon-database/database/
    /// </summary>
    private static string? ResolveDbPath()
    {
        const string dbName = "pokemon.db";

        // 1. Instalación estándar Linux (RPM / DEB / FHS)
        //    dpkg/rpm instalan los datos en /usr/share/<appname>/
        var systemShare = Path.Combine("/usr", "share", "maxpotion", dbName);
        if (File.Exists(systemShare)) return systemShare;

        // 2. AppImage / distribución portable:
        //    la DB viaja junto al ejecutable
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, dbName);
        if (File.Exists(candidate)) return candidate;

        // 3. Desarrollo — dotnet run desde la raíz del repositorio
        candidate = Path.Combine(Directory.GetCurrentDirectory(),
            "pokemon-database", "database", dbName);
        if (File.Exists(candidate)) return candidate;

        // 4. Desarrollo — subir desde bin/Debug/net9.0/ hasta la raíz del repo
        var dir = new DirectoryInfo(exeDir);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            candidate = Path.Combine(dir.FullName, "pokemon-database", "database", dbName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════
    //  SPECIES
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Retorna la entrada completa de una especie por su ID nacional.</summary>
    /// <summary>
    /// Lista liviana de TODAS las especies base (sin formas alternativas — esas viven en IDs
    /// ≥10000 en esta DB, convención heredada de PokeAPI; se excluyen a propósito acá) para
    /// el modal de selección de especie al crear un Pokémon. Id/Name/Types alcanzan para
    /// mostrar sprite+nombre+badges de tipo; no trae stats base ni abilities (eso ya lo da
    /// GetSpecies(id) si hiciera falta más detalle).
    /// <paramref name="connection"/> permite pasar una conexión propia (ver
    /// <see cref="OpenSecondaryConnection"/>) para llamar esto desde un hilo de background sin
    /// contender con <see cref="_conn"/> — si se omite, usa la conexión compartida de siempre
    /// (comportamiento sin cambios para el resto de los callers, todos en el hilo de UI).
    /// </summary>
    public List<SpeciesSummaryDbEntry> GetAllSpeciesSummaries(SqliteConnection? connection = null)
    {
        var conn = connection ?? _conn;
        var list = new List<SpeciesSummaryDbEntry>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT SpeciesId, Name
                FROM   Species
                WHERE  SpeciesId < 10000
                ORDER  BY SpeciesId
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SpeciesSummaryDbEntry { Id = r.GetInt32(0), Name = r.GetString(1) });
        }

        // Tipos en una sola query aparte (evita N+1 sobre ~1000 especies)
        var typesById = new Dictionary<int, List<string>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT SpeciesId, TypeName
                FROM   SpeciesTypes
                WHERE  SpeciesId < 10000
                ORDER  BY SpeciesId, Slot
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var sid = r.GetInt32(0);
                if (!typesById.TryGetValue(sid, out var types))
                    typesById[sid] = types = new List<string>();
                types.Add(r.GetString(1));
            }
        }

        foreach (var s in list)
            s.Types = typesById.TryGetValue(s.Id, out var t) ? t : new List<string>();

        return list;
    }

    /// <summary>
    /// GenderRate crudo estilo PokeAPI: -1 = sin género, 0 = siempre macho, 8 = siempre
    /// hembra, 1-7 = proporción de hembras sobre 8. Se usa al crear un Pokémon para derivar
    /// un género válido sin depender de ninguna clase de PKHeX.Core para esto puntual.
    /// </summary>
    public int? GetSpeciesGenderRate(int speciesId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT GenderRate FROM Species WHERE SpeciesId = $id";
        cmd.Parameters.AddWithValue("$id", speciesId);
        var result = cmd.ExecuteScalar();
        return result is long l ? (int)l : null;
    }

    private bool? _hasGen1SpecialColumn;

    /// <summary>True si la tabla Species tiene la columna BaseSpecialGen1 (DB con el hotfix
    /// fix_gen1_special aplicado).</summary>
    private bool HasGen1SpecialColumn()
    {
        if (_hasGen1SpecialColumn is { } known) return known;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM pragma_table_info('Species') WHERE name = 'BaseSpecialGen1'";
        return (_hasGen1SpecialColumn = cmd.ExecuteScalar() != null).Value;
    }

    public DbSpeciesEntry? GetSpecies(int speciesId)
    {
        if (_speciesCache.TryGetValue(speciesId, out var cachedSpecies)) return cachedSpecies;

        using var cmd = _conn.CreateCommand();
        // BaseSpecialGen1: stat Especial ÚNICO de Gen 1 (columna agregada por hotfixes/fix_gen1_special;
        // solo las 151 especies de Kanto). Una DB sin el hotfix no tiene la columna: se lee NULL.
        cmd.CommandText = $"""
            SELECT SpeciesId, Name, BaseHP, BaseAttack, BaseDefense,
                   BaseSpAttack, BaseSpDefense, BaseSpeed,
                   Height, Weight, CaptureRate, GrowthRate,
                   {(HasGen1SpecialColumn() ? "BaseSpecialGen1" : "NULL")}
            FROM   Species
            WHERE  SpeciesId = $id
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            _speciesCache[speciesId] = null;
            return null;
        }

        var entry = new DbSpeciesEntry
        {
            Id           = r.GetInt32(0),
            Name         = r.GetString(1),
            BaseHp       = r.IsDBNull(2)  ? 0 : r.GetInt32(2),
            BaseAtk      = r.IsDBNull(3)  ? 0 : r.GetInt32(3),
            BaseDef      = r.IsDBNull(4)  ? 0 : r.GetInt32(4),
            BaseSpa      = r.IsDBNull(5)  ? 0 : r.GetInt32(5),
            BaseSpd      = r.IsDBNull(6)  ? 0 : r.GetInt32(6),
            BaseSpe      = r.IsDBNull(7)  ? 0 : r.GetInt32(7),
            Height       = r.IsDBNull(8)  ? 0 : r.GetDouble(8),
            Weight       = r.IsDBNull(9)  ? 0 : r.GetDouble(9),
            CaptureRate  = r.IsDBNull(10) ? null : r.GetInt32(10),
            GrowthRate   = r.IsDBNull(11) ? string.Empty : r.GetString(11),
            BaseSpecialGen1 = r.IsDBNull(12) ? null : r.GetInt32(12),
        };

        entry.Types     = GetSpeciesTypes(speciesId);
        entry.Abilities = GetSpeciesAbilities(speciesId);
        _speciesCache[speciesId] = entry;
        return entry;
    }

    private List<string> GetSpeciesTypes(int speciesId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT TypeName FROM SpeciesTypes
            WHERE  SpeciesId = $id
            ORDER  BY Slot
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        using var r = cmd.ExecuteReader();
        var list = new List<string>(2);
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private List<DbAbilitySlot> GetSpeciesAbilities(int speciesId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT AbilityName, IsHidden FROM SpeciesAbilities
            WHERE  SpeciesId = $id
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        using var r = cmd.ExecuteReader();
        var list = new List<DbAbilitySlot>(3);
        while (r.Read())
            list.Add(new DbAbilitySlot(r.GetString(0), r.GetInt32(1) != 0));
        return list;
    }

    // ═════════════════════════════════════════════════════════════════
    //  MOVES
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Retorna la entrada de un movimiento por su ID.</summary>
    public DbMoveEntry? GetMove(int moveId)
    {
        if (_moveCache.TryGetValue(moveId, out var cachedMove)) return cachedMove;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT MoveId, Name, TypeName, DamageClass,
                   Power, Accuracy, PP, EffectText
            FROM   Moves
            WHERE  MoveId = $id
            """;
        cmd.Parameters.AddWithValue("$id", moveId);
        using var r = cmd.ExecuteReader();
        var entry = r.Read() ? ReadMoveEntry(r) : null;
        _moveCache[moveId] = entry;
        return entry;
    }

    /// <summary>
    /// Busca un movimiento por nombre display (ej. "Flare Blitz" -> slug "flare-blitz").
    /// Caso especial: "Hidden Power Ice" -> busca "hidden-power" y sobreescribe TypeName con "ice".
    /// </summary>
    public DbMoveEntry? GetMoveByName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        var trimmed = displayName.Trim();
        var slug    = trimmed.ToLowerInvariant().Replace(' ', '-');

        DbMoveEntry? TrySlug(string s)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT MoveId, Name, TypeName, DamageClass,
                       Power, Accuracy, PP, EffectText
                FROM   Moves WHERE Name = $slug
                """;
            cmd.Parameters.AddWithValue("$slug", s);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadMoveEntry(r) : null;
        }

        // Direct match first
        var entry = TrySlug(slug);
        if (entry != null) return entry;

        // Fallback: strip last word and retry (e.g. "hidden-power-ice" -> "hidden-power")
        var lastDash = slug.LastIndexOf('-');
        if (lastDash > 0)
        {
            var shortened = slug[..lastDash];
            var suffix    = slug[(lastDash + 1)..];  // e.g. "ice", "fire", "electric"
            entry = TrySlug(shortened);
            if (entry != null)
            {
                // Override TypeName with the suffix if it looks like a Pokémon type
                if (IsTypeName(suffix))
                    entry.TypeName = suffix;
                return entry;
            }
        }
        return null;
    }

    private static readonly HashSet<string> _typeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "normal","fire","water","electric","grass","ice","fighting","poison",
        "ground","flying","psychic","bug","rock","ghost","dragon","dark","steel","fairy"
    };

    private static bool IsTypeName(string s) => _typeNames.Contains(s);

    private static DbMoveEntry ReadMoveEntry(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id          = r.GetInt32(0),
        Name        = r.GetString(1),
        TypeName    = r.IsDBNull(2) ? string.Empty : r.GetString(2),
        DamageClass = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        Power       = r.IsDBNull(4) ? null : r.GetInt32(4),
        Accuracy    = r.IsDBNull(5) ? null : r.GetInt32(5),
        PP          = r.IsDBNull(6) ? null : r.GetInt32(6),
        EffectText  = r.IsDBNull(7) ? string.Empty : r.GetString(7),
    };

    // ═════════════════════════════════════════════════════════════════
    //  ABILITIES
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Retorna la entrada de una habilidad por su ID.</summary>
    public DbAbilityEntry? GetAbility(int abilityId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT AbilityId, Name, Description, LongDescription
            FROM   Abilities
            WHERE  AbilityId = $id
            """;
        cmd.Parameters.AddWithValue("$id", abilityId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DbAbilityEntry
        {
            Id              = r.GetInt32(0),
            Name            = r.GetString(1),
            Description     = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            LongDescription = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        };
    }

    /// <summary>
    /// Busca una habilidad por nombre en inglés (acepta tanto slug
    /// "static" como forma display "Static").
    /// </summary>
    public DbAbilityEntry? GetAbilityByName(string name)
    {
        var slug = name.ToLowerInvariant().Replace(' ', '-');
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT AbilityId, Name, Description, LongDescription
            FROM   Abilities
            WHERE  Name = $slug
            """;
        cmd.Parameters.AddWithValue("$slug", slug);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DbAbilityEntry
        {
            Id              = r.GetInt32(0),
            Name            = r.GetString(1),
            Description     = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            LongDescription = r.IsDBNull(3) ? string.Empty : r.GetString(3),
        };
    }

    /// <summary>
    /// Devuelve las habilidades posibles de una especie (slug + IsHidden).
    /// No filtra por generación — la tabla SpeciesAbilities no tiene esa columna;
    /// el caller debe excluir IsHidden cuando la generación no soporta hidden ability (&lt;5).
    /// </summary>
    public List<(string Slug, bool IsHidden)> GetAbilitiesBySpecies(int speciesId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT AbilityName, IsHidden
            FROM   SpeciesAbilities
            WHERE  SpeciesId = $id
            ORDER  BY IsHidden, AbilityName
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        using var r = cmd.ExecuteReader();
        var list = new List<(string, bool)>();
        while (r.Read())
            list.Add((r.GetString(0), r.GetInt32(1) != 0));
        return list;
    }

    // ═════════════════════════════════════════════════════════════════
    //  ITEMS
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Retorna la entrada de un ítem por su ID.</summary>
    public DbItemEntry? GetItem(int itemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ItemId, Name, Description, Category, Cost
            FROM   Items
            WHERE  ItemId = $id
            """;
        cmd.Parameters.AddWithValue("$id", itemId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DbItemEntry
        {
            Id          = r.GetInt32(0),
            Name        = r.GetString(1),
            Description = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            Category    = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            Cost        = r.IsDBNull(4) ? null : r.GetInt32(4),
        };
    }

    /// <summary>Versión batch de <see cref="GetItem"/> — UN solo query con <c>WHERE ItemId IN
    /// (...)</c> en vez de una consulta por ítem. Bug de rendimiento encontrado en sesión
    /// "Mochila se queda pensando al entrar": <c>ItemInventoryService.GetItems</c> llamaba
    /// <see cref="GetItem"/> DENTRO de un <c>foreach</c> sobre todos los ítems de la
    /// categoría (patrón N+1) — para una categoría con 50-100 ítems (ej. "Objetos" u "MTs/
    /// MOs") eso son 50-100 round-trips SQLite seguidos, sincrónico en el hilo de UI, sin
    /// ningún indicador visual — se sentía como que la app se congelaba. <c>IEnumerable
    /// &lt;int&gt;</c> se materializa a lista una sola vez para poder usarla dos veces
    /// (armar los parámetros Y de tamaño para el placeholder), y a <c>HashSet</c> para
    /// descartar duplicados antes de armar el IN — no debería haber duplicados en la práctica
    /// pero es gratis evitarlos. Devuelve un Dictionary por ItemId para lookup O(1) en el
    /// llamante, en vez de una lista que habría que buscar linealmente.</summary>
    public Dictionary<int, DbItemEntry> GetItems(IEnumerable<int> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        var result = new Dictionary<int, DbItemEntry>(ids.Count);
        if (ids.Count == 0) return result;

        using var cmd = _conn.CreateCommand();
        var placeholders = string.Join(",", ids.Select((_, i) => $"$id{i}"));
        cmd.CommandText = $"""
            SELECT ItemId, Name, Description, Category, Cost
            FROM   Items
            WHERE  ItemId IN ({placeholders})
            """;
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"$id{i}", ids[i]);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var entry = new DbItemEntry
            {
                Id          = r.GetInt32(0),
                Name        = r.GetString(1),
                Description = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                Category    = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                Cost        = r.IsDBNull(4) ? null : r.GetInt32(4),
            };
            result[entry.Id] = entry;
        }
        return result;
    }

    /// <summary>Descripciones de objetos en el idioma pedido (tabla <c>ItemDescriptions</c>, solo filas
    /// <c>Verified=1</c>), con respaldo al inglés de la misma tabla. UN query para todos los ids (mismo criterio
    /// anti N+1 que <see cref="GetItems"/>). Un objeto sin fila no aparece en el resultado: el llamante usa
    /// entonces <c>Items.Description</c>. Una base anterior a la tabla devuelve un diccionario vacío.</summary>
    public Dictionary<int, string> GetItemDescriptions(IEnumerable<int> itemIds, string lang)
    {
        var ids = itemIds.Distinct().ToList();
        var result = new Dictionary<int, string>(ids.Count);
        if (ids.Count == 0) return result;
        try
        {
            using var cmd = _conn.CreateCommand();
            var placeholders = string.Join(",", ids.Select((_, i) => $"$id{i}"));
            cmd.CommandText = $"""
                SELECT ItemId, Lang, Text
                FROM   ItemDescriptions
                WHERE  Verified = 1 AND Lang IN ($lang, 'en') AND ItemId IN ({placeholders})
                """;
            cmd.Parameters.AddWithValue("$lang", lang);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"$id{i}", ids[i]);
            using var r = cmd.ExecuteReader();
            var english = new Dictionary<int, string>();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                var text = r.GetString(2);
                if (r.GetString(1) == lang) result[id] = text;
                else english[id] = text;
            }
            foreach (var kv in english)
                result.TryAdd(kv.Key, kv.Value);
        }
        catch (SqliteException) { /* base sin la tabla ItemDescriptions */ }
        return result;
    }

    /// <summary>Nombres de objeto de pokemon.db (tabla <c>ItemNames</c>) para los objetos que PKHeX no nombra
    /// (p. ej. los 65 nuevos de Legends: Z-A). Solo filas Verified=1; prefiere el idioma pedido y, si no
    /// existe, el inglés de la tabla. Los ids sin fila no aparecen en el resultado. Vacío si la base no tiene la tabla.</summary>
    public Dictionary<int, string> GetItemNames(IEnumerable<int> itemIds, string lang)
    {
        var ids = itemIds.Distinct().ToList();
        var result = new Dictionary<int, string>();
        if (ids.Count == 0) return result;
        try
        {
            using var cmd = _conn.CreateCommand();
            var placeholders = string.Join(",", ids.Select((_, i) => $"$id{i}"));
            cmd.CommandText = $"""
                SELECT ItemId, Lang, Name
                FROM   ItemNames
                WHERE  Verified = 1 AND Lang IN ($lang, 'en') AND ItemId IN ({placeholders})
                """;
            cmd.Parameters.AddWithValue("$lang", lang);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"$id{i}", ids[i]);
            using var r = cmd.ExecuteReader();
            var english = new Dictionary<int, string>();
            while (r.Read())
            {
                var id = r.GetInt32(0);
                var name = r.GetString(2);
                if (r.GetString(1) == lang) result[id] = name;
                else english[id] = name;
            }
            foreach (var kv in english)
                result.TryAdd(kv.Key, kv.Value);
        }
        catch (SqliteException) { /* base sin la tabla ItemNames */ }
        return result;
    }

    /// <summary>Movimiento (id de PKHeX) que enseña cada MT/MO/DT de un juego, por ItemId de pokemon.db
    /// (tabla <c>MachineMoves</c>). Vacío si la base no la tiene.</summary>
    public Dictionary<int, int> GetMachineMovesByItem(int gameId)
    {
        var result = new Dictionary<int, int>();
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT ItemId, MoveId FROM MachineMoves WHERE GameId = $gid";
            cmd.Parameters.AddWithValue("$gid", gameId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result[r.GetInt32(0)] = r.GetInt32(1);
        }
        catch (SqliteException) { /* base sin la tabla MachineMoves */ }
        return result;
    }

    /// <summary>ItemId de pokemon.db que la Mochila no debe ofrecer en un juego (tabla <c>HiddenItems</c>:
    /// p. ej. MT sin movimiento conocido). Si el save ya trae uno, la Mochila lo sigue mostrando.
    /// Vacío si la base no tiene la tabla.</summary>
    public HashSet<int> GetHiddenItemIds(int gameId)
    {
        var result = new HashSet<int>();
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT ItemId FROM HiddenItems WHERE GameId = $gid";
            cmd.Parameters.AddWithValue("$gid", gameId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(r.GetInt32(0));
        }
        catch (SqliteException) { /* base sin la tabla HiddenItems */ }
        return result;
    }

    /// <summary>Busca un ítem por nombre en inglés (slug o display).</summary>
    public List<DbItemEntry> GetItemsByCategory(IEnumerable<string> categories)
    {
        var cats = string.Join(",", categories.Select(c => $"'{c}'"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT ItemId, Name, Description, Category, Cost
            FROM   Items
            WHERE  Category IN ({cats})
            ORDER  BY ItemId
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<DbItemEntry>();
        while (r.Read())
            list.Add(new DbItemEntry
            {
                Id          = r.GetInt32(0),
                Name        = r.GetString(1),
                Description = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                Category    = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                Cost        = r.IsDBNull(4) ? 0 : r.GetInt32(4),
            });
        return list;
    }

    /// <summary>
    /// Fila de <c>ItemGameCodes</c> para un ítem dentro de un juego puntual — la fuente que usa
    /// <see cref="ItemInventoryService"/> para armar la Mochila en TODA generación.
    ///
    /// <c>ItemId</c> es el ID unificado de pokemon.db (para buscar Name/Description/Category en
    /// <c>Items</c> — <see cref="GetItem"/>). <c>RawItemId</c> es el índice REAL que usa
    /// PKHeX.Core para ESTE juego puntual — casi nunca coincide con <c>ItemId</c> (confirmado
    /// contra un save real de Yellow: 125 de 130 filas difieren) y es el que hay que usar para
    /// matchear contra <c>InventoryPouch.Items[].Index</c>, tanto en lectura
    /// (<c>ItemInventoryService.GetPouches/GetItems</c>) como en escritura
    /// (<c>EditApplyService.ApplyBagPouch</c>, que recibe este mismo valor como
    /// <c>BagItemInfo.ItemId</c> — ver comentario ahí).
    ///
    /// Corrección de sesión "categorización de Mochila" (cuarta vuelta): las primeras tres
    /// vueltas usaron <c>ItemId</c> para todo, incluyendo el matcheo contra
    /// <c>pouch.Items[].Index</c> — funcionaba por casualidad para los ítems donde
    /// <c>ItemId == RawItemId</c> (la mayoría de los "Objetos" comunes con ID bajo), pero fallaba
    /// silenciosamente para el resto (Piedras evolutivas, Llaves, Cebo, Caña buena... y TODAS
    /// las MTs/MOs, que fue el único caso que se notó porque ahí SIEMPRE difieren). Se había
    /// llegado a armar un camino especial solo para MTs/MOs usando
    /// <c>GameStrings.GetItemStrings</c>/<c>InventoryPouch.GetAllItems()</c> de PKHeX.Core en
    /// vivo — innecesario una vez que se encontró que <c>RawItemId</c> ya tenía el dato correcto
    /// desde el hotfix `fix_database` (nunca se había usado esa columna hasta ahora).
    /// </summary>
    public readonly record struct ItemPouchRow(int ItemId, int RawItemId, string? UserPouchCategory);

    /// <summary>
    /// Categorización completa de Mochila para un <paramref name="gameId"/> puntual — ver
    /// <see cref="ItemPouchRow"/>. Fuente única para <see cref="ItemInventoryService"/> en TODA
    /// generación (ya no hay camino dual ni excepciones por categoría).
    ///
    /// Filas con <c>UserPouchCategory == null</c> son ítems sin tile de UI asignada
    /// (obsoletos/no jugables/internos — ver <c>CATEGORY_TO_POUCH</c> en
    /// <c>add_user_pouch_category.py</c>, entradas mapeadas a <c>None</c>) — el llamante debe
    /// descartarlas.
    ///
    /// ⚠️ Los ~131 ítems sintéticos exclusivos de Colosseum/XD (<c>ItemId >= 90000</c>, sin ID
    /// unificado de PKHeX.Core) tampoco tienen <c>RawItemId</c> significativo — no deben
    /// cruzarse contra <c>save.Inventory</c> real (gap conocido, ver memoria del proyecto sobre
    /// ItemGameCodes/Colosseum-XD).
    /// </summary>
    public List<ItemPouchRow> GetItemPouchRows(int gameId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ItemId, RawItemId, UserPouchCategory
            FROM   ItemGameCodes
            WHERE  GameId = $gid
            """;
        cmd.Parameters.AddWithValue("$gid", gameId);
        using var r = cmd.ExecuteReader();
        var list = new List<ItemPouchRow>();
        while (r.Read())
            list.Add(new ItemPouchRow(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    public DbItemEntry? GetItemByName(string name)
    {
        // Bug encontrado y corregido en sesión "categorización de Mochila": la versión anterior
        // SIEMPRE convertía el nombre a slug (minúsculas + guiones) antes de buscar, pero
        // Items.Name en pokemon.db está guardado en forma display tal cual ("Amulet Coin",
        // "TM01" — nunca se sluggificó al cargar la tabla) — nunca matcheaba nada. Ahora acepta
        // las dos formas: reemplaza guiones por espacios (para "amulet-coin") y compara sin
        // distinguir mayúsculas (COLLATE NOCASE, para "TM01"/"tm01" por igual).
        var displayForm = name.Replace('-', ' ');
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT ItemId, Name, Description, Category, Cost
            FROM   Items
            WHERE  Name = $name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("$name", displayForm);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DbItemEntry
        {
            Id          = r.GetInt32(0),
            Name        = r.GetString(1),
            Description = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            Category    = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            Cost        = r.IsDBNull(4) ? null : r.GetInt32(4),
        };
    }

    // ═════════════════════════════════════════════════════════════════
    //  TYPE EFFECTIVENESS
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retorna el multiplicador de daño cuando un ataque del tipo
    /// <paramref name="attackType"/> impacta a un defensor del tipo
    /// <paramref name="defenseType"/>.
    /// Retorna 1.0 si la combinación no está en la tabla (neutro).
    /// </summary>
    public double GetTypeMultiplier(string attackType, string defenseType)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT Multiplier FROM TypeEffectiveness
            WHERE  AttackType  = $atk
            AND    DefenseType = $def
            """;
        cmd.Parameters.AddWithValue("$atk", attackType.ToLowerInvariant());
        cmd.Parameters.AddWithValue("$def", defenseType.ToLowerInvariant());
        var result = cmd.ExecuteScalar();
        return result is double d ? d : 1.0;
    }

    /// <summary>
    /// Retorna todos los pares (AttackType, Multiplier) para un tipo
    /// defensor dado, excluyendo los multiplicadores neutros (1.0).
    /// </summary>
    public List<(string AttackType, double Multiplier)> GetDefenseMatchups(string defenseType)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT AttackType, Multiplier FROM TypeEffectiveness
            WHERE  DefenseType = $def
            """;
        cmd.Parameters.AddWithValue("$def", defenseType.ToLowerInvariant());
        using var r = cmd.ExecuteReader();
        var list = new List<(string, double)>(10);
        while (r.Read())
            list.Add((r.GetString(0), r.GetDouble(1)));
        return list;
    }

    /// <summary>
    /// Query bulk de defensa: retorna todas las excepciones (factor != 1)
    /// donde algo ataca a cualquiera de los tipos dados.
    /// Clave: (attackType, defenseType) → multiplier
    /// Una sola query para todos los tipos propios del Pokémon.
    /// </summary>
    public Dictionary<(string Attack, string Defense), double> GetDefenseMatchupsForTypes(
        IEnumerable<string> defenseTypes)
    {
        var types = defenseTypes.Select(t => t.ToLowerInvariant()).ToList();
        var placeholders = string.Join(",", types.Select((_, i) => $"$d{i}"));

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT AttackType, DefenseType, Multiplier
            FROM   TypeEffectiveness
            WHERE  DefenseType IN ({placeholders})
            """;
        for (int i = 0; i < types.Count; i++)
            cmd.Parameters.AddWithValue($"$d{i}", types[i]);

        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<(string, string), double>();
        while (r.Read())
            dict[(r.GetString(0), r.GetString(1))] = r.GetDouble(2);
        return dict;
    }

    /// <summary>
    /// Query bulk de ofensa: retorna todas las excepciones (factor != 1)
    /// donde cualquiera de los tipos propios ataca a otro tipo.
    /// Clave: (ownType, defenseType) → multiplier
    /// Una sola query para todos los tipos propios del Pokémon.
    /// </summary>
    public Dictionary<(string OwnType, string Defense), double> GetOffenseMatchupsForTypes(
        IEnumerable<string> ownTypes)
    {
        var types = ownTypes.Select(t => t.ToLowerInvariant()).ToList();
        var placeholders = string.Join(",", types.Select((_, i) => $"$a{i}"));

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT AttackType, DefenseType, Multiplier
            FROM   TypeEffectiveness
            WHERE  AttackType IN ({placeholders})
            """;
        for (int i = 0; i < types.Count; i++)
            cmd.Parameters.AddWithValue($"$a{i}", types[i]);

        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<(string, string), double>();
        while (r.Read())
            dict[(r.GetString(0), r.GetString(1))] = r.GetDouble(2);
        return dict;
    }

    /// <summary>
    /// Retorna el factor de todos los tipos defensores para un tipo
    /// atacante dado, incluyendo los neutros (factor 1).
    /// Equivale a:
    ///   SELECT all_types.DefenseType, IFNULL(te.Multiplier, 1) as Factor
    ///   FROM   (los 18 tipos) all_types
    ///   LEFT   JOIN TypeEffectiveness te
    ///          ON te.AttackType = @atk AND te.DefenseType = all_types.DefenseType
    /// pero resuelto en memoria para evitar tener una tabla auxiliar de tipos.
    /// </summary>
    public Dictionary<string, double> GetAllMultipliersForAttacker(string attackType)
    {
        var atk = attackType.ToLowerInvariant();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DefenseType, Multiplier
            FROM   TypeEffectiveness
            WHERE  AttackType = $atk
            """;
        cmd.Parameters.AddWithValue("$atk", atk);
        using var r = cmd.ExecuteReader();

        // Inicializar todos los tipos con factor 1
        var result = new Dictionary<string, double>
        {
            ["normal"]   = 1.0, ["fire"]     = 1.0, ["water"]    = 1.0,
            ["electric"] = 1.0, ["grass"]    = 1.0, ["ice"]      = 1.0,
            ["fighting"] = 1.0, ["poison"]   = 1.0, ["ground"]   = 1.0,
            ["flying"]   = 1.0, ["psychic"]  = 1.0, ["bug"]      = 1.0,
            ["rock"]     = 1.0, ["ghost"]    = 1.0, ["dragon"]   = 1.0,
            ["dark"]     = 1.0, ["steel"]    = 1.0, ["fairy"]    = 1.0,
        };
        // Sobreescribir con las excepciones reales
        while (r.Read())
            result[r.GetString(0)] = r.GetDouble(1);

        return result;
    }

    /// <summary>
    /// Retorna el learnset completo de una especie para un juego dado.
    /// Incluye todos los métodos (levelup, egg, tm, tutor, etc.).
    ///
    /// <paramref name="formId"/> (opcional, Fase 2 de formas): si se pasa, restringe a esa forma
    /// (<c>Learnsets.FormId</c> coincide con el índice de forma de PKHeX.Core para las formas
    /// regionales — verificado con Marowak/Slowbro/Darmanitan/Vulpix de Alola/Galar). Si esa
    /// forma no tiene NINGUNA fila en ese juego, cae al comportamiento de siempre (sin filtro)
    /// en vez de devolver una lista vacía. Con <c>null</c> (default) nada cambia respecto a
    /// antes: es la solución parcial a la TRAMPA #30, usada solo donde se conoce la forma.
    /// </summary>
    public List<LearnsetDbEntry> GetLearnsets(int speciesId, int gameId, int? formId = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT l.LearnMethod, l.LearnLevel, m.Name,
                   m.TypeName, m.DamageClass, m.Power, l.LearnItem, m.MoveId
            FROM   Learnsets l
            JOIN   Moves m ON l.MoveId = m.MoveId
            WHERE  l.SpeciesId = $sid AND l.GameId = $gid
              AND  ($fid IS NULL OR l.FormId = $fid)
            ORDER  BY l.LearnMethod, l.LearnLevel, m.Name
            """;
        cmd.Parameters.AddWithValue("$sid", speciesId);
        cmd.Parameters.AddWithValue("$gid", gameId);
        cmd.Parameters.AddWithValue("$fid", (object?)formId ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<LearnsetDbEntry>();
        while (r.Read())
            list.Add(new LearnsetDbEntry
            {
                LearnMethod = r.IsDBNull(0) ? string.Empty : r.GetString(0),
                LearnLevel  = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                MoveName    = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                TypeName    = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                DamageClass = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                Power       = r.IsDBNull(5) ? null : (int?)r.GetInt32(5),
                // "TM06"/"TR00" para método tm/tr — NULL para el resto (egg, tutor, etc.).
                // Es la fuente para mostrar el número de MT/MO en vez del nivel en esos casos.
                LearnItem   = r.IsDBNull(6) ? null : r.GetString(6),
                // Id de movimiento (mismo espacio que pkm.MoveN / GameStrings.Move): permite mostrar
                // el nombre en el idioma de los datos y editar por id (plan i18n, Fase 3 tanda 2).
                MoveId      = r.IsDBNull(7) ? 0 : r.GetInt32(7),
            });
        // Forma pedida sin filas propias en este juego -> comportamiento previo (sin filtro).
        if (list.Count == 0 && formId.HasValue)
            return GetLearnsets(speciesId, gameId);
        return list;
    }

    // ═════════════════════════════════════════════════════════════════
    //  POKÉDEX — módulo álbum (género/hábitat/huevo, evolución, flavor text)
    // ═════════════════════════════════════════════════════════════════

    /// <summary>Género, hábitat y grupos huevo de una especie base (1-1025). Consulta
    /// liviana aparte de GetSpecies (que ya está cacheado y usado en todo el editor) para
    /// no tocar su forma ni su caché — este módulo solo necesita estos 5 campos, no los
    /// stats/tipos/habilidades completos.</summary>
    public DbSpeciesOrigin? GetSpeciesOrigin(int speciesId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT GenusEs, GenusEn, Habitat, EggGroup1, EggGroup2
            FROM   Species
            WHERE  SpeciesId = $id
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new DbSpeciesOrigin
        {
            // 127/1025 especies no tienen género en español en la fuente (PokeAPI no
            // tradujo especies nuevas todavía, ej. Pecharunt) — fallback a inglés acá
            // mismo para que el ViewModel nunca tenga que saber de este hueco.
            GenusEs    = r.IsDBNull(0) ? (r.IsDBNull(1) ? null : r.GetString(1)) : r.GetString(0),
            GenusEn    = r.IsDBNull(1) ? null : r.GetString(1),
            Habitat    = r.IsDBNull(2) ? null : r.GetString(2),
            EggGroup1  = r.IsDBNull(3) ? null : r.GetString(3),
            EggGroup2  = r.IsDBNull(4) ? null : r.GetString(4),
        };
    }

    /// <summary>Texto de Pokédex para una especie/idioma/juego exactos. Devuelve null si
    /// no hay fila — puede pasar porque el juego no está en la fuente (Legends Z-A, BD/SP)
    /// o porque esa especie puntual no estaba en la dex regional de ese juego (frecuente en
    /// Escarlata/Púrpura, cobertura parcial en la fuente). Ambos casos se resuelven igual:
    /// "sin datos de origen", no hay necesidad de distinguirlos en el ViewModel.</summary>
    public string? GetFlavorText(int speciesId, string lang, string game)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT Text FROM SpeciesFlavorText
            WHERE  SpeciesId = $id AND Lang = $lang AND Game = $game
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);
        cmd.Parameters.AddWithValue("$lang", lang);
        cmd.Parameters.AddWithValue("$game", game);
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    /// <summary>Previa directa de una especie (a lo sumo una — no existe en Pokémon una
    /// especie con más de un origen evolutivo, verificado sobre los 484 registros reales
    /// al armar esta tabla). Null si es la base de su familia.</summary>
    public DbEvolutionEdge? GetEvolutionInto(int speciesId)
        => GetEvolutionEdges("ToSpeciesId", "FromSpeciesId", speciesId).FirstOrDefault();

    /// <summary>Evoluciones directas de una especie (puede haber varias — Eevee tiene 8,
    /// Tyrogue y Applin 3 cada uno). Lista vacía si es una punta final de la cadena.</summary>
    public List<DbEvolutionEdge> GetEvolutionsFrom(int speciesId)
        => GetEvolutionEdges("FromSpeciesId", "ToSpeciesId", speciesId);

    private List<DbEvolutionEdge> GetEvolutionEdges(string filterColumn, string otherColumn, int speciesId,
        string chainsTable = "EvolutionChains", string conditionsTable = "EvolutionConditions")
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT EvolutionId, FromSpeciesId, ToSpeciesId
            FROM   {chainsTable}
            WHERE  {filterColumn} = $id
            """;
        cmd.Parameters.AddWithValue("$id", speciesId);

        var edges = new List<DbEvolutionEdge>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                edges.Add(new DbEvolutionEdge
                {
                    EvolutionId   = r.GetInt32(0),
                    FromSpeciesId = r.GetInt32(1),
                    ToSpeciesId   = r.GetInt32(2),
                });
        }

        // EvolutionConditions es EAV (una fila por tipo de condición) — se resuelve
        // aparte por cada arista encontrada arriba en vez de un JOIN, porque el número de
        // condiciones por arista varía bastante (desde 1 hasta 6, ver ConditionType real
        // en la DB) y así queda un diccionario limpio por arista sin filas repetidas.
        foreach (var edge in edges)
        {
            using var condCmd = _conn.CreateCommand();
            condCmd.CommandText = $"""
                SELECT ConditionType, ConditionValue
                FROM   {conditionsTable}
                WHERE  EvolutionId = $eid
                ORDER  BY ConditionId
                """;
            condCmd.Parameters.AddWithValue("$eid", edge.EvolutionId);

            var rows = new List<(string Type, string Value)>();
            using (var cr = condCmd.ExecuteReader())
                while (cr.Read())
                    rows.Add((cr.GetString(0), cr.IsDBNull(1) ? "" : cr.GetString(1)));

            foreach (var (type, value) in conditionsTable == "EvolutionConditions" ? DropVariantSets(rows) : rows)
                edge.Conditions[type] = value;
        }

        return edges;
    }

    /// <summary>En EvolutionConditions una arista puede traer VARIOS conjuntos de condiciones apilados
    /// (los "evolution_details" de PokeAPI): el de la especie base y los de sus variantes regionales
    /// (Sandshrew → Sandslash: nivel 22 y, aparte, <c>base_form=sandshrew-alola</c> + Piedra Hielo). Al
    /// colapsarlos en un diccionario, el último pisaba al primero y la rama base mostraba la condición de
    /// la variante. Los conjuntos de variante (con <c>base_form</c> o <c>region</c>) tienen su propia fila
    /// en FormEvolutionChains, así que acá se descartan; si TODOS los conjuntos son de variante
    /// (Qwilfish → Overqwil) se conservan. Los conjuntos que quedan siguen colapsándose como antes.
    /// Cada conjunto lista sus tipos en orden alfabético, así que uno nuevo empieza cuando el orden se
    /// reinicia (verificado en las 484 aristas de la DB: 42 con más de un conjunto, 0 divisiones falsas).</summary>
    private static List<(string Type, string Value)> DropVariantSets(List<(string Type, string Value)> rows)
    {
        var sets = new List<List<(string Type, string Value)>>();
        string? previous = null;
        foreach (var row in rows)
        {
            if (previous == null || string.CompareOrdinal(row.Type, previous) <= 0) sets.Add(new());
            sets[^1].Add(row);
            previous = row.Type;
        }
        if (sets.Count < 2) return rows;

        static bool IsVariant(List<(string Type, string Value)> set)
            => set.Any(r => (r.Type == "base_form" || r.Type == "region") && r.Value is not ("" or "False"));

        var kept = sets.Where(set => !IsVariant(set)).ToList();
        return kept.Count == 0 ? rows : kept.SelectMany(set => set).ToList();
    }

    // ── Evoluciones por forma (FormEvolutionChains / FormEvolutionConditions) ──
    // Misma estructura que EvolutionChains/EvolutionConditions, pero SpeciesId puede ser una fila
    // de forma de Species (> 10000, p. ej. 10100 = raichu-alola) en cualquiera de los dos lados.
    // Se crean con pokemon-database/hotfixes fix_form_data; si la DB todavía no las tiene, estos
    // métodos devuelven lista vacía en vez de lanzar.

    /// <summary>Previa directa de una fila de Species (base o de forma) según las evoluciones por
    /// forma. Null si no hay ninguna registrada. Para la previa "normal" ver
    /// <see cref="GetEvolutionInto"/>.</summary>
    public DbEvolutionEdge? GetFormEvolutionInto(int speciesId)
        => GetFormEvolutionEdges("ToSpeciesId", "FromSpeciesId", speciesId).FirstOrDefault();

    /// <summary>Evoluciones directas de una fila de Species (base o de forma) según las evoluciones
    /// por forma (Meowth de Galar → Perrserker, Slowpoke de Galar → Slowbro/Slowking de Galar...).</summary>
    public List<DbEvolutionEdge> GetFormEvolutionsFrom(int speciesId)
        => GetFormEvolutionEdges("FromSpeciesId", "ToSpeciesId", speciesId);

    private List<DbEvolutionEdge> GetFormEvolutionEdges(string filterColumn, string otherColumn, int speciesId)
    {
        try
        {
            return GetEvolutionEdges(filterColumn, otherColumn, speciesId,
                "FormEvolutionChains", "FormEvolutionConditions");
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return new List<DbEvolutionEdge>();   // DB sin el hotfix aplicado
        }
    }

    /// <summary>
    /// Especies que el selector de creación debe ocultar para un juego (Status = 'uncreatable' en
    /// <c>SpeciesGameAvailability</c>, hotfix fix_species_availability). Un juego sin filas
    /// (Gen 1-7, BD/SP) devuelve un set vacío. Devuelve <c>null</c> si la DB no tiene la tabla
    /// todavía (hotfix sin aplicar) o no está disponible: el llamador usa entonces su respaldo.
    /// </summary>
    public HashSet<int>? GetUncreatableSpecies(int gameId)
    {
        if (!IsAvailable) return null;
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT SpeciesId
                FROM   SpeciesGameAvailability
                WHERE  GameId = $gid AND Status = 'uncreatable'
                """;
            cmd.Parameters.AddWithValue("$gid", gameId);
            using var r = cmd.ExecuteReader();
            var set = new HashSet<int>();
            while (r.Read()) set.Add(r.GetInt32(0));
            return set;
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return null;   // DB sin el hotfix aplicado
        }
    }

    /// <summary>Formas (especie, forma) que <c>PokemonService.BuildPokemon</c> no deja legales en el juego
    /// (tabla <c>FormGameAvailability</c>, Status = 'uncreatable'). Null si la DB no tiene el hotfix
    /// (<c>fix_form_availability</c>): el llamador usa el JSON embebido.</summary>
    public HashSet<(int Species, int Form)>? GetUncreatableForms(int gameId)
        => GetFormsWithStatus("uncreatable", gameId);

    /// <summary>Formas (especie, forma) de <c>FormGameAvailability</c> con el Status dado; con
    /// <paramref name="gameId"/> null, las de todos los juegos. Null si la tabla no existe.</summary>
    public HashSet<(int Species, int Form)>? GetFormsWithStatus(string status, int? gameId)
    {
        if (!IsAvailable) return null;
        try
        {
            // Conexión propia: AlternateFormCatalog.All() se llama desde el prefetch en background del
            // selector (ver SpeciesPickerViewModel.PreloadInBackground), y _conn no admite uso concurrente.
            using var conn = OpenSecondaryConnection();
            if (conn == null) return null;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = gameId == null
                ? "SELECT DISTINCT SpeciesId, FormId FROM FormGameAvailability WHERE Status = $st"
                : "SELECT SpeciesId, FormId FROM FormGameAvailability WHERE Status = $st AND GameId = $gid";
            cmd.Parameters.AddWithValue("$st", status);
            if (gameId != null) cmd.Parameters.AddWithValue("$gid", gameId.Value);
            using var r = cmd.ExecuteReader();
            var set = new HashSet<(int, int)>();
            while (r.Read()) set.Add((r.GetInt32(0), r.GetInt32(1)));
            return set;
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return null;   // DB sin el hotfix aplicado
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  IDisposable
    // ═════════════════════════════════════════════════════════════════
    public void Dispose()
    {
        _conn.Dispose();
        _instance = null;
    }
}

// ═════════════════════════════════════════════════════════════════════
//  DTOs
// ═════════════════════════════════════════════════════════════════════

public sealed class SpeciesSummaryDbEntry
{
    public int          Id    { get; set; }
    public string       Name  { get; set; } = string.Empty;
    public List<string> Types { get; set; } = new();
}

public sealed class DbSpeciesEntry
{
    public int              Id          { get; set; }
    public string           Name        { get; set; } = string.Empty;
    public int              BaseHp      { get; set; }
    public int              BaseAtk     { get; set; }
    public int              BaseDef     { get; set; }
    public int              BaseSpa     { get; set; }
    public int              BaseSpd     { get; set; }
    public int              BaseSpe     { get; set; }
    /// <summary>Stat Especial único de Gen 1 (ataque y defensa especiales eran uno solo); null fuera de las 151 de Kanto o sin el hotfix.</summary>
    public int?             BaseSpecialGen1 { get; set; }
    public double           Height      { get; set; }
    public double           Weight      { get; set; }
    public int?             CaptureRate { get; set; }
    public string           GrowthRate  { get; set; } = string.Empty;
    public List<string>     Types       { get; set; } = new();
    public List<DbAbilitySlot> Abilities { get; set; } = new();

    public int BaseTotal => BaseHp + BaseAtk + BaseDef + BaseSpa + BaseSpd + BaseSpe;
}

public sealed class DbMoveEntry
{
    public int     Id          { get; set; }
    public string  Name        { get; set; } = string.Empty;
    public string  TypeName    { get; set; } = string.Empty;
    public string  DamageClass { get; set; } = string.Empty;
    public int?    Power       { get; set; }
    public int?    Accuracy    { get; set; }
    public int?    PP          { get; set; }
    public string  EffectText  { get; set; } = string.Empty;

    /// <summary>Tipo capitalizado para coincidir con GameStrings de PKHeX.</summary>
    public string TypeNameDisplay =>
        TypeName.Length == 0 ? string.Empty
        : char.ToUpper(TypeName[0]) + TypeName[1..];

    /// <summary>Clase de daño en español.</summary>
    public string DamageClassEs => DamageClass switch
    {
        "physical" => "Físico",
        "special"  => "Especial",
        "status"   => "Estado",
        _          => DamageClass,
    };
}

public sealed class DbAbilityEntry
{
    public int    Id              { get; set; }
    public string Name            { get; set; } = string.Empty;
    public string Description     { get; set; } = string.Empty;
    public string LongDescription { get; set; } = string.Empty;
}

public sealed class DbItemEntry
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category    { get; set; } = string.Empty;
    public int?   Cost        { get; set; }
}

/// <summary>Habilidad asociada a una especie con flag de habilidad oculta.</summary>
public readonly record struct DbAbilitySlot(string Name, bool IsHidden);

/// <summary>Género/hábitat/grupos huevo de una especie — módulo Pokédex.</summary>
public sealed class DbSpeciesOrigin
{
    /// <summary>Ya resuelto con fallback a inglés si falta el español en la fuente — ver
    /// nota en PokemonDatabase.GetSpeciesOrigin. Null solo si tampoco hay inglés.</summary>
    public string? GenusEs   { get; set; }
    public string? GenusEn   { get; set; }
    public string? Habitat   { get; set; }
    public string? EggGroup1 { get; set; }
    public string? EggGroup2 { get; set; }
}

/// <summary>Arista de evolución con sus condiciones en crudo (EAV tal como está en
/// EvolutionConditions) — formatear a texto legible es responsabilidad de la capa de
/// presentación (mismo criterio que LegalityArgumentFormatter/LegalityMessageMapper),
/// no de esta capa de acceso a datos.</summary>
public sealed class DbEvolutionEdge
{
    public int EvolutionId   { get; set; }
    public int FromSpeciesId { get; set; }
    public int ToSpeciesId   { get; set; }
    public Dictionary<string, string> Conditions { get; } = new();
}

/// <summary>Entrada de learnset devuelta por PokemonDatabase.</summary>
public sealed class LearnsetDbEntry
{
    public string LearnMethod { get; set; } = string.Empty;
    public int    LearnLevel  { get; set; }
    public string MoveName    { get; set; } = string.Empty;
    public string TypeName    { get; set; } = string.Empty;
    public string DamageClass { get; set; } = string.Empty;
    public int?   Power       { get; set; }
    public string? LearnItem  { get; set; }
    public int     MoveId     { get; set; }
}
