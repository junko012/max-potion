using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fase 4: guardas baratas contra los errores típicos de mover textos a claves sin poder
// verlos en pantalla — una clave usada en una vista que no existe, o una traducción con distinto número
// de placeholders ({0}, {1}) que el original.
public class LocalizationKeysTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "exxeguttor.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró exxeguttor.sln subiendo desde " + AppContext.BaseDirectory);
    }

    private static string UiDir => Path.Combine(RepoRoot(), "src", "Exxeguttor.UI");

    private static Dictionary<string, string> Load(string lang)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(UiDir, "i18n", lang + ".json")))!;

    [Fact]
    public void TodaClaveLocUsadaEnUnaVista_ExisteEnEsYEn()
    {
        var es = Load("es"); var en = Load("en");
        var rx = new Regex(@"Loc\[([A-Za-z0-9_]+)\]");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(UiDir, "Views"), "*.axaml"))
            foreach (Match m in rx.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!es.ContainsKey(key)) missing.Add($"{Path.GetFileName(file)}: {key} (falta en es)");
                if (!en.ContainsKey(key)) missing.Add($"{Path.GetFileName(file)}: {key} (falta en en)");
            }
        Assert.True(missing.Count == 0, string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void EsYEn_TienenLasMismasClaves()
    {
        var es = Load("es"); var en = Load("en");
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(es.Keys));
    }

    [Fact]
    public void LasTraducciones_ConservanLosPlaceholders()
    {
        var es = Load("es"); var en = Load("en");
        static string[] Ph(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToArray();
        var bad = en.Where(kv => es.TryGetValue(kv.Key, out var e) && !Ph(kv.Value).SequenceEqual(Ph(e)))
                    .Select(kv => kv.Key).ToList();
        Assert.True(bad.Count == 0, "Placeholders distintos entre es y en: " + string.Join(", ", bad));
    }
}
