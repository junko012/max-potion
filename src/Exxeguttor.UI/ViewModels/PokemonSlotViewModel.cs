using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Exxeguttor.App.Services;
using Exxeguttor.UI.Services;
using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

public class PokemonSlotViewModel : INotifyPropertyChanged
{
    private string  _name      = string.Empty;
    private int     _level;
    private bool    _isEmpty   = true;
    private bool    _isShiny;
    private string  _gender    = string.Empty;
    private int     _speciesId;
    private bool    _isSelected;
    private Bitmap? _sprite;
    private bool    _hasHeldItem;
    private Bitmap? _heldItemSprite;
    private bool    _isCreateSlot;
    private bool    _isJustCreated;
    private bool    _releaseAvailable = true;
    private bool    _releaseDisabled;
    private bool    _isReleasing;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    public int SlotIndex { get; init; }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            Set(ref _isEmpty, value);
            OnPropertyChanged(nameof(ShowEmptyDash));
            OnPropertyChanged(nameof(ShowReleaseButton));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public int Level
    {
        get => _level;
        set => Set(ref _level, value);
    }

    public bool IsShiny
    {
        get => _isShiny;
        set => Set(ref _isShiny, value);
    }

    public string Gender
    {
        get => _gender;
        set => Set(ref _gender, value);
    }

    public int SpeciesId
    {
        get => _speciesId;
        set => Set(ref _speciesId, value);
    }

    /// <summary>
    /// Sprite del Pokémon cargado desde recursos embebidos.
    /// Null si los sprites no fueron descargados durante el build.
    /// </summary>
    public Bitmap? Sprite
    {
        get => _sprite;
        set => Set(ref _sprite, value);
    }

    /// <summary>True cuando hay sprite disponible para mostrar.</summary>
    public bool HasSprite => _sprite != null;

    /// <summary>True si el Pokémon tiene algún objeto equipado (pkm.HeldItem > 0).
    /// Gatea el badge de objeto en la esquina del sprite en Box/Party.</summary>
    public bool HasHeldItem
    {
        get => _hasHeldItem;
        set => Set(ref _hasHeldItem, value);
    }

    /// <summary>Sprite en miniatura del objeto equipado, o null si no se encontró ese sprite
    /// puntual — en ese caso el badge cae a un ícono genérico dibujado en XAML (no existe
    /// ningún PNG genérico de "objeto equipado" en SpriteService, se revisó a propósito).</summary>
    public Bitmap? HeldItemSprite
    {
        get => _heldItemSprite;
        set => Set(ref _heldItemSprite, value);
    }

    /// <summary>True cuando hay que mostrar el "+" grande de crear Pokémon en este slot
    /// vacío. En Caja: todos los slots vacíos. En Equipo: solo el que sigue al último
    /// Pokémon (ver BoxViewModel/PartyViewModel — este flag se decide del lado de esos
    /// ViewModels, no acá, porque el criterio depende del panel).</summary>
    /// <summary>True cuando hay que mostrar el "+" grande de crear Pokémon en este slot
    /// vacío. En Caja: todos los slots vacíos. En Equipo: solo el que sigue al último
    /// Pokémon (ver BoxViewModel/PartyViewModel — este flag se decide del lado de esos
    /// ViewModels, no acá, porque el criterio depende del panel).</summary>
    public bool IsCreateSlot
    {
        get => _isCreateSlot;
        set
        {
            Set(ref _isCreateSlot, value);
            OnPropertyChanged(nameof(ShowEmptyDash));
        }
    }

    /// <summary>True para el "—" tenue de siempre — solo cuando el slot está vacío y NO
    /// ofrece crear (hoy casi no pasa en Caja, ya que todo vacío ofrece crear; sigue
    /// existiendo para los slots de relleno de BoxViewModel.RefreshVisibleSlots, que quedan
    /// con IsCreateSlot=false por default).</summary>
    public bool ShowEmptyDash => IsEmpty && !IsCreateSlot;

    // ── Liberar Pokémon: botón "−" en la esquina del slot (ver PokemonReleaseService) ─────────
    /// <summary>False si este save/slot no admite liberar (Let's Go, slot protegido): el botón no se muestra.</summary>
    public bool ReleaseAvailable
    {
        get => _releaseAvailable;
        set { Set(ref _releaseAvailable, value); OnPropertyChanged(nameof(ShowReleaseButton)); }
    }

    /// <summary>True si el "−" se ve pero deshabilitado (único Pokémon del equipo).</summary>
    public bool ReleaseDisabled
    {
        get => _releaseDisabled;
        set { Set(ref _releaseDisabled, value); OnPropertyChanged(nameof(ReleaseTooltip)); }
    }

    /// <summary>True durante el desvanecido previo a quitar el slot.</summary>
    public bool IsReleasing
    {
        get => _isReleasing;
        set => Set(ref _isReleasing, value);
    }

    public bool ShowReleaseButton => !IsEmpty && ReleaseAvailable;

    public string ReleaseTooltip => UiText.T(ReleaseDisabled ? "Release_DisabledTip" : "Release_Tip");

    /// <summary>True brevemente (450ms) justo después de crear un Pokémon en este slot —
    /// dispara el pulso visual antes de que se seleccione automáticamente. Mismo patrón que
    /// MainWindowViewModel.IsNotebookPulsing.</summary>
    public bool IsJustCreated
    {
        get => _isJustCreated;
        set => Set(ref _isJustCreated, value);
    }

    /// <summary>True cuando hay que mostrar el ícono genérico (tiene objeto, pero no se
    /// encontró su sprite puntual).</summary>
    public bool ShowGenericHeldItemBadge => HasHeldItem && _heldItemSprite == null;

    /// <summary>Sprite de Poké Ball usado como fallback del badge de objeto cuando no se
    /// encuentra el sprite del objeto puntual — "poke-ball.png" ya existe en los assets
    /// embebidos, no hace falta agregar nada nuevo. Instancia (no static): los bindings
    /// compilados de Avalonia (AvaloniaUseCompiledBindingsByDefault=true) necesitan una
    /// propiedad de instancia, no static, para resolver {Binding HeldItemFallbackSprite}.</summary>
    public Bitmap? HeldItemFallbackSprite => SpriteService.Instance.GetItemSprite("poke-ball");

    public string LevelText => IsEmpty ? string.Empty : $"Nv.{Level}";

    public void LoadFrom(PKM pkm)
    {
        if (pkm.Species == 0)
        {
            Clear();
            return;
        }

        SpeciesId = pkm.Species;
        Name      = pkm.IsNicknamed ? pkm.Nickname : FormNameFormatter.Format(DataLanguageService.Instance.Strings.Species[pkm.Species], pkm.Species, pkm.Form);
        Level     = pkm.CurrentLevel;
        IsShiny   = pkm.IsShiny;
        Gender    = pkm.Gender switch
        {
            0 => "♂",
            1 => "♀",
            _ => string.Empty
        };
        IsEmpty = false;

        // Cargar sprite desde recursos embebidos
        Sprite = SpriteService.Instance.GetPokemonSprite(pkm.Species, pkm.IsShiny, form: pkm.Form);

        // Badge de objeto equipado — mismo criterio de resolución de nombre que
        // PokemonEditorViewModel.HeldItem (strings.Item[id] vía GameInfo), para que el
        // sprite que se busca sea consistente con el que ya se ve en el combo del tab Info.
        HasHeldItem = pkm.HeldItem > 0;
        HeldItemSprite = HasHeldItem
            ? SpriteService.Instance.GetItemSprite(GameInfo.GetStrings("en").Item[pkm.HeldItem])
            : null;

        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(HasSprite));
        OnPropertyChanged(nameof(ShowGenericHeldItemBadge));
    }

    public void Clear()
    {
        Name      = string.Empty;
        Level     = 0;
        IsShiny   = false;
        Gender    = string.Empty;
        SpeciesId = 0;
        IsEmpty   = true;
        Sprite    = null;
        HasHeldItem    = false;
        HeldItemSprite = null;
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(HasSprite));
        OnPropertyChanged(nameof(ShowGenericHeldItemBadge));
    }
}
