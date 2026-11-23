using System.Collections.Concurrent;
using System.Reflection;
using Avalonia.Media.Imaging;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Carga sprites desde recursos embebidos en el ensamblado.
///
/// Estructura de recursos embebidos:
///   Exxeguttor.UI.Assets.sprites.pokemon.normal.{id}.png
///   Exxeguttor.UI.Assets.sprites.pokemon.shiny.{id}.png
///   Exxeguttor.UI.Assets.sprites.pokemon.female.{id}.png
///   Exxeguttor.UI.Assets.sprites.pokemon.shiny-female.{id}.png
///   Exxeguttor.UI.Assets.sprites.items.{nombre}.png
///   Exxeguttor.UI.Assets.sprites.types.{tipo}.png
/// </summary>
public class SpriteService
{
    // static readonly: la CLR garantiza inicialización thread-safe (antes `??=` podía crear dos
    // instancias, cada una con su propio caché, si dos hilos la pedían a la vez).
    private static readonly SpriteService _instance = new();
    public static SpriteService Instance => _instance;

    private readonly Assembly _assembly;
    private const string NS = "Exxeguttor.UI.Assets.sprites";

    // Caché en memoria
    // ConcurrentDictionary: lecturas SIN bloqueo. Antes (y en la versión con `lock`) el hilo de UI
    // tenía que esperar a que un hilo de background (prefetch del modal de crear, íconos de la
    // mochila) terminara de decodificar un sprite para poder leer el suyo — con el prefetch
    // corriendo justo después de abrir un save, eso hacía que clickear un Pokémon se sintiera
    // congelado varios segundos. Ahora un acierto de caché nunca espera a nadie.
    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    private SpriteService()
    {
        _assembly = Assembly.GetExecutingAssembly();
    }

    // ── Pokémon ───────────────────────────────────────────────────────

    /// <summary>Especie cuya forma 0 también tiene archivo propio en <c>pokemon/forms/</c> (Maushold).</summary>
    private const int FormZeroHasOwnSprite = 925;

    /// <summary>
    /// Obtiene el sprite de un Pokémon por ID de especie.
    /// Hace fallback automático si la variante exacta no existe.
    /// </summary>
    public Bitmap? GetPokemonSprite(int speciesId, bool shiny = false, bool female = false, byte form = 0)
    {
        if (speciesId <= 0) return null;

        // Fase 2 de formas: una forma regional (Marowak de Alola...) tiene sprite propio en
        // pokemon/forms/ (y su variante shiny en pokemon/forms/shiny/), indexado por especie +
        // índice de forma de PKHeX. Para cualquier forma sin sprite propio (cosméticas, de
        // combate...) Load devuelve null y se sigue con el sprite de la especie, como siempre.
        // Excepción: Maushold. En PKHeX la forma 0 es la Familia de Tres y la 1 la de Cuatro, pero
        // el sprite "base" de PokeAPI (925.png) es el de Cuatro: la forma 0 también tiene archivo propio.
        if (form != 0 || speciesId == FormZeroHasOwnSprite)
        {
            var formSprite = (shiny ? Load($"{NS}.pokemon.forms.shiny.{speciesId}_{form}.png") : null)
                             ?? Load($"{NS}.pokemon.forms.{speciesId}_{form}.png");
            if (formSprite != null) return formSprite;
        }

        // Intentar variante exacta → fallback a normal
        if (shiny && female)
        {
            var s = Load($"{NS}.pokemon.shiny-female.{speciesId}.png");
            if (s != null) return s;
        }
        if (female)
        {
            var s = Load($"{NS}.pokemon.female.{speciesId}.png");
            if (s != null) return s;
        }
        if (shiny)
        {
            var s = Load($"{NS}.pokemon.shiny.{speciesId}.png");
            if (s != null) return s;
        }
        return Load($"{NS}.pokemon.normal.{speciesId}.png");
    }

    /// <summary>
    /// Sprite de una forma regional (Alola/Galar/Hisui/Paldea), por especie + índice de forma de
    /// PKHeX.Core (ver RegionalFormCatalog). Los assets viven en
    /// <c>Assets/sprites/pokemon/forms/{especie}_{forma}.png</c>. Si no existe el de esa forma,
    /// cae al sprite normal de la especie base en vez de devolver null.
    /// </summary>
    public Bitmap? GetPokemonFormSprite(int speciesId, byte form) =>
        GetPokemonSprite(speciesId, shiny: false, female: false, form: form);

    // ── Portadas de juego (Pantalla 1 — Estado vacío / Carga de save) ──

    /// <summary>
    /// Sprite de portada por juego para la lista de "Recientes". <paramref name="gameVersion"/>
    /// es el código CRUDO de <c>SaveFile.Version.ToString()</c> (ej. "RD", "SI", "SV", "ZA") —
    /// el mismo valor que ya usa <c>TrainerViewModel.ExpandGameName</c>. Devuelve null si
    /// todavía no existe el asset para ese juego — el caller (ver <c>GameCoverService</c>)
    /// debe dibujar el fallback procedural (color + ícono por generación) en ese caso, nunca
    /// dejar el sprite en blanco ni bloquear la fila.
    /// </summary>
    public Bitmap? GetGameCoverSprite(string gameVersion)
    {
        if (string.IsNullOrEmpty(gameVersion)) return null;
        return Load($"{NS}.games.{gameVersion.ToLowerInvariant()}.png");
    }

    /// <summary>
    /// Sprite de MissingNo. para el modal de "save corrupto" (Caso 2 en
    /// <c>mockups/error_empty_state/</c>). No es un Pokémon con entrada real de Pokédex
    /// post-Gen 1 — no encaja en la convención numérica de <see cref="GetPokemonSprite"/>
    /// (<c>pokemon/normal/{id}.png</c>), por eso vive en su propio archivo dedicado
    /// (<c>pokemon/missingno.png</c>, el sprite glitch clásico de 8-bit) en vez de forzar un
    /// ID falso en esa carpeta.
    /// </summary>
    /// <summary>Rotom en escala de grises y semitransparente (ícono de la barra de estado, sin cambios pendientes).</summary>
    public Bitmap? GetStatusRotomGraySprite() => Load($"{NS}.pokemon.ui.rotom_gray.png");

    public Bitmap? GetMissingNoSprite() => Load($"{NS}.pokemon.missingno.png");

    // ── Ítems ─────────────────────────────────────────────────────────

    /// <summary>
    /// Obtiene el sprite de un ítem por nombre de PokeAPI.
    /// Ej: "poke-ball", "master-ball", "leftovers"
    /// </summary>
    public Bitmap? GetItemSprite(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        var normalized = itemName.ToLowerInvariant()
                                 .Replace(" ", "-")
                                 .Replace("'", "");
        return Load($"{NS}.items.{normalized}.png");
    }

    // ── Entrenador ────────────────────────────────────────────────────

    /// <summary>
    /// Sprite de entrenador/entrenadora para la tarjeta del modal de revisión. Devuelve null
    /// si el asset no está embebido — HOY EL PROYECTO NO TIENE sprites de entrenador
    /// (solo pokemon/items/types, ver namespace arriba), así que esto siempre da null hasta
    /// que alguien agregue "Exxeguttor.UI.Assets.sprites.trainer.male.png" /
    /// "...trainer.female.png" como EmbeddedResource. La tarjeta del modal ya maneja el caso
    /// null sin romperse (Image con Source null no dibuja nada).
    /// </summary>
    public Bitmap? GetTrainerSprite(bool female) =>
        Load($"{NS}.trainers.generic.{(female ? "female" : "male")}.png");

    /// <summary>
    /// Ícono de entrenador/entrenadora PARA ESE JUEGO (tarjeta "Entrenador" del modal de exportación).
    /// Los PNG salen de <c>scripts/gen-trainer-sprites.py</c> (arte original, no sprites oficiales) y
    /// se embeben desde <c>Assets/sprites/trainers/&lt;clave&gt;.&lt;male|female&gt;.png</c>. Si el juego no
    /// tiene ficha propia, o no existe esa variante (RBY y Oro/Plata no tienen entrenadora), cae al
    /// genérico del género pedido. Géneros: PKHeX guarda 0/1 en todas las generaciones — no hay un
    /// tercer valor (Escarlata/Púrpura y Z-A siguen siendo look masculino/femenino).
    /// </summary>
    /// <param name="gameVersionId">Valor numérico de <c>GameVersion</c> (mismo criterio que
    /// <c>PokemonEditorViewModel.MapVersionToGameId</c>: se usa el entero para no depender de nombres de enum).</param>
    public Bitmap? GetTrainerSprite(int gameVersionId, bool female)
    {
        var g = female ? "female" : "male";
        var key = TrainerKey(gameVersionId);
        return (key != null ? Load($"{NS}.trainers.{key}.{g}.png") : null)
               ?? GetTrainerSprite(female);
    }

    /// <summary>GameVersion (entero) → clave de ficha de entrenador; null = sin ficha (usa el genérico).</summary>
    private static string? TrainerKey(int v) => v switch
    {
        35 or 36 or 37 or 38     => "g1",       // Red / Green(JP) / Blue / Yellow
        39 or 40                 => "g2gs",     // Gold / Silver (solo masculino)
        41                       => "g2c",      // Crystal
        1 or 2 or 3 or 26 or 27  => "g3rse",    // Sapphire / Ruby / Emerald + OR/AS (mismos protagonistas)
        4 or 5                   => "g3frlg",   // FireRed / LeafGreen
        10 or 11 or 12 or 48 or 49 => "g4dppt", // Diamond / Pearl / Platinum + BD/SP
        7 or 8                   => "g4hgss",   // HeartGold / SoulSilver
        20 or 21                 => "g5bw",     // White / Black
        22 or 23                 => "g5b2w2",   // White 2 / Black 2
        24 or 25                 => "g6xy",     // X / Y
        30 or 31 or 32 or 33     => "g7sm",     // Sun / Moon / Ultra Sun / Ultra Moon
        42 or 43                 => "g7lgpe",   // Let's Go Pikachu / Eevee
        44 or 45                 => "g8swsh",   // Sword / Shield
        47                       => "g8la",     // Legends: Arceus
        50 or 51                 => "g9sv",     // Scarlet / Violet
        _                        => null,
    };

    // ── Tipos ─────────────────────────────────────────────────────────

    /// <summary>
    /// Obtiene el badge de un tipo.
    /// Ej: "fire", "water", "grass"
    /// </summary>
    public Bitmap? GetTypeSprite(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        return Load($"{NS}.types.{typeName.ToLowerInvariant()}.png");
    }

    // ── Estado ────────────────────────────────────────────────────────

    public bool HasSprites()
        => _assembly.GetManifestResourceStream($"{NS}.pokemon.normal.1.png") != null;

    public void ClearCache()
    {
        foreach (var b in _cache.Values) b?.Dispose();
        _cache.Clear();
    }

    // ── Carga interna ─────────────────────────────────────────────────

    private Bitmap? Load(string resourceName)
    {
        // Camino rápido: acierto de caché, sin locks ni esperas.
        if (_cache.TryGetValue(resourceName, out var cached))
            return cached;

        // Fallo de caché: se decodifica FUERA de cualquier lock. Si dos hilos piden el mismo
        // sprite a la vez, ambos lo decodifican y GetOrAdd se queda con el primero (el otro
        // se descarta) — un desperdicio de unos pocos ms, a cambio de que nadie bloquee a nadie.
        return _cache.GetOrAdd(resourceName, Decode);
    }

    private Bitmap? Decode(string resourceName)
    {
        try
        {
            // Nota: los guiones en nombres de carpeta/archivo se convierten
            // a guiones bajos en nombres de recursos embebidos de .NET
            // shiny-female → shiny_female
            var dotnetName = resourceName.Replace("-", "_")
                                         .Replace("Assets.sprites.", "Assets.sprites.");

            // Intentar nombre original primero, luego con guiones convertidos
            using var stream = _assembly.GetManifestResourceStream(resourceName)
                            ?? _assembly.GetManifestResourceStream(dotnetName);

            return stream == null ? null : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
