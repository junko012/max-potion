namespace Exxeguttor.UI.Services;

/// <summary>
/// Catálogo de íconos vectoriales reutilizables para el módulo de Tips (ver
/// TipsService/TipsViewModel). Cada tip en tips.{locale}.json referencia un ícono por
/// CLAVE de texto (ej. "buscar"), nunca por índice — así agregar un tip nuevo que
/// reutiliza un concepto ya ilustrado (muy el caso común) no requiere tocar código,
/// solo el JSON. Recién cuando aparece un concepto realmente nuevo hace falta sumar
/// UNA entrada acá — tarea ocasional, desacoplada del ritmo de escribir contenido.
///
/// Cada entrada es un solo string de Path.Data combinando varios sub-trazos (mismo
/// mini-lenguaje que SVG: M/L/H/V/A/Z, múltiples figuras separadas por "M" dentro del
/// mismo string) para poder bindearse con un único &lt;Path Data="..."/&gt; por tarjeta
/// en vez de necesitar una lista de controles por ícono. Simplificación a propósito
/// respecto a los íconos de la barra de acciones (MainWindow.axaml): acá TODO es
/// Stroke de un solo grosor, sin Fill ni circles/rects separados — con el tamaño
/// chico que van a tener dentro de una tarjeta de tip, esa distinción no se nota, y
/// mantiene el catálogo entero como un simple Dictionary&lt;string,string&gt;.
///
/// Círculos: representados con dos arcos de 180° (técnica estándar de SVG/Avalonia
/// para un círculo completo, ya que un solo comando "A" no puede cerrar 360°):
///   M {cx+r},{cy} A {r},{r} 0 1 1 {cx-r},{cy} A {r},{r} 0 1 1 {cx+r},{cy} Z
///
/// IMPORTANTE: esta geometría no se pudo compilar/verificar en este entorno (sin SDK
/// de .NET disponible) — antes de dar por buena la integración, correr un
/// `dotnet build` y confirmar que cada Path.Data parsea sin excepción.
/// </summary>
public static class TipIconCatalog
{
    /// <summary>Clave de respaldo — se usa si un tip referencia una clave que no
    /// existe en este diccionario (typo en el JSON, ícono todavía no definido, etc.),
    /// para que eso nunca rompa la UI ni deje un tip sin ícono.</summary>
    public const string FallbackKey = "tips";

    public static readonly IReadOnlyDictionary<string, string> Icons =
        new Dictionary<string, string>
        {
            // ── Reutilizados de MainWindow.axaml (rail + barra de acciones) ──
            ["pokemon"] = "M20.3,12 A8.3,8.3 0 1 1 3.7,12 A8.3,8.3 0 1 1 20.3,12 Z "
                        + "M3.7,12 H9 M15,12 H20.3 "
                        + "M14.3,12 A2.3,2.3 0 1 1 9.7,12 A2.3,2.3 0 1 1 14.3,12 Z",

            ["mochila"] = "M9,7 V5.6 A1.6,1.6 0 0 1 10.6,4 H13.4 A1.6,1.6 0 0 1 15,5.6 V7 "
                        + "M5.3,7 H18.7 A1.8,1.8 0 0 1 20.5,8.8 V17.2 A1.8,1.8 0 0 1 18.7,19 "
                        + "H5.3 A1.8,1.8 0 0 1 3.5,17.2 V8.8 A1.8,1.8 0 0 1 5.3,7 Z "
                        + "M3.5,12.3 H20.5",

            ["guardar"] = "M5.5,4 H16 L18.5,6.5 V20 H5.5 Z "
                        + "M8.3,4 V8.6 H14.7 V4 "
                        + "M7.7,13.2 H16.3 V20 H7.7 Z",

            ["exportar"] = "M12,15.2 V4.5 "
                         + "M8.2,8.4 L12,4.4 L15.8,8.4 "
                         + "M5,14 V17.6 A1.8,1.8 0 0 0 6.8,19.4 H17.2 A1.8,1.8 0 0 0 19,17.6 V14",

            ["limpiar"] = "M12,3 L12,13.2 M9,13.2 H15 M9,13.2 L6.3,18.6 M15,13.2 L17.7,18.6 "
                        + "M8.3,15.4 L7.2,19.2 M11,15.4 L11,20 M13,15.4 L13,20 M15.7,15.4 L16.8,19.2",

            ["cerrar"] = "M6,6 L18,18 M18,6 L6,18",

            ["tips"] = "M12,3.5 A5.5,5.5 0 0 1 15.3,13 C14.4,13.7 14,14.6 14,15.6 V16.2 H10 "
                     + "V15.6 C10,14.6 9.6,13.7 8.7,13 A5.5,5.5 0 0 1 12,3.5 Z "
                     + "M10.2,18.4 H13.8 M10.6,20.3 H13.4",

            ["info"] = "M4.5,4 H19.5 V20 H4.5 Z "
                     + "M9.5,4 V20 "
                     + "M12.5,8.5 H16.5 M12.5,11.3 H16.5 M12.5,14.1 H15.1",

            // ── Nuevos, para el primer lote de contenido de tips.ES.json ──
            ["carpeta"] = "M4,7.5 A1.5,1.5 0 0 1 5.5,6 H9.5 L11.5,8 H18.5 A1.5,1.5 0 0 1 20,9.5 "
                        + "V17.5 A1.5,1.5 0 0 1 18.5,19 H5.5 A1.5,1.5 0 0 1 4,17.5 Z",

            ["buscar"] = "M17.5,11 A6.5,6.5 0 1 1 4.5,11 A6.5,6.5 0 1 1 17.5,11 Z "
                       + "M15.8,15.8 L20.5,20.5",

            ["teclado"] = "M3.5,7 H20.5 A1,1 0 0 1 21.5,8 V16 A1,1 0 0 1 20.5,17 H3.5 "
                        + "A1,1 0 0 1 2.5,16 V8 A1,1 0 0 1 3.5,7 Z "
                        + "M6,10.2 H6.6 M9,10.2 H9.6 M12,10.2 H12.6 M15,10.2 H15.6 M17.9,10.2 H18.5 "
                        + "M6,13.2 H18",

            ["grafico"] = "M4.5,20 V4.5 M4.5,20 H20 "
                        + "M8,20 V13 M12.5,20 V9 M17,20 V15.5",

            ["medalla"] = "M16.5,8.5 A4.5,4.5 0 1 1 7.5,8.5 A4.5,4.5 0 1 1 16.5,8.5 Z "
                        + "M9.3,12 L7,20 L12,17.5 L17,20 L14.7,12",

            ["advertencia"] = "M12,4.5 L21,19.5 H3 Z M12,10 V14.5 M12,16.7 V17",

            // ── Íconos del rail de CATEGORÍAS del módulo Tips — distintos de los
            // íconos por-tip de arriba. General reutiliza "carpeta". ──
            ["lapiz"] = "M17,3 L21,7 L11,17 L6,18.5 L7.5,13.5 Z M9,15 L9.7,14.3",
            ["balanza"] = "M12,4 V19 M8,19 H16 M5,8 H19 "
                        + "M6,8 L4,12 H8 Z M18,8 L16,12 H20 Z",
            ["chispa"] = "M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z",
        };

    /// <summary>Devuelve la geometría para <paramref name="key"/>, o la de
    /// <see cref="FallbackKey"/> si la clave no está definida (nunca lanza,
    /// nunca deja un tip sin ícono).</summary>
    public static string Resolve(string? key) =>
        key != null && Icons.TryGetValue(key, out var data) ? data : Icons[FallbackKey];

    /// <summary>Ícono del RAIL de categorías (distinto del ícono por-tip) — mapeo
    /// fijo por id de categoría (<see cref="TipEntry.CategoryKey"/>; los nombres en inglés/español se aceptan por compatibilidad), ya que esas 4 categorías están hardcodeadas en
    /// el diseño (ver mockups/tips_stack), no vienen del JSON de tips.</summary>
    public static string ResolveCategoryIcon(string categoryName) => categoryName switch
    {
        "general" or "General" => Resolve("carpeta"),
        "creation" or "Creación" or "Creation" => Resolve("chispa"),
        "editing" or "Edición" or "Editing"   => Resolve("lapiz"),
        "storage" => Resolve("mochila"),
        "legality" or "Legalidad" or "Legality" => Resolve("balanza"),
        _ => Resolve(FallbackKey),
    };
}
