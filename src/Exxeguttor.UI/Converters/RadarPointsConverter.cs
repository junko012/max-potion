using System.Globalization;
using Avalonia.Data.Converters;

namespace Exxeguttor.UI.Converters;

/// <summary>
/// Convierte 6 valores normalizados (0-1) en List&lt;Avalonia.Point&gt; para Polygon.Points,
/// centrados en (85, 85) para un Canvas de 170×170.
///
/// Orden: [0]=HP(top) [1]=Atk [2]=Def [3]=Spe(bottom) [4]=SpD [5]=SpA
/// </summary>
public sealed class RadarPointsConverter : IMultiValueConverter
{
    public static readonly RadarPointsConverter Instance = new();

    public const double Radius  = 73.0;
    public const double CenterX = 85.0;
    public const double CenterY = 85.0;

    public static readonly double[] AnglesDeg = [270, 330, 30, 90, 150, 210];

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var pts = new List<Avalonia.Point>(6);
        for (int i = 0; i < 6; i++)
        {
            double v   = i < values.Count && values[i] is double d ? Math.Clamp(d, 0, 1) : 0;
            double rad = AnglesDeg[i] * Math.PI / 180.0;
            pts.Add(new Avalonia.Point(
                CenterX + v * Radius * Math.Cos(rad),
                CenterY + v * Radius * Math.Sin(rad)));
        }
        return pts;
    }
}
