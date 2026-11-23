using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Exxeguttor.Tests;

// Paquetes de idioma sueltos (lang/*.json): JSON válido, solo claves que existen en en.json y los mismos {n}.
// it.json además debe estar completo (los demás paquetes son parciales y caen al inglés).
public class LangPacksTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "exxeguttor.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró exxeguttor.sln");
    }

    // Los paquetes pueden traer metadatos que no son texto (ar.json: "_rtl": true); solo se leen las claves de texto.
    private static Dictionary<string, string> Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    private static readonly Regex Placeholder = new(@"\{\d+\}");

    public static IEnumerable<object[]> Packs() =>
        Directory.EnumerateFiles(Path.Combine(Root(), "lang"), "*.json").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Theory]
    [MemberData(nameof(Packs))]
    public void CadaPaquete_SoloUsaClavesConocidasYMarcadoresIguales(string code)
    {
        var en = Load(Path.Combine(Root(), "src", "Exxeguttor.UI", "i18n", "en.json"));
        var pack = Load(Path.Combine(Root(), "lang", code + ".json"));
        foreach (var (key, value) in pack)
        {
            if (key.StartsWith('_')) continue; // metadatos del paquete (p. ej. "_rtl")
            Assert.True(en.ContainsKey(key), $"{code}: la clave {key} no existe en en.json");
            Assert.Equal(
                Placeholder.Matches(en[key]).Select(m => m.Value).OrderBy(x => x),
                Placeholder.Matches(value).Select(m => m.Value).OrderBy(x => x));
        }
    }

    [Fact]
    public void ItJson_TieneTodasLasClaves()
    {
        var en = Load(Path.Combine(Root(), "src", "Exxeguttor.UI", "i18n", "en.json"));
        var it = Load(Path.Combine(Root(), "lang", "it.json"));
        var missing = en.Keys.Where(k => !it.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Faltan en it.json: " + string.Join(", ", missing));
    }
}
