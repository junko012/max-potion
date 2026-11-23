using System.Text.Json;
using Xunit;

namespace Exxeguttor.Tests;

// Todos los tips_XX.json deben ser el mismo contenido en distintos idiomas (mismos íconos, mismas categorías, mismo orden).
// (El proyecto de tests solo referencia Exxeguttor.App: se leen los JSON directamente, sin tipos de la UI.)
public class TipsLocalizationTests
{
    private sealed record Tip(string Category, string CategoryId, string Icon, string Title, string Text, string Sample);

    private static string TipsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "exxeguttor.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Exxeguttor.UI", "Assets", "Tips");
    }

    private static List<Tip> Read(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TipsDir(), $"tips_{code}.json")));
        return doc.RootElement.EnumerateArray().Select(e => new Tip(
            e.GetProperty("category").GetString() ?? "",
            e.GetProperty("categoryId").GetString() ?? "",
            e.GetProperty("icon").GetString() ?? "",
            e.GetProperty("title").GetString() ?? "",
            e.GetProperty("text").GetString() ?? "",
            e.TryGetProperty("sample", out var sm) ? sm.GetString() ?? "" : "")).ToList();
    }

    public static IEnumerable<object[]> Codes() =>
        new[] { "ES", "AR", "DE", "FR", "HI", "ID", "IT", "JA", "KO", "PT", "RU", "ZH" }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Codes))]
    public void CadaIdioma_TieneLosMismosTipsQueEn(string code)
    {
        var en = Read("EN"); var other = Read(code);
        Assert.NotEmpty(en);
        Assert.Equal(en.Count, other.Count);
        Assert.Equal(en.Select(t => t.Icon), other.Select(t => t.Icon));
        Assert.Equal(en.Select(t => t.CategoryId), other.Select(t => t.CategoryId));
        Assert.Equal(en.Select(t => t.Sample), other.Select(t => t.Sample));
        Assert.All(other, t => Assert.False(string.IsNullOrWhiteSpace(t.Category) || string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Text)));
    }

    [Fact]
    public void LosTipsEnEsYEn_SonTextosDistintos()
    {
        var es = Read("ES"); var en = Read("EN");
        Assert.NotEqual(en.Select(t => t.Title), es.Select(t => t.Title));
    }

    // Ids de miniaturas que TipSampleView sabe dibujar (Views/TipSampleView.axaml): un id desconocido no muestra nada.
    private static readonly HashSet<string> KnownSamples = new()
    {
        "", "steps", "severity", "autofix", "retry", "ivbanner", "ivbuttons", "recreate", "gomoves", "methods", "nickname",
        "export", "review", "undo", "langs", "keys", "search", "shift", "tab-ivs", "tab-sets", "tab-special", "tab-ribbons",
        "picker", "release", "releaseblock", "bagtiles", "bagrow", "bagmax", "bagstore",
    };

    [Fact]
    public void LasMiniaturas_UsanSoloIdsConocidos()
    {
        Assert.All(Read("EN"), t => Assert.Contains(t.Sample, KnownSamples));
    }
}
