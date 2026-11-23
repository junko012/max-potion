using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Exxeguttor.UI.Controls;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

public enum AutoFixState { Working, Done, Failed }

/// <summary>
/// Modal de feedback de "Corregir automáticamente" (Rotom PC, el mismo lenguaje visual del modal
/// "Buscar IVs legales"). Antes el botón no daba ninguna señal: el usuario no sabía si había hecho
/// algo ni si debía volver a tocarlo (algunas correcciones buscan al azar y a veces no encuentran
/// nada). Ahora: Procesando (la PC trabaja) → Corregido (✔, se cierra solo) o No se pudo (con
/// Reintentar). El trabajo en sí lo define quien llama (PokemonEditorViewModel.ApplyAutoFixAsync).
/// </summary>
public class AutoFixViewModel : INotifyPropertyChanged
{
    private const int MinWorkingMs = 900;     // que la pantalla de "procesando" llegue a verse aunque el trabajo sea instantáneo
    private const int DoneVisibleMs = 1400;

    private readonly LocalizationService _loc;
    private string _workingTitle = string.Empty;
    private Func<Task<bool>>? _work;
    private bool _quiet;
    private string? _failedText;

    public AutoFixViewModel(LocalizationService loc)
    {
        _loc = loc;
        CloseCommand = new RelayCommand(Close);
        RetryCommand = new RelayCommand(() => _ = ExecuteAsync());

        var sprites = new List<Bitmap?> { SpriteService.Instance.GetPokemonSprite(479) };
        for (byte form = 1; form <= 5; form++)
            sprites.Add(SpriteService.Instance.GetPokemonSprite(479, false, false, form));
        RotomSprites = sprites;
    }

    // ── Estado ───────────────────────────────────────────────────────────────────────────
    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        private set { Set(ref _isVisible, value); OnPropertyChanged(nameof(IsRotomRunning)); }
    }

    private AutoFixState _state = AutoFixState.Working;
    public AutoFixState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(SceneMode));
            OnPropertyChanged(nameof(IsRotomRunning));
        }
    }

    public bool IsWorking => State == AutoFixState.Working;
    public bool IsDone => State == AutoFixState.Done;
    public bool IsFailed => State == AutoFixState.Failed;

    public RotomMatrixMode SceneMode => State switch
    {
        AutoFixState.Done => RotomMatrixMode.Found,
        AutoFixState.Failed => RotomMatrixMode.NoSolution,
        _ => RotomMatrixMode.Searching,
    };

    public bool IsRotomRunning => IsVisible && IsWorking;

    /// <summary>Velocidad media del cambio de formas de Rotom (la escena la escala de 1× a 3× con este valor).</summary>
    public double Progress => 0.4;

    public IReadOnlyList<Bitmap?> RotomSprites { get; }

    public string Title => State switch
    {
        AutoFixState.Done => _loc["AutoFix_Done_Title"],
        AutoFixState.Failed => _loc["AutoFix_Failed_Title"],
        _ => _workingTitle,
    };

    public string Message => State switch
    {
        AutoFixState.Done => _loc["AutoFix_Done_Text"],
        AutoFixState.Failed => _failedText ?? _loc["AutoFix_Failed_Text"],
        _ => string.Empty,
    };

    public string CloseLabel => _loc["AutoFix_Action_Close"];
    public string RetryLabel => _loc["AutoFix_Action_Retry"];

    public ICommand CloseCommand { get; }
    public ICommand RetryCommand { get; }

    // ── Flujo ────────────────────────────────────────────────────────────────────────────

    /// <summary>Muestra el modal mientras corre <paramref name="work"/> (true = se aplicó). Se asigna
    /// a <see cref="PokemonEditorViewModel.AutoFixFeedback"/>.</summary>
    public async Task RunAsync(string workingTitle, Func<Task<bool>> work)
    {
        if (IsVisible) return;               // ya hay una corrección en curso
        _workingTitle = workingTitle;
        _work = work;
        _quiet = false;
        _failedText = null;
        await ExecuteAsync();
    }

    /// <summary>Igual que <see cref="RunAsync"/> pero para una BÚSQUEDA que desemboca en otro modal: si
    /// termina bien se cierra enseguida, sin la pantalla "Corrección aplicada" (no corrigió nada todavía).
    /// Si falla muestra "No se pudo" como siempre.</summary>
    public async Task RunQuietAsync(string workingTitle, Func<Task<bool>> work, string? failedText = null)
    {
        if (IsVisible) return;
        _workingTitle = workingTitle;
        _work = work;
        _quiet = true;
        _failedText = failedText;
        await ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        if (_work == null) return;

        State = AutoFixState.Working;
        OnPropertyChanged(nameof(Title));
        IsVisible = true;

        var sw = Stopwatch.StartNew();
        bool ok;
        try { ok = await _work(); }
        catch { ok = false; }                // una falla inesperada se informa como "no se pudo", no deja el modal pegado

        var rest = MinWorkingMs - (int)sw.ElapsedMilliseconds;
        if (rest > 0) await Task.Delay(rest);

        if (ok && _quiet)
        {
            Close();
        }
        else if (ok)
        {
            State = AutoFixState.Done;
            await Task.Delay(DoneVisibleMs);
            if (State == AutoFixState.Done && IsVisible) Close();   // se cierra solo (o antes, con Aceptar)
        }
        else
        {
            State = AutoFixState.Failed;      // espera a que el usuario reintente o cierre
        }
    }

    private void Close()
    {
        IsVisible = false;
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Cambió el idioma con el modal abierto: título, texto y botones son getters sobre <c>_loc</c>.</summary>
    public void OnLocaleChanged() => OnPropertyChanged(string.Empty);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}
