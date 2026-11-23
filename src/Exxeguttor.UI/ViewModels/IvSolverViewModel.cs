using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Controls;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

public enum IvSolverState { Searching, Found, None }

/// <summary>Un IV en el modal (chip de la fila de 6): etiqueta corta, valor y cómo se pinta.</summary>
public sealed record IvChipViewModel(string Label, int Value, bool IsPerfect, bool IsDifferent);

/// <summary>Una fila de la tabla "qué cambia" de la opción elegida.</summary>
public sealed record IvChangeRowViewModel(string Attribute, string Value, string Status, bool Changed);

/// <summary>
/// Modal "Buscar IVs legales" (mockups/ivs_solver_modal/). Corre <see cref="IvSeedSolver"/> con
/// progreso real y cancelación, y muestra el resultado: opciones exactas elegibles, o —si no hay
/// ninguna semilla con esos IVs— la más cercana. Aplicar entrega la opción elegida al editor
/// (<see cref="PokemonEditorViewModel.ApplyIvSeedOption"/>), que la guarda como una edición
/// compuesta de la sesión.
/// </summary>
public class IvSolverViewModel : INotifyPropertyChanged
{
    private readonly LocalizationService _loc;
    private readonly Action<IvSeedPlan, IvSeedOption> _apply;

    private IvSeedPlan? _plan;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<IvSeedOption> _options = [];
    private IvSeedOption? _closest;
    private int _optionIndex;

    public IvSolverViewModel(LocalizationService loc, Action<IvSeedPlan, IvSeedOption> apply)
    {
        _loc = loc;
        _apply = apply;

        CancelCommand = new RelayCommand(Cancel);
        DiscardCommand = new RelayCommand(Cancel);
        BackToOriginalCommand = new RelayCommand(Cancel);
        ApplyCommand = new RelayCommand(ApplyCurrent);
        UseClosestCommand = new RelayCommand(UseClosest);
        PrevOptionCommand = new RelayCommand(() => MoveOption(-1));
        NextOptionCommand = new RelayCommand(() => MoveOption(+1));

        // Sprites de Rotom para la escena: base + Calor, Lavadora, Frío, Ventilador, Corte
        var sprites = new List<Bitmap?> { SpriteService.Instance.GetPokemonSprite(479) };
        for (byte form = 1; form <= 5; form++)
            sprites.Add(SpriteService.Instance.GetPokemonSprite(479, false, false, form));
        RotomSprites = sprites;
    }

    // ── Estado ───────────────────────────────────────────────────────────────────────────
    private bool _isVisible;
    public bool IsVisible { get => _isVisible; private set => Set(ref _isVisible, value); }

    private IvSolverState _state = IvSolverState.Searching;
    public IvSolverState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(IsFound));
            OnPropertyChanged(nameof(IsNone));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(SceneMode));
            OnPropertyChanged(nameof(IsRotomRunning));
        }
    }

    public bool IsSearching => State == IvSolverState.Searching;
    public bool IsFound => State == IvSolverState.Found;
    public bool IsNone => State == IvSolverState.None;

    /// <summary>Estado de la escena Rotom PC (control propio).</summary>
    public RotomMatrixMode SceneMode => State switch
    {
        IvSolverState.Found => RotomMatrixMode.Found,
        IvSolverState.None => RotomMatrixMode.NoSolution,
        _ => RotomMatrixMode.Searching,
    };

    public bool IsRotomRunning => IsVisible && IsSearching;
    public IReadOnlyList<Bitmap?> RotomSprites { get; }

    public string Title => State switch
    {
        IvSolverState.Found => _loc["IvSolver_Found_Title"],
        IvSolverState.None => _loc["IvSolver_None_Title"],
        _ => _loc["IvSolver_Searching_Title"],
    };

    public string HintText => _loc["IvSolver_Searching_Hint"];
    public string VerifiedText => _loc["IvSolver_Found_Verified"];

    // ── Progreso (estado "Buscando") ────────────────────────────────────────────────────
    private double _progress;
    public double Progress
    {
        get => _progress;
        private set { Set(ref _progress, value); OnPropertyChanged(nameof(InverseProgress)); }
    }
    public double InverseProgress => 1.0 - _progress;

    private string _progressText = "0 %";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    private string _checkedText = string.Empty;
    public string CheckedText { get => _checkedText; private set => Set(ref _checkedText, value); }

    private string _timeText = string.Empty;
    public string TimeText { get => _timeText; private set => Set(ref _timeText, value); }

    private string _hitsText = string.Empty;
    public string HitsText { get => _hitsText; private set => Set(ref _hitsText, value); }

    // ── IVs mostrados ───────────────────────────────────────────────────────────────────
    private IReadOnlyList<IvChipViewModel> _ivChips = [];
    /// <summary>En "Buscando": los IVs objetivo. En "Encontrada": los de la opción elegida. En
    /// "Sin solución": los de la más cercana (las diferencias contra lo pedido, marcadas).</summary>
    public IReadOnlyList<IvChipViewModel> IvChips { get => _ivChips; private set => Set(ref _ivChips, value); }

    // ── Opciones (estado "Encontrada") ──────────────────────────────────────────────────
    private string _optionLabel = string.Empty;
    public string OptionLabel { get => _optionLabel; private set => Set(ref _optionLabel, value); }

    public bool HasMultipleOptions => _options.Count > 1;

    private IReadOnlyList<IvChangeRowViewModel> _changeRows = [];
    public IReadOnlyList<IvChangeRowViewModel> ChangeRows { get => _changeRows; private set => Set(ref _changeRows, value); }

    // ── Estado "Sin solución" ───────────────────────────────────────────────────────────
    private string _noneText = string.Empty;
    public string NoneText { get => _noneText; private set => Set(ref _noneText, value); }

    public bool HasClosest => _closest != null;

    // ── Textos de botones ───────────────────────────────────────────────────────────────
    public string CancelLabel => _loc["IvSolver_Action_Cancel"];
    public string ApplyLabel => _loc["IvSolver_Action_Apply"];
    public string DiscardLabel => _loc["IvSolver_Action_Discard"];
    public string UseClosestLabel => _loc["IvSolver_Action_UseClosest"];
    public string BackToOriginalLabel => _loc["IvSolver_Action_BackToOriginal"];

    public ICommand CancelCommand { get; }
    public ICommand DiscardCommand { get; }
    public ICommand BackToOriginalCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand UseClosestCommand { get; }
    public ICommand PrevOptionCommand { get; }
    public ICommand NextOptionCommand { get; }

    // ── Flujo ───────────────────────────────────────────────────────────────────────────

    /// <summary>Abre el modal y arranca la búsqueda. Llamar desde el hilo de UI (el progreso se
    /// publica en el contexto de sincronización de quien crea el <see cref="Progress{T}"/>).</summary>
    public async void Open(IvSeedPlan plan)
    {
        if (IsVisible) return;

        _plan = plan;
        _options = [];
        _closest = null;
        _optionIndex = 0;
        Progress = 0;
        ProgressText = "0 %";
        CheckedText = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Checked"], "0", FormatMillions(1UL << 32));
        TimeText = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Time"], "0:00", "—");
        HitsText = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Hits"], 0);
        IvChips = BuildChips(plan.TargetIvs, plan.TargetIvs);
        State = IvSolverState.Searching;
        IsVisible = true;
        OnPropertyChanged(nameof(IsRotomRunning));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HasMultipleOptions));
        OnPropertyChanged(nameof(HasClosest));

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new System.Progress<IvSearchProgress>(OnProgress);

        IvSearchResult result;
        try { result = await IvSeedSolver.SearchAsync(plan, progress, token); }
        catch (Exception)
        {
            // Una falla inesperada no debe dejar el modal pegado: se cierra sin tocar nada.
            Close();
            return;
        }

        if (token.IsCancellationRequested || result.Cancelled) { Close(); return; }
        ShowResult(plan, result);
    }

    private void OnProgress(IvSearchProgress p)
    {
        if (!IsSearching) return;
        Progress = p.Fraction;
        ProgressText = $"{Math.Round(p.Fraction * 100)} %";
        CheckedText = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Checked"],
            FormatMillions(p.Checked), FormatMillions(p.Total));

        // El tiempo restante sale del ritmo REAL de lo ya recorrido, no de una constante
        // (depende de cuántos núcleos tenga el equipo).
        var left = p.Fraction > 0.02
            ? TimeSpan.FromSeconds(p.Elapsed.TotalSeconds * (1 - p.Fraction) / p.Fraction)
            : (TimeSpan?)null;
        TimeText = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Time"],
            FormatTime(p.Elapsed), left is { } l ? "≈ " + FormatTime(l) : "—");
        HitsText = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Hits"], p.ExactFound);
    }

    private void ShowResult(IvSeedPlan plan, IvSearchResult result)
    {
        _options = result.Options;
        _closest = result.Closest;
        _optionIndex = 0;

        if (_options.Count > 0)
        {
            State = IvSolverState.Found;
            ShowOption();
        }
        else
        {
            RenderNone(plan, result);
            State = IvSolverState.None;
        }

        OnPropertyChanged(nameof(HasMultipleOptions));
        OnPropertyChanged(nameof(HasClosest));
        OnPropertyChanged(nameof(IsRotomRunning));
    }

    private IvSearchResult? _lastResult;

    private void RenderNone(IvSeedPlan plan, IvSearchResult result)
    {
        _lastResult = result;
        if (_closest != null)
        {
            IvChips = BuildChips(_closest.Ivs, plan.TargetIvs);
            NoneText = string.Format(CultureInfo.CurrentCulture,
                _loc[result.Exhaustive ? "IvSolver_None_Exhaustive_Closest" : "IvSolver_None_NotExhaustive_Closest"],
                _closest.Distance, FormatTime(result.Elapsed));
        }
        else
        {
            IvChips = BuildChips(plan.TargetIvs, plan.TargetIvs);
            NoneText = _loc["IvSolver_None_NoCandidate"];
        }
    }

    /// <summary>Cambió el idioma con el modal abierto: las etiquetas de solo lectura se reevalúan y los textos que se
    /// armaron al llegar el resultado (chips, filas de cambios, «opción x de y») se vuelven a armar.</summary>
    public void OnLocaleChanged()
    {
        OnPropertyChanged(string.Empty);
        if (!IsVisible || _plan == null) return;
        switch (State)
        {
            case IvSolverState.Found: ShowOption(); break;
            case IvSolverState.None when _lastResult != null: RenderNone(_plan, _lastResult); break;
            case IvSolverState.Searching: IvChips = BuildChips(_plan.TargetIvs, _plan.TargetIvs); break;
        }
    }

    private void ShowOption()
    {
        if (_plan == null || _options.Count == 0) return;
        var option = _options[_optionIndex];
        IvChips = BuildChips(option.Ivs, _plan.TargetIvs);
        ChangeRows = BuildRows(_plan, option);
        OptionLabel = string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_OptionOf"],
            _optionIndex + 1, _options.Count, Summarize(option));
    }

    private void MoveOption(int delta)
    {
        if (_options.Count < 2) return;
        _optionIndex = (_optionIndex + delta + _options.Count) % _options.Count;
        ShowOption();
    }

    private void ApplyCurrent()
    {
        if (_plan == null || _options.Count == 0) return;
        _apply(_plan, _options[_optionIndex]);
        Close();
    }

    private void UseClosest()
    {
        if (_plan == null || _closest == null) return;
        _apply(_plan, _closest);
        Close();
    }

    /// <summary>Cancelar / Descartar / Volver a los IVs originales: no deja ninguna edición.</summary>
    private void Cancel()
    {
        _cts?.Cancel();
        Close();
    }

    private void Close()
    {
        IsVisible = false;
        OnPropertyChanged(nameof(IsRotomRunning));
        _plan = null;
        _options = [];
        _closest = null;
    }

    // ── Armado de textos ────────────────────────────────────────────────────────────────

    private IReadOnlyList<IvChipViewModel> BuildChips(int[] ivs, int[] target)
    {
        string[] keys = ["IvSolver_Stat_Hp", "IvSolver_Stat_Atk", "IvSolver_Stat_Def",
                         "IvSolver_Stat_Spa", "IvSolver_Stat_Spd", "IvSolver_Stat_Spe"];
        var chips = new List<IvChipViewModel>(6);
        for (var i = 0; i < 6; i++)
            chips.Add(new IvChipViewModel(_loc[keys[i]], ivs[i], ivs[i] == 31, ivs[i] != target[i]));
        return chips;
    }

    private string Summarize(IvSeedOption option)
    {
        if (option.PreservedCount == 3) return _loc["IvSolver_Option_KeepsAll"];
        var changed = new List<string>();
        if (option.NatureChanged) changed.Add(_loc["IvSolver_Attr_Nature"]);
        if (option.AbilityChanged) changed.Add(_loc["IvSolver_Attr_Ability"]);
        if (option.GenderChanged) changed.Add(_loc["IvSolver_Attr_Gender"]);
        return string.Format(CultureInfo.CurrentCulture, _loc["IvSolver_Option_Changes"], string.Join(", ", changed));
    }

    private IReadOnlyList<IvChangeRowViewModel> BuildRows(IvSeedPlan plan, IvSeedOption option)
    {
        var lang = DataLanguageService.Instance.PkhexCode;
        var strings = GameInfo.GetStrings(lang);
        var kept = _loc["IvSolver_Status_Kept"];
        var changed = _loc["IvSolver_Status_Changed"];

        var edited = plan.Edited;
        string AbilityName(int id) => id > 0 && id < strings.Ability.Count ? strings.Ability[id] : "—";
        string GenderSymbol(byte g) => g switch { 0 => "♂", 1 => "♀", _ => "—" };

        string Describe(string oldValue, string newValue, bool didChange) =>
            didChange ? $"{oldValue} → {newValue}" : newValue;

        var rows = new List<IvChangeRowViewModel>
        {
            new(_loc["IvSolver_Attr_Nature"],
                Describe(strings.Natures[(int)edited.Nature], strings.Natures[(int)option.Nature], option.NatureChanged),
                option.NatureChanged ? changed : kept, option.NatureChanged),
            new(_loc["IvSolver_Attr_Ability"],
                Describe(AbilityName(edited.Ability), AbilityName(option.Ability), option.AbilityChanged),
                option.AbilityChanged ? changed : kept, option.AbilityChanged),
            new(_loc["IvSolver_Attr_Gender"],
                Describe(GenderSymbol(edited.Gender), GenderSymbol(option.Gender), option.GenderChanged),
                option.GenderChanged ? changed : kept, option.GenderChanged),
            // El brillante nunca cambia: una semilla que lo cambiaría no se ofrece (ver IvSeedSolver).
            new(_loc["IvSolver_Attr_Shiny"], _loc[edited.IsShiny ? "IvSolver_Yes" : "IvSolver_No"], kept, false),
            new(_loc["IvSolver_Attr_Size"], "—", option.SizeChanged ? changed : kept, option.SizeChanged),
        };
        return rows;
    }

    private static string FormatMillions(ulong n) =>
        (n / 1_000_000UL).ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatTime(TimeSpan t) =>
        $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ── INotifyPropertyChanged ──────────────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
