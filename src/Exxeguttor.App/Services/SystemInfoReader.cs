using System.Runtime.InteropServices;

namespace Exxeguttor.App.Services;

/// <summary>Descripción del sistema operativo, escritorio y sesión para el diagnóstico (sin datos personales).</summary>
public static class SystemInfoReader
{
    /// <summary>Nombre «bonito» del SO: <c>PRETTY_NAME</c> de /etc/os-release en Linux; la descripción de .NET en el resto.</summary>
    public static string DescribeOs()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var path in new[] { "/etc/os-release", "/usr/lib/os-release" })
                    if (File.Exists(path) && ParsePrettyName(File.ReadAllText(path)) is { } name)
                        return name;
            }
            catch { /* cae a la descripción genérica */ }
        }
        return RuntimeInformation.OSDescription.Trim();
    }

    public static string? Desktop() => Clean(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));

    /// <summary>«X11» / «Wayland» (solo Linux; null si no se puede saber).</summary>
    public static string? Session()
    {
        if (!OperatingSystem.IsLinux()) return null;
        var type = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        return type?.ToLowerInvariant() switch
        {
            "x11" => "X11",
            "wayland" => "Wayland",
            { Length: > 0 } other => other,
            _ => null,
        };
    }

    /// <summary>Extrae <c>PRETTY_NAME</c> (o NAME + VERSION_ID) del contenido de os-release. Pública para pruebas.</summary>
    public static string? ParsePrettyName(string osRelease)
    {
        string? pretty = null, name = null, version = null;
        foreach (var raw in osRelease.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0 || line[0] == '#') continue;
            var key = line[..eq];
            var value = line[(eq + 1)..].Trim().Trim('"', '\'');
            switch (key)
            {
                case "PRETTY_NAME": pretty = value; break;
                case "NAME": name = value; break;
                case "VERSION_ID": version = value; break;
            }
        }
        if (!string.IsNullOrWhiteSpace(pretty)) return pretty;
        return string.IsNullOrWhiteSpace(name) ? null : (version is { Length: > 0 } ? $"{name} {version}" : name);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
