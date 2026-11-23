using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Exxeguttor.App.Services;
using Exxeguttor.UI.i18n;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

public class TrainerViewModel : INotifyPropertyChanged
{
    private readonly LocalizationService _loc;

    /// <summary>Textos localizados para los bindings de <c>TrainerView</c> (<c>{Binding Loc[Clave]}</c>).</summary>
    public LocalizationService Loc => _loc;
    private readonly EditSessionService _editSession;
    private bool _isLoading;
    private static readonly PokemonSlotKey TrainerKey = PokemonSlotKey.ForTrainer();

    private string _name       = string.Empty;
    private int    _tid;
    private int    _sid;
    private string _playTime   = string.Empty;
    private string _gender     = string.Empty;
    private string _gameName   = string.Empty;
    private string _language   = string.Empty;
    private int    _money;
    private int    _coins;
    private int    _generation;
    private int    _badgeCount;
    private bool   _isVisible;
    private bool   _hasMoney;
    private bool   _hasCoins;
    private bool   _hasLanguage;
    private int    _battlePoints;
    private bool   _hasBattlePoints;
    private int    _maxMoney = -1;
    private int    _maxCoins = -1;

    /// <summary>Los juegos reales limitan Battle Points a 9999 — PKHeX.Core no expone
    /// ningún "MaxBP" (confirmado por reflection, no existe esa propiedad en ningún save
    /// de las generaciones que sí tienen BP), así que este límite queda como regla fija del
    /// juego, mismo criterio que ya usan NatureDatabase/MegaStoneDatabase para datos de
    /// dominio chicos y estables que no vienen de ningún lado más.</summary>
    private const int MaxBattlePointsValue = 9999;

    // Códigos numéricos de idioma de juego de PKHeX con nombre (claves Trainer_Lang_<id>)
    private static readonly HashSet<int> KnownLanguageIds = new() { 1, 2, 3, 4, 5, 7, 8, 9, 10 };
    private int _languageId;

    public TrainerViewModel(LocalizationService loc, EditSessionService editSession)
    {
        _loc = loc;
        _editSession = editSession;
        AvailableGenders = [_loc["Trainer_Male"], _loc["Trainer_Female"]];
        MaxMoneyCommand        = new RelayCommand(() => { if (_maxMoney >= 0) Money = _maxMoney; });
        MaxCoinsCommand        = new RelayCommand(() => { if (_maxCoins >= 0) Coins = _maxCoins; });
        MaxBattlePointsCommand = new RelayCommand(() => BattlePoints = MaxBattlePointsValue);
    }

    public ICommand MaxMoneyCommand        { get; }
    public ICommand MaxCoinsCommand        { get; }
    public ICommand MaxBattlePointsCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Mismo patrón que PokemonEditorViewModel.Set&lt;T&gt;: captura el valor
    /// "tal cual venía" ANTES del chequeo de "no cambió" (para que un valor que ya coincide
    /// con el default no se pierda como prístino), y solo graba edición real fuera de una
    /// carga (_isLoading). Comparación siempre contra el último valor prístino, igual que
    /// en los Pokémon — tildar/destildar o volver al valor original no cuenta como cambio.</summary>
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (_isLoading && name != null)
            _editSession.CapturePristine(TrainerKey, name, value);

        if (Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);

        if (!_isLoading && name != null)
            _editSession.RecordEdit(TrainerKey, name, value);
    }

    public bool IsVisible  { get => _isVisible; set => Set(ref _isVisible, value); }
    public bool HasMoney    { get => _hasMoney;   set => Set(ref _hasMoney,   value); }
    public bool HasLanguage { get => _hasLanguage; set => Set(ref _hasLanguage, value); }
    public bool HasCoins   { get => _hasCoins;  set => Set(ref _hasCoins,  value); }
    public bool HasBattlePoints { get => _hasBattlePoints; set => Set(ref _hasBattlePoints, value); }

    // ── Editables ───────────────────────────────────────────────
    public string Name { get => _name; set => Set(ref _name, value); }

    public int TID { get => _tid; set => Set(ref _tid, value); }
    public string TIDDisplay
    {
        get => _tid.ToString("D5");
        set { if (int.TryParse(value, out var v)) TID = v; }
    }

    public int SID { get => _sid; set => Set(ref _sid, value); }
    public string SIDDisplay
    {
        get => _sid.ToString("D5");
        set { if (int.TryParse(value, out var v)) SID = v; }
    }

    /// <summary>Opciones para el ComboBox de género — siempre Macho/Hembra. En Gen 1 el
    /// juego no tiene selección de género de entrenador en la práctica (el sprite siempre
    /// es masculino), pero PKHeX igual expone el campo — se deja editable por simplicidad,
    /// igual que ya se mostraba (de solo lectura) antes en todas las generaciones.</summary>
    public List<string> AvailableGenders
    {
        get => _availableGenders;
        private set { _availableGenders = value; OnPropertyChanged(); }
    }
    private List<string> _availableGenders = [];

    /// <summary>¿Este texto es «Masculino» en CUALQUIER idioma? El género se guarda como el texto que mostraba
    /// el combo, y una edición hecha con la UI en un idioma debe seguir entendiéndose al cambiar de idioma.</summary>
    private bool IsMaleText(string? text)
        => !string.IsNullOrEmpty(text) && _loc.AllTranslations("Trainer_Male").Contains(text);

    /// <summary>Cambió el idioma de la UI: reconstruye las opciones del combo y traduce el género mostrado
    /// sin registrarlo como edición.</summary>
    public void OnLocaleChanged()
    {
        var wasLoading = _isLoading;
        var hadGender = !string.IsNullOrEmpty(_gender);
        var female = hadGender && !IsMaleText(_gender);
        _isLoading = true; // el ComboBox puede devolver SelectedItem = null al cambiar la lista: no es una edición
        try
        {
            AvailableGenders = [_loc["Trainer_Male"], _loc["Trainer_Female"]];
            if (hadGender) Gender = female ? _loc["Trainer_Female"] : _loc["Trainer_Male"];
            if (HasLanguage && KnownLanguageIds.Contains(_languageId)) Language = _loc["Trainer_Lang_" + _languageId];
        }
        finally { _isLoading = wasLoading; }
    }

    public string Gender
    {
        get => _gender;
        set
        {
            var changed = _gender != value;
            Set(ref _gender, value);
            if (changed) OnPropertyChanged(nameof(GenderIcon));
        }
    }

    /// <summary>Mismo símbolo ♂/♀ que ya se usa para el género de los Pokémon en el resto
    /// de la app (caja/equipo, panel de info) — sin color especial, es texto simple ahí
    /// también, así que se mantiene consistente.</summary>
    public string GenderIcon => IsMaleText(Gender) ? "♂" : "♀";

    /// <summary>Para elegir el sprite de entrenador/entrenadora en la tarjeta del modal de
    /// revisión — ver SpriteService.GetTrainerSprite.</summary>
    public bool IsFemale => !IsMaleText(Gender);

    public int Money
    {
        get => _money;
        set { Set(ref _money, value); OnPropertyChanged(nameof(MoneyDisplay)); }
    }
    public string MoneyDisplay
    {
        get => _money.ToString("N0", CultureInfo.InvariantCulture);
        set
        {
            // Tope real del save (PKHeX); sin save cargado no hay tope más allá del int.
            if (TryParseClamped(value, _maxMoney >= 0 ? _maxMoney : int.MaxValue, out var v)) Money = v;
            OnPropertyChanged(nameof(MoneyDisplay)); // reescribe el texto aunque el valor no cambie (ej. se pasó del tope)
        }
    }

    public int Coins
    {
        get => _coins;
        set { Set(ref _coins, value); OnPropertyChanged(nameof(CoinsDisplay)); }
    }
    public string CoinsDisplay
    {
        get => _coins.ToString("N0", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseClamped(value, _maxCoins >= 0 ? _maxCoins : int.MaxValue, out var v)) Coins = v;
            OnPropertyChanged(nameof(CoinsDisplay));
        }
    }

    public int BattlePoints
    {
        get => _battlePoints;
        set { Set(ref _battlePoints, value); OnPropertyChanged(nameof(BattlePointsDisplay)); }
    }
    public string BattlePointsDisplay
    {
        get => _battlePoints.ToString("N0", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseClamped(value, MaxBattlePointsValue, out var v)) BattlePoints = v;
            OnPropertyChanged(nameof(BattlePointsDisplay));
        }
    }

    /// <summary>Acepta "45000" y "45,000" (lo que se tipea o lo que queda tras reformatear con separador de miles).
    /// Descarta cualquier otro carácter y topa el resultado en <paramref name="max"/> (también si el número
    /// desborda un int). Devuelve false si no hay ningún dígito: el valor anterior se conserva.</summary>
    private static bool TryParseClamped(string? value, int max, out int result)
    {
        result = 0;
        var digits = new string((value ?? string.Empty).Where(c => c >= '0' && c <= '9').ToArray());
        if (digits.Length == 0) return false;
        result = digits.Length > 9 || long.Parse(digits, CultureInfo.InvariantCulture) > max
            ? max
            : int.Parse(digits, CultureInfo.InvariantCulture);
        return true;
    }

    // ── Solo lectura ────────────────────────────────────────────
    public string PlayTime  { get => _playTime;  set => Set(ref _playTime,  value); }
    public string GameName  { get => _gameName;  set => Set(ref _gameName,  value); }
    public string Language  { get => _language;  set => Set(ref _language,  value); }
    public int Generation  { get => _generation; set => Set(ref _generation, value); }
    public int BadgeCount  { get => _badgeCount; set => Set(ref _badgeCount, value); }

    public void LoadFrom(TrainerInfo info)
    {
        _isLoading = true;
        Name       = info.Name;
        TID        = info.TrainerId;
        SID        = info.SecretId;
        PlayTime   = info.PlayTimeFormatted;
        Gender     = info.Gender == TrainerGender.Male
            ? _loc["Trainer_Male"]
            : _loc["Trainer_Female"];
        GameName   = ExpandGameName(info.GameName);
        Generation = info.Generation;
        BadgeCount = info.BadgeCount;

        // Idioma — ocultar si no disponible (-1, 0)
        _languageId = info.Language;
        if (info.Language > 0 && KnownLanguageIds.Contains(info.Language))
        { Language = _loc["Trainer_Lang_" + info.Language]; HasLanguage = true; }
        else
        { Language = string.Empty; HasLanguage = false; }

        // Dinero — solo mostrar si el save lo tiene
        if (info.Money >= 0)
        {
            Money    = info.Money;
            HasMoney = true;
            _maxMoney = info.MaxMoney;
        }
        else
        {
            Money    = 0;
            HasMoney = false;
            _maxMoney = -1;
        }

        // Monedas de casino — solo mostrar si el save lo tiene (Gen 1-4; a partir de Gen 5
        // el Game Corner se sacó de las versiones occidentales y PKHeX.Core no expone un
        // contador — ver evaluación de atributos de entrenador por generación).
        if (info.Coins >= 0)
        {
            Coins    = info.Coins;
            HasCoins = true;
            _maxCoins = info.MaxCoins;
        }
        else
        {
            Coins    = 0;
            HasCoins = false;
            _maxCoins = -1;
        }

        // Battle Points — Emerald (Gen3), Gen4 (DP/Pt/HGSS), Gen6 (XY/ORAS). No existía
        // ningún soporte para esto todavía, se agrega de cero.
        if (info.BattlePoints >= 0)
        {
            BattlePoints    = info.BattlePoints;
            HasBattlePoints = true;
        }
        else
        {
            BattlePoints    = 0;
            HasBattlePoints = false;
        }

        IsVisible = true;
        _isLoading = false;
    }

    public void Clear()
    {
        Name = PlayTime = Gender = GameName = Language = string.Empty;
        TID = SID = Money = Coins = Generation = BadgeCount = BattlePoints = 0;
        HasMoney = HasCoins = HasLanguage = HasBattlePoints = IsVisible = false;
        _maxMoney = _maxCoins = -1;
    }

    /// <remarks>
    /// Antes <c>private</c> — se sube a <c>internal</c> (sin tocar el cuerpo del switch) para
    /// que <c>RecentSaveRowViewModel</c> (Pantalla 1 — Estado vacío) pueda reusar exactamente
    /// este mismo mapeo código→nombre en vez de duplicar la tabla. Sigue siendo el mismo
    /// método, mismo namespace/ensamblado — no cambia superficie pública de la app.
    /// </remarks>
    internal static string ExpandGameName(string name) => name switch
    {
        // Gen 1 — PKHeX GameVersion.ToString()
        "RD"  => "Red",
        "BU"  => "Blue",
        "GN"  => "Green",
        "YW"  => "Yellow",
        // Gen 2
        "GD"  => "Gold",
        "SI"  => "Silver",   // PKHeX uses SI=40, not SL (SL=50 is Scarlet)
        "C"   => "Crystal",
        // Gen 3
        "S"   => "Sapphire",  // S=1 in PKHeX
        "R"   => "Ruby",      // R=2 in PKHeX
        "E"   => "Emerald",
        "FR"  => "FireRed",
        "LG"  => "LeafGreen",
        "CXD" => "Colosseum/XD",
        // Gen 4
        "D"   => "Diamond",
        "P"   => "Pearl",
        "Pt"  => "Platinum",
        "HG"  => "HeartGold",
        "SS"  => "SoulSilver",
        // Gen 5
        "B"   => "Black",
        "W"   => "White",
        "B2"  => "Black 2",
        "W2"  => "White 2",
        // Gen 6
        "X"   => "X",
        "Y"   => "Y",
        "OR"  => "Omega Ruby",
        "AS"  => "Alpha Sapphire",
        // Gen 7
        "SN"  => "Sun",
        "MN"  => "Moon",
        "US"  => "Ultra Sun",
        "UM"  => "Ultra Moon",
        "GP"  => "Let's Go Pikachu",
        "GE"  => "Let's Go Eevee",
        // Gen 8
        "SW"  => "Sword",
        "SH"  => "Shield",
        "BD"  => "Brilliant Diamond",
        "SP"  => "Shining Pearl",
        "PLA" => "Legends: Arceus",
        // Gen 9 — PKHeX SL=50 .ToString()="SL", but conflicts with Silver "SI"
        "SL"  => "Scarlet",  // SL=50 (Scarlet) - Silver uses "SI"
        "VL"  => "Violet",
        "SV"  => "Scarlet/Violet",  // valor "agrupado" — ver sesión de diagnóstico Gen9
        "ZA"  => "Legends: Z-A",
        // Virtual Console (Gen 1/2 VC en 3DS)
        "RD2" => "Red (VC)",
        "BU2" => "Blue (VC)",
        "YW2" => "Yellow (VC)",
        "GD2" => "Gold (VC)",
        "SI2" => "Silver (VC)",  // Silver VC
        "C2"  => "Crystal (VC)",
        // Otros
        "GO"  => "Pokémon GO",
        "SWSH"=> "Sword/Shield",
        // Fallback: devolver el valor original si no se reconoce
        _     => name
    };
}
