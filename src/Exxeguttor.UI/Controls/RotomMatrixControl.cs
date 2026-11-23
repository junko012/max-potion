using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Exxeguttor.UI.Controls;

/// <summary>Estado visual de la escena del modal "Buscar IVs legales".</summary>
public enum RotomMatrixMode
{
    /// <summary>Pantalla oscura con lluvia de código; Rotom cambia de forma "hackeado".</summary>
    Searching,
    /// <summary>PC quieta, borde verde, Rotom estable.</summary>
    Found,
    /// <summary>PC quieta, borde ámbar, Rotom apagado.</summary>
    NoSolution,
}

/// <summary>
/// Escena "Rotom PC procesando" del modal de búsqueda de semilla (mockups/ivs_solver_modal/).
/// Se dibuja a mano (DrawingContext) con un DispatcherTimer:
///   · lluvia de dígitos hexadecimales estilo Matrix detrás de Rotom;
///   · Rotom cambia de forma "hackeado": se disuelve de arriba hacia abajo en código (bloques de
///     6×6 px) y se rematerializa como otra forma. La velocidad sube de 1× a 3× con
///     <see cref="Progress"/> (el progreso REAL de la búsqueda);
///   · teclado con teclas que se iluminan en secuencia.
/// Solo anima mientras <see cref="Mode"/> es Searching y <see cref="IsRunning"/> es true; en los
/// demás estados queda un cuadro fijo (y se detiene el timer: sin costo de CPU).
/// No tiene hijos visuales ni bindings internos. Tamaño pensado: 200 × 150.
/// </summary>
public class RotomMatrixControl : Control
{
    // ── Propiedades ──────────────────────────────────────────────────────────────────────
    public static readonly StyledProperty<RotomMatrixMode> ModeProperty =
        AvaloniaProperty.Register<RotomMatrixControl, RotomMatrixMode>(nameof(Mode));

    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<RotomMatrixControl, bool>(nameof(IsRunning));

    /// <summary>Progreso real de la búsqueda (0..1): acelera el cambio de formas de 1× a 3×.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<RotomMatrixControl, double>(nameof(Progress));

    /// <summary>Sprites de Rotom: [0] base, [1] Calor, [2] Lavadora, [3] Frío, [4] Ventilador, [5] Corte.</summary>
    public static readonly StyledProperty<IReadOnlyList<Bitmap?>?> SpritesProperty =
        AvaloniaProperty.Register<RotomMatrixControl, IReadOnlyList<Bitmap?>?>(nameof(Sprites));

    public RotomMatrixMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public IReadOnlyList<Bitmap?>? Sprites { get => GetValue(SpritesProperty); set => SetValue(SpritesProperty, value); }

    static RotomMatrixControl()
    {
        AffectsRender<RotomMatrixControl>(ModeProperty, SpritesProperty);
        IsRunningProperty.Changed.AddClassHandler<RotomMatrixControl>((c, _) => c.UpdateTimer());
        ModeProperty.Changed.AddClassHandler<RotomMatrixControl>((c, _) => c.UpdateTimer());
    }

    // ── Máscaras de bloques opacos (16×16 bloques de 6 px) de los 6 sprites, calculadas una vez
    //    a partir de los PNG de Assets/sprites/pokemon (alfa > 40). Evita decodificar píxeles en
    //    tiempo de ejecución. ─────────────────────────────────────────────────────────────────
    private static readonly string[] Masks =
    [
        "0000000000000000000000000000000000000000000000000000000010000000000000011000000000000001100000000000001111000000000000111100000000001111111000000000111111111000000111111111000000111000001110000000000000001000000000000000000000000000000000000000000000000000",
        "0000000000000000000000000000000000000000000000000001100000000000001111000011100000011111001110000001111111111000000111111111000000001111111100000000111111110000000011111110000000001111110000000000000110000000000000000000000000000000000000000000000000000000",
        "0000000000000000000000000000000000000001100000000000000110000000000000011110000000000111111100000001111111110000001111111111000000111111111111000111111111111110001111111111111000000111111011000000001111100000000000000000000000000000000000000000000000000000",
        "0000000000000000000000000000000000000011100000000010001110000000011011111111011001111111111101100111111111111110001111111111111000111111111111100000111111111100000011111111000000001111111100000000111111110000000001111110000000000000000000000000000000000000",
        "0000000000000000000000000000000000000000000000000111011111000000011111111110000000111111111000000000111111100000000011111111000000000111111110000000001111111100000000111111110000000011111000000000000111000000000000000000000000000000000000000000000000000000",
        "0000000000000000000000000000000000000011110000000000001111000000000000111100000000000011110000000000011111110000000111111111100000011111111100000001111111110000000011111111000000000111011000000001111100000000000001100000000000000000000000000000000000000000"
    ];

    // ── Geometría de la escena (200×150) ─────────────────────────────────────────────────
    private const double ScreenX = 12, ScreenY = 0, ScreenW = 176, ScreenH = 104;
    private const double RotomSize = 88;
    private const double CellW = 11, CellH = 12;
    private const string HexChars = "0123456789ABCDEF";

    private static readonly IBrush ScreenDark = new SolidColorBrush(Color.Parse("#0b1220"));
    private static readonly IBrush ScreenLight = new SolidColorBrush(Color.Parse("#f4f4f5"));
    private static readonly IBrush Neck = new SolidColorBrush(Color.Parse("#b8b8bf"));
    private static readonly IBrush KeyIdle = new SolidColorBrush(Color.Parse("#dcdce0"));
    private static readonly IBrush KeyHot = new SolidColorBrush(Color.Parse("#3b82f6"));
    private static readonly IBrush KeyOk = new SolidColorBrush(Color.Parse("#2f9e5b"));
    private static readonly IBrush White = Brushes.White;

    private static readonly Pen BorderSearching = new(new SolidColorBrush(Color.Parse("#1e3a5f")), 2);
    private static readonly Pen BorderFound = new(new SolidColorBrush(Color.Parse("#2f9e5b")), 2);
    private static readonly Pen BorderNone = new(new SolidColorBrush(Color.Parse("#c2410c")), 2);
    private static readonly Pen KeyboardBorder = new(new SolidColorBrush(Color.Parse("#b8b8bf")), 1.5);

    // Glifos hexadecimales pre-armados (crear FormattedText en cada cuadro sería caro)
    private static readonly FormattedText[] RainHead = BuildGlyphs(11, "#e0f2fe");
    private static readonly FormattedText[][] RainBody =
    [
        BuildGlyphs(11, "#38bdf8", 0.85), BuildGlyphs(11, "#38bdf8", 0.55),
        BuildGlyphs(11, "#38bdf8", 0.32), BuildGlyphs(11, "#38bdf8", 0.16),
    ];
    private static readonly FormattedText[] CodeBright = BuildGlyphs(9, "#e0f2fe");
    private static readonly FormattedText[] CodeNormal = BuildGlyphs(9, "#38bdf8", 0.9);

    private static FormattedText[] BuildGlyphs(double size, string color, double alpha = 1.0)
    {
        var c = Color.Parse(color);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B));
        var glyphs = new FormattedText[16];
        for (var i = 0; i < 16; i++)
            glyphs[i] = new FormattedText(HexChars[i].ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("monospace"), size, brush);
        return glyphs;
    }

    // ── Estado de la animación ───────────────────────────────────────────────────────────
    private readonly Random _rnd = new();
    private DispatcherTimer? _timer;
    private long _lastRainMs;

    private readonly double[] _dropY;      // posición (en celdas) de la cabeza de cada columna
    private readonly double[] _dropV;      // velocidad (celdas por cuadro de lluvia)
    private readonly int[] _glyphs;        // índices hex vigentes por columna y posición de estela

    private enum Phase { Hold, Out, In }
    private Phase _phase = Phase.Hold;
    private double _phaseStartMs;
    private double _holdMs = 600;
    private double _microUntilMs;
    private int _form;
    private readonly double[] _thr = new double[256];
    private double _surge;                 // intensidad actual del efecto (0..1): acelera la lluvia
    private double _intensity;             // 0 = sprite intacto … 1 = todo convertido en código

    private static double NowMs => Environment.TickCount64;

    public RotomMatrixControl()
    {
        var cols = (int)(ScreenW / CellW);
        _dropY = new double[cols];
        _dropV = new double[cols];
        _glyphs = new int[cols * 8];
        for (var i = 0; i < cols; i++)
        {
            _dropY[i] = -_rnd.NextDouble() * 20;
            _dropV[i] = 0.35 + _rnd.NextDouble() * 0.9;
        }
        for (var i = 0; i < _glyphs.Length; i++) _glyphs[i] = _rnd.Next(16);
        RollThresholds(isOut: true);
        _phaseStartMs = NowMs;
        // Cuadro inicial: un poco de lluvia ya caída para que no arranque vacío
        for (var k = 0; k < 40; k++) StepRain();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        var shouldRun = IsRunning && Mode == RotomMatrixMode.Searching && ((Visual)this).IsAttachedToVisualTree();
        if (shouldRun)
        {
            _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };   // ~40 cuadros/s
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _lastRainMs = 0;
            _phase = Phase.Hold;
            _phaseStartMs = NowMs;
            _timer.Start();
        }
        else
        {
            _timer?.Stop();
            _surge = 0;
            _intensity = 0;
            InvalidateVisual();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        StepRotom();
        var now = (long)NowMs;
        if (now - _lastRainMs >= 55)           // la lluvia va más lenta (~18 cuadros/s) que Rotom
        {
            _lastRainMs = now;
            StepRain();
        }
        InvalidateVisual();
    }

    // ── Lógica ───────────────────────────────────────────────────────────────────────────

    private void StepRain()
    {
        var h = ScreenH / CellH;
        for (var i = 0; i < _dropY.Length; i++)
        {
            _dropY[i] += _dropV[i] * (1 + _surge * 2.5);
            if (_dropY[i] > h + 8 && _rnd.NextDouble() > 0.97)
            {
                _dropY[i] = -_rnd.NextDouble() * 10;
                _dropV[i] = 0.35 + _rnd.NextDouble() * 0.9;
            }
        }
        for (var i = 0; i < _glyphs.Length; i++)
            if (_rnd.Next(3) == 0) _glyphs[i] = _rnd.Next(16);   // los dígitos mutan
    }

    private void StepRotom()
    {
        var now = NowMs;
        var speed = 1 + 2 * Math.Clamp(Progress, 0, 1);       // increscendo: 1× → 3×
        var outMs = 170.0 / speed;
        var inMs = 170.0 / speed;
        double intensity = 0;

        if (_phase == Phase.Hold)
        {
            if (now - _phaseStartMs > _holdMs / speed)
            {
                _phase = Phase.Out;
                _phaseStartMs = now;
                RollThresholds(isOut: true);
            }
            else if (now < _microUntilMs) intensity = 0.14 + _rnd.NextDouble() * 0.1;   // micro-fallo
            else if (_rnd.NextDouble() < 0.04) _microUntilMs = now + 70;
        }

        if (_phase == Phase.Out)
        {
            var k = Math.Min(1.0, (now - _phaseStartMs) / outMs);
            intensity = k;
            if (k >= 1)
            {
                _form = NextForm(_form);
                _phase = Phase.In;
                _phaseStartMs = now;
                RollThresholds(isOut: false);
            }
        }
        else if (_phase == Phase.In)
        {
            var k = Math.Min(1.0, (now - _phaseStartMs) / inMs);
            intensity = 1 - k;
            if (k >= 1)
            {
                _phase = Phase.Hold;
                _phaseStartMs = now;
                _holdMs = 250 + _rnd.NextDouble() * 450;
                intensity = 0;
            }
        }

        _intensity = intensity;
        _surge = intensity;
    }

    /// <summary>Siguiente forma: al azar sin repetir la actual; el Rotom original reaparece seguido
    /// (35 %) para que se note que es él quien está siendo "hackeado".</summary>
    private int NextForm(int cur)
    {
        if (cur != 0 && _rnd.NextDouble() < 0.35) return 0;
        int n;
        do { n = _rnd.Next(Masks.Length); } while (n == cur || n == 0);
        return n;
    }

    private void RollThresholds(bool isOut)
    {
        for (var by = 0; by < 16; by++)
            for (var bx = 0; bx < 16; bx++)
            {
                var k = isOut ? by / 15.0 : 1 - by / 15.0;           // barrido de arriba hacia abajo
                _thr[by * 16 + bx] = 0.08 + 0.92 * (0.65 * _rnd.NextDouble() + 0.35 * k);
            }
    }

    // ── Dibujo ───────────────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var searching = Mode == RotomMatrixMode.Searching;
        var screen = new Rect(ScreenX, ScreenY, ScreenW, ScreenH);
        var pen = Mode switch
        {
            RotomMatrixMode.Found => BorderFound,
            RotomMatrixMode.NoSolution => BorderNone,
            _ => BorderSearching,
        };

        // Pantalla
        ctx.DrawRectangle(searching ? ScreenDark : ScreenLight, pen, screen, 10, 10);

        using (ctx.PushClip(new RoundedRect(screen.Deflate(1), 9)))
        {
            // Lluvia de código (tenue y congelada fuera de la búsqueda)
            using (ctx.PushOpacity(searching ? 1.0 : 0.22))
                DrawRain(ctx);

            DrawRotom(ctx, searching);
        }

        // Cuello y teclado
        ctx.DrawRectangle(Neck, null, new Rect(89, ScreenH + 1, 22, 8));
        var kb = new Rect(25, ScreenH + 11, 150, 18);
        ctx.DrawRectangle(White, KeyboardBorder, kb, 4, 4);
        var tHot = (long)(NowMs / 100);
        for (var i = 0; i < 12; i++)
        {
            IBrush b = Mode switch
            {
                RotomMatrixMode.Found => KeyOk,
                RotomMatrixMode.NoSolution => KeyIdle,
                _ => ((tHot + i * 3) % 12) < 2 ? KeyHot : KeyIdle,
            };
            ctx.DrawRectangle(b, null, new Rect(29 + i * 12, ScreenH + 17, 10, 6), 1, 1);
        }
    }

    private void DrawRain(DrawingContext ctx)
    {
        for (var c = 0; c < _dropY.Length; c++)
        {
            var x = ScreenX + 2 + c * CellW;
            var head = _dropY[c];
            for (var t = 0; t < 8; t++)
            {
                var y = ScreenY + (head - t) * CellH;
                if (y < ScreenY - CellH || y > ScreenY + ScreenH) continue;
                var g = _glyphs[c * 8 + t];
                var glyph = t == 0 ? RainHead[g] : RainBody[Math.Min(3, (t - 1) / 2)][g];
                ctx.DrawText(glyph, new Point(x, y));
            }
        }
    }

    private void DrawRotom(DrawingContext ctx, bool searching)
    {
        var sprites = Sprites;
        var idx = searching ? _form : 0;
        var bmp = sprites != null && idx < sprites.Count ? sprites[idx] : null;
        if (bmp == null) return;

        var bob = searching ? Math.Sin(NowMs / 2200.0 * Math.PI * 2) * 3 : 0;
        var dx = ScreenX + (ScreenW - RotomSize) / 2;
        var dy = ScreenY + (ScreenH - RotomSize) / 2 + bob;
        var scale = RotomSize / 96.0;
        var srcScale = bmp.Size.Width / 96.0;
        var intensity = searching ? _intensity : 0.0;

        using var dim = ctx.PushOpacity(Mode == RotomMatrixMode.NoSolution ? 0.55 : 1.0);

        // Eco: dos copias desplazadas en horizontal, más fuertes mientras se disuelve
        if (intensity > 0.04)
        {
            var d = Math.Round(1 + intensity * 7) * (_rnd.Next(2) == 0 ? -1 : 1);
            var ghostA = Math.Min(0.65, 0.2 + intensity * 0.6) * (1 - 0.85 * Math.Max(0, (intensity - 0.85) / 0.15));
            var full = new Rect(0, 0, bmp.Size.Width, bmp.Size.Height);
            using (ctx.PushOpacity(ghostA * 0.6))
            {
                ctx.DrawImage(bmp, full, new Rect(dx - d * scale, dy, RotomSize, RotomSize));
                ctx.DrawImage(bmp, full, new Rect(dx + d * scale, dy, RotomSize, RotomSize));
            }
        }

        // Sprite por bloques de 6×6: los bloques "disueltos" se sustituyen por dígitos hexadecimales
        var mask = Masks[Math.Min(idx, Masks.Length - 1)];
        for (var by = 0; by < 16; by++)
        {
            var shift = intensity > 0.04 && _rnd.NextDouble() < intensity * 0.55
                ? Math.Round((_rnd.NextDouble() - 0.5) * intensity * 26) * scale
                : 0.0;
            for (var bx = 0; bx < 16; bx++)
            {
                var b = by * 16 + bx;
                if (mask[b] != '1') continue;
                var x = dx + bx * 6 * scale;
                var y = dy + by * 6 * scale;
                if (_thr[b] > intensity)
                {
                    ctx.DrawImage(bmp,
                        new Rect(bx * 6 * srcScale, by * 6 * srcScale, 6 * srcScale, 6 * srcScale),
                        new Rect(x + shift, y, 6 * scale + 0.5, 6 * scale + 0.5));
                }
                else if (_thr[b] > intensity - 0.35 && _rnd.NextDouble() < 0.7)
                {
                    var set = _rnd.NextDouble() < 0.15 ? CodeBright : CodeNormal;
                    ctx.DrawText(set[_rnd.Next(16)], new Point(x, y - 1));
                }
            }
        }
    }
}
