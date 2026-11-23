using Avalonia.Controls;
using Avalonia.Interactivity;
using Exxeguttor.UI.ViewModels;
using System.Reflection;

namespace Exxeguttor.UI.Views;

public partial class PokemonInfoView : UserControl
{
    public PokemonInfoView()
    {
        InitializeComponent();
    }

    // Tag format: "PropertyName:min:max"
    private void OnSpinnerIncrement(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ParseTag(btn.Tag, out var prop, out _, out var max))
            AdjustValue(prop, +1, max);
    }

    private void OnSpinnerDecrement(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ParseTag(btn.Tag, out var prop, out var min, out _))
            AdjustValue(prop, -1, min);
    }

    private void OnSpinnerTextChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && ParseTag(tb.Tag, out var prop, out var min, out var max))
            if (int.TryParse(tb.Text, out var val))
                SetValue(prop, Math.Clamp(val, min, max));
    }

    private void AdjustValue(string propertyName, int delta, int clampBound)
    {
        if (DataContext is not PokemonEditorViewModel vm) return;
        var prop = typeof(PokemonEditorViewModel).GetProperty(propertyName);
        if (prop?.GetValue(vm) is int current)
            prop.SetValue(vm, delta > 0
                ? Math.Min(current + 1, clampBound)
                : Math.Max(current - 1, clampBound));
    }

    private void SetValue(string propertyName, int value)
    {
        if (DataContext is not PokemonEditorViewModel vm) return;
        typeof(PokemonEditorViewModel).GetProperty(propertyName)?.SetValue(vm, value);
    }

    private static bool ParseTag(object? tag, out string prop, out int min, out int max)
    {
        prop = string.Empty; min = 0; max = 0;
        if (tag is not string s) return false;
        var parts = s.Split(':');
        if (parts.Length != 3) return false;
        prop = parts[0];
        return int.TryParse(parts[1], out min) && int.TryParse(parts[2], out max);
    }
}
