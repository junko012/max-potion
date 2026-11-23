using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Paleta de color por categoría de cinta (tab Ribbons). Decisión de diseño: solo las
/// categorías "trofeo" (Champion, Battle, Contest, Mark) tienen color propio — Common/Event/
/// Memory quedan en gris neutro para no saturar visualmente un panel angosto (~380px) con 7
/// tarjetas coloreadas a la vez. Los 3 grises neutros son deliberadamente distintos entre sí
/// (no el mismo gris repetido) para que las 3 categorías sigan siendo distinguibles de un
/// vistazo. "Todas" no es una categoría real — solo se usa para el estado "seleccionado" del
/// chip de filtro correspondiente, con su propio gris oscuro (no pastel, para que se lea como
/// "sin filtro específico" en vez de como una categoría más).
/// </summary>
public static class RibbonCategoryColorPalette
{
    public sealed record Entry(Color Background, Color Border, Color Text, string DisplayName);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Categorías con color propio ──────────────────────────────────
        ["Champion"] = new(Color.Parse("#FAEEDA"), Color.Parse("#EF9F27"), Color.Parse("#412402"), "Campeón"),
        ["Battle"]   = new(Color.Parse("#FAECE7"), Color.Parse("#D85A30"), Color.Parse("#4A1B0C"), "Batalla"),
        ["Contest"]  = new(Color.Parse("#FBEAF0"), Color.Parse("#D4537E"), Color.Parse("#4B1528"), "Concursos"),
        ["Mark"]     = new(Color.Parse("#EEEDFE"), Color.Parse("#7F77DD"), Color.Parse("#26215C"), "Marcas"),

        // ── Categorías neutras: 3 grises distintos, de más claro a más oscuro ────────────
        ["Memory"]   = new(Color.Parse("#F1EFE8"), Color.Parse("#B4B2A9"), Color.Parse("#444441"), "Memoria"),
        ["Event"]    = new(Color.Parse("#E5E2D8"), Color.Parse("#A3A196"), Color.Parse("#3A3937"), "Evento"),
        ["Common"]   = new(Color.Parse("#D8D5C8"), Color.Parse("#8F8D82"), Color.Parse("#2C2C2A"), "General"),

        // ── Pseudo-categoría del chip "sin filtro" ───────────────────────
        ["Todas"]    = new(Color.Parse("#2C2C2A"), Color.Parse("#2C2C2A"), Colors.White,           "Todas"),
    };

    private static readonly Entry Fallback =
        new(Color.Parse("#D8D5C8"), Color.Parse("#8F8D82"), Color.Parse("#2C2C2A"), "Otras");

    /// <summary>Nombre de la categoría en el idioma actual de la UI (claves Ribbon_Cat_*).</summary>
    public static string LocalizedName(string? category) => category switch
    {
        "Todas"    => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_All"),
        "Champion" => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Champion"),
        "Battle"   => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Battle"),
        "Contest"  => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Contest"),
        "Mark"     => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Mark"),
        "Memory"   => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Memory"),
        "Event"    => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Event"),
        "Common"   => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Common"),
        _          => Exxeguttor.UI.Services.UiText.T("Ribbon_Cat_Other"),
    };

    public static Entry Get(string? category) =>
        category != null && Entries.TryGetValue(category, out var e) ? e : Fallback;
}

/// <summary>Color de fondo de tarjeta/chip por categoría (siempre visible, no depende de selección).</summary>
public class RibbonCategoryBackgroundConverter : IValueConverter
{
    public static readonly RibbonCategoryBackgroundConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(RibbonCategoryColorPalette.Get(value as string).Background);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Color de borde de tarjeta/chip por categoría.</summary>
public class RibbonCategoryBorderConverter : IValueConverter
{
    public static readonly RibbonCategoryBorderConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(RibbonCategoryColorPalette.Get(value as string).Border);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Color de texto (siempre el tono oscuro/legible de la misma familia) por categoría.</summary>
public class RibbonCategoryTextConverter : IValueConverter
{
    public static readonly RibbonCategoryTextConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(RibbonCategoryColorPalette.Get(value as string).Text);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Fondo del chip de filtro: coloreado con la paleta de su categoría SOLO si es el
/// chip actualmente seleccionado (values[0]=Category del chip, values[1]=IsSelected del mismo
/// chip — ver RibbonChipViewModel); transparente el resto del tiempo, para no repetir 7 chips
/// coloreados a la vez (el color "vive" en la selección activa, igual que en las tarjetas).</summary>
public class RibbonChipBackgroundConverter : IMultiValueConverter
{
    public static readonly RibbonChipBackgroundConverter Instance = new();
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var category   = values.Count > 0 ? values[0] as string : null;
        var isSelected = values.Count > 1 && values[1] is true;
        if (!isSelected) return Brushes.Transparent;
        return new SolidColorBrush(RibbonCategoryColorPalette.Get(category).Border);
    }
}

/// <summary>Texto del chip: el tono oscuro de su paleta cuando está seleccionado (mismo
/// criterio de contraste que las tarjetas), gris tenue el resto del tiempo.</summary>
public class RibbonChipForegroundConverter : IMultiValueConverter
{
    public static readonly RibbonChipForegroundConverter Instance = new();
    private static readonly SolidColorBrush Muted = new(Color.Parse("#5F5E5A"));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var category   = values.Count > 0 ? values[0] as string : null;
        var isSelected = values.Count > 1 && values[1] is true;
        if (!isSelected) return Muted;
        return new SolidColorBrush(RibbonCategoryColorPalette.Get(category).Text);
    }
}

/// <summary>
/// Colores de la tarjeta-medalla de una cinta (tab Ribbons). values[0]=Category, values[1]=IsObtained.
/// ConverterParameter: "bg" | "border" | "fg" | "icon".
/// Obtenida → paleta de su categoría; pendiente → fondo/borde transparentes (el borde punteado gris
/// lo dibuja un Rectangle aparte, Border no soporta guiones) y texto/medalla atenuados.
/// </summary>
public class RibbonTileBrushConverter : IMultiValueConverter
{
    public static readonly RibbonTileBrushConverter Instance = new();
    private static readonly SolidColorBrush MutedText = new(Color.Parse("#8A8880"));
    private static readonly SolidColorBrush MutedIcon = new(Color.Parse("#C9C7BE"));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = values.Count > 0 ? values[0] as string : null;
        var obtained = values.Count > 1 && values[1] is true;
        var pal      = RibbonCategoryColorPalette.Get(category);

        return (parameter as string, obtained) switch
        {
            ("bg",     true)  => new SolidColorBrush(pal.Background),
            ("border", true)  => new SolidColorBrush(pal.Border),
            ("fg",     true)  => new SolidColorBrush(pal.Text),
            ("icon",   true)  => new SolidColorBrush(pal.Border),
            ("fg",     false) => MutedText,
            ("icon",   false) => MutedIcon,
            _                 => Brushes.Transparent,
        };
    }
}
