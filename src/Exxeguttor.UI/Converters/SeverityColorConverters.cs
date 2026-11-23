using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Etapa 4.6.6 — tab Diagnóstico. Un DiagnosticStep solo puede traer
/// <see cref="Severity.Invalid"/> (bloquea la legalidad, mismo rojo que las fichas de la
/// cabecera) o <see cref="Severity.Fishy"/> (sospechoso pero ya es legal así como está —
/// tono ámbar, más suave, para no confundirlo con un problema real que haya que arreglar).
/// </summary>
public class SeverityBackgroundConverter : IValueConverter
{
    public static readonly SeverityBackgroundConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Severity.Invalid => new SolidColorBrush(Color.Parse("#FBEAEA")),
            Severity.Fishy    => new SolidColorBrush(Color.Parse("#FFF6E0")),
            _                 => Brushes.Transparent,
        };

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}

public class SeverityBorderConverter : IValueConverter
{
    public static readonly SeverityBorderConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Severity.Invalid => new SolidColorBrush(Color.Parse("#C0392B")),
            Severity.Fishy    => new SolidColorBrush(Color.Parse("#B8860B")),
            _                 => Brushes.Transparent,
        };

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}

/// <summary>Mismo criterio de color que arriba, para el texto de la categoría/severidad
/// dentro de la tarjeta (el fondo tenue por sí solo no da suficiente contraste para texto
/// del mismo color en tamaño chico).</summary>
public class SeverityTextConverter : IValueConverter
{
    public static readonly SeverityTextConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Severity.Invalid => new SolidColorBrush(Color.Parse("#C0392B")),
            Severity.Fishy    => new SolidColorBrush(Color.Parse("#8A6100")),
            _                 => Brushes.Black,
        };

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}

/// <summary>Etiqueta amigable de Severity para mostrar en la tarjeta — nunca el nombre del
/// enum en inglés crudo ("Invalid"/"Fishy") directo en la UI.</summary>
public class SeverityLabelConverter : IValueConverter
{
    public static readonly SeverityLabelConverter Instance = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            Severity.Invalid => UiText.T("Legality_Severity_Invalid"),
            Severity.Fishy    => UiText.T("Legality_Severity_Fishy"),
            _                 => string.Empty,
        };

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
        => throw new System.NotSupportedException();
}
