using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Services;

/// <summary>Arma el <see cref="DiagnosticsInfo"/> actual. Del save abierto solo toma juego y generación.</summary>
public static class DiagnosticsService
{
    public static DiagnosticsInfo Collect(string uiLocale, string dataLocale, string? saveGame) => new(
        AppInfo.Version, AppInfo.Commit, AppInfo.BuildDate,
        AppInfo.DotNet, AppInfo.PkhexVersion, AppInfo.AvaloniaVersion,
        SystemInfoReader.DescribeOs(), SystemInfoReader.Desktop(), SystemInfoReader.Session(),
        uiLocale, dataLocale, saveGame,
        PokemonDatabase.Instance.Compatibility.Info?.Full);
}
