using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

public enum ExportReviewCardKind { Trainer, BagPouch, Pokemon, Release }

/// <summary>
/// Tarjeta unificada del carrusel de revisión pre-exportación (Pantalla 3 /
/// mockups/export_modal) — envuelve <see cref="EditedTrainerSummary"/>,
/// <see cref="EditedBagPouchSummary"/> y <see cref="EditedPokemonSummary"/> bajo una forma
/// común para que las 3 vivan en el mismo carrusel con el mismo template visual (checklist +
/// checkbox + sprite/ícono).
/// </summary>
/// <remarks>
/// Reusa <see cref="EditedFieldSummary"/> como fila de checklist para las 3 — ya era
/// exactamente esa forma para Pokémon y Entrenador. Para Mochila se arma agrupando
/// <see cref="EditedBagItemLine"/> por <c>CategoryLabel</c> (ver <see cref="BuildBagRows"/>),
/// con <c>HasIssue</c> siempre <c>false</c>: la UI de Mochila/PC (alternate storage, gen 1-3)
/// no deja crear una combinación inválida en primer lugar, así que no hay nada que
/// correlacionar ahí como sí pasa con <c>LegalityAnalysis</c> para Pokémon — decisión
/// confirmada en la sesión de diseño de esta pantalla.
///
/// No reemplaza ni modifica <see cref="EditedTrainerSummary"/> / <see cref="EditedBagPouchSummary"/>
/// / <see cref="EditedPokemonSummary"/> — siguen siendo propiedad de sus respectivas áreas
/// (Mochila incluida). Esta clase solo ENVUELVE instancias ya construidas; <see cref="IsIncluded"/>
/// reenvía get/set directo al objeto original, así que <c>EditApplyService.ApplyIncluded</c>
/// (que sigue leyendo las 3 listas originales sin cambios) ve exactamente el mismo valor que
/// el usuario tildó en el carrusel.
/// </remarks>
public sealed class ExportReviewCardViewModel : INotifyPropertyChanged
{
    public ExportReviewCardKind Kind { get; }
    public string DisplayName { get; }
    public string? Subtitle { get; }

    /// <summary>Evita depender de un converter de string vacío/null en el XAML (no pude
    /// confirmar si <c>StringConverters.IsNotNullOrEmpty</c> existe en esta versión exacta
    /// de Avalonia sin poder compilar acá) — Trainer/BagPouch no tienen subtítulo.</summary>
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    /// <summary>Línea de arriba de la tarjeta, en negrita — el apodo si existe (Pokémon con
    /// apodo puesto), si no <see cref="DisplayName"/> (Entrenador/Mochila, o un Pokémon sin
    /// apodo propio). Reemplaza el orden anterior (DisplayName siempre arriba) — ahora lo
    /// más específico/personal (el apodo) es lo primero que se lee.</summary>
    public string PrimaryLine => HasSubtitle ? Subtitle! : DisplayName;

    /// <summary>Línea de abajo, sin negrita — <see cref="DisplayName"/> (especie + ubicación,
    /// ej. "Bulbasaur — Equipo #3") solo cuando hay apodo real arriba. Null para Entrenador/
    /// Mochila o un Pokémon sin apodo, ya que ahí DisplayName ya se muestra arriba.</summary>
    public string? SecondaryLine => HasSubtitle ? DisplayName : null;

    public bool HasSecondaryLine => SecondaryLine != null;

    public Bitmap? Sprite { get; }

    /// <summary>Evita depender de un converter null-check en el XAML — la tarjeta usa esto
    /// para elegir entre mostrar el sprite real o el glyph de texto (IconGlyph).</summary>
    public bool HasSprite => Sprite != null;

    /// <summary>True para tarjetas de Mochila: usan el ícono vectorial de mochila (azul, a
    /// tamaño de sprite) en vez del glyph de texto.</summary>
    public bool IsBagPouch => Kind == ExportReviewCardKind.BagPouch;

    /// <summary>El glyph de texto solo se muestra si no hay sprite ni es una tarjeta de Mochila.</summary>
    public bool ShowGlyph => !HasSprite && !IsBagPouch;

    /// <summary>Glyph de texto para cuando no hay sprite real (hoy: tarjetas de Mochila,
    /// que usan <see cref="EditedBagPouchSummary.Icon"/>).</summary>
    public string? IconGlyph { get; }

    public List<EditedFieldSummary> Rows { get; }

    /// <summary>Primeras 4 filas — usadas por la tarjeta lateral (no focalizada), que
    /// muestra un checklist reducido en vez del completo (ver mockup).</summary>
    public List<EditedFieldSummary> RowsPreview => Rows.Count <= 4 ? Rows : Rows.Take(4).ToList();

    /// <summary>Siempre true para Trainer/BagPouch (su UI no permite crear un estado
    /// inválido — no hay LegalityAnalysis que correr ahí). Para Pokémon, viene directo de
    /// <see cref="EditedPokemonSummary.IsLegal"/>.</summary>
    public bool IsLegal { get; }

    /// <summary>Null salvo para Pokémon con problemas de legalidad.</summary>
    public string? FriendlyReason { get; }

    /// <summary>Null salvo para Pokémon con problemas de legalidad (detalle técnico crudo de
    /// PKHeX.Core, colapsado por defecto detrás de "Ver detalle técnico").</summary>
    public string? TechnicalDetail { get; }

    /// <summary>Gatea el expander de detalle técnico — solo para Pokémon con problemas de
    /// legalidad y detalle no vacío. Trainer/BagPouch nunca lo muestran (IsLegal siempre
    /// true para ellos).</summary>
    public bool ShowTechnicalDetailToggle => !IsLegal && !string.IsNullOrEmpty(TechnicalDetail);

    /// <summary>Solo relevante para Pokémon — ver <see cref="EditedPokemonSummary.IsNewlyCreated"/>.</summary>
    public bool IsNewlyCreated { get; }

    /// <summary>True si alguna fila del checklist está en warning. Para Trainer/BagPouch
    /// siempre false (ver remarks de la clase) — el badge "N a revisar" nunca las cuenta.</summary>
    public bool HasWarnings => Rows.Any(r => r.HasIssue);

    private bool _isFocused;
    /// <summary>Controlado externamente por <c>MainWindowViewModel.UpdateCardLayout</c> —
    /// sigue gateando qué CONTENIDO se muestra (checkbox, checklist completo vs. reducido,
    /// detalle técnico) — eso se mantiene binario. Lo que dejó de ser binario es el TAMAÑO/
    /// posición/opacidad de la tarjeta — ver <see cref="CardWidth"/> etc. más abajo.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set { if (_isFocused == value) return; _isFocused = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCheck)); }
    }

    /// <summary>El checkbox aparece en la tarjeta enfocada. En las de liberación solo si se puede excluir
    /// (ver PokemonReleaseService.CanExclude); si no, se deshace con "Deshacer"/"Descartar ediciones".</summary>
    public bool ShowCheck => _isFocused && (Kind != ExportReviewCardKind.Release || _canToggle);
    private bool _canToggle = true;

    // ── Layout continuo del carrusel (cover flow) — todo esto lo recalcula
    // MainWindowViewModel.UpdateCardLayout() cada vez que cambia el índice enfocado, en
    // base a |índice de esta tarjeta − índice enfocado|. Ninguna de estas 4 propiedades
    // depende de un converter ni de un RenderTransform — son doubles/int planos, para que
    // Avalonia pueda animarlas con Transitions normales sin pelearse con el "no animator
    // registered" que ya salió una vez con RenderTransform compuesto (ver EmptyStateView).

    private double _cardWidth = 200;
    /// <summary>Ancho de la tarjeta — más chico cuanto más lejos está del centro. La altura
    /// no se fija a mano: queda auto por contenido (el contenido en sí ya varía según
    /// IsFocused, así que la tarjeta enfocada es naturalmente más alta).</summary>
    public double CardWidth
    {
        get => _cardWidth;
        set { if (_cardWidth == value) return; _cardWidth = value; OnPropertyChanged(); }
    }

    private double _cardOpacity = 1;
    public double CardOpacity
    {
        get => _cardOpacity;
        set { if (_cardOpacity == value) return; _cardOpacity = value; OnPropertyChanged(); }
    }

    private double _cardLeft;
    /// <summary>Posición X absoluta dentro del Canvas del carrusel (Canvas.Left) — así la
    /// tarjeta enfocada queda SIEMPRE centrada en el viewport sin importar en qué posición
    /// de la lista esté (primera, última, del medio), a diferencia de un scroll horizontal
    /// normal que no garantiza eso.</summary>
    public double CardLeft
    {
        get => _cardLeft;
        set { if (_cardLeft == value) return; _cardLeft = value; OnPropertyChanged(); }
    }

    private int _cardZIndex;
    /// <summary>La tarjeta enfocada va siempre más arriba en Z — así se superpone
    /// visualmente sobre sus vecinas (efecto "abanico" del cover flow real), en vez de
    /// quedar todas al mismo nivel con separación limpia.</summary>
    public int CardZIndex
    {
        get => _cardZIndex;
        set { if (_cardZIndex == value) return; _cardZIndex = value; OnPropertyChanged(); }
    }

    private readonly Func<bool> _getIncluded;
    private readonly Action<bool> _setIncluded;

    public bool IsIncluded
    {
        get => _getIncluded();
        set
        {
            if (_getIncluded() == value) return;
            _setIncluded(value);
            OnPropertyChanged();
        }
    }

    private ExportReviewCardViewModel(ExportReviewCardKind kind, string displayName, string? subtitle,
        Bitmap? sprite, string? iconGlyph, List<EditedFieldSummary> rows, bool isNewlyCreated,
        bool isLegal, string? friendlyReason, string? technicalDetail,
        Func<bool> getIncluded, Action<bool> setIncluded)
    {
        Kind = kind;
        DisplayName = displayName;
        Subtitle = subtitle;
        Sprite = sprite;
        IconGlyph = iconGlyph;
        Rows = rows;
        IsNewlyCreated = isNewlyCreated;
        IsLegal = isLegal;
        FriendlyReason = friendlyReason;
        TechnicalDetail = technicalDetail;
        _getIncluded = getIncluded;
        _setIncluded = setIncluded;
    }

    public static ExportReviewCardViewModel FromTrainer(EditedTrainerSummary t) => new(
        ExportReviewCardKind.Trainer, t.DisplayName, subtitle: null, t.Sprite, iconGlyph: null,
        t.Changes, isNewlyCreated: false,
        isLegal: true, friendlyReason: null, technicalDetail: null,
        () => t.IsIncluded, v => t.IsIncluded = v);

    public static ExportReviewCardViewModel FromBagPouch(EditedBagPouchSummary b) => new(
        ExportReviewCardKind.BagPouch, b.PouchDisplayName, subtitle: null, sprite: null, b.Icon,
        BuildBagRows(b.Changes), isNewlyCreated: false,
        isLegal: true, friendlyReason: null, technicalDetail: null,
        () => b.IsIncluded, v => b.IsIncluded = v);

    public static ExportReviewCardViewModel FromPokemon(EditedPokemonSummary p) => new(
        ExportReviewCardKind.Pokemon, p.DisplayName, subtitle: p.Nickname, p.Sprite, iconGlyph: null,
        p.Changes, p.IsNewlyCreated,
        p.IsLegal, p.FriendlyReason, p.TechnicalDetail,
        () => p.IsIncluded, v => p.IsIncluded = v);

    /// <summary>Tarjeta de un Pokémon liberado (se quita del save al exportar). Siempre incluida.</summary>
    public static ExportReviewCardViewModel FromRelease(ReleaseRecord r, bool canToggle)
    {
        Bitmap? sprite = null;
        if (r.Original is { } o && !o.IsEgg)
            sprite = SpriteService.Instance.GetPokemonSprite(o.Species, o.IsShiny, form: o.Form);
        var rows = new List<EditedFieldSummary> { new(UiText.T("Release_Confirm"), new List<string> { r.Where }, false, null) };
        var card = new ExportReviewCardViewModel(ExportReviewCardKind.Release, r.DisplayName, subtitle: null, sprite,
            iconGlyph: sprite == null ? "🥚" : null,
            rows, isNewlyCreated: false, isLegal: true, friendlyReason: null, technicalDetail: null,
            () => r.Include, v => r.Include = v);
        card._canToggle = canToggle;
        return card;
    }

    /// <summary>Agrupa <see cref="EditedBagItemLine"/> por <c>CategoryLabel</c> — misma forma
    /// visual (<see cref="EditedFieldSummary"/>) que ya usan Pokémon/Entrenador. HasIssue
    /// siempre false (ver remarks de la clase).</summary>
    private static List<EditedFieldSummary> BuildBagRows(List<EditedBagItemLine> lines) =>
        lines.GroupBy(l => l.CategoryLabel)
             .Select(g => new EditedFieldSummary(
                 g.Key,
                 g.Select(l => $"{l.ItemName} ×{l.Quantity}").ToList(),
                 false,
                 null))
             .ToList();

    /// <summary>Orden fijo: Entrenador primero (si se editó algo), luego Mochila (una tarjeta
    /// por origen — Mochila / PC), luego Pokémon en el orden que ya trae la lista original.
    /// <paramref name="trainer"/> null si no se editó nada del entrenador esta sesión (mismo
    /// criterio que hoy usa <c>HasTrainerSummary</c>).</summary>
    public static List<ExportReviewCardViewModel> BuildAll(
        EditedTrainerSummary? trainer, List<EditedBagPouchSummary> bagPouches, List<EditedPokemonSummary> pokemon,
        IEnumerable<ReleaseRecord>? releases = null, Func<ReleaseRecord, bool>? canExclude = null)
    {
        var cards = new List<ExportReviewCardViewModel>();
        if (trainer != null) cards.Add(FromTrainer(trainer));
        cards.AddRange(bagPouches.Select(FromBagPouch));
        cards.AddRange(pokemon.Select(FromPokemon));
        if (releases != null) cards.AddRange(releases.Where(r => r.Original != null).Select(r => FromRelease(r, canExclude?.Invoke(r) ?? false)));
        return cards;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
