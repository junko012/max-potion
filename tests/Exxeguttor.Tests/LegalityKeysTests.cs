using System.Text.Json;
using System.Text.RegularExpressions;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Mensajes de legalidad localizados: cada categoría que el mapper puede producir y cada clave AppText
// escrita en Services/*.cs debe existir en es y en.
public class LegalityKeysTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "exxeguttor.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró exxeguttor.sln");
    }

    private static Dictionary<string, string> Load(string lang)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "Exxeguttor.UI", "i18n", lang + ".json")))!;

    [Fact]
    public void CadaCheckIdentifier_TieneEtiquetaYFraseEnEsYEn()
    {
        var es = Load("es"); var en = Load("en");
        var missing = new List<string>();
        foreach (var id in Enum.GetValues<CheckIdentifier>())
        {
            var cat = LegalityMessageMapper.GetCategory(id);
            if (cat == null) continue;
            foreach (var key in new[] { LegalityMessageMapper.GetCategoryLabelKey(cat), LegalityMessageMapper.GetFriendlyKey(cat) })
                if (key == null || !es.ContainsKey(key) || !en.ContainsKey(key)) missing.Add($"{id}→{cat}→{key}");
        }
        Assert.True(missing.Count == 0, "Faltan claves: " + string.Join("; ", missing));
    }

    [Fact]
    public void LasClavesAppTextLiterales_ExistenEnEsYEn()
    {
        var es = Load("es"); var en = Load("en");
        var rx = new Regex("AppText\\.[TF]\\(\"([A-Za-z0-9_]+)\"");
        var dir = Path.Combine(RepoRoot(), "src", "Exxeguttor.App");
        var missing = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (key.EndsWith('_')) continue; // prefijo de familia dinámica (Review_Stat_, Trainer_Lang_)
                if (!es.ContainsKey(key) || !en.ContainsKey(key)) missing.Add(key);
            }
        Assert.True(missing.Count == 0, "Faltan claves: " + string.Join(", ", missing));
    }

    [Fact]
    public void LasClavesLegality_TienenLosMismosPlaceholdersEnEsYEn()
    {
        var es = Load("es"); var en = Load("en");
        var rx = new Regex("\\{\\d+\\}");
        foreach (var key in es.Keys.Where(k => k.StartsWith("Legality_")))
        {
            Assert.True(en.ContainsKey(key), key);
            Assert.Equal(
                rx.Matches(es[key]).Select(m => m.Value).OrderBy(x => x),
                rx.Matches(en[key]).Select(m => m.Value).OrderBy(x => x));
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Humanize_DevuelveTextoLegibleParaUnPokemonIlegal(string code)
    {
        var pk = new PK9 { Species = 25, CurrentLevel = 1, MetLevel = 100 };
        var analysis = new LegalityAnalysis(pk);
        var invalid = analysis.Results.Where(r => !r.Valid).ToList();
        Assert.NotEmpty(invalid);
        foreach (var r in invalid)
        {
            var text = LegalityHumanizer.Humanize(analysis, r, code);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("CheckResult {", text);
        }
    }
}
