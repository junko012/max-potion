using System.Globalization;
using Avalonia.Data.Converters;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// int == ConverterParameter (como texto, ej. "3") → bool. Se usa con Classes.hot="{Binding
/// HoveredStat, Converter=..., ConverterParameter=N}" para resaltar a la vez la fila de un stat y
/// su vértice/etiqueta/eje en el radar (tab IVs/EVs). Índices = orden del radar:
/// 0=PS 1=Atq 2=Def 3=Vel 4=DefE 5=AtqE.
/// </summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int v && parameter is not null
           && int.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
           && v == p;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
