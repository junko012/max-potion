using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

/// <summary>Servicios puros del menú Ayuda: limpieza de datos personales, URL del reporte, rutas, os-release.</summary>
public class HelpServicesTests
{
    private static DiagnosticsInfo Info(string? game = "Pokémon Escarlata") =>
        new("1.2.0", "abc123", "2026-11-05", ".NET 9", "25.11.7", "11.3.8", "Arch Linux", "KDE", "Wayland", "es", "en", game);

    [Fact]
    public void Scrub_ReplacesHomeAndKeepsShortUserNames()
    {
        Assert.Equal("~/.config/maxpotion/x.json", PathScrubber.Scrub("/home/erick/.config/maxpotion/x.json", "/home/erick", "erick"));
        Assert.Equal(@"~\AppData\a", PathScrubber.Scrub(@"C:\Users\Erick\AppData\a", @"C:\Users\Erick", "Erick"));
        Assert.Equal("hola al", PathScrubber.Scrub("hola al", "/home/al", "al"));
    }

    [Fact]
    public void BugReportUrl_HasFormFieldsAndNoPersonalData()
    {
        var url = IssueUrlBuilder.BuildBugReport("Falla en /home/erick/x\nlinea", Info(), "/home/erick", "erick");
        Assert.StartsWith("https://github.com/junko012/max-potion/issues/new?template=bug_report.yml", url);
        Assert.DoesNotContain("erick", url);
        Assert.DoesNotContain("\n", url);
        Assert.Contains("&version=1.2.0%20%28abc123%29", url);
        Assert.Contains("&game=Pok%C3%A9mon%20Escarlata", url);
    }

    [Fact]
    public void BugReportUrl_IsCappedWhenFieldsAreHuge()
    {
        var url = IssueUrlBuilder.BuildBugReport(new string('t', 500), Info(new string('x', 9000)));
        Assert.True(url.Length <= IssueUrlBuilder.MaxUrlLength);
    }

    [Fact]
    public void LogsDir_FollowsXdgStateHomeOnLinux()
    {
        // La ruta se arma con el separador del sistema donde corre la prueba (en el runner de Windows es "\\"): se compara normalizada.
        static string N(string path) => path.Replace('\\', '/');
        Assert.Equal("/home/e/.local/state/maxpotion/logs", N(AppPaths.ResolveLogsDir(true, null, "/home/e", "/x")));
        Assert.Equal("/s/maxpotion/logs", N(AppPaths.ResolveLogsDir(true, "/s", "/home/e", "/x")));
        Assert.Equal("/home/e/.local/state/maxpotion/logs", N(AppPaths.ResolveLogsDir(true, "rel", "/home/e", "/x")));
    }

    [Fact]
    public void OsRelease_PrefersPrettyName()
    {
        Assert.Equal("Arch Linux", SystemInfoReader.ParsePrettyName("NAME=\"Arch\"\nPRETTY_NAME=\"Arch Linux\"\n"));
        Assert.Equal("Fedora 41", SystemInfoReader.ParsePrettyName("NAME=Fedora\nVERSION_ID=41\n"));
        Assert.Null(SystemInfoReader.ParsePrettyName("# nada\n"));
    }

    [Fact]
    public void AppLog_RotatesToThreeBoundedFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mplog" + Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.Initialize(dir, 2000);
            for (var i = 0; i < 200; i++) AppLog.Info("linea " + i + " " + new string('z', 100));
            var names = Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "maxpotion.log", "maxpotion.log.1", "maxpotion.log.2" }, names);
            Assert.All(Directory.GetFiles(dir), f => Assert.True(new FileInfo(f).Length < 2300));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void CrashReport_FormatsTypeAndMessage_AndFeedsTheIssueUrl()
    {
        try { throw new InvalidOperationException("Sequence contains no elements"); }
        catch (Exception ex)
        {
            Assert.StartsWith("InvalidOperationException: Sequence contains no elements", CrashReport.Format(ex));
            Assert.Equal("InvalidOperationException: Sequence contains no elements", CrashReport.Summary(ex));
            var url = IssueUrlBuilder.BuildBugReport(CrashReport.Summary(ex), Info(), null, null, CrashReport.Format(ex));
            Assert.Contains("Error%3A", url);
            Assert.True(url.Length <= IssueUrlBuilder.MaxUrlLength);
        }
    }
}
