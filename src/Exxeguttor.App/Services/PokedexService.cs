using System.Reflection;
using System.Text.Json;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>Una tarjeta del álbum con su estado de posesión para el save actual. Sigue siendo UNA
/// tarjeta por especie (así guarda el save la Pokédex), pero si la especie tiene una forma regional
/// que existe en el juego cargado, la tarjeta es la de esa forma (decisión de diseño del 11 oct:
/// "la forma de la generación"; Gen 7 muestra Exeggutor de Alola, no el de Kanto).</summary>
public sealed class PokedexAlbumEntry
{
    /// <summary>N.º de Pokédex nacional (siempre el de la especie base).</summary>
    public int          SpeciesId { get; set; }
    /// <summary>0 = especie base; &gt; 0 = forma regional que muestra la tarjeta.</summary>
    public byte         Form      { get; set; }
    /// <summary>Id de la fila de <c>Species</c> con los datos mostrados: la base o la fila de la forma (&gt; 10000).</summary>
    public int          DbSpeciesId { get; set; }
    /// <summary>Nombre de pokemon.db de la especie BASE (la UI arma el nombre mostrado con PKHeX + la forma).</summary>
    public string        Name     { get; set; } = string.Empty;
    public List<string>  Types    { get; set; } = new();
    public bool           Owned    { get; set; }
}

/// <summary>Un nodo de la cadena evolutiva ya con el texto de condición formateado —
/// listo para bindear directo en la UI, sin que el ViewModel toque
/// EvolutionConditionFormatter.</summary>
/// <param name="SpeciesId">Id de la fila de Species (base o forma, &gt; 10000).</param>
/// <param name="NationalId">N.º de Pokédex de la especie base (0 en los nodos de la API antigua: usar SpeciesId).</param>
/// <param name="Form">Forma regional del nodo (0 = base).</param>
public sealed record PokedexEvolutionNode(int SpeciesId, string Name, string ConditionText, int NationalId = 0, byte Form = 0);

/// <summary>Previa (a lo sumo una) + evoluciones directas (0 o más) de una especie.</summary>
public sealed class PokedexEvolutionFamily
{
    public PokedexEvolutionNode? Prev { get; set; }
    public List<PokedexEvolutionNode> Next { get; set; } = new();
    /// <summary>Otras ramas que salen de la misma previa (alternativas a la especie de la tarjeta). Solo
    /// las llena <see cref="PokedexService.GetAlbumEvolutionFamily"/>.</summary>
    public List<PokedexEvolutionNode> Siblings { get; set; } = new();
}

/// <summary>Género (con fallback a inglés ya resuelto) + hábitat + flavor text de una
/// especie para el juego cargado. FlavorText null si no hay dato — la UI muestra el
/// estado "sin datos de origen" en ese caso, sin distinguir por qué faltó.</summary>
public sealed class PokedexOriginInfo
{
    public string? Genus      { get; set; }
    public string? Habitat    { get; set; }
    public string? FlavorText { get; set; }
}

/// <summary>
/// Orquestador del módulo Pokédex (álbum de figuritas). Combina PokemonService (qué hay
/// en Caja/Equipo → posesión), PokemonDatabase (género/hábitat/evolución/flavor text) y
/// la lista embebida de disponibilidad restringida por juego (Espada/Escudo, Escarlata/
/// Púrpura, Legends Arceus, Legends Z-A — ver GameVersionMappings).
///
/// Sin estado de UI ni dependencia de Avalonia — el ViewModel de la vista es el único
/// responsable de paginar/cachear para pantalla.
/// </summary>
public sealed class PokedexService
{
    private readonly PokemonService _pokemonService;
    private readonly PokemonDatabase _db;

    // Lazy + double-check lock, mismo patrón que MovesetDatabase — es un JSON chico
    // (4 listas, ~1600 números en total) así que el costo de parsearlo una vez es
    // insignificante, pero no hay razón para hacerlo si el usuario nunca abre la Pokédex.
    private static Dictionary<string, HashSet<int>>? _restrictedDex;
    private static readonly object _restrictedDexLock = new();

    public PokedexService(PokemonService pokemonService, PokemonDatabase db)
    {
        _pokemonService = pokemonService;
        _db = db;
    }

    /// <summary>Todas las tarjetas del álbum para el save actual, en orden de dex nacional.
    /// Alcance: Id ≤ MaxSpeciesID menos las especies que no existen en ese juego, con la misma fuente
    /// que el selector de crear Pokémon (<see cref="CreatableSpecies.HiddenForCreation"/>: tabla
    /// <c>SpeciesGameAvailability</c> 'uncreatable' + toda especie ausente de la tabla personal del
    /// juego). Se muestran las que existen en el juego (native, present_illegal, dlc); las
    /// 'transfer_only' (p. ej. Sandshrew en Z-A) ya no, porque ese save no puede guardarlas. Si no hay información de disponibilidad para un juego restringido
    /// (DB sin la tabla), se usa la lista embebida <c>restricted_dex_availability.json</c>.</summary>
    public List<PokedexAlbumEntry> GetAlbumEntries(SaveFile save)
    {
        var owned = GetOwnedSpeciesIds(save);
        // Misma fuente que el selector de crear Pokémon: además de lo 'uncreatable' de la DB, se
        // ocultan las especies que la tabla personal del juego no tiene (p. ej. las 'transfer_only'
        // de Z-A, que no existen en ese save). hiddenDb solo decide el respaldo del JSON de abajo.
        var hiddenDb = CreatableSpecies.HiddenIn(save.Version);
        var hidden = CreatableSpecies.HiddenForCreation(save);
        var restrictedKey = GameVersionMappings.TryGetRestrictedDexKey(save.Version);

        // Formas regionales que REALMENTE existen en este juego (IsPresentInGame de PKHeX).
        // PROVISIONAL — caso por decidir: si una especie tiene varias formas regionales disponibles en el
        // mismo juego (p. ej. las 3 razas de Tauros de Paldea) se muestra la primera del catálogo.
        var regionalBySpecies = RegionalFormCatalog.AvailableIn(save)
            .GroupBy(f => f.Species)
            .ToDictionary(g => g.Key, g => g.First());

        // Formas no regionales con encuentro propio (Giratina Origen en Legends: Arceus, Zygarde en
        // Z-A...). Solo importan para especies cuya forma base no existe en el juego: ver abajo.
        var altBySpecies = AlternateFormCatalog.AvailableIn(save.Version)
            .Where(a => RegionalFormCatalog.Find(a.Species, a.Form) == null)
            .GroupBy(a => a.Species)
            .ToDictionary(g => g.Key, g => g.Min(a => a.Form));

        IEnumerable<int> speciesIds;
        if (hiddenDb.Count == 0 && restrictedKey != null)
        {
            // Respaldo: juego restringido sin datos de disponibilidad (ni en la DB ni en el JSON de no creables).
            var dex = LoadRestrictedDex();
            speciesIds = dex.TryGetValue(restrictedKey, out var set) ? set : Enumerable.Empty<int>();
        }
        else
        {
            // Una especie cuya forma BASE no es creable (Goodra en Legends: Arceus: solo existe la de Hisui)
            // sigue siendo una entrada del álbum si el juego tiene una forma suya: se muestra con esa forma.
            speciesIds = Enumerable.Range(1, save.MaxSpeciesID)
                .Where(id => !hidden.Contains(id) || regionalBySpecies.ContainsKey(id) || altBySpecies.ContainsKey(id));
        }

        var entries = new List<PokedexAlbumEntry>();
        foreach (var id in speciesIds.OrderBy(x => x))
        {
            var species = _db.GetSpecies(id);
            if (species == null) continue; // especie fuera de pokemon.db (no debería pasar, defensivo)

            byte form = 0;
            var dbId = id;
            var types = species.Types;
            byte? shownForm = null;
            if (regionalBySpecies.TryGetValue(id, out var regional))
                shownForm = regional.Form;
            else if (hidden.Contains(id) && altBySpecies.TryGetValue(id, out var altForm))
                shownForm = altForm;
            if (shownForm is { } sf)
            {
                var formDbId = SpeciesFormResolver.ResolveDbSpeciesId(id, sf);
                var formRow = formDbId != id ? _db.GetSpecies(formDbId) : null;
                if (formRow != null)
                {
                    form = sf;
                    dbId = formDbId;
                    types = formRow.Types;
                }
            }

            entries.Add(new PokedexAlbumEntry
            {
                SpeciesId   = id,
                Form        = form,
                DbSpeciesId = dbId,
                Name        = species.Name,
                Types       = types,
                Owned       = owned.Contains(id), // posesión por especie (cualquier forma)
            });
        }
        return entries;
    }

    /// <summary>Especies presentes ahora mismo en Caja o Equipo — "posesión" para el
    /// álbum es esto, no los flags de Pokédex del juego (ver justificación en spec de
    /// diseño: más frágil entre generaciones, no refleja saves editados a mano).</summary>
    private HashSet<int> GetOwnedSpeciesIds(SaveFile save)
    {
        var owned = new HashSet<int>();

        foreach (var pkm in _pokemonService.GetParty())
            if (pkm.Species > 0) owned.Add(pkm.Species);

        var boxCount = save.BoxCount;
        for (int box = 0; box < boxCount; box++)
            foreach (var pkm in _pokemonService.GetBox(box))
                if (pkm.Species > 0) owned.Add(pkm.Species);

        return owned;
    }

    // ── Cadena del álbum: todas las ramas, con formas regionales ─────────────────────────────
    private static Dictionary<int, RegionalForm>? _regionalByDbId;
    private static readonly object _regionalByDbIdLock = new();

    /// <summary>Fila de Species de cada forma regional del catálogo → (especie, forma). Sirve para
    /// traducir los ids de forma (&gt; 10000) de FormEvolutionChains a (n.º nacional, forma).</summary>
    private static Dictionary<int, RegionalForm> RegionalByDbId()
    {
        if (_regionalByDbId != null) return _regionalByDbId;
        lock (_regionalByDbIdLock)
        {
            if (_regionalByDbId != null) return _regionalByDbId;
            var map = new Dictionary<int, RegionalForm>();
            foreach (var f in RegionalFormCatalog.All)
            {
                var id = SpeciesFormResolver.ResolveDbSpeciesId(f.Species, f.Form);
                if (id != f.Species) map[id] = f;
            }
            return _regionalByDbId = map;
        }
    }

    /// <summary>Cadena evolutiva de la tarjeta del álbum: la previa, las evoluciones y las demás ramas
    /// que salen de la misma previa (<see cref="PokedexEvolutionFamily.Siblings"/>), mezclando
    /// <c>EvolutionChains</c> (especies base) y <c>FormEvolutionChains</c> (formas regionales).
    /// Un nodo de forma solo aparece si esa forma existe en el juego cargado; un nodo base se oculta si la
    /// especie tiene forma regional en el juego pero la forma base no (p. ej. Growlithe en Legends: Arceus).
    /// Los textos de condición salen en el idioma de la UI. Límite conocido: las ramas por formas NO
    /// regionales (Wormadam arena/basura, etc.) no se muestran todavía.</summary>
    public PokedexEvolutionFamily GetAlbumEvolutionFamily(int nationalId, byte form, SaveFile save, string lang)
    {
        var family = new PokedexEvolutionFamily();
        var available = new HashSet<int>(RegionalFormCatalog.AvailableIn(save)
            .Select(f => SpeciesFormResolver.ResolveDbSpeciesId(f.Species, f.Form)));
        var speciesWithRegional = new HashSet<int>(RegionalFormCatalog.AvailableIn(save).Select(f => f.Species));
        var selfDbId = SpeciesFormResolver.ResolveDbSpeciesId(nationalId, form);

        // Previa: una forma regional sale de FormEvolutionChains; una especie base, de EvolutionChains y,
        // si ahí no figura (p. ej. Perrserker, que viene de Meowth de Galar), de FormEvolutionChains.
        var prevEdge = selfDbId != nationalId
            ? _db.GetFormEvolutionInto(selfDbId)
            : _db.GetEvolutionInto(nationalId) ?? _db.GetFormEvolutionInto(nationalId);
        if (prevEdge != null)
            family.Prev = MakeNode(prevEdge.FromSpeciesId, ConditionText(prevEdge, selfDbId, lang), available, speciesWithRegional, save);

        foreach (var edge in EdgesFrom(selfDbId))
        {
            var node = MakeNode(edge.ToSpeciesId, ConditionText(edge, edge.ToSpeciesId, lang), available, speciesWithRegional, save);
            if (node != null && family.Next.All(n => n.SpeciesId != node.SpeciesId)) family.Next.Add(node);
        }

        if (prevEdge != null)
        {
            foreach (var edge in EdgesFrom(prevEdge.FromSpeciesId))
            {
                var node = MakeNode(edge.ToSpeciesId, ConditionText(edge, edge.ToSpeciesId, lang), available, speciesWithRegional, save);
                if (node != null && node.SpeciesId != selfDbId && family.Siblings.All(n => n.SpeciesId != node.SpeciesId))
                    family.Siblings.Add(node);
            }
        }

        return family;
    }

    /// <summary>Aristas que salen de una fila de Species: una forma solo tiene aristas de forma; una
    /// especie base tiene las suyas más las que van de ella a una forma (Exeggcute → Exeggutor de Alola).</summary>
    private IEnumerable<DbEvolutionEdge> EdgesFrom(int dbId)
    {
        if (dbId <= 10000)
            foreach (var e in _db.GetEvolutionsFrom(dbId)) yield return e;
        foreach (var e in _db.GetFormEvolutionsFrom(dbId)) yield return e;
    }

    /// <summary>Texto de la condición. Hacia una forma incluye la región ("Piedra Hoja · en Alola"); hacia una
    /// especie base la ignora, porque en pokemon.db las aristas base arrastran la condición de la variante regional.</summary>
    private static string ConditionText(DbEvolutionEdge edge, int targetDbId, string lang)
        => targetDbId > 10000
            ? EvolutionConditionFormatter.FormatForm(edge.Conditions, lang)
            : EvolutionConditionFormatter.FormatBase(edge.Conditions, lang);

    private PokedexEvolutionNode? MakeNode(int dbId, string conditionText, HashSet<int> availableForms,
        HashSet<int> speciesWithRegional, SaveFile save)
    {
        var national = dbId;
        byte form = 0;
        if (dbId > 10000)
        {
            // Solo formas regionales presentes en el juego (las demás formas con fila propia: pendiente).
            if (!availableForms.Contains(dbId) || !RegionalByDbId().TryGetValue(dbId, out var rf)) return null;
            national = rf.Species;
            form = rf.Form;
        }
        else if (speciesWithRegional.Contains(dbId) && !IsBaseFormPresent(save, dbId))
        {
            return null; // la especie solo existe en su forma regional en este juego
        }

        var species = _db.GetSpecies(national);
        return species == null ? null : new PokedexEvolutionNode(dbId, species.Name, conditionText, national, form);
    }

    private static bool IsBaseFormPresent(SaveFile save, int species)
    {
        try { return save.Personal.IsPresentInGame((ushort)species, 0); }
        catch (Exception) { return true; } // ante la duda, no se oculta la rama base
    }

    /// <summary>Previa + evoluciones directas de una especie, con el texto de condición
    /// ya formateado. Ambos lados pueden venir vacíos (especie sin previa y/o sin
    /// evolución) — la UI decide cómo mostrar eso, no esta capa.</summary>
    public PokedexEvolutionFamily GetEvolutionFamily(int speciesId)
    {
        var family = new PokedexEvolutionFamily();

        var prevEdge = _db.GetEvolutionInto(speciesId);
        if (prevEdge != null)
        {
            var prevSpecies = _db.GetSpecies(prevEdge.FromSpeciesId);
            if (prevSpecies != null)
                family.Prev = new PokedexEvolutionNode(
                    prevEdge.FromSpeciesId, prevSpecies.Name,
                    EvolutionConditionFormatter.Format(prevEdge.Conditions));
        }

        foreach (var edge in _db.GetEvolutionsFrom(speciesId))
        {
            var nextSpecies = _db.GetSpecies(edge.ToSpeciesId);
            if (nextSpecies == null) continue;
            family.Next.Add(new PokedexEvolutionNode(
                edge.ToSpeciesId, nextSpecies.Name,
                EvolutionConditionFormatter.Format(edge.Conditions)));
        }

        return family;
    }

    /// <summary>
    /// Igual que <see cref="GetEvolutionFamily(int)"/> pero consciente de la forma: para una forma
    /// con fila propia en pokemon.db (Meowth de Galar, Slowpoke de Galar, Rattata de Alola...)
    /// usa las evoluciones por forma (<c>FormEvolutionChains</c>), que sí distinguen "Slowbro de
    /// Galar" de "Slowbro". Para una forma sin fila propia (cosméticas, o la forma base) devuelve
    /// la cadena normal. Los <c>SpeciesId</c> de los nodos pueden ser filas de forma (&gt; 10000).
    /// </summary>
    public PokedexEvolutionFamily GetEvolutionFamily(int speciesId, byte form)
    {
        var dbId = SpeciesFormResolver.ResolveDbSpeciesId(speciesId, form);
        if (dbId == speciesId) return GetEvolutionFamily(speciesId);

        var family = new PokedexEvolutionFamily();

        var prevEdge = _db.GetFormEvolutionInto(dbId);
        if (prevEdge != null)
        {
            var prevSpecies = _db.GetSpecies(prevEdge.FromSpeciesId);
            if (prevSpecies != null)
                family.Prev = new PokedexEvolutionNode(
                    prevEdge.FromSpeciesId, prevSpecies.Name,
                    EvolutionConditionFormatter.Format(prevEdge.Conditions));
        }

        foreach (var edge in _db.GetFormEvolutionsFrom(dbId))
        {
            var nextSpecies = _db.GetSpecies(edge.ToSpeciesId);
            if (nextSpecies == null) continue;
            family.Next.Add(new PokedexEvolutionNode(
                edge.ToSpeciesId, nextSpecies.Name,
                EvolutionConditionFormatter.Format(edge.Conditions)));
        }

        return family;
    }

    /// <summary>Género/hábitat/flavor text para el juego cargado. lang: "es"/"en", mismo
    /// criterio que LocalizationService.CurrentLocale.</summary>
    public PokedexOriginInfo GetOriginInfo(int speciesId, GameVersion version, string lang)
    {
        var origin = _db.GetSpeciesOrigin(speciesId);
        var result = new PokedexOriginInfo
        {
            Genus   = lang == "es" ? origin?.GenusEs : origin?.GenusEn,
            Habitat = origin?.Habitat,
        };

        var slug = GameVersionMappings.TryGetFlavorTextSlug(version);
        if (slug != null)
            result.FlavorText = _db.GetFlavorText(speciesId, lang, slug);

        return result;
    }

    private static Dictionary<string, HashSet<int>> LoadRestrictedDex()
    {
        if (_restrictedDex != null) return _restrictedDex;
        lock (_restrictedDexLock)
        {
            if (_restrictedDex != null) return _restrictedDex;

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Exxeguttor.App.Assets.restricted_dex_availability.json")
                ?? throw new InvalidOperationException(
                    "No se encontró restricted_dex_availability.json embebido en el ensamblado principal.");

            var raw = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(stream)
                ?? new Dictionary<string, List<int>>();

            _restrictedDex = raw.ToDictionary(kv => kv.Key, kv => new HashSet<int>(kv.Value));
            return _restrictedDex;
        }
    }
}
