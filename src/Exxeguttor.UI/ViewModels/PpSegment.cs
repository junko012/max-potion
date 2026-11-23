using Avalonia.Media;

namespace Exxeguttor.UI.ViewModels;

/// <summary>Un segmento (= 1 PP) de la barra de PP de la tarjeta de movimiento.
/// La barra siempre tiene <see cref="Total"/> segmentos (el máximo de PP que puede tener cualquier
/// movimiento: 40 base + 3 PP Ups = 64) y no cambia de tamaño al editar PP ni PP Ups.
/// Tres estados: lleno (PP actuales), faltante (hasta el máximo del movimiento, pero gastado)
/// y no disponible (por encima del máximo que permite ese movimiento).</summary>
public sealed class PpSegment
{
    /// <summary>Máximo de PP posible en el juego: PP base 40 × (5+3)/5.</summary>
    public const int Total = 64;

    private static readonly IBrush FilledBrush  = new SolidColorBrush(Color.Parse("#7D848E"));
    private static readonly IBrush MissingBrush = new SolidColorBrush(Color.Parse("#597D848E"));
    private static readonly IBrush UnavailBrush = new SolidColorBrush(Color.Parse("#17000000"));

    public IBrush Brush { get; }

    private PpSegment(IBrush brush) => Brush = brush;

    /// <summary>Barra de nivel simple (p. ej. Dynamax Level 0-10): [0, level) llenos, el resto vacío.</summary>
    public static IReadOnlyList<PpSegment> BuildLevel(int level, int total)
    {
        var list = new List<PpSegment>(total);
        for (int i = 0; i < total; i++)
            list.Add(new PpSegment(i < level ? FilledBrush : UnavailBrush));
        return list;
    }

    /// <summary>Arma los 64 segmentos: [0, PP actual) llenos, [PP actual, PP máximo) faltantes, resto no disponible.</summary>
    public static IReadOnlyList<PpSegment> Build(int currentPP, int maxPP)
    {
        int max = Math.Clamp(maxPP, 0, Total);
        int cur = Math.Clamp(currentPP, 0, max);
        var list = new List<PpSegment>(Total);
        for (int i = 0; i < Total; i++)
            list.Add(new PpSegment(i < cur ? FilledBrush : i < max ? MissingBrush : UnavailBrush));
        return list;
    }
}
