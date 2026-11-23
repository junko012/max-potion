using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Aplica a un Pokémon las ediciones de "origen de huevo" (Fecha de captura, "From egg", Ubicación y Fecha de huevo) y deja
/// el resultado en un estado que PKHeX acepta. Lo usan, con el mismo código, la vista previa de legalidad
/// (<c>EditSessionService</c>) y la escritura real al exportar (<c>EditApplyService</c>): antes cada una tenía su propio
/// bloque y no coincidían — la previa no aplicaba la fecha de huevo ni <c>MetDate</c>, así que el panel decía "Legal"
/// sobre algo distinto de lo que se terminaba escribiendo.
///
/// Reglas medidas contra PKHeX.Core 25.11.07:
///  · Ubicación y fecha de huevo van JUNTAS: ubicación sin fecha, o fecha sin ubicación, da <c>Memory:DateCalendarInvalidEgg</c>
///    (es lo que apareció al recargar un save exportado con solo la fecha escrita).
///  · Un huevo eclosionado de Gen4–6 exige el tipo de terreno de captura en "ninguno" (<c>IGroundTile.GroundTile</c>); una
///    captura en hierba o cueva lo trae distinto y da <c>Encounter:EncTypeMismatch</c> (12 de 12 capturas reales de DP/Pt/HGSS).
///  · Tildar "From egg" expresa la intención de que el Pokémon salga de un huevo: si tras aplicar las ediciones no quedó
///    ninguna ubicación de huevo, se completa con la plantilla de PKHeX en vez de perder esa intención en silencio.
/// </summary>
public static class EggEditApplier
{
    /// <summary>Aplica fecha de captura, "From egg", ubicación y fecha de huevo, y normaliza el resultado.</summary>
    public static void Apply(PKM pkm, IReadOnlyDictionary<string, object?> edits)
    {
        if (edits.TryGetValue("MetDate", out var metDateVal) && metDateVal is string metDateStr
            && DateOnly.TryParseExact(metDateStr, "yyyy-MM-dd", out var metDate))
        {
            pkm.MetDate = metDate;
        }

        // Sin ninguna edición de huevo no se toca nada: un Pokémon editado por otra razón conserva sus datos de huevo tal cual.
        if (!edits.ContainsKey("HatchedFromEgg") && !edits.ContainsKey("SelectedEggLocation") && !edits.ContainsKey("EggDate")) return;

        var hatched = edits.TryGetValue("HatchedFromEgg", out var hv) && hv is bool hb ? hb : (bool?)null;

        if (hatched == false)
        {
            // Destildado a mano: "este Pokémon ya no tiene origen de huevo".
            pkm.EggLocation = NoLocation(pkm);
            pkm.EggMetDate = null;
            return;
        }

        if (edits.TryGetValue("SelectedEggLocation", out var locVal) && locVal is ComboItem loc)
            pkm.EggLocation = (ushort)loc.Value;

        if (edits.TryGetValue("EggDate", out var dateVal) && dateVal is string dateStr
            && DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", out var eggDate))
        {
            pkm.EggMetDate = eggDate;
        }

        if (hatched == true) FillFromTemplateIfMissing(pkm, edits);

        Normalize(pkm);
    }

    /// <summary>Ubicación y fecha de huevo siempre juntas, y terreno de captura en "ninguno" cuando hay origen de huevo.</summary>
    public static void Normalize(PKM pkm)
    {
        if (pkm.Format < 4) return;   // Gen1–3 no tienen fecha ni ubicación de huevo

        if (pkm.EggLocation == NoLocation(pkm))
        {
            if (pkm.EggMetDate is not null) pkm.EggMetDate = null;
            return;
        }

        if (pkm.EggMetDate is null) pkm.EggMetDate = pkm.MetDate ?? DateOnly.FromDateTime(DateTime.Today);
        if (pkm is IGroundTile ground && ground.GroundTile != default) ground.GroundTile = default;
    }

    private static ushort NoLocation(PKM pkm) => pkm is PB8 ? Locations.Default8bNone : (ushort)0;

    /// <summary>"From egg" tildado pero sin ubicación de huevo: completa con la plantilla de PKHeX lo que el usuario no editó.
    /// No se usa en Gen3/Cristal (su origen de huevo es solo Met level + bola, ver <see cref="EggOriginService"/>).</summary>
    private static void FillFromTemplateIfMissing(PKM pkm, IReadOnlyDictionary<string, object?> edits)
    {
        if (pkm.Format < 4 || pkm.IsEgg || EggOriginService.StoresEggOnlyAsMetLevel(pkm)) return;
        if (pkm.EggLocation != NoLocation(pkm)) return;

        var trainer = new SimpleTrainerInfo(pkm.Version)
        {
            OT = pkm.OriginalTrainerName,
            TID16 = pkm.TID16,
            SID16 = pkm.SID16,
            Language = pkm.Language,
            Gender = pkm.OriginalTrainerGender,
        };
        if (!EggOriginService.TryGetEggTemplate(pkm, trainer, out var t)) return;

        pkm.EggLocation = t.EggLocation;
        if (!edits.ContainsKey("MetLevel")) pkm.MetLevel = t.MetLevel;
        if (!edits.ContainsKey("BallName") && !edits.ContainsKey("BallId")) pkm.Ball = t.Ball;
        if (t.NeedsMetLocation && !edits.ContainsKey("SelectedMetLocation")) pkm.MetLocation = t.MetLocation;
        pkm.EggMetDate ??= pkm.MetDate ?? t.EggDate;
    }
}
