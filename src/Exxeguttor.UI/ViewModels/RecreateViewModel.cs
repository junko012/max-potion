using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

public enum RecreateKind { Encounter, CatchRate, Level, IvSearch }

/// <summary>Lo que el editor le pide al modal: qué corrección confirmar y con qué datos.</summary>
public sealed record RecreateRequest(
    RecreateKind Kind,
    int Generation,
    string Species,
    IReadOnlyList<RecreateOption> Options,
    IReadOnlyList<byte> CatchValues,
    int CurrentCatchRate,
    int FromLevel, int ToLevel, uint FromExp, uint ToExp,
    Func<bool, CancellationToken, Task<IReadOnlyList<RecreateOption>>>? Reload = null,
    IReadOnlyList<IvSearchOption>? IvOptions = null);

/// <summary>Lo que el usuario confirmó (vuelve al editor).</summary>
public sealed record RecreateChoice(RecreateKind Kind, RecreateOption? Option, int Value, IvPidCandidate? Candidate = null);

/// <summary>Una opción elegible de la lista del modal.</summary>
public sealed class RecreateItemViewModel : INotifyPropertyChanged
{
    private readonly Action<RecreateItemViewModel> _onSelected;
    private bool _isSelected;

    public RecreateItemViewModel(string title, string subtitle, string tag, bool tagIsEgg,
        RecreateOption? option, int value, Action<RecreateItemViewModel> onSelected)
    {
        Title = title; Subtitle = subtitle; Tag = tag; TagIsEgg = tagIsEgg;
        Option = option; Value = value; _onSelected = onSelected;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Tag { get; }
    public bool HasTag => Tag.Length > 0;
    public bool TagIsEgg { get; }
    public bool TagIsSamePlace => HasTag && !TagIsEgg;
    public RecreateOption? Option { get; }
    public int Value { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            if (value) _onSelected(this);
        }
    }

    internal void SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return;
        _isSelected = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Fila de la tabla "qué cambia".</summary>
public sealed record RecreateChangeRow(string Attribute, string Before, string After, bool Changed);

/// <summary>
/// Modal de confirmación de las correcciones que reescriben el origen o un dato visible del Pokémon
/// (mockups/encounter_recreate/): "Recrear con un encuentro válido" (Gen2–Gen4), catch rate inválido
/// (Gen1) y nivel demasiado bajo (Gen1). Lista de opciones + tabla "qué cambia" + Cancelar/Aplicar.
/// Al aplicar entrega la elección al editor, que corre el trabajo con el modal de feedback de
/// "Corregir automáticamente" (Rotom PC).
/// </summary>
public class RecreateViewModel : INotifyPropertyChanged
{
    private const int EncounterVisible = 6;

    private readonly LocalizationService _loc;
    private readonly Func<RecreateChoice, Task> _apply;
    private RecreateRequest? _request;
    private List<RecreateItemViewModel> _all = [];
    private int _visibleCount;
    private bool _expanded;
    private RecreateItemViewModel? _selected;

    public RecreateViewModel(LocalizationService loc, Func<RecreateChoice, Task> apply)
    {
        _loc = loc;
        _apply = apply;
        CancelCommand = new RelayCommand(Close);
        ApplyCommand = new RelayCommand(() => _ = ApplyAsync(), () => _selected != null && !_isBusy);
        ToggleMoreCommand = new RelayCommand(ToggleMore);

        var rotom = new List<Bitmap?> { SpriteService.Instance.GetPokemonSprite(479) };
        RotomSprite = rotom[0];
    }

    public Bitmap? RotomSprite { get; }

    // ── Estado ───────────────────────────────────────────────────────────────────────────
    private bool _isVisible;
    public bool IsVisible { get => _isVisible; private set { _isVisible = value; OnPropertyChanged(); } }

    private string _title = string.Empty;
    public string Title { get => _title; private set { _title = value; OnPropertyChanged(); } }

    private string _subtitle = string.Empty;
    public string Subtitle { get => _subtitle; private set { _subtitle = value; OnPropertyChanged(); } }

    private string _note = string.Empty;
    public string Note { get => _note; private set { _note = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNote)); } }
    public bool HasNote => Note.Length > 0;

    private string _pickHint = string.Empty;
    public string PickHint { get => _pickHint; private set { _pickHint = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowPickHint)); } }
    public bool ShowPickHint => PickHint.Length > 0 && _selected == null;

    private bool _showKeepNature;
    public bool ShowKeepNature { get => _showKeepNature; private set { _showKeepNature = value; OnPropertyChanged(); } }
    private bool _keepNature;
    public bool KeepNature
    {
        get => _keepNature;
        set { if (_keepNature == value) return; _keepNature = value; OnPropertyChanged(); _ = ReloadAsync(value); }
    }
    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); RaiseApplyChanged(); } }
    public bool IsNotBusy => !_isBusy;
    public string KeepNatureLabel => _loc["Recreate_KeepNature_Label"];
    public string KeepNatureHint => _loc["Recreate_KeepNature_Hint"];
    public string BusyText => _loc["Recreate_KeepNature_Busy"];
    private string _emptyText = string.Empty;
    public string EmptyText { get => _emptyText; private set { _emptyText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasEmptyText)); } }
    public bool HasEmptyText => _emptyText.Length > 0;

    public ObservableCollection<RecreateItemViewModel> Items { get; } = [];
    public ObservableCollection<RecreateChangeRow> ChangeRows { get; } = [];
    public bool HasChangeRows => ChangeRows.Count > 0;

    public bool HasMore => _all.Count > _visibleCount;
    public string MoreLabel => _expanded
        ? _loc["Recreate_ShowLess"]
        : _request?.Kind == RecreateKind.CatchRate
            ? _loc["Recreate_Catch_ShowOthers"]
            : string.Format(CultureInfo.CurrentCulture, _loc["Recreate_ShowMore"], _all.Count - _visibleCount);

    public string CancelLabel => _loc["Recreate_Action_Cancel"];
    public string ApplyLabel => _loc["Recreate_Action_Apply"];
    public string ChangesHeader => _loc["Recreate_Changes_Header"];

    public ICommand CancelCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ToggleMoreCommand { get; }

    // ── Apertura ─────────────────────────────────────────────────────────────────────────
    public void Open(RecreateRequest req)
    {
        _reloadCts?.Cancel();
        _request = req;
        _expanded = false;
        _selected = null;
        _keepNature = false;
        OnPropertyChanged(nameof(KeepNature));
        IsBusy = false;
        EmptyText = string.Empty;
        ShowKeepNature = req.Kind == RecreateKind.Encounter && req.Generation is 3 or 4 && req.Reload != null
                         && req.Options.Any(o => o.NatureChanged);
        ChangeRows.Clear();
        Items.Clear();

        switch (req.Kind)
        {
            case RecreateKind.Encounter:
                Title = _loc["Recreate_Modal_Title"];
                Subtitle = _loc["Recreate_Modal_Subtitle"];
                Note = req.Generation == 4 ? _loc["Recreate_Gen4_Note"] : string.Empty;
                PickHint = _loc["Recreate_PickHint"];
                _visibleCount = EncounterVisible;
                _all = req.Options.Select(MakeEncounterItem).ToList();
                // Decisión de diseño: solo se preselecciona la primera opción si conserva el lugar de captura.
                if (_all.Count > 0 && _all[0].Option!.KeepsPlace) Select(_all[0]);
                break;

            case RecreateKind.CatchRate:
                Title = _loc["Recreate_Catch_Title"];
                Subtitle = string.Format(CultureInfo.CurrentCulture, _loc["Recreate_Catch_Subtitle"], req.CurrentCatchRate);
                Note = _loc["Recreate_Catch_Why"];
                PickHint = string.Empty;
                _visibleCount = 1;
                _all = req.CatchValues.Select((v, i) => new RecreateItemViewModel(
                    v.ToString(CultureInfo.CurrentCulture),
                    v == 0 ? _loc["Recreate_Catch_NoItem"] : string.Empty,
                    i == 0 ? _loc["Recreate_Catch_Proposed"] : string.Empty, false, null, v, Select)).ToList();
                if (_all.Count > 0) Select(_all[0]);
                break;

            case RecreateKind.IvSearch:
                Title = _loc["Recreate_Iv_Title"];
                Subtitle = _loc["Recreate_Iv_Subtitle"];
                Note = _loc["Recreate_Iv_Why"];
                PickHint = _loc["Recreate_PickHint"];
                _visibleCount = EncounterVisible;
                _all = (req.IvOptions ?? []).Select((o, i) => new RecreateItemViewModel(
                    o.Cand.IvText,
                    string.Join(" · ", new[]
                    {
                        string.Format(CultureInfo.CurrentCulture, _loc["Recreate_Iv_Changed"], o.Cand.Changed),
                        o.Nature,
                        o.Gender,
                    }.Where(t => t != "—")),
                    o.NatureChanged ? string.Empty : _loc["Recreate_Iv_KeepsNature"], false, null, i, Select)).ToList();
                break;

            default:
                Title = _loc["Recreate_Level_Title"];
                Subtitle = string.Format(CultureInfo.CurrentCulture, _loc["Recreate_Level_Subtitle"], req.Species, req.FromLevel, req.ToLevel);
                Note = _loc["Recreate_Level_Warning"];
                PickHint = string.Empty;
                _visibleCount = 1;
                _all = [new RecreateItemViewModel(
                    string.Format(CultureInfo.CurrentCulture, _loc["Recreate_Level_Item"], req.ToLevel),
                    string.Empty, string.Empty, false, null, req.ToLevel, Select)];
                Select(_all[0]);
                break;
        }

        RebuildItems();
        IsVisible = true;
        RaiseApplyChanged();
    }

    private RecreateItemViewModel MakeEncounterItem(RecreateOption o)
    {
        var parts = new List<string> { string.Format(CultureInfo.CurrentCulture, _loc["Recreate_Item_Level"], o.CaptureLevel) };
        if (o.Ball != "—") parts.Add(o.Ball);
        if (o.Nature != "—") parts.Add(o.Nature);
        if (o.Gender != "—") parts.Add(o.Gender);
        if (o.IvsChanged) parts.Add("IVs " + o.NewIvText);
        var tag = o.IsEgg ? _loc["Recreate_Tag_Egg"] : o.KeepsPlace ? _loc["Recreate_Tag_SamePlace"] : string.Empty;
        var title = o.IsEgg ? string.Format(CultureInfo.CurrentCulture, _loc["Recreate_Item_Hatched"], o.Place) : o.Place;
        return new RecreateItemViewModel(title, string.Join(" · ", parts), tag, o.IsEgg, o, 0, Select);
    }

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var item in _expanded ? _all : _all.Take(_visibleCount))
            Items.Add(item);
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreLabel));
    }

    private void ToggleMore()
    {
        _expanded = !_expanded;
        RebuildItems();
    }

    private void Select(RecreateItemViewModel item)
    {
        if (_selected != null && !ReferenceEquals(_selected, item)) _selected.SetSelectedSilently(false);
        _selected = item;
        item.SetSelectedSilently(true);
        BuildChangeRows();
        OnPropertyChanged(nameof(ShowPickHint));
        RaiseApplyChanged();
    }

    private void RaiseApplyChanged() => (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();

    // ── "Qué cambia" ─────────────────────────────────────────────────────────────────────
    private void BuildChangeRows()
    {
        ChangeRows.Clear();
        if (_request == null || _selected == null) return;

        switch (_request.Kind)
        {
            case RecreateKind.Encounter when _selected.Option is { } o:
                Row("Recreate_Attr_Place", o.OriginalPlace, o.Place);
                Row("Recreate_Attr_CaptureLevel", o.OriginalCaptureLevel.ToString(CultureInfo.CurrentCulture), o.CaptureLevel.ToString(CultureInfo.CurrentCulture));
                if (o.Ball != "—") Row("Recreate_Attr_Ball", o.OriginalBall, o.Ball);
                if (_request.Generation >= 3)
                {
                    Row("Recreate_Attr_Nature", o.OriginalNature, o.Nature);
                    Row("Recreate_Attr_Gender", o.OriginalGender, o.Gender);
                }
                if (o.IvsChanged) Row("Recreate_Attr_Ivs", o.OriginalIvText, o.NewIvText);
                // Gen5+: solo se muestra lo que la opción tuvo que reemplazar (lo demás se conserva tal cual)
                if (o.Groups.HasFlag(RecreateGroups.Identity)) Row("Recreate_Attr_Shiny", o.OriginalShiny, o.Shiny);
                if (o.Groups.HasFlag(RecreateGroups.Ribbons))
                    Row("Recreate_Attr_Ribbons", o.OriginalRibbons.ToString(CultureInfo.CurrentCulture), o.Ribbons.ToString(CultureInfo.CurrentCulture));
                if (o.Groups.HasFlag(RecreateGroups.Handler)) Row("Recreate_Attr_Handler", o.OriginalHandler, o.Handler);
                if (o.Awakening != o.OriginalAwakening)
                    Row("Recreate_Attr_Awakening", o.OriginalAwakening.ToString(CultureInfo.CurrentCulture), o.Awakening.ToString(CultureInfo.CurrentCulture));
                break;
            case RecreateKind.IvSearch when _request.IvOptions is { } ivs && _selected.Value < ivs.Count:
                var io = ivs[_selected.Value];
                Row("Recreate_Attr_Ivs", io.OriginalIvText, io.Cand.IvText);
                Row("Recreate_Attr_Nature", io.OriginalNature, io.Nature);
                break;
            case RecreateKind.CatchRate:
                Row("Recreate_Attr_CatchRate", _request.CurrentCatchRate.ToString(CultureInfo.CurrentCulture), _selected.Value.ToString(CultureInfo.CurrentCulture));
                break;
            case RecreateKind.Level:
                Row("Recreate_Attr_Level", _request.FromLevel.ToString(CultureInfo.CurrentCulture), _request.ToLevel.ToString(CultureInfo.CurrentCulture));
                Row("Recreate_Attr_Exp", _request.FromExp.ToString("N0", CultureInfo.CurrentCulture), _request.ToExp.ToString("N0", CultureInfo.CurrentCulture));
                break;
        }
        OnPropertyChanged(nameof(HasChangeRows));
    }

    private void Row(string key, string before, string after) =>
        ChangeRows.Add(new RecreateChangeRow(_loc[key], before, after, before != after));

    private CancellationTokenSource? _reloadCts;

    /// <summary>Casilla "Conservar la Naturaleza": recalcula las opciones en segundo plano. Se puede
    /// desmarcar en cualquier momento: cancela la búsqueda y vuelve a la lista original.</summary>
    private async Task ReloadAsync(bool keepNature)
    {
        var req = _request;
        if (req?.Reload == null) return;

        _reloadCts?.Cancel();
        var cts = _reloadCts = new CancellationTokenSource();

        // La lista vieja deja de valer ya mismo: nada seleccionado y Aplicar apagado mientras se busca.
        foreach (var item in _all) item.SetSelectedSilently(false);
        _selected = null;
        ChangeRows.Clear();
        OnPropertyChanged(nameof(HasChangeRows));
        EmptyText = string.Empty;
        IsBusy = keepNature;
        OnPropertyChanged(nameof(ShowPickHint));
        RaiseApplyChanged();

        try
        {
            var options = keepNature ? await req.Reload(true, cts.Token) : req.Options;
            if (cts.IsCancellationRequested || !ReferenceEquals(req, _request)) return;   // desmarcada o cerrada mientras tanto
            _all = options.Select(MakeEncounterItem).ToList();
            _expanded = false;
            if (_all.Count == 0) EmptyText = _loc["Recreate_KeepNature_None"];
            else if (_all[0].Option!.KeepsPlace && !keepNature) Select(_all[0]);
            RebuildItems();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (cts.IsCancellationRequested) return;
            EmptyText = _loc["Recreate_KeepNature_None"]; _all = []; RebuildItems();
        }
        finally
        {
            if (ReferenceEquals(_reloadCts, cts)) IsBusy = false;
            OnPropertyChanged(nameof(ShowPickHint));
            RaiseApplyChanged();
        }
    }

    // ── Acciones ─────────────────────────────────────────────────────────────────────────
    private void Close()
    {
        _reloadCts?.Cancel();   // una búsqueda en curso no debe seguir consumiendo CPU con el modal cerrado
        IsVisible = false;
    }

    private async Task ApplyAsync()
    {
        if (_request == null || _selected == null) return;
        var choice = new RecreateChoice(_request.Kind, _selected.Option, _selected.Value,
            _request.Kind == RecreateKind.IvSearch && _request.IvOptions is { } ivs && _selected.Value < ivs.Count
                ? ivs[_selected.Value].Cand : null);
        Close();
        await _apply(choice);
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    /// <summary>Cambió el idioma con el modal abierto: etiquetas (getters) y textos armados en <see cref="Open"/> (título,
    /// subtítulo, filas de opciones). Se vuelve a abrir con la misma solicitud conservando opción elegida y «ver más».
    /// Con «mantener naturaleza» activo las opciones vienen de una recarga asíncrona que no se puede rearmar: solo se
    /// refrescan las etiquetas.</summary>
    public void OnLocaleChanged()
    {
        if (IsVisible && _request != null && !_keepNature && !IsBusy)
        {
            var index = _selected == null ? -1 : _all.IndexOf(_selected);
            var expanded = _expanded;
            Open(_request);
            if (index >= 0 && index < _all.Count) Select(_all[index]);
            if (expanded && !_expanded) { _expanded = true; RebuildItems(); }
        }
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
