using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Services;

/// <summary>Tipo de dato que acepta un TextBox (ver <see cref="NumericInput"/>).</summary>
public enum NumericKind
{
    /// <summary>Sin restricción (texto libre).</summary>
    None,
    /// <summary>Solo dígitos 0-9, con rango opcional.</summary>
    Digits,
    /// <summary>Dígitos y separador de miles (","): dinero, monedas, BP.</summary>
    Grouped,
    /// <summary>Fecha «yyyy-MM-dd» (solo dígitos y "-").</summary>
    Date,
}

/// <summary>
/// Restringe lo que se puede escribir en un TextBox al tipo de dato que necesita, y lo mantiene dentro del rango válido.
/// <para>Se activa de dos formas, sin código en cada vista:</para>
/// <list type="bullet">
/// <item>Todo TextBox con <c>Tag="Propiedad:min:max"</c> (los spinners de Nivel, Amistad, IVs, EVs…) es de dígitos, con ese
/// rango. El máximo por generación (IVs 0-15 en Gen 1-2, EVs 255/65535) sale de <see cref="NumericLimits"/>.</item>
/// <item>Cualquier otro campo con <c>NumericInput.Kind="Digits|Grouped|Date"</c> (y opcionalmente <c>NumericInput.Max</c>).</item>
/// </list>
/// <para>Mientras se escribe: se descartan los caracteres no permitidos (también al pegar) y un valor por encima del máximo
/// se baja al máximo. Al salir del campo: se normaliza (sin ceros a la izquierda, dentro del rango) y, si quedó vacío o
/// inválido, se restaura el valor que tenía al entrar. Una fecha incompleta o inexistente se marca en rojo (clase
/// <c>invalid</c>) y nunca se acepta como valor final.</para>
/// <para>El rango real de cada dato se vuelve a limitar en el ViewModel, así que ningún valor fuera de rango llega al guardado.</para>
/// </summary>
public sealed class NumericInput
{
    private NumericInput() { } // solo hospeda propiedades adjuntas

    public static readonly AttachedProperty<NumericKind> KindProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, NumericKind>("Kind");

    /// <summary>Mínimo permitido cuando no hay <c>Tag</c> (por defecto 0).</summary>
    public static readonly AttachedProperty<int> MinProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, int>("Min", 0);

    /// <summary>Máximo permitido cuando no hay <c>Tag</c> (por defecto sin tope).</summary>
    public static readonly AttachedProperty<int> MaxProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, int>("Max", int.MaxValue);

    // Texto que tenía el campo al recibir el foco: es a lo que se vuelve si se sale con algo vacío o inválido.
    private static readonly AttachedProperty<string?> OriginalTextProperty =
        AvaloniaProperty.RegisterAttached<NumericInput, TextBox, string?>("OriginalText");

    public static NumericKind GetKind(TextBox tb) => tb.GetValue(KindProperty);
    public static void SetKind(TextBox tb, NumericKind value) => tb.SetValue(KindProperty, value);
    public static int GetMin(TextBox tb) => tb.GetValue(MinProperty);
    public static void SetMin(TextBox tb, int value) => tb.SetValue(MinProperty, value);
    public static int GetMax(TextBox tb) => tb.GetValue(MaxProperty);
    public static void SetMax(TextBox tb, int value) => tb.SetValue(MaxProperty, value);

    private static readonly Regex TagPattern = new(@"^(?<prop>[A-Za-z_][A-Za-z0-9_]*):(?<min>\d+):(?<max>\d+)$", RegexOptions.Compiled);
    private static bool _registered;
    private static bool _busy; // evita reentradas al reescribir el texto desde el propio handler

    /// <summary>Se llama una vez al arrancar (App.OnFrameworkInitializationCompleted).</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((tb, _) => OnTextChanged(tb));
        InputElement.IsFocusedProperty.Changed.AddClassHandler<TextBox>((tb, e) =>
        {
            if (e.NewValue is true) OnGotFocus(tb);
            else OnLostFocus(tb);
        });
    }

    private readonly record struct Spec(NumericKind Kind, string? Prop, int Min, int Max);

    private static Spec? GetSpec(TextBox tb)
    {
        var kind = GetKind(tb);
        if (kind == NumericKind.Digits || kind == NumericKind.Grouped || kind == NumericKind.Date)
            return new Spec(kind, null, GetMin(tb), GetMax(tb));

        if (tb.Tag is string tag && TagPattern.Match(tag) is { Success: true } m
            && int.TryParse(m.Groups["min"].Value, out var min) && int.TryParse(m.Groups["max"].Value, out var max))
        {
            var prop = m.Groups["prop"].Value;
            return new Spec(NumericKind.Digits, prop, min, NumericLimits.EffectiveMax(tb.DataContext, prop, max));
        }
        return null;
    }

    private static void OnGotFocus(TextBox tb)
    {
        if (GetSpec(tb) is null) return;
        tb.SetValue(OriginalTextProperty, tb.Text ?? string.Empty);
    }

    private static void OnTextChanged(TextBox tb)
    {
        // Solo cuando el usuario escribe/pega: los cambios que hace el ViewModel al cargar otro Pokémon no se tocan
        // (un valor ilegal ya guardado, como un EV de 255 en Gen 6, no debe corregirse en silencio).
        if (_busy || !tb.IsFocused || GetSpec(tb) is not { } spec) return;
        var text = tb.Text ?? string.Empty;

        var allowed = spec.Kind switch
        {
            NumericKind.Digits => (Func<char, bool>)IsAsciiDigit,
            NumericKind.Grouped => c => IsAsciiDigit(c) || c == ',',
            NumericKind.Date => c => IsAsciiDigit(c) || c == '-',
            _ => _ => true,
        };

        var clean = Filter(text, allowed, tb.CaretIndex, out var caret);
        if (spec.Kind == NumericKind.Digits && clean.Length > 0)
        {
            // Más de 9 dígitos desborda un int: siempre es mayor que cualquier máximo real.
            if (clean.Length > 9 || long.Parse(clean, CultureInfo.InvariantCulture) > spec.Max)
            {
                clean = spec.Max.ToString(CultureInfo.InvariantCulture);
                caret = clean.Length;
            }
        }

        if (spec.Kind == NumericKind.Date)
            SetInvalid(tb, clean.Length == 10 && !IsValidDate(clean));

        if (clean == text) return;
        _busy = true;
        try
        {
            tb.Text = clean;
            tb.CaretIndex = Math.Clamp(caret, 0, clean.Length);
        }
        finally { _busy = false; }
    }

    private static void OnLostFocus(TextBox tb)
    {
        if (_busy || GetSpec(tb) is not { } spec) return;
        var text = tb.Text ?? string.Empty;
        var original = tb.GetValue(OriginalTextProperty) ?? string.Empty;
        if (spec.Kind == NumericKind.Date) SetInvalid(tb, false);
        if (text == original) return; // sin edición del usuario: no se toca nada
        string? result = null;

        switch (spec.Kind)
        {
            case NumericKind.Digits:
                if (text.Length > 0 && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
                    result = Math.Clamp(v, spec.Min, spec.Max).ToString(CultureInfo.InvariantCulture);
                else
                    result = original; // vacío → vuelve al valor anterior
                break;
            case NumericKind.Grouped:
                // El formato (separador de miles) y el tope los pone el ViewModel; acá solo se evita dejarlo vacío.
                if (!text.Any(IsAsciiDigit)) result = original;
                break;
            case NumericKind.Date:
                // Una fecha vacía es válida solo si ya estaba vacía al entrar (juegos/orígenes sin fecha).
                if (!(text.Length == 0 ? original.Length == 0 : IsValidDate(text))) result = original;
                break;
        }

        if (result != null && result != text)
        {
            _busy = true;
            try { tb.Text = result; }
            finally { _busy = false; }
        }
    }

    /// <summary>Fecha «yyyy-MM-dd» existente en el calendario y dentro de 2000-2099 (el rango que guardan los juegos).</summary>
    public static bool IsValidDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        && d.Year is >= 2000 and <= 2099;

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    /// <summary>Quita lo no permitido y recalcula dónde queda el cursor (cuenta lo quitado antes de él).</summary>
    private static string Filter(string text, Func<char, bool> allowed, int caretBefore, out int caretAfter)
    {
        var sb = new StringBuilder(text.Length);
        var removedBeforeCaret = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (allowed(text[i])) sb.Append(text[i]);
            else if (i < caretBefore) removedBeforeCaret++;
        }
        caretAfter = Math.Max(0, caretBefore - removedBeforeCaret);
        return sb.ToString();
    }

    private static void SetInvalid(TextBox tb, bool invalid)
    {
        var has = tb.Classes.Contains("invalid");
        if (invalid && !has) tb.Classes.Add("invalid");
        else if (!invalid && has) tb.Classes.Remove("invalid");
    }
}

/// <summary>Límites que dependen del save/generación, compartidos entre el filtro de entrada y el code-behind de los spinners.</summary>
public static class NumericLimits
{
    /// <summary>
    /// Máximo real de un campo del editor de Pokémon cuyo <c>Tag</c> dice <paramref name="taggedMax"/>.
    /// Gen 1-2: los IVs son DVs (0-15) y los EVs son «Stat Experience» (0-65535). Gen 3-5: el tope legal por EV es 255
    /// (el 252 estricto empieza en Gen 6).
    /// </summary>
    public static int EffectiveMax(object? dataContext, string propertyName, int taggedMax)
    {
        if (dataContext is not PokemonEditorViewModel vm) return taggedMax;

        if (vm.UsesLegacyIVs)
        {
            if (propertyName.StartsWith("Iv", StringComparison.Ordinal)) return Math.Min(taggedMax, 15);
            if (propertyName.StartsWith("Ev", StringComparison.Ordinal)) return 65535;
            return taggedMax;
        }

        if (propertyName.StartsWith("Ev", StringComparison.Ordinal)
            && vm.Capabilities is { Generation: >= 3 and <= 5 })
            return 255;

        return taggedMax;
    }
}
