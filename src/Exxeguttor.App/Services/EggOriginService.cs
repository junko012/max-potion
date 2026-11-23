using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Origen de huevo eclosionado y su PID — Gen3 y Gen4 (Gen2 ya funciona sin PID; Gen5+ se revisa aparte, ver
/// <see cref="IsSupported"/>).
///
/// Por qué existe (medido contra PKHeX.Core 25.11.07, EncounterEgg3 + LegalityAnalysis):
///  · Un huevo eclosionado de Gen3 tiene Met level 0 (el 5 es el nivel actual al nacer, no el de
///    captura), ubicación de eclosión fija (<see cref="HatchLocation"/>) y Poké Ball. Con Met level 5
///    PKHeX marca <c>EggFMetLevel_0</c>.
///  · Su PID NO puede seguir ningún patrón del RNG: la correlación esperada es <c>PIDType.None</c>.
///    Un PID Method 1 (el que fuerzan <see cref="Method1Solver"/> y <see cref="Method1IvSearch"/>)
///    da <c>PIDTypeMismatch</c>. Por eso, con origen de huevo, esos solvers nunca podían tener éxito
///    y la búsqueda de IVs se quedaba recorriendo millones de semillas sin resultado.
/// </summary>
public static class EggOriginService
{
    /// <summary>Solo Gen3 por ahora (PK3 de Rubí/Zafiro/Esmeralda/Rojo Fuego/Verde Hoja: Colosseum/XD no
    /// tienen huevos). Gen4+ se agrega al revisar cada generación.</summary>
    public static bool IsSupported(PKM pkm) =>
        pkm is PK3 { Version: GameVersion.R or GameVersion.S or GameVersion.E or GameVersion.FR or GameVersion.LG }
            or PK4 { Version: GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS };

    /// <summary>Gen3 y Gen2 Cristal no guardan ubicación ni fecha de huevo: "nació de huevo" se reduce a unos pocos
    /// campos de captura (ver <see cref="HatchedMeta"/>). El atajo del check "From egg" del editor aplica
    /// únicamente ahí; Gen4+ sí tiene <c>EggLocation</c>/fechas reales y las elige el usuario.</summary>
    public static bool StoresEggOnlyAsMetLevel(PKM pkm) => TryGetDirectHatchedMeta(pkm, out _);

    /// <summary>Datos de origen de un huevo eclosionado en las generaciones que NO tienen ubicación/fecha de huevo
    /// (Gen3 y Gen2 Cristal). <see cref="MetLocation"/>, <see cref="Ball"/> y <see cref="TimeOfDay"/> son null cuando
    /// ese juego no los exige/guarda. Medido contra PKHeX 25.11.07: Gen3 → Met level 0 + ubicación de eclosión +
    /// Poké Ball; Cristal → Met level 1 + hora del día 1–3 (con 0 PKHeX marca <c>MetDetailTimeOfDay</c>).</summary>
    public readonly record struct HatchedMeta(byte MetLevel, ushort? MetLocation, byte? Ball, byte? TimeOfDay);

    public static bool TryGetDirectHatchedMeta(PKM pkm, out HatchedMeta meta)
    {
        meta = default;
        if (pkm is PK3 && IsSupported(pkm))
        {
            meta = new HatchedMeta(0, HatchLocation(pkm.Version), (byte)Ball.Poke, null);
            return true;
        }
        // Solo Cristal guarda datos de captura (HasOriginalMetLocation); en Oro/Plata no hay origen que marcar.
        if (pkm is PK2 { HasOriginalMetLocation: true } p2)
        {
            meta = new HatchedMeta(1, null, null, p2.MetTimeOfDay is >= 1 and <= 3 ? (byte)p2.MetTimeOfDay : (byte)1);
            return true;
        }
        return false;
    }

    /// <summary>¿Estos datos corresponden a un huevo eclosionado? (Gen3: Met level 0; Cristal: Met level 1.)</summary>
    public static bool IsHatchedMeta(PKM pkm, int metLevel) =>
        TryGetDirectHatchedMeta(pkm, out var m) && metLevel == m.MetLevel;

    /// <summary>¿PKHeX empareja el Pokémon con un encuentro de huevo?</summary>
    public static bool IsEggEncounter(LegalityAnalysis la) => la.EncounterMatch is IEncounterEgg;

    /// <summary>Ubicación en la que PKHeX espera que eclosione un huevo de ese juego.</summary>
    public static ushort HatchLocation(GameVersion version) => version is GameVersion.FR or GameVersion.LG
        ? Locations.HatchLocationFRLG
        : Locations.HatchLocationRSE;

    /// <summary>Marca el origen como "eclosionó de un huevo": Met level 0, ubicación de eclosión, Poké Ball y
    /// sin encuentro especial. No toca el PID (eso lo resuelve <see cref="TrySolvePid"/>).</summary>
    public static bool ApplyHatchedOrigin(PKM pkm)
    {
        if (pkm is not PK3 || !IsSupported(pkm)) return false;   // Gen4 tiene EggLocation/fechas reales: lo elige el usuario
        pkm.MetLevel = 0;
        pkm.MetLocation = HatchLocation(pkm.Version);
        pkm.Ball = (byte)Ball.Poke;
        pkm.FatefulEncounter = false;
        return true;
    }

    /// <summary>Lo que PKHeX espera de un huevo eclosionado en Gen4+ (medido contra 25.11.07 en DP/Pt/HGSS, BW,
    /// XY/ORAS, SM/USUM, SwSh, BDSP y SV): Met level 0 en Gen4 y 1 desde Gen5, Poké Ball, ubicación de huevo
    /// (guardería) y fecha de huevo. La ubicación de eclosión (<c>MetLocation</c>) no condiciona la legalidad.</summary>
    /// <param name="NeedsMetLocation">En algunos juegos (ORAS) PKHeX marca <c>EggLocationInvalid</c> si la ubicación de
    /// eclosión no es una válida para ese huevo; solo entonces <paramref name="MetLocation"/> hay que aplicarla.</param>
    public readonly record struct EggTemplate(byte MetLevel, byte Ball, ushort EggLocation, DateOnly? EggDate,
        ushort MetLocation, bool NeedsMetLocation);

    /// <summary>Pide a PKHeX el encuentro de huevo de la especie y juego del Pokémon y devuelve sus datos de origen, en
    /// vez de fijar constantes por generación (cubre juegos nuevos sin tocar código). Gen4+ solamente: Gen3 usa
    /// <see cref="ApplyHatchedOrigin"/>. Devuelve false si la especie no puede salir de un huevo en ese juego.</summary>
    public static bool TryGetEggTemplate(PKM pkm, ITrainerInfo trainer, out EggTemplate template)
    {
        template = default;
        if (pkm.Format < 4) return false;
        try
        {
            foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pkm.Clone(), ReadOnlyMemory<ushort>.Empty, pkm.Version))
            {
                if (enc is not IEncounterEgg || enc is not IEncounterConvertible conv) continue;
                var egg = conv.ConvertToPKM(trainer, EncounterCriteria.Unrestricted);

                // ¿Basta con Met level + bola + ubicación y fecha de huevo? Se comprueba con PKHeX sobre una copia.
                var probe = pkm.Clone();
                probe.MetLevel = egg.MetLevel;
                probe.Ball = egg.Ball;
                probe.EggLocation = egg.EggLocation;
                probe.EggMetDate = probe.MetDate ?? egg.EggMetDate;
                probe.RefreshChecksum();
                var needsMet = new LegalityAnalysis(probe).Results
                    .Any(r => !r.Valid && r.Identifier == CheckIdentifier.Encounter && r.Result.ToString() == "EggLocationInvalid");

                template = new EggTemplate((byte)egg.MetLevel, (byte)egg.Ball, (ushort)egg.EggLocation, egg.EggMetDate,
                    (ushort)egg.MetLocation, needsMet);
                return true;
            }
        }
        catch { /* sin plantilla: el check queda como estaba, el usuario elige los datos a mano */ }
        return false;
    }

    /// <summary>
    /// Busca un PID válido para un huevo de Gen3 conservando Naturaleza, Género, slot de Habilidad y
    /// Brillante del Pokémon (y sus IVs, que no se tocan). Cada candidato se VERIFICA con
    /// <see cref="LegalityAnalysis"/> antes de aceptarlo (mismo criterio generate-and-verify que
    /// <see cref="Method1Solver"/>). Es inmediato en la práctica; el tope de verificaciones y de
    /// vueltas evita que se cuelgue si la combinación fuera imposible.
    /// </summary>
    /// <param name="edited">El Pokémon al que se le va a poner el PID (con las ediciones pendientes ya aplicadas).</param>
    /// <param name="reference">Pokémon del que se conservan Naturaleza, Género, slot de Habilidad y Brillante. En
    /// Gen3 los tres primeros salen del PID, así que si <paramref name="edited"/> es un Pokémon recién generado
    /// (PID al azar) hay que pasar el ORIGINAL; null = el propio <paramref name="edited"/>.</param>
    public static bool TrySolvePid(PKM edited, out uint pid, PKM? reference = null, CancellationToken ct = default)
    {
        pid = edited.PID;
        if (!IsSupported(edited)) return false;
        reference ??= edited;

        // Slot de habilidad actual (0/1); -1 si no es ninguna de las dos normales (mismo criterio que
        // PokemonService.TryRegeneratePidMethod1).
        var slot = -1;
        var probe = reference.Clone();
        probe.RefreshAbility(0);
        if (probe.Ability == reference.Ability) slot = 0;
        else
        {
            probe.RefreshAbility(1);
            if (probe.Ability == reference.Ability) slot = 1;
        }

        var wantGender = reference.Gender;
        var wantNature = reference.Nature;
        var wantShiny = reference.IsShiny;
        var version = edited.Version;
        var trial = edited.Clone();
        var rng = Random.Shared;
        var verified = 0;

        // Mismo recorrido que PokemonService.TryFindPidFor: mitad baja al azar y las 65.536 mitades altas
        // de forma sistemática (la Naturaleza mezcla ambas mitades, no se puede fijar solo con una).
        for (var outer = 0; outer < 4000; outer++)
        {
            ct.ThrowIfCancellationRequested();
            var lo = (uint)rng.Next(0, 65536);
            for (var hi = 0u; hi < 65536; hi++)
            {
                var candidate = (hi << 16) | lo;
                if (slot != -1 && (int)(candidate & 1) != slot) continue;
                if ((Nature)(candidate % 25) != wantNature) continue;
                if (EntityGender.GetFromPID(edited.Species, candidate) != wantGender) continue;
                if (edited is PK3 && !Daycare3.IsValidProcPID(candidate, version)) continue;

                trial.PID = candidate;
                if (trial.IsShiny != wantShiny) continue;
                if (slot != -1) trial.RefreshAbility(slot);   // la habilidad sigue al bit del PID
                if (trial.Format == 4) trial.Nature = wantNature;   // Gen4 guarda la Naturaleza aparte; debe coincidir con PID % 25
                trial.RefreshChecksum();

                var la = new LegalityAnalysis(trial);
                if (la.Results.Any(r => !r.Valid && r.Identifier == CheckIdentifier.PID))
                {
                    if (++verified >= 50) return false;   // no debería pasar: no seguir probando sin fin
                    continue;
                }

                pid = candidate;
                return true;
            }
        }
        return false;
    }
}
