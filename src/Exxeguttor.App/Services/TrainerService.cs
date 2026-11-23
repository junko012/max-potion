using PKHeX.Core;

namespace Exxeguttor.App.Services;

public class TrainerService
{
    private readonly SaveFileService _saveService;

    public TrainerService(SaveFileService saveService)
    {
        _saveService = saveService;
    }

    public TrainerInfo GetTrainerInfo()
    {
        var save = RequireSave();

        return new TrainerInfo
        {
            Name            = save.OT,
            TrainerId       = save.TID16,
            SecretId        = save.SID16,
            Gender          = save.Gender == 0 ? TrainerGender.Male : TrainerGender.Female,
            PlayTimeHours   = (ushort)save.PlayedHours,
            PlayTimeMinutes = (byte)save.PlayedMinutes,
            PlayTimeSeconds = (byte)save.PlayedSeconds,
            BadgeCount      = GetBadgeCount(save),
            GameName        = save.Version.ToString(),
            Generation      = save.Generation,
            Language        = save.Language,
            Money           = GetMoney(save),
            Coins           = GetCoins(save),
            BattlePoints    = GetBattlePoints(save),
            MaxMoney        = GetMaxMoney(save),
            MaxCoins        = GetMaxCoins(save),
        };
    }

    private static int GetBadgeCount(SaveFile save)
    {
        try
        {
            var prop = save.GetType().GetProperty("BadgeCount");
            if (prop != null)
                return (int)(prop.GetValue(save) ?? 0);

            var badges = save.GetType().GetProperty("Badges");
            if (badges != null)
            {
                int flags = (int)(badges.GetValue(save) ?? 0);
                return CountBits(flags);
            }
            return 0;
        }
        catch { return 0; }
    }

    private static int GetMoney(SaveFile save) => ReadIntProperty(save, "Money");
    private static int GetMaxMoney(SaveFile save) => ReadIntProperty(save, "MaxMoney");
    private static int GetMaxCoins(SaveFile save) => ReadIntProperty(save, "MaxCoins");

    private static int GetBattlePoints(SaveFile save) =>
        // BP existe en Esmeralda (Gen3), Gen4 (DP/Pt/HGSS) y Gen6 (XY/ORAS) — confirmado
        // por reflection. No existía ningún getter para esto todavía, se agrega de cero.
        ReadIntProperty(save, "BP");

    private static int GetCoins(SaveFile save) =>
        // BUG encontrado en sesión de evaluación de atributos de entrenador: PKHeX.Core
        // expone esto como "Coin" (singular), no "Coins" — confirmado por reflection sobre
        // el ensamblado. Con el nombre viejo esto nunca matcheaba en ninguna generación,
        // siempre devolvía -1 (no disponible) aunque el save sí tuviera monedas de casino
        // (Gen 1-4).
        ReadIntProperty(save, "Coin");

    /// <summary>Lee por reflection una propiedad numérica del SaveFile concreto (Money, Coin,
    /// BP, MaxMoney, MaxCoins...) y la devuelve como int, sea cual sea su tipo real interno.
    /// -1 = la propiedad no existe en esta generación (esperado, se oculta el campo en la UI).
    ///
    /// Dos bugs encontrados en sesión de evaluación de entrenador, ambos con el mismo
    /// síntoma (el campo se ocultaba sin ningún aviso):
    /// 1. Money/Coin son `uint` en PKHeX.Core (confirmado por reflection en Gen1/2/3/4/6),
    ///    no `int` — un cast directo "(int)raw" tira InvalidCastException al hacer unboxing
    ///    (exige tipo exacto). Corregido con Convert.ToInt32, que sirve para cualquier tipo
    ///    numérico real de adentro (uint, ushort, etc.), no solo int.
    /// 2. Si el valor real (uint) es mayor a int.MaxValue (~2147 millones) — típico en saves
    ///    editados con un truco de "dinero infinito" — Convert.ToInt32 también tira, esta
    ///    vez por overflow, no por tipo. Acá se lo achica al máximo de int en vez de perder
    ///    el dato entero: sigue siendo un número gigante y visible en vez de ocultarse.</summary>
    private static int ReadIntProperty(SaveFile save, string propertyName)
    {
        try
        {
            var prop = save.GetType().GetProperty(propertyName);
            if (prop == null) return -1;
            var raw = prop.GetValue(save);
            if (raw == null) return 0;
            try { return Convert.ToInt32(raw); }
            catch (OverflowException) { return int.MaxValue; }
        }
        catch { return -1; }
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0) { count += value & 1; value >>= 1; }
        return count;
    }

    /// <summary>Aplica ediciones de entrenador directo sobre el SaveFile real — simétrico a
    /// GetTrainerInfo/ReadIntProperty, mismo mecanismo de reflection para Money/Coin/BP por
    /// el mismo motivo (son uint en PKHeX.Core, Convert.ChangeType respeta el tipo real en
    /// vez de asumir int). Parámetros null = "no se tocó ese campo", no "ponerlo en 0/vacío".
    /// Llamado por EditApplyService (Exxeguttor.UI) al confirmar la exportación — antes de
    /// esto, ninguna edición de entrenador se escribía al SaveFile real (ver GAP CRÍTICO).</summary>
    public void ApplyEdits(string? name, int? tid, int? sid, bool? isFemale,
        int? money, int? coins, int? battlePoints)
    {
        var save = RequireSave();

        if (name != null) save.OT = name;
        if (tid.HasValue) save.TID16 = (ushort)tid.Value;
        if (sid.HasValue) save.SID16 = (ushort)sid.Value;
        if (isFemale.HasValue) save.Gender = (byte)(isFemale.Value ? 1 : 0);
        if (money.HasValue) WriteIntProperty(save, "Money", money.Value);
        if (coins.HasValue) WriteIntProperty(save, "Coin", coins.Value);
        if (battlePoints.HasValue) WriteIntProperty(save, "BP", battlePoints.Value);
    }

    /// <summary>Escribe por reflection una propiedad numérica del SaveFile concreto (Money,
    /// Coin, BP...) respetando su tipo real interno (uint en la mayoría de los casos) — mismo
    /// motivo que ReadIntProperty del lado de lectura: un Convert.ChangeType al tipo real de
    /// la propiedad evita el mismo InvalidCastException que ya se documentó ahí.</summary>
    private static void WriteIntProperty(SaveFile save, string propertyName, int value)
    {
        try
        {
            var prop = save.GetType().GetProperty(propertyName);
            if (prop == null || !prop.CanWrite) return;
            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(save, converted);
        }
        catch { /* mismo criterio defensivo que ReadIntProperty: si esta generación no tiene
                   el campo o el tipo no convierte limpio, no rompemos la exportación entera
                   por un campo de entrenador puntual. */ }
    }

    private SaveFile RequireSave()
        => _saveService.CurrentSave
            ?? throw new InvalidOperationException("No save file is loaded.");
}

public record TrainerInfo
{
    public string Name            { get; init; } = string.Empty;
    public ushort TrainerId       { get; init; }
    public ushort SecretId        { get; init; }
    public TrainerGender Gender   { get; init; }
    public ushort PlayTimeHours   { get; init; }
    public byte   PlayTimeMinutes { get; init; }
    public byte   PlayTimeSeconds { get; init; }
    public int    BadgeCount      { get; init; }
    public string GameName        { get; init; } = string.Empty;
    public int    Generation      { get; init; }
    public int    Language        { get; init; }
    public int    Money           { get; init; }
    public int    Coins           { get; init; }
    public int    BattlePoints    { get; init; }
    public int    MaxMoney        { get; init; }
    public int    MaxCoins        { get; init; }

    public string PlayTimeFormatted =>
        string.Format("{0}:{1:D2}:{2:D2}", PlayTimeHours, PlayTimeMinutes, PlayTimeSeconds);
}

public enum TrainerGender { Male, Female }
