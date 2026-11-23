using Exxeguttor.App.Services;

namespace Exxeguttor.App.Services;

/// <summary>
/// Traduce el diccionario crudo de <see cref="DbEvolutionEdge.Conditions"/> (EAV, valores
/// en inglés tal como vienen de PokeAPI vía EvolutionConditions) a una única línea corta en el
/// idioma de la UI (es / en; cualquier otro idioma cae a inglés) para el reverso de la tarjeta
/// del álbum Pokédex. Los nombres de objetos, movimientos y especies salen de PKHeX por índice
/// (<see cref="EvolutionNameLocalizer"/>), no de tablas propias — mismo espíritu que
/// LegalityMessageMapper/LegalityArgumentFormatter (traducir identificadores técnicos a
/// texto legible, sin intentar ser exhaustivo con cada combinación posible de PKHeX.Core).
///
/// Best-effort por diseño: si aparece un ConditionType nuevo que esta clase no conoce
/// todavía, cae al fallback humanizado de <c>trigger</c> en vez de romper — igual que
/// LegalityMessageMapper con CheckIdentifier. Caveat de fidelidad heredado de la fuente:
/// Sylveon (known_move Encanto + amistad) solo trae min_happiness en PokeAPI, sin
/// known_move — ver screen-pokedex-album.md.
/// </summary>
public static class EvolutionConditionFormatter
{
    // Objetos de evolución más comunes — el resto cae al humanizado genérico (guiones →
    // espacios, capitalizado), que para nombres en inglés no traducidos es aceptable como
    // fallback pero no ideal; ampliar esta tabla si se nota alguno feo en la UI real.
    private static readonly Dictionary<string, string> ItemNamesEs = new()
    {
        ["water-stone"]      = "Piedra Agua",
        ["fire-stone"]       = "Piedra Fuego",
        ["thunder-stone"]    = "Piedra Trueno",
        ["leaf-stone"]       = "Piedra Hoja",
        ["moon-stone"]       = "Piedra Lunar",
        ["sun-stone"]        = "Piedra Solar",
        ["shiny-stone"]      = "Piedra Alba",
        ["dusk-stone"]       = "Piedra Noche",
        ["dawn-stone"]       = "Piedra Día",
        ["ice-stone"]        = "Piedra Hielo",
        ["kings-rock"]       = "Roca Dragón",
        ["metal-coat"]       = "Recubrimetal",
        ["dragon-scale"]     = "Escama Dragón",
        ["upgrade"]          = "Up-Grade",
        ["dubious-disc"]     = "Disco Extraño",
        ["protector"]        = "Protector",
        ["electirizer"]      = "Electrizador",
        ["magmarizer"]       = "Magmatizador",
        ["reaper-cloth"]     = "Paño Mortal",
        ["prism-scale"]      = "Escama Prisma",
        ["whipped-dream"]    = "Nata Chantillí",
        ["sachet"]           = "Perfume Saquito",
        ["strawberry-sweet"] = "Dulce Fresa",
        ["cracked-pot"]      = "Tetera Rota",
        ["chipped-pot"]      = "Tetera Melladita",
        ["black-augurite"]   = "Mineral Negro",
        ["peat-block"]       = "Bloque de Turba",
        ["galarica-cuff"]    = "Brazal Galarnip",
        ["galarica-wreath"]  = "Corona Galarnip",
        ["auspicious-armor"] = "Armadura Fasta",
        ["malicious-armor"]  = "Armadura Nefasta",
        ["masterpiece-teacup"] = "Taza Obra Maestra",
        ["unremarkable-teacup"] = "Taza Corriente",
        ["scroll-of-darkness"]  = "Rollo de las Tinieblas",
        ["scroll-of-waters"]    = "Rollo de las Aguas",
    };

    // ── Textos por idioma ────────────────────────────────────────────────────
    // La capa App no tiene LocalizationService (es de la UI): estos textos viven acá, con el mismo
    // contrato que PokedexService.GetOriginInfo(lang): "es" y "en"; cualquier otro idioma usa inglés.
    private static readonly Dictionary<string, (string Es, string En)> Texts = new()
    {
        ["level"]        = ("Nivel {0}", "Level {0}"),
        ["happiness"]    = ("Amistad alta", "High friendship"),
        ["affection"]    = ("Cariño alto", "High affection"),
        ["beauty"]       = ("Belleza alta", "High beauty"),
        ["tradeHolding"] = ("Intercambio llevando {0}", "Trade holding {0}"),
        ["tradeFor"]     = ("Intercambio por {0}", "Trade for {0}"),
        ["trade"]        = ("Intercambio", "Trade"),
        ["knownMove"]    = ("Conoce {0}", "Knows {0}"),
        ["knownType"]    = ("Conoce movimiento de tipo {0}", "Knows a {0}-type move"),
        ["usedMove"]     = ("Usar {0}", "Use {0}"),
        ["location"]     = ("Cerca de {0}", "Near {0}"),
        ["region"]       = ("En la región {0}", "In the {0} region"),
        ["regionSuffix"] = (" · en {0}", " · in {0}"),
        ["party"]        = ("Con {0} en el equipo", "With {0} in the party"),
        ["steps"]        = ("Caminar {0} pasos", "Walk {0} steps"),
        ["female"]       = ("Género: hembra", "Gender: female"),
        ["male"]         = ("Género: macho", "Gender: male"),
        ["atkGtDef"]     = ("Ataque > Defensa", "Attack > Defense"),
        ["atkLtDef"]     = ("Ataque < Defensa", "Attack < Defense"),
        ["atkEqDef"]     = ("Ataque = Defensa", "Attack = Defense"),
        ["atkVsDef"]     = ("Comparar Ataque/Defensa", "Compare Attack/Defense"),
        ["upsideDown"]   = ("Sostener la consola al revés", "Hold the console upside down"),
        ["form"]         = ("Forma: {0}", "Form: {0}"),
        ["day"]          = (", de día", ", during the day"),
        ["night"]        = (", de noche", ", at night"),
        ["unknown"]      = ("Condición desconocida", "Unknown condition"),
    };

    private static string T(string lang, string key, string? arg = null)
    {
        var (es, en) = Texts[key];
        var text = lang == "es" ? es : en;
        return arg is null ? text : string.Format(text, arg);
    }

    /// <summary>Línea corta ("Nivel 16", "Piedra Agua", "Amistad alta, de día") para una arista
    /// de evolución. Nunca vacío — como mínimo devuelve el trigger humanizado. Comportamiento
    /// histórico: la región cuenta como condición si no hay otra (usar FormatBase / FormatForm en la
    /// Pokédex con formas).</summary>
    public static string Format(IReadOnlyDictionary<string, string> c, string lang = "es")
        => FormatCore(c, lang).Text;

    /// <summary>Arista hacia una especie BASE: ignora <c>region</c>. En pokemon.db las aristas base
    /// mezclan las condiciones de la variante regional (p. ej. Exeggcute → Exeggutor trae también
    /// <c>region=alola</c>, que pertenece a Exeggutor de Alola).</summary>
    public static string FormatBase(IReadOnlyDictionary<string, string> c, string lang = "es")
        => FormatCore(WithoutRegion(c), lang).Text;

    /// <summary>Arista hacia una FORMA regional: la condición principal más la región
    /// ("Piedra Hoja · en Alola"). Si la región es la única condición, queda "En la región Alola".</summary>
    public static string FormatForm(IReadOnlyDictionary<string, string> c, string lang = "es")
    {
        if (!c.TryGetValue("region", out var region) || string.IsNullOrEmpty(region))
            return FormatCore(c, lang).Text;

        var core = FormatCore(WithoutRegion(c), lang);
        return core.IsFallback
            ? FormatCore(c, lang).Text
            : core.Text + T(lang, "regionSuffix", Humanize(region));
    }

    private static IReadOnlyDictionary<string, string> WithoutRegion(IReadOnlyDictionary<string, string> c)
        => c.ContainsKey("region") ? c.Where(kv => kv.Key != "region").ToDictionary(kv => kv.Key, kv => kv.Value) : c;

    private static (string Text, bool IsFallback) FormatCore(IReadOnlyDictionary<string, string> c, string lang)
    {
        static bool Has(IReadOnlyDictionary<string, string> d, string k, out string v)
            => d.TryGetValue(k, out v!) && !string.IsNullOrEmpty(v);

        // Objeto (evolución por objeto, con o sin trade) — el caso más común después de nivel.
        if (Has(c, "item", out var item)) return (PrettyItem(item, lang), false);

        // Nivel — min_level ausente con trigger=level-up significa que la condición real es OTRA
        // (amistad, movimiento, ubicación...), evaluada más abajo.
        if (Has(c, "min_level", out var lvl)) return (T(lang, "level", lvl), false);

        if (Has(c, "min_happiness", out _)) return (T(lang, "happiness") + TimeOfDaySuffix(c, lang), false);
        if (Has(c, "min_affection", out _)) return (T(lang, "affection") + TimeOfDaySuffix(c, lang), false);
        if (Has(c, "min_beauty", out _))    return (T(lang, "beauty"), false);

        if (Has(c, "held_item", out var held))       return (T(lang, "tradeHolding", PrettyItem(held, lang)), false);
        if (Has(c, "trade_species", out var tradeSp)) return (T(lang, "tradeFor", PrettySpecies(tradeSp, lang)), false);
        if (c.TryGetValue("trigger", out var trig) && trig == "trade") return (T(lang, "trade"), false);

        if (Has(c, "known_move", out var move))           return (T(lang, "knownMove", PrettyMove(move, lang)), false);
        if (Has(c, "known_move_type", out var moveType))  return (T(lang, "knownType", Humanize(moveType)), false);
        if (Has(c, "used_move", out var usedMove))        return (T(lang, "usedMove", PrettyMove(usedMove, lang)), false);

        if (Has(c, "location", out var loc))     return (T(lang, "location", Humanize(loc)), false);
        if (Has(c, "region", out var region))    return (T(lang, "region", Humanize(region)), false);

        if (Has(c, "party_species", out var partySp)) return (T(lang, "party", PrettySpecies(partySp, lang)), false);
        if (Has(c, "min_steps", out var steps))       return (T(lang, "steps", steps), false);

        if (Has(c, "gender", out var gender)) return (T(lang, gender == "1" ? "female" : "male"), false);

        if (Has(c, "relative_physical_stats", out var rel))
            return (T(lang, rel switch { "1" => "atkGtDef", "-1" => "atkLtDef", "0" => "atkEqDef", _ => "atkVsDef" }), false);

        if (c.TryGetValue("turn_upside_down", out var upsideDown) && upsideDown == "True")
            return (T(lang, "upsideDown"), false);

        if (Has(c, "base_form", out var baseForm)) return (T(lang, "form", Humanize(baseForm)), false);

        // Fallback: humanizar el trigger tal cual (ej. "shed" → "Shed"). Nunca vacío para el usuario.
        return c.TryGetValue("trigger", out var t) && !string.IsNullOrEmpty(t)
            ? (Humanize(t), true)
            : (T(lang, "unknown"), true);
    }

    private static string TimeOfDaySuffix(IReadOnlyDictionary<string, string> c, string lang)
    {
        if (!c.TryGetValue("time_of_day", out var tod) || string.IsNullOrEmpty(tod)) return "";
        return tod switch
        {
            "day"   => T(lang, "day"),
            "night" => T(lang, "night"),
            _ => "",
        };
    }

    // Objeto: nombre de PKHeX en el idioma de la UI; respaldo, la tabla en español de arriba (solo "es") y,
    // por último, el slug humanizado.
    private static string PrettyItem(string slug, string lang)
        => EvolutionNameLocalizer.Item(slug, lang)
           ?? (lang == "es" && ItemNamesEs.TryGetValue(slug, out var name) ? name : Humanize(slug));

    private static string PrettyMove(string slug, string lang)
        => EvolutionNameLocalizer.Move(slug, lang) ?? Humanize(slug);

    private static string PrettySpecies(string slug, string lang)
        => EvolutionNameLocalizer.Species(slug, lang) ?? Humanize(slug);

    /// <summary>Fallback genérico para slugs sin traducción propia: "water-stone" →
    /// "Water stone". No es una traducción real, es mejor que mostrar el slug crudo con guiones.</summary>
    private static string Humanize(string slug)
    {
        var withSpaces = slug.Replace('-', ' ').Replace('_', ' ');
        return withSpaces.Length == 0
            ? withSpaces
            : char.ToUpperInvariant(withSpaces[0]) + withSpaces[1..];
    }
}
