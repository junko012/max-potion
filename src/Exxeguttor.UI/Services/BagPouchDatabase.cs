using PKHeX.Core;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Tabla estática InventoryType → (nombre en español, ícono) para el módulo Mochila.
/// Mismo criterio que <see cref="NatureDatabase"/>/<see cref="RibbonDatabase"/>: dominio fijo
/// del motor Pokémon (16 valores exactos de <see cref="InventoryType"/>, confirmados por
/// metadata de PKHeX.Core 25.11.7 — ver InventoryApiDiagnosticTests), no vale la pena
/// migrarlo a SQL para un conjunto tan chico y estable.
///
/// Qué pouches aparecen realmente para un save concreto NO se decide acá — sale de
/// <see cref="Exxeguttor.App.Services.ItemInventoryService.GetPouches"/> leyendo
/// SaveFile.Inventory. Esta tabla solo resuelve "cómo se muestra" cada InventoryType si
/// aparece.
/// </summary>
public static class BagPouchDatabase
{
    // Sprites reales de ítem (los mismos assets embebidos que ya usa BagItemRowViewModel/
    // SpriteService.GetItemSprite en todo el resto de Mochila) en vez de emoji — corrección
    // de sesión "iconos de categoría": los emoji (🎒🗝️💿 etc.) se ven inconsistentes entre
    // sistemas (en Linux en particular, la fuente Noto Color Emoji los renderiza con un
    // estilo caricaturesco que no pega con el resto de la UI, que es toda sprites pixel art)
    // y además obligaban un FontSize=40 gigante en la tile. Elegido un ítem representativo
    // y reconocible por categoría — el string es el mismo formato slug que ya espera
    // SpriteService.GetItemSprite (minúsculas, guiones), no hace falta tocar ese método.
    private static readonly Dictionary<InventoryType, (string Name, string IconSpriteKey)> Map = new()
    {
        [InventoryType.Items]       = ("Objetos", "escape-rope"),
        [InventoryType.KeyItems]    = ("Clave", "key-to-room-1"),
        [InventoryType.TMHMs]       = ("MTs/MOs", "tm-case"),
        [InventoryType.Medicine]    = ("Medicina", "potion"),
        [InventoryType.Berries]     = ("Bayas", "razz-berry"),
        [InventoryType.Balls]       = ("Bolas", "poke-ball"),
        [InventoryType.BattleItems] = ("Batalla", "dire-hit"),
        [InventoryType.MailItems]   = ("Correo", "greet-mail"),
        [InventoryType.PCItems]     = ("Objetos (PC)", "nugget"),
        [InventoryType.FreeSpace]   = ("Espacio libre", "coin-case"),
        [InventoryType.ZCrystals]   = ("Z-Crystals", "normalium-z--bag"),
        [InventoryType.Candy]       = ("Caramelos", "rare-candy"),
        [InventoryType.Treasure]    = ("Tesoros", "relic-gold"),
        [InventoryType.Ingredients] = ("Ingredientes", "apricorn-box"),
        [InventoryType.MegaStones]  = ("Mega Piedras", "venusaurite"),
    };

    // Sesión "iconos de categoría": Map de arriba ahora tiene el slug del SPRITE (para la
    // tile de la grilla, ver IconSpriteKey). Este segundo mapa, más chico, es solo para el
    // emoji-prefijo de la línea de la libreta (EditSessionService.BuildBagSummary) — un
    // texto plano de una línea, contexto totalmente distinto a la tile grande de 40pt donde
    // el emoji se veía mal; acá, chico y como prefijo de texto, no molesta y evita tener que
    // tocar el formato de la libreta en esta sesión.
    private static readonly Dictionary<InventoryType, string> EmojiMap = new()
    {
        [InventoryType.Items]       = "🎒",
        [InventoryType.KeyItems]    = "🗝️",
        [InventoryType.TMHMs]       = "💿",
        [InventoryType.Medicine]    = "💊",
        [InventoryType.Berries]     = "🍇",
        [InventoryType.Balls]       = "🔴",
        [InventoryType.BattleItems] = "⚔️",
        [InventoryType.MailItems]   = "✉️",
        [InventoryType.PCItems]     = "🗄️",
        [InventoryType.FreeSpace]   = "📦",
        [InventoryType.ZCrystals]   = "💎",
        [InventoryType.Candy]       = "🍬",
        [InventoryType.Treasure]    = "🏺",
        [InventoryType.Ingredients] = "🌿",
        [InventoryType.MegaStones]  = "🔶",
    };

    /// <summary>Emoji chico para usar como prefijo de texto (ej. libreta de cambios) — NO
    /// usar para la tile de la grilla, ver <see cref="IconSpriteKey"/> para eso.</summary>
    public static string Emoji(InventoryType type) =>
        EmojiMap.TryGetValue(type, out var e) ? e : "📦";

    /// <summary>Nombre en español para mostrar en la tile/título — fallback al nombre crudo
    /// del enum si aparece un InventoryType nuevo que todavía no mapeamos acá (no debería
    /// pasar salvo que PKHeX.Core agregue un valor en una migración de versión futura).</summary>
    public static string DisplayName(InventoryType type) =>
        Map.ContainsKey(type) ? UiText.T("Bag_Pouch_" + type) : type.ToString();

    /// <summary>Slug del sprite representativo de la categoría — pasar directo a
    /// <see cref="Exxeguttor.UI.Services.SpriteService.GetItemSprite"/>, mismo formato que ya
    /// usa para cualquier otro ítem. Fallback a "poke-ball" (siempre existe, es de los
    /// primeros sprites que se embebieron) si aparece un InventoryType nuevo sin mapear.</summary>
    public static string IconSpriteKey(InventoryType type) =>
        Map.TryGetValue(type, out var entry) ? entry.IconSpriteKey : "poke-ball";

    /// <summary>PCItems y FreeSpace son "storages" aparte de la mochila normal — ver
    /// "Objetos en PC de primeras generaciones" en context.md. El resto son pouches de la
    /// mochila propiamente dicha.</summary>
    public static bool IsAlternateStorage(InventoryType type) =>
        type is InventoryType.PCItems or InventoryType.FreeSpace;
}
