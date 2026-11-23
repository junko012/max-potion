using System.Reflection;

namespace Exxeguttor.UI.Services;

/// <summary>Versión, commit y fecha de compilación de la app (de los metadatos del ensamblado).</summary>
public static class AppInfo
{
    public const string ProductName = "Max Potion";

    private static readonly Assembly Asm = typeof(AppInfo).Assembly;

    private static string[] SplitInformational()
    {
        var raw = Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = raw.IndexOf('+');
        return plus < 0 ? [raw, ""] : [raw[..plus], raw[(plus + 1)..]];
    }

    public static string Version
    {
        get
        {
            var v = SplitInformational()[0];
            return v.Length > 0 ? v : Asm.GetName().Version?.ToString(3) ?? "?";
        }
    }

    /// <summary>Hash corto del commit (7 caracteres), o null si el build no lo trae.</summary>
    public static string? Commit
    {
        get
        {
            var c = SplitInformational()[1];
            return c.Length == 0 ? null : (c.Length > 7 ? c[..7] : c);
        }
    }

    public static string? BuildDate =>
        Asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BuildDate")?.Value;

    public static string DotNet => $".NET {Environment.Version.ToString(3)}";

    public static string PkhexVersion =>
        typeof(PKHeX.Core.SaveFile).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static string AvaloniaVersion =>
        typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3) ?? "?";
}
