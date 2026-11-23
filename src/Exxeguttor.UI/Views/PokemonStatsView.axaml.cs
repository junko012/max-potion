using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;
using Exxeguttor.UI.ViewModels;
using System.Reflection;

namespace Exxeguttor.UI.Views;

public partial class PokemonStatsView : UserControl
{
    public PokemonStatsView()
    {
        InitializeComponent();

        // Tira de pestañas en una sola fila con flechas de desplazamiento (ver el Template del
        // TabControl en el AXAML): se cablean cuando el template se aplica, porque los eventos de
        // las partes nombradas no se pueden declarar desde el AXAML de un ControlTemplate.
        StatsTabs.TemplateApplied += OnStatsTabsTemplateApplied;
        // SelectionChanged burbujea: los ComboBox de dentro de cada pestaña también lo disparan, por eso
        // se filtra por el origen (solo importa el cambio de pestaña del propio TabControl).
        StatsTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, StatsTabs)) ScrollSelectedTabIntoView();
        };
    }

    // ── Pestañas: una sola fila + flechas ◀ ▶ ───────────────────────────────────────────
    private ScrollViewer? _tabScroll;
    private RepeatButton? _tabLeft, _tabRight;

    private void OnStatsTabsTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        // Si el template se re-aplica, soltar lo anterior para no duplicar suscripciones.
        if (_tabScroll != null)
        {
            _tabScroll.ScrollChanged -= OnTabScrollChanged;
            _tabScroll.PointerWheelChanged -= OnTabWheel;
        }
        if (_tabLeft  != null) _tabLeft.Click  -= OnTabLeftClick;
        if (_tabRight != null) _tabRight.Click -= OnTabRightClick;

        _tabScroll = e.NameScope.Find<ScrollViewer>("PART_TabScroll");
        _tabLeft   = e.NameScope.Find<RepeatButton>("PART_TabLeft");
        _tabRight  = e.NameScope.Find<RepeatButton>("PART_TabRight");

        if (_tabScroll != null)
        {
            _tabScroll.ScrollChanged += OnTabScrollChanged;
            _tabScroll.PointerWheelChanged += OnTabWheel;
        }
        if (_tabLeft  != null) _tabLeft.Click  += OnTabLeftClick;
        if (_tabRight != null) _tabRight.Click += OnTabRightClick;
    }

    private void OnTabLeftClick(object? sender, RoutedEventArgs e)  => ScrollTabs(-1);
    private void OnTabRightClick(object? sender, RoutedEventArgs e) => ScrollTabs(+1);

    private void OnTabWheel(object? sender, PointerWheelEventArgs e)
    {
        // La rueda sobre la tira de pestañas también las desplaza (sin ella no hay scroll horizontal).
        if (_tabScroll == null || _tabScroll.Extent.Width <= _tabScroll.Viewport.Width) return;
        ScrollTabs(e.Delta.Y > 0 ? -1 : +1);
        e.Handled = true;
    }

    private void ScrollTabs(int direction)
    {
        if (_tabScroll == null) return;
        var max  = Math.Max(0, _tabScroll.Extent.Width - _tabScroll.Viewport.Width);
        var step = Math.Max(60, _tabScroll.Viewport.Width * 0.5);
        _tabScroll.Offset = new Vector(Math.Clamp(_tabScroll.Offset.X + direction * step, 0, max), 0);
    }

    // Las flechas solo aparecen si las pestañas no entran; se deshabilitan en cada extremo.
    private void OnTabScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_tabScroll == null) return;
        var overflow = _tabScroll.Extent.Width > _tabScroll.Viewport.Width + 0.5;
        var max      = Math.Max(0, _tabScroll.Extent.Width - _tabScroll.Viewport.Width);
        if (_tabLeft != null)
        {
            _tabLeft.IsVisible = overflow;
            _tabLeft.IsEnabled = _tabScroll.Offset.X > 0.5;
        }
        if (_tabRight != null)
        {
            _tabRight.IsVisible = overflow;
            _tabRight.IsEnabled = _tabScroll.Offset.X < max - 0.5;
        }
    }

    // Al cambiar de pestaña (o aparecer una nueva, ej. Diagnóstico) la seleccionada queda a la vista.
    private void ScrollSelectedTabIntoView()
        => Dispatcher.UIThread.Post(() => (StatsTabs.SelectedItem as TabItem)?.BringIntoView(),
                                    DispatcherPriority.Loaded);

    // ── Spinners IV/EV ───────────────────────────────────────────────────

    // Tag format: "PropertyName:min:max"
    private void OnSpinnerIncrement(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ParseTag(btn.Tag, out var prop, out _, out var max))
            AdjustValue(prop, +1, EffectiveMax(prop, max));
    }

    private void OnSpinnerDecrement(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ParseTag(btn.Tag, out var prop, out var min, out _))
            AdjustValue(prop, -1, min);
    }

    private void OnSpinnerMax(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ParseTag(btn.Tag, out var prop, out _, out var max))
            SetValue(prop, EffectiveMax(prop, max));
    }

    // Post-4.6 — botón "Corregir automáticamente" del tab Diagnóstico. El DataContext acá
    // es el FieldFix puntual (viene del DataTemplate anidado, no del PokemonEditorViewModel
    // de toda la vista), así que lo leemos del sender y llamamos al método del ViewModel
    // real (this.DataContext, el de la UserControl) pasándoselo — mismo patrón que
    // OnOpenMoveSelector usa para lo mismo con los movimientos.
    // Selectores rápidos de Mega Stone / Z-Crystal. Se equipa el item y se vacía el ComboBox con Post
    // (fuera del ciclo de SelectionChanged) para que siga funcionando como "acción": elegir de nuevo
    // el mismo item vuelve a disparar el evento. SelectedItem = null dispara SelectionChanged con
    // selección nula, que se ignora acá.
    private void OnMegaOptionPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: string option } combo) return;
        if (DataContext is PokemonEditorViewModel vm) vm.EquipMegaOption(option);
        Dispatcher.UIThread.Post(() => combo.SelectedItem = null);
    }

    private void OnZCrystalOptionPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: string option } combo) return;
        if (DataContext is PokemonEditorViewModel vm) vm.EquipZCrystalOption(option);
        Dispatcher.UIThread.Post(() => combo.SelectedItem = null);
    }

    // Botón "Quitar" de las tarjetas Mega/Z-Moves del tab Special: esos objetos no están en el
    // combo de Held Item del tab Info, así que se quitan desde acá.
    private void OnUnequipHeldItem(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PokemonEditorViewModel vm) vm.UnequipHeldItem();
    }

    private void OnApplyAutoFix(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: FieldFix fix } && DataContext is PokemonEditorViewModel vm)
            vm.ApplyAutoFix(fix);
    }

    // Simétrico a OnSpinnerMax, solo para EVs (ver comentario en el XAML de por qué no se
    // agregó también para IVs) — el mínimo es 0 en todas las generaciones, sin excepción,
    // así que a diferencia de EffectiveMax acá no hace falta ninguna lógica por generación.
    private void OnSpinnerMin(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && ParseTag(btn.Tag, out var prop, out var min, out _))
            SetValue(prop, min);
    }

    private void OnSpinnerTextChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && ParseTag(tb.Tag, out var prop, out var min, out var max))
        {
            if (int.TryParse(tb.Text, out var val))
                SetValue(prop, Math.Clamp(val, min, EffectiveMax(prop, max)));
        }
    }

    // En Gen 1/2 los IVs son en realidad DVs: 0-15, no 0-31 (ver
    // PokemonEditorViewModel.UsesLegacyIVs). Y los EVs son "Stat Experience": 0-65.535, no
    // 0-252. El Tag de los steppers sigue diciendo ":0:31"/":0:252" siempre (no vale la pena
    // bindear ~48 tags distintos por generación) — acá se topea en runtime el único caso que
    // cambia. El botón "max" usa este mismo método, así que ya sale diferenciado por
    // generación sin tocar nada más.
    //
    // Bug encontrado (sesión post-4.6): el tope estricto de 252 por stat recién se introduce
    // en Gen 6 (X/Y) — confirmado contra múltiples fuentes de mecánica del juego real, no
    // asumido. En Gen 3, 4 y 5 el tope real y legal por stat sigue siendo 255 (el "252" que
    // usa todo el mundo es una convención de eficiencia — las EVs se cuentan de a 4, así que
    // los últimos 3 puntos de 255 se desperdician — no una regla de legalidad de esas
    // generaciones). Antes esta función devolvía 252 (el Tag) para CUALQUIER generación que
    // no fuera 1/2, lo cual topeaba mal el botón "max" (y el clamp al tipear) en Gen 3-5.
    private int EffectiveMax(string propertyName, int taggedMax)
        => NumericLimits.EffectiveMax(DataContext, propertyName, taggedMax);

    private void AdjustValue(string propertyName, int delta, int clampBound)
    {
        if (DataContext is not PokemonEditorViewModel vm) return;
        var prop = typeof(PokemonEditorViewModel).GetProperty(propertyName);
        if (prop == null) return;
        var current = prop.GetValue(vm);
        if (current is int intVal)
        {
            var newVal = delta > 0
                ? Math.Min(intVal + 1, clampBound)
                : Math.Max(intVal - 1, clampBound);
            prop.SetValue(vm, newVal);
        }
    }

    private void SetValue(string propertyName, int value)
    {
        if (DataContext is not PokemonEditorViewModel vm) return;
        var prop = typeof(PokemonEditorViewModel).GetProperty(propertyName);
        prop?.SetValue(vm, value);
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

    // ── Hover/foco cruzado fila ↔ radar (tab IVs/EVs) ─────────────────────────────────
    // El Tag de cada fila (Border.statrow) es el índice del radar: 0=PS 1=Atq 2=Def 3=Vel 4=DefE 5=AtqE.
    // HoveredStat es solo estado visual del ViewModel (no se registra como edición del Pokémon).
    private static int StatIndexOf(object? sender)
        => sender is Control { Tag: string t } && int.TryParse(t, out var i) ? i : -1;

    private void OnStatRowPointerEntered(object? sender, PointerEventArgs e)
    {
        if (VM is { } vm) vm.HoveredStat = StatIndexOf(sender);
    }

    private void OnStatRowPointerExited(object? sender, PointerEventArgs e)
    {
        if (VM is { } vm) vm.HoveredStat = -1;
    }

    private void OnStatRowGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (VM is { } vm) vm.HoveredStat = StatIndexOf(sender);
    }

    private void OnStatRowLostFocus(object? sender, RoutedEventArgs e)
    {
        if (VM is { } vm) vm.HoveredStat = -1;
    }

    // El DataContext del punto del radar es el RadarVertexInfo (viene del ItemTemplate).
    private void OnRadarVertexPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: RadarVertexInfo v } && VM is { } vm)
            vm.HoveredStat = v.Index;
    }

    // ── Move selector panel ───────────────────────────────────────────────

    private PokemonEditorViewModel? VM =>
        DataContext as PokemonEditorViewModel;

    private void OnOpenMoveSelector(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.Tag is string slotStr &&
            int.TryParse(slotStr, out int slot))
            VM?.OpenMoveSelector(slot);
    }

    // Pliega/despliega una tarjeta de categoría desde su encabezado (solo vista "Todas", sin búsqueda).
    private void OnRibbonGroupHeaderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: RibbonGroupViewModel g } && g.IsToggleable)
            g.IsCollapsed = !g.IsCollapsed;
    }

    private void OnRibbonCategoryChipClick(object? sender, RoutedEventArgs e)
    {
        // Tag sigue siendo el string de categoría cruda ("Champion", "Todas", ...) — se bindea
        // desde RibbonChipViewModel.Category en el XAML, no el objeto completo, para no tener
        // que castear acá.
        if (sender is Button btn && btn.Tag is string category && VM is { } vm)
            vm.SelectedRibbonCategory = category;
    }

    private void OnApplyMoveSelection(object? sender, RoutedEventArgs e)
        => VM?.ApplyMoveSelection();

    private void OnCancelMoveSelection(object? sender, RoutedEventArgs e)
        => VM?.CancelMoveSelection();

    private void OnMoveSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && VM?.MoveSelector != null)
            VM.MoveSelector.Search = tb.Text ?? string.Empty;
    }

    private void OnMethodFilterClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string method)
            VM?.MoveSelector?.SelectMethod(method);
    }

    private void OnMoveRowDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control ctrl &&
            ctrl.DataContext is MoveOption opt &&
            VM?.MoveSelector != null)
        {
            VM.MoveSelector.Selected = opt;
            VM.ApplyMoveSelection();
        }
    }
}
