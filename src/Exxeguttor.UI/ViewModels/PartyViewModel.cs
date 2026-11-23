using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

public class PartyViewModel : INotifyPropertyChanged
{
    private readonly PokemonService _pokemonService;
    private readonly EditSessionService _editSession;
    private readonly PokemonReleaseService _release;
    private bool _isVisible;

    public event Action<PKM, PokemonSlotKey>? PokemonSelected;
    /// <summary>Se dispara cuando el usuario clickea el "+" del slot siguiente al último
    /// Pokémon del equipo — slotIndex.</summary>
    public event Action<int>? CreateRequested;
    /// <summary>Clic en el "−" de un Pokémon del equipo (pide confirmar la liberación).</summary>
    public event Action<PokemonSlotKey>? ReleaseRequested;

    public LocalizationService Loc { get; }

    public PartyViewModel(LocalizationService loc, PokemonService pokemonService, EditSessionService editSession,
                          PokemonReleaseService release)
    {
        Loc               = loc;
        _pokemonService   = pokemonService;
        _editSession      = editSession;
        _release          = release;
        Slots             = new ObservableCollection<PokemonSlotViewModel>();
        SelectSlotCommand = new RelayCommand<int>(SelectSlot);
        CreateSlotCommand = new RelayCommand<int>(RequestCreate);

        for (int i = 0; i < 6; i++)
            Slots.Add(new PokemonSlotViewModel { SlotIndex = i });
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

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public ObservableCollection<PokemonSlotViewModel> Slots { get; }
    public ICommand SelectSlotCommand { get; }
    public ICommand CreateSlotCommand { get; }

    public void Load()
    {
        try
        {
            var party = _pokemonService.GetParty();
            var firstEmptyMarked = false;
            for (int i = 0; i < 6; i++)
            {
                var key = PokemonSlotKey.ForParty(i);
                var pending = _editSession.GetPendingCreation(key);
                if (pending != null)
                {
                    // Pokémon creado pero todavía sin aplicar al SaveFile real — ver
                    // BoxViewModel.LoadCurrentBox para el mismo criterio del lado de Caja.
                    Slots[i].LoadFrom(pending);
                    Slots[i].IsCreateSlot = false;
                    // Ocupa su lugar pero NO consume el "+": el "+" va en el primer slot LIBRE que
                    // sigue (antes acá se marcaba firstEmptyMarked = true, y por eso, tras crear un
                    // Pokémon, el slot vacío siguiente se quedaba sin "+").
                }
                else if (i < party.Count && party[i].Species != 0)
                {
                    Slots[i].LoadFrom(party[i]);
                    Slots[i].IsCreateSlot = false;
                }
                else
                {
                    Slots[i].Clear();
                    // Solo el primer slot vacío (el que sigue al último Pokémon) ofrece
                    // crear — un equipo real nunca tiene huecos entre Pokémon, así que no
                    // tiene sentido permitir "crear" en el resto.
                    Slots[i].IsCreateSlot = !firstEmptyMarked;
                    firstEmptyMarked = true;
                }
            }
            // "−": se ve en cada Pokémon; deshabilitado si es el único del equipo; oculto si el save no admite liberar.
            for (int i = 0; i < 6; i++)
            {
                var block = Slots[i].IsEmpty ? PokemonReleaseService.Block.Empty : _release.GetBlock(PokemonSlotKey.ForParty(i));
                Slots[i].ReleaseAvailable = block is PokemonReleaseService.Block.None or PokemonReleaseService.Block.LastPartyMember;
                Slots[i].ReleaseDisabled  = block == PokemonReleaseService.Block.LastPartyMember;
                Slots[i].IsReleasing      = false;
            }
            IsVisible = true;
        }
        catch { Clear(); }
    }

    /// <summary>Clic en el "−" de un slot del equipo.</summary>
    public void RequestRelease(PokemonSlotViewModel slot)
    {
        if (slot.IsEmpty || !slot.ShowReleaseButton || slot.ReleaseDisabled) return;
        ReleaseRequested?.Invoke(PokemonSlotKey.ForParty(slot.SlotIndex));
    }

    /// <summary>Desvanece el slot antes de quitarlo.</summary>
    public void FadeOutSlot(int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < Slots.Count) Slots[slotIndex].IsReleasing = true;
    }

    private void RequestCreate(int slotIndex) => CreateRequested?.Invoke(slotIndex);

    /// <summary>Alias de Load() para el after-creación — mismo método, nombre más claro en
    /// el call site de MainWindowViewModel.</summary>
    public void Refresh() => Load();

    public void SelectSlot(int slotIndex)
    {
        var key = PokemonSlotKey.ForParty(slotIndex);
        var pending = _editSession.GetPendingCreation(key);
        if (pending != null)
        {
            PokemonSelected?.Invoke(pending, key);
            return;
        }

        try
        {
            var party = _pokemonService.GetParty();
            if (slotIndex >= 0 && slotIndex < party.Count && party[slotIndex].Species > 0)
                PokemonSelected?.Invoke(party[slotIndex], key);
            else
                Console.WriteLine($"[PartyVM] SelectSlot skip: idx={slotIndex} partyCount={party.Count}");
        }
        catch (Exception ex) { Console.WriteLine($"[PartyVM] SelectSlot error: {ex.Message}"); }
    }

    public void Clear()
    {
        foreach (var slot in Slots)
            slot.Clear();
        IsVisible = false;
    }
}
