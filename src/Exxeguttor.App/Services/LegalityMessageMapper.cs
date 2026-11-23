using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Traduce los resultados crudos de <see cref="LegalityAnalysis"/> (PKHeX.Core: en inglés,
/// con identificadores internos como "Move", "IVs", etc.) a mensajes cortos, en el idioma de la interfaz, que
/// un usuario sin conocimiento de PKHeX pueda entender de un vistazo.
///
/// Fuente única, compartida entre <c>PokemonService.GetLegalitySummary</c> (badge rápido bajo
/// el sprite del panel principal) y <c>EditSessionService.AnalyzeLegality</c> (modal de
/// revisión antes de exportar) — antes existían dos copias parecidas de un mapeo
/// categoría↔CheckIdentifier, cada una en su propio archivo.
///
/// El mensaje técnico original (<c>CheckResult.ToString()</c>) nunca se descarta: queda
/// disponible aparte en <see cref="LegalitySummary.TechnicalDetail"/> para quien lo necesite
/// (casos raros que el mensaje amigable no explica bien, reporte de bugs) — pero no es lo
/// primero que ve el usuario. Ver conversación de sesión sobre "Opción B" (mensaje amigable
/// por categoría + detalle técnico oculto detrás de un expand/tooltip).
///
/// Es deliberadamente genérico por categoría (no intenta parsear el texto exacto de cada
/// CheckResult) — más robusto ante cambios de versión de PKHeX.Core que un parseo de texto
/// libre (ya pasó con <c>CheckResult.Comment</c> en la migración a 25.11.7, ver context.md).
/// </summary>
public static class LegalityMessageMapper
{
    /// <summary>CheckIdentifier.ToString() (PKHeX.Core) → categoría propia. Cobertura completa
    /// de los 34 valores del enum en 25.11.7 (confirmado por reflection sobre el ensamblado,
    /// mismo mecanismo que se usó para diagnosticar la migración de versión — ver
    /// context.md/"PKHeX.Core 25.11.7"). Best-effort: si una versión nueva de PKHeX agrega o
    /// renombra un identifier, esa categoría puntual simplemente deja de categorizarse (no
    /// rompe nada — el issue sigue en TechnicalDetail igual).</summary>
    private static readonly Dictionary<string, string> CategoryByIdentifier = new()
    {
        ["Nickname"] = "Apodo",
        ["Level"] = "Nivel",
        ["Ability"] = "Habilidad",
        ["Nature"] = "Naturaleza",
        ["IVs"] = "IVs/EVs",
        ["EVs"] = "IVs/EVs",
        ["GVs"] = "IVs/EVs",       // Ganbaru Values (Legends Arceus, reemplaza EVs)
        ["AVs"] = "IVs/EVs",       // Awakening Values (Legends Arceus, reemplaza EVs)
        ["CurrentMove"] = "Movimientos",
        ["Move"] = "Movimientos",
        ["RelearnMove"] = "Movimientos",
        ["Geography"] = "Ubicación",
        ["Encounter"] = "Origen del encuentro",
        ["SlotType"] = "Origen del encuentro",
        ["Evolution"] = "Evolución",
        ["Egg"] = "Huevo",
        ["GameOrigin"] = "Juego de origen",
        ["Shiny"] = "Brillante",
        ["Gender"] = "Género",
        ["Form"] = "Forma",
        ["Language"] = "Idioma",
        ["Fateful"] = "Encuentro especial",
        ["Ball"] = "Poké Ball",
        ["HeldItem"] = "Objeto equipado",
        ["Ribbon"] = "Cintas",
        ["RibbonMark"] = "Cintas",
        ["Marking"] = "Marcas",
        ["Memory"] = "Recuerdos",
        ["Training"] = "Entrenamiento",
        ["Trainer"] = "Entrenador",
        ["Handler"] = "Entrenador",
        ["EC"] = "Datos internos",
        ["PID"] = "Datos internos",
        ["TrashBytes"] = "Datos internos",
        ["Misc"] = "Otros",
    };

    /// <summary>Orden de resolución sugerido para el diagnosticador — por categoría
    /// amigable, no por <see cref="CheckIdentifier"/> crudo (varios identifiers ya
    /// colapsan a la misma categoría en <see cref="CategoryByIdentifier"/>, ej.
    /// IVs/EVs/GVs/AVs). Agrupado por dependencia, no por el orden en que PKHeX corrió
    /// sus verificadores internos (ese es arbitrario para el usuario):
    ///
    /// <list type="number">
    /// <item>Origen — todo lo demás deriva de esto (encuentro, juego, huevo, ubicación).</item>
    /// <item>Identidad — forma, género, habilidad, evolución.</item>
    /// <item>Núcleo — datos internos (EC/PID), naturaleza, brillante, IVs/EVs.</item>
    /// <item>Progresión — nivel, movimientos, entrenamiento.</item>
    /// <item>Periférico/cosmético — apodo, idioma, entrenador, ball, objeto, cintas, marcas, recuerdos.</item>
    /// </list>
    ///
    /// Espaciado de a 10 dentro de cada tier (10, 20, 30...) para poder insertar una
    /// categoría nueva en el medio más adelante sin tener que renumerar todo lo demás.
    /// Cualquier categoría no listada acá (no debería pasar, pero por las dudas) cae al
    /// final vía <see cref="GetOrder"/>.</summary>
    private static readonly Dictionary<string, int> OrderByCategory = new()
    {
        // Tier 1 — Origen
        ["Origen del encuentro"] = 10,
        ["Juego de origen"]      = 11,
        ["Huevo"]                = 12,
        ["Ubicación"]            = 13,
        ["Encuentro especial"]   = 14,

        // Tier 2 — Identidad
        ["Forma"]     = 20,
        ["Género"]    = 21,
        ["Habilidad"] = 22,
        ["Evolución"] = 23,

        // Tier 3 — Núcleo
        ["Datos internos"] = 30,
        ["Naturaleza"]     = 31,
        ["Brillante"]      = 32,
        ["IVs/EVs"]        = 33,

        // Tier 4 — Progresión
        ["Nivel"]         = 40,
        ["Movimientos"]   = 41,
        ["Entrenamiento"] = 42,

        // Tier 5 — Periférico/cosmético
        ["Apodo"]           = 50,
        ["Idioma"]          = 51,
        ["Entrenador"]      = 52,
        ["Poké Ball"]       = 53,
        ["Objeto equipado"] = 54,
        ["Cintas"]          = 55,
        ["Marcas"]          = 56,
        ["Recuerdos"]       = 57,
    };

    /// <summary>Orden de resolución para una categoría amigable — 999 (al final) si la
    /// categoría no está en la tabla, en vez de tirar una excepción. Pensado para que un
    /// identifier nuevo que PKHeX agregue algún día (y que <see cref="CategoryByIdentifier"/>
    /// todavía no conozca) no rompa el orden del resto, solo quede último.</summary>
    public static int GetOrder(string category) =>
        OrderByCategory.GetValueOrDefault(category, 999);

    /// <summary>Categoría (llave interna, en español) → sufijo de las claves de idioma
    /// <c>Legality_Cat_*</c> (nombre visible) y <c>Legality_Friendly_*</c> (frase corta, sin jerga de PKHeX).
    /// Las llaves NO se traducen ni se renombran: las usan <c>CategoryOrder</c>/<c>HasIssue</c> de
    /// EditSessionService y las comparaciones de la UI; solo el texto que ve el usuario sale de las claves.</summary>
    private static readonly Dictionary<string, string> SlugByCategory = new()
    {
        ["Apodo"] = "Nickname", ["Nivel"] = "Level", ["Habilidad"] = "Ability", ["Naturaleza"] = "Nature",
        ["IVs/EVs"] = "IvsEvs", ["Movimientos"] = "Moves", ["Ubicación"] = "Location",
        ["Origen del encuentro"] = "Encounter", ["Evolución"] = "Evolution", ["Huevo"] = "Egg",
        ["Juego de origen"] = "OriginGame", ["Brillante"] = "Shiny", ["Género"] = "Gender", ["Forma"] = "Form",
        ["Idioma"] = "Language", ["Encuentro especial"] = "Fateful", ["Poké Ball"] = "Ball",
        ["Objeto equipado"] = "HeldItem", ["Cintas"] = "Ribbons", ["Marcas"] = "Marks", ["Recuerdos"] = "Memories",
        ["Entrenamiento"] = "Training", ["Entrenador"] = "Trainer", ["Datos internos"] = "Internal", ["Otros"] = "Other",
    };

    /// <summary>Clave de idioma del nombre visible de una categoría; null si la categoría no está en la tabla.</summary>
    public static string? GetCategoryLabelKey(string category) =>
        SlugByCategory.TryGetValue(category, out var slug) ? "Legality_Cat_" + slug : null;

    /// <summary>Clave de idioma de la frase amigable de una categoría; null si no está en la tabla.</summary>
    public static string? GetFriendlyKey(string category) =>
        SlugByCategory.TryGetValue(category, out var slug) ? "Legality_Friendly_" + slug : null;

    private static readonly Dictionary<string, string> FieldLabelKeys = new()
    {
        ["Moves"] = "Legality_Field_Moves", ["Language"] = "Legality_Field_Language",
        ["Memory_Handler"] = "Legality_Field_MemoryHandler", ["Memory_OT"] = "Legality_Field_MemoryOT",
        ["Level"] = "Legality_Field_Level", ["OT"] = "Legality_Field_OT", ["Nickname"] = "Legality_Field_Nickname",
        ["Pokerus"] = "Legality_Field_Pokerus", ["Species"] = "Legality_Field_Species", ["Ribbons"] = "Legality_Field_Ribbons",
        ["Ability"] = "Legality_Field_Ability", ["PID"] = "Legality_Field_Pid", ["CatchRate"] = "Legality_Field_CatchRate",
        ["MetLocation"] = "Legality_Field_MetLocation", ["EV_TOTAL"] = "Legality_Field_EvTotal",
    };

    private static readonly Dictionary<string, string> StatKeys = new()
    {
        ["HP"] = "Hp", ["ATK"] = "Atk", ["DEF"] = "Def", ["SPA"] = "Spa", ["SPD"] = "Spd", ["SPE"] = "Spe",
    };

    /// <summary>Nombre visible de un <c>FieldFix.FieldId</c> en el idioma de la UI. Los ids con número o stat
    /// («Move2_PP», «IV_SPA», «Marking_3») se componen con claves; un id desconocido se muestra tal cual.</summary>
    public static string GetFieldLabel(string fieldId)
    {
        if (FieldLabelKeys.TryGetValue(fieldId, out var key)) return AppText.T(key);

        // IV_HP / EV_ATK… → «PS (IV)» reutilizando las claves del resumen de ediciones
        if (fieldId.Length > 3 && (fieldId.StartsWith("IV_", StringComparison.Ordinal) || fieldId.StartsWith("EV_", StringComparison.Ordinal))
            && StatKeys.TryGetValue(fieldId[3..], out var stat))
            return AppText.F(fieldId[0] == 'I' ? "Review_Sub_Iv" : "Review_Sub_Ev", AppText.T("Review_Stat_" + stat));

        // Marking_3, Move2, Move2_PP, Move2_PPUps, RelearnMove1
        if (fieldId.StartsWith("Marking_", StringComparison.Ordinal) && int.TryParse(fieldId["Marking_".Length..], out var mark))
            return AppText.F("Legality_Field_Marking", mark);
        if (fieldId.StartsWith("RelearnMove", StringComparison.Ordinal) && int.TryParse(fieldId["RelearnMove".Length..], out var relearn))
            return AppText.F("Legality_Field_RelearnMove", relearn);
        if (fieldId.StartsWith("Move", StringComparison.Ordinal))
        {
            var rest = fieldId["Move".Length..];
            var underscore = rest.IndexOf('_');
            var numberText = underscore < 0 ? rest : rest[..underscore];
            if (int.TryParse(numberText, out var slot))
            {
                var suffix = underscore < 0 ? string.Empty : rest[underscore..];
                return suffix switch
                {
                    "" => AppText.F("Review_Sub_Move", slot),
                    "_PP" => AppText.F("Legality_Field_MovePP", slot),
                    "_PPUps" => AppText.F("Legality_Field_MovePPUps", slot),
                    _ => fieldId,
                };
            }
        }
        return fieldId;
    }

    /// <summary>Nombre visible de una categoría en el idioma de la UI (la llave tal cual si es desconocida).</summary>
    public static string GetCategoryLabel(string category) =>
        GetCategoryLabelKey(category) is { } key ? AppText.T(key) : category;


    /// <summary>Categoría amigable para un <see cref="CheckIdentifier"/> — <see langword="null"/>
    /// si no está en la tabla (no debería pasar, PKHeX no agrega identifiers nuevos seguido,
    /// pero mejor null que una excepción). Expuesto para que
    /// <see cref="LegalityDiagnosticBuilder"/> pueda agrupar sin reimplementar este mapeo.</summary>
    public static string? GetCategory(CheckIdentifier identifier) =>
        CategoryByIdentifier.TryGetValue(identifier.ToString(), out var cat) ? cat : null;

    /// <summary>El texto amigable de una categoría — el genérico de respaldo si por algún
    /// motivo la categoría no está en la tabla (nunca debería pasar si vino de
    /// <see cref="GetCategory"/>, pero la función queda total, no parcial).</summary>
    public static string GetFriendlyText(string category) =>
        AppText.T(GetFriendlyKey(category) ?? "Legality_Friendly_Fallback");

    /// <summary>Corre y categoriza un LegalityAnalysis ya construido. No instancia el análisis
    /// acá adentro para que el caller decida sobre qué PKM correrlo (real o un clon/preview).</summary>
    public static LegalitySummary Summarize(LegalityAnalysis analysis)
    {
        if (analysis.Valid) return LegalitySummary.LegalResult;

        var categories = new List<string>(); // orden de aparición, sin duplicados
        var technicalLines = new List<string>();
        foreach (var r in analysis.Results)
        {
            if (r.Valid) continue;

            // CheckResult.Comment (string) ya no existe en PKHeX.Core 25.11.7, pero
            // ToString() sigue dando el mensaje legible — ver context.md, sección de cambios
            // de API. No hace falta tocar .Result/.Code para esto.
            var text = LegalityHumanizer.Humanize(analysis, r);
            if (!string.IsNullOrWhiteSpace(text)) technicalLines.Add(text);

            var category = CategoryByIdentifier.TryGetValue(r.Identifier.ToString(), out var cat) ? cat : null;
            if (category != null && !categories.Contains(category)) categories.Add(category);
        }

        var friendlyByCategory = categories.ToDictionary(
            c => c,
            c => GetFriendlyText(c));

        return new LegalitySummary(
            IsLegal: false,
            Categories: categories,
            FriendlyByCategory: friendlyByCategory,
            TechnicalDetail: string.Join("\n", technicalLines));
    }
}

/// <summary>Resultado categorizado de un análisis de legalidad: qué categorías tienen
/// problemas, un mensaje amigable por cada una (sin jerga), y el detalle técnico crudo de
/// PKHeX.Core disponible aparte (nunca se descarta, solo se oculta detrás del resumen).</summary>
public sealed record LegalitySummary(
    bool IsLegal,
    List<string> Categories,
    Dictionary<string, string> FriendlyByCategory,
    string TechnicalDetail)
{
    public static readonly LegalitySummary LegalResult = new(true, new List<string>(), new Dictionary<string, string>(), string.Empty);

    /// <summary>Texto amigable de varias líneas para un vistazo rápido (tooltip, badge) —
    /// una viñeta por categoría con problema, sin jerga técnica. Vacío si es legal.</summary>
    public string FriendlySummaryText => IsLegal
        ? string.Empty
        : string.Join("\n", Categories.Select(c => "• " + (FriendlyByCategory.TryGetValue(c, out var m) ? m : AppText.T("Legality_Friendly_FieldFallback"))));

    /// <summary>Etiquetas cortas de categorías afectadas, para un resumen de una sola línea
    /// (ej. "Movimientos, Nivel"). Vacío si es legal.</summary>
    public string CategoryTags => string.Join(", ", Categories.Select(LegalityMessageMapper.GetCategoryLabel));
}
