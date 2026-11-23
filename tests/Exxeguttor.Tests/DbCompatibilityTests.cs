using Exxeguttor.App.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Exxeguttor.Tests;

/// <summary>Contrato app ↔ pokemon.db: parseo de versión de datos, regla de compatibilidad y lectura de Metadata.</summary>
public class DbCompatibilityTests
{
    private static DbVersionInfo V(int schema, string data) =>
        new(schema, DbDataVersion.TryParse(data, out var d) ? d : default, null, null);

    [Theory]
    [InlineData("2026.06", 2026, 6, 0)]
    [InlineData("2026.11.0", 2026, 11, 0)]
    [InlineData("2027.01.12", 2027, 1, 12)]
    public void DataVersion_Parses(string text, int y, int m, int r)
    {
        Assert.True(DbDataVersion.TryParse(text, out var v));
        Assert.Equal(new DbDataVersion(y, m, r), v);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("2026")] [InlineData("2026.13.0")]
    [InlineData("2026.00")] [InlineData("abc.def")] [InlineData("2026.11.0.1")] [InlineData("1999.01.0")]
    public void DataVersion_RejectsGarbage(string? text) => Assert.False(DbDataVersion.TryParse(text, out _));

    [Fact]
    public void DataVersion_ComparesAsTupleNotText()
    {
        DbDataVersion.TryParse("2026.9.0", out var a);
        DbDataVersion.TryParse("2026.10.0", out var b);
        DbDataVersion.TryParse("2026.10.2", out var c);
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(c) < 0);
    }

    [Fact]
    public void Evaluate_Ok_WhenSameSchemaAndDataNotOlderThanMinimum()
    {
        Assert.Equal(DbCompatibilityStatus.Ok, DbCompatibility.Evaluate(V(1, "2026.06"), 1, new DbDataVersion(2026, 6, 0)).Status);
        Assert.Equal(DbCompatibilityStatus.Ok, DbCompatibility.Evaluate(V(1, "2027.02.3"), 1, new DbDataVersion(2026, 6, 0)).Status);
    }

    [Fact]
    public void Evaluate_DetectsEachMismatch()
    {
        var min = new DbDataVersion(2026, 6, 0);
        Assert.Equal(DbCompatibilityStatus.AppTooOld, DbCompatibility.Evaluate(V(2, "2026.11"), 1, min).Status);
        Assert.Equal(DbCompatibilityStatus.DbTooOld, DbCompatibility.Evaluate(V(1, "2026.11"), 2, min).Status);
        Assert.Equal(DbCompatibilityStatus.DataTooOld, DbCompatibility.Evaluate(V(1, "2026.05"), 1, min).Status);
        Assert.Equal(DbCompatibilityStatus.Unreadable, DbCompatibility.Evaluate(null, 1, min).Status);
    }

    [Fact]
    public void StatusKeys_ExistAndDistinguishAppFromDb()
    {
        Assert.Equal("Status_Db_AppTooOld", DbCompatibility.StatusKey(DbCompatibilityStatus.AppTooOld));
        Assert.Equal("Status_Db_DbTooOld", DbCompatibility.StatusKey(DbCompatibilityStatus.DbTooOld));
        Assert.Equal("Status_Db_DbTooOld", DbCompatibility.StatusKey(DbCompatibilityStatus.DataTooOld));
        Assert.Equal("Status_Db_Missing", DbCompatibility.StatusKey(DbCompatibilityStatus.Missing));
    }

    private static SqliteConnection Mem(params string[] sql)
    {
        var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        foreach (var s in sql) { using var cmd = c.CreateCommand(); cmd.CommandText = s; cmd.ExecuteNonQuery(); }
        return c;
    }

    [Fact]
    public void Read_UsesMetadata()
    {
        using var c = Mem("CREATE TABLE Metadata(Key TEXT PRIMARY KEY, Value TEXT)",
            "INSERT INTO Metadata VALUES('SchemaVersion','1'),('DataVersion','2026.11.2'),('BuildDate','2026-11-10')");
        var i = DbCompatibility.Read(c)!;
        Assert.Equal(1, i.Schema);
        Assert.Equal("1.2026.11.2", i.Full);
        Assert.Equal("2026-11-10", i.BuildDate);
    }

    [Fact]
    public void Read_FallsBackToUserVersionForSchema()
    {
        using var c = Mem("CREATE TABLE Metadata(Key TEXT PRIMARY KEY, Value TEXT)",
            "INSERT INTO Metadata VALUES('DataVersion','2026.11')", "PRAGMA user_version = 3");
        Assert.Equal(3, DbCompatibility.Read(c)!.Schema);
    }

    [Fact]
    public void Read_ReturnsNullWhenThereIsNoMetadata()
    {
        using var c = Mem("CREATE TABLE Other(x)");
        Assert.Null(DbCompatibility.Read(c));
    }

    [Fact]
    public void BundledDatabase_IsCompatibleWithThisApp()
    {
        var db = PokemonDatabase.Instance;
        if (!db.IsAvailable) return;   // sin pokemon.db en el entorno de pruebas
        Assert.True(db.Compatibility.IsOk, $"pokemon.db no es compatible: {db.Compatibility.Status} ({db.Compatibility.Info?.Full})");
    }
}
