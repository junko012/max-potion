using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Exxeguttor.App.Services;

/// <summary>
/// Versión de datos de <c>pokemon.db</c>: <c>AAAA.MM</c> o <c>AAAA.MM.R</c> (R = revisión dentro del mes,
/// 0 si falta). Se compara como tupla, nunca como texto.
/// </summary>
public readonly record struct DbDataVersion(int Year, int Month, int Revision) : IComparable<DbDataVersion>
{
    public static bool TryParse(string? text, out DbDataVersion v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var p = text.Trim().Split('.');
        if (p.Length is < 2 or > 3) return false;
        if (!int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y < 2000) return false;
        if (!int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 1 or > 12) return false;
        var r = 0;
        if (p.Length == 3 && !int.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out r)) return false;
        v = new DbDataVersion(y, m, r);
        return true;
    }

    public int CompareTo(DbDataVersion o)
    {
        var c = Year.CompareTo(o.Year);
        if (c != 0) return c;
        c = Month.CompareTo(o.Month);
        return c != 0 ? c : Revision.CompareTo(o.Revision);
    }

    public override string ToString() => $"{Year:D4}.{Month:D2}.{Revision}";
}

/// <summary>Lo que <c>pokemon.db</c> declara en su tabla <c>Metadata</c> (y en <c>PRAGMA user_version</c>).</summary>
public sealed record DbVersionInfo(int Schema, DbDataVersion Data, string? BuildDate, string? Generator)
{
    /// <summary>Versión completa <c>S.AAAA.MM.R</c> (la que lleva el tag <c>db-vS.AAAA.MM.R</c> del repo de la DB).</summary>
    public string Full => $"{Schema}.{Data}";
}

public enum DbCompatibilityStatus
{
    /// <summary>Mismo schema y datos al día respecto al mínimo que pide el app.</summary>
    Ok,
    /// <summary>No se encontró <c>pokemon.db</c>.</summary>
    Missing,
    /// <summary>Existe pero no trae <c>Metadata</c> legible (archivo ajeno o dañado).</summary>
    Unreadable,
    /// <summary>La DB tiene un schema MÁS NUEVO que el que entiende este app: hay que actualizar el app.</summary>
    AppTooOld,
    /// <summary>La DB tiene un schema MÁS VIEJO que el que pide este app: hay que actualizar la DB.</summary>
    DbTooOld,
    /// <summary>Mismo schema, pero datos anteriores al mínimo que pide el app.</summary>
    DataTooOld,
}

public sealed record DbCompatibilityResult(DbCompatibilityStatus Status, DbVersionInfo? Info)
{
    public bool IsOk => Status == DbCompatibilityStatus.Ok;
}

/// <summary>
/// Contrato app ↔ base de datos. El SCHEMA es el único acuerdo: dentro de un mismo schema los cambios de la
/// DB son solo aditivos (filas nuevas, columnas nuevas opcionales), así que cualquier DB con ese schema y datos
/// no anteriores a <see cref="MinData"/> funciona con este app. Un cambio que rompa consultas existentes sube
/// el schema y obliga a sacar app y DB juntos (el app sube <see cref="RequiredSchema"/>).
/// </summary>
public static class DbCompatibility
{
    /// <summary>Schema de <c>pokemon.db</c> que entiende este app. Subir solo junto con una DB de schema nuevo.</summary>
    public const int RequiredSchema = 1;

    /// <summary>Datos más viejos aceptados dentro del schema requerido.</summary>
    public static readonly DbDataVersion MinData = new(2026, 6, 0);

    /// <summary>Lee la versión de la DB: <c>Metadata</c> (SchemaVersion, DataVersion, BuildDate, Generator);
    /// si falta SchemaVersion cae a <c>PRAGMA user_version</c>. null si no hay forma de saberla.</summary>
    public static DbVersionInfo? Read(SqliteConnection conn)
    {
        try
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Key, Value FROM Metadata";
                using var r = cmd.ExecuteReader();
                while (r.Read()) meta[r.GetString(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
            }

            int schema;
            if (!meta.TryGetValue("SchemaVersion", out var s) ||
                !int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out schema))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA user_version";
                schema = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                if (schema <= 0) return null;
            }

            if (!meta.TryGetValue("DataVersion", out var d) || !DbDataVersion.TryParse(d, out var data)) return null;
            meta.TryGetValue("BuildDate", out var build);
            meta.TryGetValue("Generator", out var gen);
            return new DbVersionInfo(schema, data, build, gen);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Regla pura de compatibilidad (sin E/S): la que prueban los tests.</summary>
    public static DbCompatibilityResult Evaluate(DbVersionInfo? info, int requiredSchema = RequiredSchema, DbDataVersion? minData = null)
    {
        if (info == null) return new(DbCompatibilityStatus.Unreadable, null);
        if (info.Schema > requiredSchema) return new(DbCompatibilityStatus.AppTooOld, info);
        if (info.Schema < requiredSchema) return new(DbCompatibilityStatus.DbTooOld, info);
        if (info.Data.CompareTo(minData ?? MinData) < 0) return new(DbCompatibilityStatus.DataTooOld, info);
        return new(DbCompatibilityStatus.Ok, info);
    }

    /// <summary>Clave i18n del mensaje de la barra de estado para un resultado no Ok.</summary>
    public static string StatusKey(DbCompatibilityStatus s) => s switch
    {
        DbCompatibilityStatus.AppTooOld => "Status_Db_AppTooOld",
        DbCompatibilityStatus.DbTooOld or DbCompatibilityStatus.DataTooOld => "Status_Db_DbTooOld",
        _ => "Status_Db_Missing",
    };
}
