namespace Exxeguttor.App.Services;

/// <summary>
/// Enlaces del proyecto (menú Ayuda y «Acerca de»). Un solo lugar: si el repo cambia de nombre o de
/// dueño, se toca acá. Repos: <c>max-potion</c> (app) y <c>max-potion-database</c> (pokemon.db, proyecto propio).
/// </summary>
public static class AppLinks
{
    public const string RepoUrl = "https://github.com/junko012/max-potion";
    public const string DatabaseRepoUrl = "https://github.com/junko012/max-potion-database";
    public const string DatabaseReleasesUrl = DatabaseRepoUrl + "/releases";
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string LicenseUrl = RepoUrl + "/blob/master/LICENSE";
    public const string NewIssueUrl = RepoUrl + "/issues/new";
    public const string BugTemplate = "bug_report.yml";
    public const string FeatureTemplate = "feature_request.yml";
    public const string CreatorGitHubUrl = "https://github.com/junko012";
    public const string CreatorAlias = "junko012";

    /// <summary>Formulario «Sugerir una función» (sin prellenar nada).</summary>
    public static string FeatureRequestUrl => $"{NewIssueUrl}?template={FeatureTemplate}";
}
