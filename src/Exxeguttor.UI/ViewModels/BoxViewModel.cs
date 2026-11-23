using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

public class BoxViewModel : INotifyPropertyChanged
{
    private readonly PokemonService _pokemonService;
    private readonly EditSessionService _editSession;
    private readonly PokemonReleaseService _release;

    private int    _currentBoxIndex;
    private int    _currentRowOffset;   // fila visible actual dentro de la caja (0-based, de 2 en 2)
    private string _currentBoxName = string.Empty;
    private int    _totalBoxes;
    private bool   _isVisible;
    private PokemonSlotViewModel? _selectedSlot;

    private const int Columns    = 6;
    private const int VisibleRows = 3;
    private const int PageSize   = Columns * VisibleRows; // 18 slots visibles

    public event Action<PKM, PokemonSlotKey>? PokemonSelected;
    /// <summary>Se dispara cuando el usuario clickea el "+" de un slot vacío — (boxIndex, slotIndex).</summary>
    public event Action<int, int>? CreateRequested;
    /// <summary>Se dispara al clickear el "−" de un slot con Pokémon (pide confirmar la liberación).</summary>
    public event Action<PokemonSlotKey>? ReleaseRequested;

    private readonly LocalizationService _loc;
    public LocalizationService Loc => _loc;

    public BoxViewModel(LocalizationService loc, PokemonService pokemonService, EditSessionService editSession,
                        PokemonReleaseService release)
    {
        _loc               = loc;
        _pokemonService    = pokemonService;
        _editSession       = editSession;
        _release           = release;
        Slots              = new ObservableCollection<PokemonSlotViewModel>();
        VisibleSlots       = new ObservableCollection<PokemonSlotViewModel>();
        PreviousBoxCommand = new RelayCommand(PreviousBox);
        NextBoxCommand     = new RelayCommand(NextBox);
        PreviousRowCommand = new RelayCommand(PreviousRow, () => _currentRowOffset > 0);
        NextRowCommand     = new RelayCommand(NextRow,     () => _currentRowOffset + VisibleRows < TotalRows);
        SelectSlotCommand  = new RelayCommand<PokemonSlotViewModel>(SelectSlot);
        CreateSlotCommand  = new RelayCommand<PokemonSlotViewModel>(RequestCreate);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    public bool   IsVisible       { get => _isVisible;       set => Set(ref _isVisible,       value); }
    public int    CurrentBoxIndex { get => _currentBoxIndex; set => Set(ref _currentBoxIndex, value); }
    public string CurrentBoxName  { get => _currentBoxName;  set => Set(ref _currentBoxName,  value); }
    public int    TotalBoxes      { get => _totalBoxes;       set => Set(ref _totalBoxes,       value); }

    public int TotalRows => (int)Math.Ceiling(Slots.Count / (double)Columns);

    public string BoxNavText =>
        string.Format(_loc["Box_Nav"], CurrentBoxIndex + 1, TotalBoxes, _currentRowOffset + 1,
            Math.Min(_currentRowOffset + VisibleRows, TotalRows), TotalRows);

    /// <summary>Cambió el idioma de la UI: el texto de navegación se calculó con el idioma anterior.</summary>
    public void OnLocaleChanged() => OnPropertyChanged(nameof(BoxNavText));

    public bool CanScrollRows => TotalRows > VisibleRows;

    public ObservableCollection<PokemonSlotViewModel> Slots        { get; }
    public ObservableCollection<PokemonSlotViewModel> VisibleSlots { get; }

    public PokemonSlotViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (_selectedSlot != null) _selectedSlot.IsSelected = false;
            Set(ref _selectedSlot, value);
            if (_selectedSlot != null) _selectedSlot.IsSelected = true;
        }
    }

    public ICommand PreviousBoxCommand { get; }
    public ICommand NextBoxCommand     { get; }
    public ICommand PreviousRowCommand { get; }
    public ICommand NextRowCommand     { get; }
    public ICommand SelectSlotCommand  { get; }
    public ICommand CreateSlotCommand  { get; }

    public void Initialize(int totalBoxes)
    {
        TotalBoxes      = totalBoxes;
        CurrentBoxIndex = 0;
        IsVisible       = true;
        LoadCurrentBox();
    }

    public void SelectSlot(PokemonSlotViewModel slot)
    {
        if (slot.SlotIndex < 0) return; // slot vacío de relleno
        SelectedSlot = slot;

        var key = PokemonSlotKey.ForBox(CurrentBoxIndex, slot.SlotIndex);
        var pending = _editSession.GetPendingCreation(key);
        if (pending != null)
        {
            PokemonSelected?.Invoke(pending, key);
            return;
        }

        try
        {
            var pokemon = _pokemonService.GetBox(CurrentBoxIndex);
            if (slot.SlotIndex >= 0 && slot.SlotIndex < pokemon.Count)
            {
                var pkm = pokemon[slot.SlotIndex];
                if (pkm.Species > 0)
                    PokemonSelected?.Invoke(pkm, key);
            }
        }
        catch (Exception ex) { Console.WriteLine($"SelectSlot error: {ex.Message}"); }
    }

    /// <summary>Clic en el "−" de un slot.</summary>
    public void RequestRelease(PokemonSlotViewModel slot)
    {
        if (slot.SlotIndex < 0 || slot.IsEmpty || !slot.ShowReleaseButton) return;
        ReleaseRequested?.Invoke(PokemonSlotKey.ForBox(CurrentBoxIndex, slot.SlotIndex));
    }

    /// <summary>Desvanece el slot (antes de quitarlo); false si no está visible en la caja actual.</summary>
    public bool FadeOutSlot(int boxIndex, int slotIndex)
    {
        if (boxIndex != CurrentBoxIndex) return false;
        var slot = Slots.FirstOrDefault(s => s.SlotIndex == slotIndex);
        if (slot == null) return false;
        slot.IsReleasing = true;
        return true;
    }

    private void RequestCreate(PokemonSlotViewModel slot)
    {
        if (slot.SlotIndex < 0) return; // slot vacío de relleno
        CreateRequested?.Invoke(CurrentBoxIndex, slot.SlotIndex);
    }

    /// <summary>Recarga la caja actual — usado después de crear un Pokémon nuevo, para que
    /// aparezca en la grilla. Nota: como reusa LoadCurrentBox, también resetea el scroll de
    /// filas a la primera fila — aceptable acá porque el Pokémon recién creado normalmente
    /// cae en un slot visible de esa primera página de todos modos.</summary>
    public void Refresh() => LoadCurrentBox();

    /// <summary>Recarga la caja actual conservando la fila visible (al liberar/deshacer no se quiere
    /// volver a la primera fila).</summary>
    public void RefreshKeepingScroll()
    {
        var row = _currentRowOffset;
        LoadCurrentBox();
        var maxStart = Math.Max(0, TotalRows - VisibleRows);
        _currentRowOffset = Math.Min(row, maxStart);
        RefreshVisibleSlots();
    }

    private void LoadCurrentBox()
    {
        try
        {
            var pokemon = _pokemonService.GetBox(CurrentBoxIndex);
            var names   = _pokemonService.GetBoxNames();

            CurrentBoxName = names.Count > CurrentBoxIndex
                ? names[CurrentBoxIndex]
                : string.Format(_loc["Box_DefaultName"], CurrentBoxIndex + 1);

            Slots.Clear();
            for (int i = 0; i < pokemon.Count; i++)
            {
                var slot = new PokemonSlotViewModel { SlotIndex = i };
                var key = PokemonSlotKey.ForBox(CurrentBoxIndex, i);
                var pending = _editSession.GetPendingCreation(key);
                if (pending != null)
                {
                    // Pokémon creado pero todavía sin aplicar al SaveFile real (ver
                    // EditSessionService.RecordPendingCreation) — se muestra igual, como si
                    // ya estuviera, aunque "vive" solo en la sesión hasta exportar.
                    slot.LoadFrom(pending);
                    slot.IsCreateSlot = false;
                }
                else
                {
                    slot.LoadFrom(pokemon[i]);
                    // En Caja: cualquier slot vacío puede usarse para crear un Pokémon (a
                    // diferencia de Equipo, donde solo el que sigue al último puede — ver
                    // PartyViewModel.Load).
                    slot.IsCreateSlot = slot.IsEmpty;
                }
                if (!slot.IsEmpty)
                    slot.ReleaseAvailable = _release.GetBlock(key) == PokemonReleaseService.Block.None;
                Slots.Add(slot);
            }

            _currentRowOffset = 0;
            RefreshVisibleSlots();
            OnPropertyChanged(nameof(BoxNavText));
            OnPropertyChanged(nameof(TotalRows));
            OnPropertyChanged(nameof(CanScrollRows));
        }
        catch (Exception ex)
        {
            CurrentBoxName = $"Error: {ex.Message}";
        }
    }

    private void RefreshVisibleSlots()
    {
        VisibleSlots.Clear();
        int start = _currentRowOffset * Columns;
        int end   = Math.Min(start + PageSize, Slots.Count);

        for (int i = start; i < end; i++)
            VisibleSlots.Add(Slots[i]);

        // Rellenar con slots vacíos si la página no está completa
        while (VisibleSlots.Count < PageSize)
            VisibleSlots.Add(new PokemonSlotViewModel { SlotIndex = -1 });

        OnPropertyChanged(nameof(BoxNavText));
        (PreviousRowCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextRowCommand     as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public void Clear()
    {
        Slots.Clear();
        VisibleSlots.Clear();
        CurrentBoxIndex = 0;
        TotalBoxes      = 0;
        IsVisible       = false;
        SelectedSlot    = null;
    }

    private void PreviousBox()
    {
        if (CurrentBoxIndex <= 0) return;
        CurrentBoxIndex--;
        LoadCurrentBox();
    }

    private void NextBox()
    {
        if (CurrentBoxIndex >= TotalBoxes - 1) return;
        CurrentBoxIndex++;
        LoadCurrentBox();
    }

    private void PreviousRow()
    {
        if (_currentRowOffset <= 0) return;
        _currentRowOffset -= VisibleRows;
        if (_currentRowOffset < 0) _currentRowOffset = 0;
        RefreshVisibleSlots();
    }

    private void NextRow()
    {
        if (_currentRowOffset + VisibleRows >= TotalRows) return;
        _currentRowOffset += VisibleRows;
        RefreshVisibleSlots();
    }
}
