using System.ComponentModel;
using System.Windows.Input;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Una fila de los submenús de Preferencias (idioma de la interfaz / idioma de los datos).
/// El <see cref="Command"/> ya viene armado: el <c>Style</c> del <c>MenuItem</c> solo bindea
/// <c>Header</c> (<see cref="Display"/>) y <c>Command</c>. Se reconstruyen las listas enteras
/// cuando cambia algún idioma, así que la marca de activo es inmutable por instancia.
/// </summary>
public sealed class LocaleMenuEntry
{
    /// <summary>Código del idioma (UI: "es", "zh-CN"…; datos: "auto" o código PKHeX).</summary>
    public string Code { get; }

    /// <summary>Autoglotónimo ("Español", "日本語"…), o el texto de «Igual que la interfaz».</summary>
    public string Label { get; }

    /// <summary>Código corto de apoyo visual ("ES"). Vacío en la fila «Igual que la interfaz».</summary>
    public string ShortCode { get; }

    /// <summary>Leyenda opcional ("datos en inglés" para idiomas sin datos en PKHeX).</summary>
    public string Note { get; }

    public bool IsActive { get; }

    public ICommand Command { get; }

    /// <summary>Texto del <c>Header</c>: marca ✓ + nombre + código + leyenda.</summary>
    public string Display =>
        $"{(IsActive ? "✓" : "\u2003")}  {Label}"
        + (ShortCode.Length > 0 ? $"   {ShortCode}" : "")
        + (Note.Length > 0 ? $"   · {Note}" : "");

    public LocaleMenuEntry(string code, string label, string shortCode, string note, bool isActive, ICommand command)
    {
        Code = code; Label = label; ShortCode = shortCode; Note = note; IsActive = isActive; Command = command;
    }
}
