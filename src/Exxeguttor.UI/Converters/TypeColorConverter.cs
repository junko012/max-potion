using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
using Exxeguttor.App.Services;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Convierte un nombre de tipo Pokémon (slug o display) al color
/// de fondo del badge de ese tipo.
/// </summary>
public class TypeColorConverter : IValueConverter
{
    public static readonly TypeColorConverter Instance = new();

    private static readonly Dictionary<string, Color> TypeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["normal"]   = Color.Parse("#A8A878"),
        ["fire"]     = Color.Parse("#F08030"),
        ["water"]    = Color.Parse("#6890F0"),
        ["electric"] = Color.Parse("#F8D030"),
        ["grass"]    = Color.Parse("#78C850"),
        ["ice"]      = Color.Parse("#98D8D8"),
        ["fighting"] = Color.Parse("#C03028"),
        ["poison"]   = Color.Parse("#A040A0"),
        ["ground"]   = Color.Parse("#E0C068"),
        ["flying"]   = Color.Parse("#A890F0"),
        ["psychic"]  = Color.Parse("#F85888"),
        ["bug"]      = Color.Parse("#A8B820"),
        ["rock"]     = Color.Parse("#B8A038"),
        ["ghost"]    = Color.Parse("#705898"),
        ["dragon"]   = Color.Parse("#7038F8"),
        ["dark"]     = Color.Parse("#705848"),
        ["steel"]    = Color.Parse("#B8B8D0"),
        ["fairy"]    = Color.Parse("#EE99AC"),
        // Nombres en español (para cuando el locale es es)
        ["fuego"]    = Color.Parse("#F08030"),
        ["agua"]     = Color.Parse("#6890F0"),
        ["eléctrico"]= Color.Parse("#F8D030"),
        ["planta"]   = Color.Parse("#78C850"),
        ["hielo"]    = Color.Parse("#98D8D8"),
        ["lucha"]    = Color.Parse("#C03028"),
        ["veneno"]   = Color.Parse("#A040A0"),
        ["tierra"]   = Color.Parse("#E0C068"),
        ["volador"]  = Color.Parse("#A890F0"),
        ["psíquico"] = Color.Parse("#F85888"),
        ["bicho"]    = Color.Parse("#A8B820"),
        ["roca"]     = Color.Parse("#B8A038"),
        ["fantasma"] = Color.Parse("#705898"),
        ["dragón"]   = Color.Parse("#7038F8"),
        ["siniestro"]= Color.Parse("#705848"),
        ["acero"]    = Color.Parse("#B8B8D0"),
        ["hada"]     = Color.Parse("#EE99AC"),
        ["normal"]   = Color.Parse("#A8A878"),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value as string ?? string.Empty;
        return new SolidColorBrush(GetBaseColor(type));
    }

    /// <summary>Color base (saturado) de un tipo, reutilizado por los conversores "suaves" de tarjeta.</summary>
    public static Color GetBaseColor(string? type) =>
        // TypeNames.ToKey acepta el nombre inglés o el del idioma de los datos (p. ej. el Tera Type
        // viene de strings.Types): sin esto el color se perdía en cuanto el idioma no era en/es.
        TypeColors.TryGetValue(TypeNames.ToKey(type), out var c) ? c : Color.Parse("#888888");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
