using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Mapeos de <see cref="GameVersion"/> usados por el módulo Pokédex. Deliberadamente
/// separado de los otros dos <c>MapVersionToGameId</c> que ya existen en
/// PokemonService.cs y PokemonEditorViewModel.cs — auditar el schema real de PKHeX.Core
/// 25.11.7 (no confiar en el enum de memoria; PokemonService.cs ya documenta un caso real
/// donde GameVersion.E resultó ser 769 en este paquete y no 3) reveló que:
///
///   1. Los dos mapeos existentes usan agrupaciones de miembros distintas entre sí para
///      el mismo propósito — señal de que el enum expone tanto valores "específicos"
///      (ej. GameVersion.R para Rubí) como valores "agregados" (ej. GameVersion.RS) y
///      <c>SaveFile.Version</c> puede devolver cualquiera de los dos según el caso (esto
///      ya causó dos bugs reales reportados y arreglados en SaveCapabilities.cs: el tab
///      Mega no aparecía para algunos saves de Gen6, el tab Special no aparecía para
///      Gen9, ambos por switches que solo cubrían el valor específico y no el agregado).
///
///   2. Cuando el valor agregado es lo que llega (ej. GameVersion.RS en vez de R o S),
///      NO hay forma de saber cuál de los dos juegos del par es en realidad solo mirando
///      el enum — Rubí y Zafiro (o Negro/Blanco, Negro2/Blanco2) tienen flavor text
///      distinto entre sí, así que adivinar cualquiera de los dos mostraría texto
///      incorrecto, peor que no mostrar nada.
///
/// Por eso <see cref="TryGetFlavorTextSlug"/> devuelve null tanto para juegos sin fuente
/// (Legends Z-A, BD/SP) como para agregados ambiguos — nunca adivina. Esto es más
/// conservador que estrictamente necesario (algunos casos agregados podrían resolverse
/// leyendo una propiedad distinta del objeto SaveFile real, no el enum) pero no se pudo
/// auditar eso en esta sesión por falta de acceso a PKHeX.Core.dll — ver pendiente en
/// screen-pokedex-album.md.
/// </summary>
public static class GameVersionMappings
{
    /// <summary>GameId de la tabla <c>Games</c> de pokemon.db (una fila por par de versiones) para
    /// la versión del save; 0 si no hay mapeo. Mismos miembros del enum que usan los otros
    /// <c>MapVersionToGameId</c> del proyecto (no se agregan S/W/W2/B2/GN/GP sueltos: comparten
    /// valor entero con otros miembros y el compilador rechazaría el switch).</summary>
    public static int TryGetGameId(GameVersion version) => version switch
    {
        GameVersion.RB or GameVersion.RD or GameVersion.GN or GameVersion.BU => 16,
        GameVersion.YW => 23,
        GameVersion.GS or GameVersion.GD or GameVersion.SI => 9,
        GameVersion.C => 4,
        GameVersion.RS or GameVersion.R or GameVersion.S => 17,
        GameVersion.E => 6,
        GameVersion.FRLG or GameVersion.FR or GameVersion.LG => 7,
        GameVersion.CXD => 8,
        GameVersion.DP or GameVersion.D or GameVersion.P => 5,
        GameVersion.Pt => 15,
        GameVersion.HGSS or GameVersion.HG or GameVersion.SS => 10,
        GameVersion.BW or GameVersion.B or GameVersion.W => 2,
        GameVersion.B2W2 or GameVersion.B2 or GameVersion.W2 => 1,
        GameVersion.XY or GameVersion.X or GameVersion.Y => 22,
        GameVersion.ORAS or GameVersion.OR or GameVersion.AS => 14,
        GameVersion.SM or GameVersion.SN or GameVersion.MN => 19,
        GameVersion.USUM or GameVersion.US or GameVersion.UM => 21,
        GameVersion.GG or GameVersion.GP or GameVersion.GE => 13,
        GameVersion.SWSH or GameVersion.SW or GameVersion.SH => 20,
        GameVersion.BDSP or GameVersion.BD or GameVersion.SP => 3,
        GameVersion.PLA => 11,
        GameVersion.ZA => 12,
        GameVersion.SL or GameVersion.VL or GameVersion.SV or GameVersion.Gen9 => 18,
        _ => 0,
    };

    /// <summary>Clave de <c>restricted_dex_availability.json</c> para juegos que recortan
    /// qué especies tienen datos programados (a diferencia de Gen 1-7 + BDSP, donde
    /// alcanza con <c>SaveFile.MaxSpeciesID</c>). Null si el juego no está en ese grupo.
    /// No hace falta distinguir versión específica dentro del par — el JSON ya fusiona
    /// Sword+Shield / Scarlet+Violet en una sola lista cada uno.</summary>
    public static string? TryGetRestrictedDexKey(GameVersion version) => version switch
    {
        GameVersion.SW or GameVersion.SH or GameVersion.SWSH => "sword-shield",
        GameVersion.SL or GameVersion.VL or GameVersion.SV or GameVersion.Gen9 => "scarlet-violet",
        GameVersion.PLA => "legends-arceus",
        GameVersion.ZA  => "legends-za",
        _ => null,
    };

    /// <summary>Slug de <c>SpeciesFlavorText.Game</c> para la versión EXACTA cargada.
    /// Null si no hay dato en la fuente para ese juego (Legends Z-A, BD/SP — ningún
    /// species_id tiene fila para estos) o si <paramref name="version"/> llegó como valor
    /// agregado ambiguo (ver remarks de la clase) — en ambos casos el llamador debe
    /// mostrar el estado "sin datos de origen", nunca elegir un juego al azar del par.</summary>
    public static string? TryGetFlavorTextSlug(GameVersion version) => version switch
    {
        GameVersion.RD => "red",
        GameVersion.BU => "blue",
        GameVersion.YW => "yellow",
        GameVersion.GD => "gold",
        GameVersion.SI => "silver",
        GameVersion.C  => "crystal",
        GameVersion.R  => "ruby",
        // GameVersion.S (Zafiro) deliberadamente ausente: mismo enum-value que otro
        // miembro en este paquete según ya documentó PokemonService.cs — no incluir
        // hasta confirmar con reflection real que no colisiona.
        GameVersion.E  => "emerald",
        GameVersion.FR => "firered",
        GameVersion.LG => "leafgreen",
        GameVersion.D  => "diamond",
        GameVersion.P  => "pearl",
        GameVersion.Pt => "platinum",
        GameVersion.HG => "heartgold",
        GameVersion.SS => "soulsilver",
        // Negro/Blanco/Negro2/Blanco2 individuales: mismo motivo que Zafiro arriba.
        GameVersion.X  => "x",
        GameVersion.Y  => "y",
        GameVersion.OR => "omega-ruby",
        GameVersion.AS => "alpha-sapphire",
        GameVersion.SN => "sun",
        GameVersion.MN => "moon",
        GameVersion.US => "ultra-sun",
        GameVersion.UM => "ultra-moon",
        GameVersion.GG => "lets-go-pikachu",
        GameVersion.GE => "lets-go-eevee",
        GameVersion.SW => "sword",
        GameVersion.SH => "shield",
        GameVersion.PLA => "legends-arceus",
        GameVersion.SL => "scarlet",
        GameVersion.VL => "violet",
        _ => null,
    };
}
