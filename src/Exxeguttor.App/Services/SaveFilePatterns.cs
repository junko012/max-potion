namespace Exxeguttor.App.Services;

/// <summary>
/// Patrones de nombre de archivo para los diálogos de abrir/guardar saves. Viven en App (no en el servicio de diálogos de UI)
/// para poder probarlos.
///
/// Por qué existen (confirmado leyendo Avalonia 11.3.8): <c>FilePickerFileTypes.All</c> se envía al selector GTK como el patrón
/// <c>*.*</c> más el tipo MIME <c>*/*</c>. El patrón exige un punto en el nombre, así que los saves SIN extensión — los de
/// Gen 6 en adelante, que se llaman <c>main</c> — no aparecían ni marcando "All"; y el tipo <c>*/*</c> no es un tipo real, así que
/// no los rescata. El commit c92d486 (20 oct) había quitado además el <c>"*"</c> que antes cubría ese caso en el filtro de saves.
/// </summary>
public static class SaveFilePatterns
{
    /// <summary>Extensiones de saves Pokémon. El selector GTK distingue mayúsculas, así que cada una va en minúscula y mayúscula
    /// (sin eso los .DSV de emuladores quedaban ocultos).</summary>
    public static readonly string[] Extensions = { "sav", "dsv", "gci", "bin", "bak", "sa1", "sav1", "srm", "dat", "fla", "gsv" };

    /// <summary>Nombre exacto de los saves de 3DS y Switch (Gen 6 en adelante), que no llevan extensión.</summary>
    public static readonly string[] ExtensionlessNames = { "main", "MAIN" };

    /// <summary>Filtro "Saves Pokémon": extensiones conocidas (ambas capitalizaciones) y los nombres sin extensión.</summary>
    public static string[] Saves() =>
        Extensions.SelectMany(e => new[] { "*." + e, "*." + e.ToUpperInvariant() })
                  .Concat(ExtensionlessNames)
                  .ToArray();

    /// <summary>Filtro "todos los archivos" real: <c>*</c> (sin exigir punto), a diferencia de <c>FilePickerFileTypes.All</c>.</summary>
    public static string[] AllFiles() => new[] { "*" };
}
