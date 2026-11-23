using Exxeguttor.App.Services;
using System.Reflection;
using System.Text.Json;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Lee movesets_all_combined.json con la estructura:
///
///   { "PokémonName": { "Source": { "genN": dataNode } } }
///
/// Fuentes (4, case-sensitive):
///   "Smogon"             → dict setName → { moves, item, ability, nature, evs, ivs, teratypes }
///                          TODOS los campos pueden ser string O array de alternativas
///   "Showdown"           → igual que Smogon pero sin arrays (siempre strings directos)
///   "Showdown Most Used" → siempre un solo set: { "Showdown Usage": { level, moves, ... } }
///   "Randbats"           → gen1: { level, moves, evs, ivs } (plano)
///                          gen2+: { level, abilities, items, evs, roles: { roleName: { moves, teraTypes, ... } } }
///
/// Pitfall crítico: "Randbats" tiene estructura completamente diferente a las otras fuentes.
/// NO se puede parsear con lógica genérica de "iterar setName → data".
/// Ver SPECIFICATION_movesets_all_combined.md para detalles.
/// </summary>
public class MovesetDatabase
{
    private static MovesetDatabase? _instance;
    public static MovesetDatabase Instance => _instance ??= new MovesetDatabase();

    // pokemonName (case-insensitive) → lista de sets de todas las fuentes/gens
    private readonly Dictionary<string, List<RecommendedSet>> _sets =
        new(StringComparer.OrdinalIgnoreCase);
    private bool   _loaded;
    private readonly object _lock = new();

    private MovesetDatabase() { }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Devuelve todos los sets de un Pokémon, opcionalmente filtrados por generación.
    /// pokemonName debe coincidir con el nombre tal como aparece en Pokémon Showdown.
    /// </summary>
    public List<RecommendedSet> GetSets(string pokemonName, int? generation = null)
    {
        EnsureLoaded();
        if (!_sets.TryGetValue(pokemonName, out var sets)) return [];
        return generation.HasValue
            ? sets.Where(s => s.Generation == generation.Value).ToList()
            : sets;
    }

    public static string FormatStats(Dictionary<string, int>? stats)
    {
        if (stats == null || stats.Count == 0) return string.Empty;
        return string.Join(" / ", stats
            .Where(kv => kv.Value > 0)
            .Select(kv => $"{kv.Value} {StatLabel(kv.Key)}"));
    }

    private static string StatLabel(string key) => key switch
    {
        "hp"  => "HP", "atk" => "Atk", "def" => "Def",
        "spa" => "SpA", "spd" => "SpD", "spe" => "Spe",
        _ => key,
    };

    // ── Lazy load ─────────────────────────────────────────────────────────────

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock) { if (_loaded) return; Load(); _loaded = true; }
    }

    private void Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(
                "Exxeguttor.UI.Assets.movesets_all_combined.json");
            if (stream == null) return;

            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            // Nivel 1: PokémonName
            foreach (var pokeProp in doc.RootElement.EnumerateObject())
            {
                var pokeName = pokeProp.Name;
                if (pokeProp.Value.ValueKind != JsonValueKind.Object) continue;

                // Nivel 2: Source
                foreach (var sourceProp in pokeProp.Value.EnumerateObject())
                {
                    var source = sourceProp.Name;
                    if (sourceProp.Value.ValueKind != JsonValueKind.Object) continue;

                    // Nivel 3: genN
                    foreach (var genProp in sourceProp.Value.EnumerateObject())
                    {
                        int gen = ParseGen(genProp.Name); // "gen1" → 1
                        if (gen == 0) continue;
                        if (genProp.Value.ValueKind != JsonValueKind.Object) continue;

                        // Nivel 4: dataNode — la forma varía según source
                        switch (source)
                        {
                            case "Smogon":
                            case "Showdown":
                                ParseSmogonOrShowdown(pokeName, source, gen, genProp.Value);
                                break;
                            case "Showdown Most Used":
                                ParseShowdownMostUsed(pokeName, gen, genProp.Value);
                                break;
                            case "Randbats":
                                ParseRandbats(pokeName, gen, genProp.Value);
                                break;
                            // fuente desconocida → ignorar silenciosamente
                        }
                    }
                }
            }
        }
        catch { /* JSON malformado o recurso faltante → conjuntos vacíos */ }
    }

    // ── Parsers por source ────────────────────────────────────────────────────

    /// <summary>
    /// Smogon / Showdown: { setName: { moves, item, ability, nature, evs, ivs, teratypes } }
    /// En Smogon los campos pueden ser string o array de alternativas.
    /// En Showdown siempre son strings directos.
    /// </summary>
    private void ParseSmogonOrShowdown(string pokeName, string source, int gen, JsonElement dataNode)
    {
        foreach (var setProp in dataNode.EnumerateObject())
        {
            var setData = setProp.Value;
            if (setData.ValueKind != JsonValueKind.Object) continue;

            var rs = new RecommendedSet
            {
                PokemonName = pokeName,
                SetName     = setProp.Name,
                Source      = source,
                Generation  = gen,
            };

            if (setData.TryGetProperty("moves",     out var movesEl))   rs.Moves     = ParseMoveSlots(movesEl);
            if (setData.TryGetProperty("item",      out var itemEl))    rs.Item      = PickFirst(itemEl);
            if (setData.TryGetProperty("ability",   out var abilEl))    rs.Ability   = PickFirst(abilEl);
            if (setData.TryGetProperty("nature",    out var natEl))     rs.Nature    = PickFirst(natEl);
            if (setData.TryGetProperty("teratypes", out var teraEl))    rs.TeraTypes = ParseStringList(teraEl);
            if (setData.TryGetProperty("evs",       out var evsEl))     rs.Evs       = ParseStatDict(evsEl);
            if (setData.TryGetProperty("ivs",       out var ivsEl))     rs.Ivs       = ParseStatDict(ivsEl);

            AddSet(pokeName, rs);
        }
    }

    /// <summary>
    /// Showdown Most Used: { "Showdown Usage": { level, moves, ability, item, nature, evs, ivs } }
    /// Siempre un único set por gen, sin alternativas.
    /// </summary>
    private void ParseShowdownMostUsed(string pokeName, int gen, JsonElement dataNode)
    {
        if (!dataNode.TryGetProperty("Showdown Usage", out var setData)) return;
        if (setData.ValueKind != JsonValueKind.Object) return;

        var rs = new RecommendedSet
        {
            PokemonName = pokeName,
            SetName     = "Showdown Usage",
            Source      = "Showdown Most Used",
            Generation  = gen,
        };

        if (setData.TryGetProperty("level",   out var lvEl))   rs.Level   = lvEl.GetInt32();
        if (setData.TryGetProperty("moves",   out var movesEl)) rs.Moves  = ParseMoveSlots(movesEl);
        if (setData.TryGetProperty("item",    out var itemEl))  rs.Item   = PickFirst(itemEl);
        if (setData.TryGetProperty("ability", out var abilEl))  rs.Ability = PickFirst(abilEl);
        if (setData.TryGetProperty("nature",  out var natEl))   rs.Nature  = PickFirst(natEl);
        if (setData.TryGetProperty("evs",     out var evsEl))   rs.Evs    = ParseStatDict(evsEl);
        if (setData.TryGetProperty("ivs",     out var ivsEl))   rs.Ivs    = ParseStatDict(ivsEl);

        AddSet(pokeName, rs);
    }

    /// <summary>
    /// Randbats gen1: { level, moves, evs, ivs }  — plano, moves = pool completo
    /// Randbats gen2+: { level, abilities, items, evs, roles: { roleName: { moves, teraTypes, ... } } }
    ///
    /// IMPORTANTE: NO tiene la forma setName → data. Hay que distinguir gen1 vs gen2+
    /// comprobando si existe la clave "roles".
    /// </summary>
    private void ParseRandbats(string pokeName, int gen, JsonElement dataNode)
    {
        int level = dataNode.TryGetProperty("level", out var lvEl) ? lvEl.GetInt32() : 0;

        if (dataNode.TryGetProperty("roles", out var rolesEl) &&
            rolesEl.ValueKind == JsonValueKind.Object)
        {
            // gen2+ — un RecommendedSet por rol
            foreach (var roleProp in rolesEl.EnumerateObject())
            {
                var roleData = roleProp.Value;
                if (roleData.ValueKind != JsonValueKind.Object) continue;

                var rs = new RecommendedSet
                {
                    PokemonName = pokeName,
                    SetName     = roleProp.Name,
                    Source      = "Randbats",
                    Generation  = gen,
                    IsRandbats  = true,
                    Level       = level,
                };

                if (roleData.TryGetProperty("moves",     out var movesEl))  rs.Moves     = ParseMoveSlots(movesEl);
                if (roleData.TryGetProperty("abilities", out var abilsEl))  rs.Ability   = PickFirst(abilsEl);
                if (roleData.TryGetProperty("items",     out var itemsEl))  rs.Item      = PickFirst(itemsEl);
                if (roleData.TryGetProperty("teraTypes", out var teraEl))   rs.TeraTypes = ParseStringList(teraEl);
                if (roleData.TryGetProperty("evs",       out var evsEl))    rs.Evs       = ParseStatDict(evsEl);
                if (roleData.TryGetProperty("ivs",       out var ivsEl))    rs.Ivs       = ParseStatDict(ivsEl);

                AddSet(pokeName, rs);
            }
        }
        else
        {
            // gen1 — set plano único; moves = pool completo (puede ser >4)
            var rs = new RecommendedSet
            {
                PokemonName = pokeName,
                SetName     = "Randbats",
                Source      = "Randbats",
                Generation  = gen,
                IsRandbats  = true,
                Level       = level,
            };

            if (dataNode.TryGetProperty("moves", out var movesEl)) rs.Moves = ParseMoveSlots(movesEl);
            if (dataNode.TryGetProperty("evs",   out var evsEl))   rs.Evs   = ParseStatDict(evsEl);
            if (dataNode.TryGetProperty("ivs",   out var ivsEl))   rs.Ivs   = ParseStatDict(ivsEl);

            AddSet(pokeName, rs);
        }
    }

    // ── Parse helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parsea un array de slots de movimientos.
    /// Cada slot puede ser un string ("Flare Blitz") o un array de alternativas (["Fire Blast", "Flamethrower"]).
    /// Siempre toma la primera alternativa.
    /// </summary>
    private static List<string> ParseMoveSlots(JsonElement el)
    {
        var result = new List<string>(6);
        if (el.ValueKind != JsonValueKind.Array) return result;

        foreach (var slot in el.EnumerateArray())
        {
            if (slot.ValueKind == JsonValueKind.String)
                result.Add(slot.GetString() ?? "");
            else if (slot.ValueKind == JsonValueKind.Array)
            {
                // array de alternativas → tomar el primero
                var first = slot.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String)
                    result.Add(first.GetString() ?? "");
            }
        }
        return result;
    }

    /// <summary>
    /// Si el elemento es un string lo devuelve; si es un array devuelve el primer elemento.
    /// Usado para item / ability / nature / abilities / items en Randbats.
    /// </summary>
    private static string PickFirst(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        if (el.ValueKind == JsonValueKind.Array)
        {
            var first = el.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.String ? first.GetString() ?? "" : "";
        }
        return "";
    }

    /// <summary>
    /// Parsea un campo que puede ser string o array de strings (ej. teratypes / teraTypes).
    /// </summary>
    private static List<string> ParseStringList(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            var v = el.GetString();
            return v != null ? [v] : [];
        }
        if (el.ValueKind == JsonValueKind.Array)
            return el.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToList();
        return [];
    }

    private static Dictionary<string, int> ParseStatDict(JsonElement el)
    {
        var dict = new Dictionary<string, int>();
        if (el.ValueKind != JsonValueKind.Object) return dict;
        foreach (var kv in el.EnumerateObject())
            if (kv.Value.ValueKind == JsonValueKind.Number)
                dict[kv.Name] = kv.Value.GetInt32();
        return dict;
    }

    private static int ParseGen(string genKey) =>
        genKey.StartsWith("gen") && int.TryParse(genKey[3..], out var n) ? n : 0;

    private void AddSet(string pokemonName, RecommendedSet set)
    {
        if (!_sets.TryGetValue(pokemonName, out var list))
            _sets[pokemonName] = list = [];
        list.Add(set);
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

public class RecommendedSet
{
    public string                  PokemonName { get; set; } = string.Empty;
    public string                  SetName     { get; set; } = string.Empty;

    /// <summary>"Smogon" | "Showdown" | "Showdown Most Used" | "Randbats"</summary>
    public string                  Source      { get; set; } = string.Empty;
    public int                     Generation  { get; set; }
    public int                     Level       { get; set; }
    public bool                    IsRandbats  { get; set; }

    /// <summary>Movimientos ya resueltos (primera alternativa cuando hay arrays).</summary>
    public List<string>            Moves       { get; set; } = [];
    public string                  Ability     { get; set; } = string.Empty;
    public string                  Item        { get; set; } = string.Empty;
    public string                  Nature      { get; set; } = string.Empty;
    public List<string>            TeraTypes   { get; set; } = [];
    public Dictionary<string, int> Evs         { get; set; } = [];
    public Dictionary<string, int> Ivs         { get; set; } = [];

    // ── Propiedades calculadas para la UI ─────────────────────────────────────

    public string EvsFormatted  => MovesetDatabase.FormatStats(Evs);
    public string IvsFormatted  => MovesetDatabase.FormatStats(Ivs);

    public string TeraTypeDisplay  => TeraTypes.Count > 0
        ? string.Join(", ", TeraTypes.Select(TypeNames.Localize)) : string.Empty;

    // Texto a mostrar en el idioma de los datos. Las llaves (Ability/Item/Moves) siguen en inglés
    // porque así vienen del JSON de Showdown/Smogon; los nombres localizados se calculan al leer (nunca se
    // cachean: la instancia vive en MovesetDatabase y el idioma puede cambiar en cualquier momento).
    public string AbilityDisplay => GameNames.Ability(Ability);
    public string ItemDisplay    => GameNames.Item(Item);

    // Alias para compatibilidad con binding en PokemonStatsView.axaml
    public string TeraTypesDisplay => TeraTypeDisplay;

    public string SourceDisplay => Source switch
    {
        "Smogon"             => "Smogon",
        "Showdown"           => "Showdown",
        "Showdown Most Used" => "Usage",
        "Randbats"           => "Randbats",
        _                    => Source,
    };

    public string Label => Generation > 0
        ? $"Gen {Generation}  —  {SetName}"
        : SetName;

    public bool HasAbility   => !string.IsNullOrEmpty(Ability);
    public bool HasItem      => !string.IsNullOrEmpty(Item);

    /// <summary>Descripción del objeto para el tooltip en el tab Sets — misma fuente
    /// (ItemDatabase, pokemon.db) que ya usa el combo de Held Item del tab Info.</summary>
    public string ItemDescription => HasItem
        ? ItemDatabase.Instance.GetByName(Item)?.Description ?? string.Empty
        : string.Empty;
    public bool HasNature    => !string.IsNullOrEmpty(Nature);
    public bool HasCustomEvs => Evs.Count > 0;
    public bool HasCustomIvs => Ivs.Count > 0;
    public bool HasTeraType  => TeraTypes.Count > 0;
    public bool HasLevel     => Level > 0;

    /// <summary>
    /// Movimientos enriquecidos con datos de tipo/clase/poder desde MoveDatabase.
    /// Se resuelven lazily la primera vez que se accede.
    /// </summary>
    private List<SetMoveEntry>? _resolvedMoves;
    public List<SetMoveEntry> ResolvedMoves =>
        _resolvedMoves ??= Moves
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m =>
            {
                var entry = MoveDatabase.Instance.GetByName(m);
                return new SetMoveEntry
                {
                    EnglishName = m,
                    TypeName    = entry != null ? CapitalizeType(entry.Type) : string.Empty,
                    DamageClass = LegacyDamageClass.Resolve(entry?.DamageClass, entry?.Type),
                    Power       = entry?.Power,
                    Description = entry?.EffectEntries ?? string.Empty,
                };
            })
            .ToList();

    private static string CapitalizeType(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}

// ── Set move DTO ──────────────────────────────────────────────────────────────

/// <summary>Movimiento dentro de un set recomendado, con datos enriquecidos de la DB.</summary>
public sealed class SetMoveEntry
{
    /// <summary>Nombre inglés del JSON de Showdown/Smogon: llave para buscar tipo/poder en pokemon.db.</summary>
    public string  EnglishName { get; set; } = string.Empty;

    /// <summary>Nombre a mostrar, en el idioma de los datos activo (calculado al leer, no cacheado).</summary>
    public string  MoveName    => MoveNames.Localize(EnglishName);

    public string  TypeName    { get; set; } = string.Empty;
    public string  DamageClass { get; set; } = string.Empty;
    public int?    Power       { get; set; }
    public string  Description { get; set; } = string.Empty;

    public string PowerDisplay => Power.HasValue ? Power.Value.ToString() : "—";
}
