using System.Globalization;

namespace Exxeguttor.App.Services;

/// <summary>
/// Escala (zoom) de la interfaz: pasos permitidos y utilidades puras (sin Avalonia, probables en xunit).
/// La UI aplica el valor con un <c>LayoutTransformControl</c> y lo guarda en <c>settings.json</c>
/// (<see cref="AppSettingsService.GetUiScale"/>).
/// </summary>
public static class InterfaceScale
{
    public const double Default = 1.0;

    /// <summary>Pasos del menú Preferencias → «Tamaño de la interfaz» y de Ctrl +/−.</summary>
    public static readonly IReadOnlyList<double> Steps = new[] { 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0 };

    /// <summary>El paso permitido más cercano. NaN, infinito o fuera de rango dan el límite o el valor por defecto.</summary>
    public static double Snap(double value)
    {
        if (double.IsNaN(value)) return Default;
        value = Math.Clamp(value, Steps[0], Steps[^1]); // también cubre ±infinito
        var best = Steps[0];
        foreach (var s in Steps)
            if (Math.Abs(s - value) < Math.Abs(best - value)) best = s;
        return best;
    }

    /// <summary>El paso siguiente (más grande); en el máximo se queda donde está.</summary>
    public static double Next(double current)
    {
        var cur = Snap(current);
        foreach (var s in Steps) if (s > cur + 0.0001) return s;
        return cur;
    }

    /// <summary>El paso anterior (más chico); en el mínimo se queda donde está.</summary>
    public static double Previous(double current)
    {
        var cur = Snap(current);
        for (var i = Steps.Count - 1; i >= 0; i--) if (Steps[i] < cur - 0.0001) return Steps[i];
        return cur;
    }

    /// <summary>«125 %» (siempre con punto de miles/decimales neutros: son valores enteros).</summary>
    public static string Format(double value)
        => Math.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
}
