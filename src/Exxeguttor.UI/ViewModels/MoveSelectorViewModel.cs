using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// ViewModel para el panel de selección de movimiento (slot 1-4).
/// Se construye desde la lista de LearnsetEntry ya cargada en memoria —
/// sin nueva query a la DB.
/// </summary>
public sealed class MoveSelectorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    // ── Slot que se está editando (1-4) ──────────────────────────────────
    public int    SlotIndex   { get; init; }
    public string SlotLabel   => $"Select move — Slot {SlotIndex}";
    public string CurrentMove { get; init; } = string.Empty;

    // ── Lista fuente (del learnset cargado) ──────────────────────────────
    private readonly List<MoveOption> _allMoves;

    // ── Filtros ──────────────────────────────────────────────────────────
    private string _search = string.Empty;
    public string Search
    {
        get => _search;
        set { _search = value; OnPropertyChanged(); RefreshFiltered(); }
    }

    private string _methodFilter = "All";
    public string MethodFilter
    {
        get => _methodFilter;
        set
        {
            _methodFilter = value; OnPropertyChanged();
            foreach (var chip in AvailableMethods) chip.IsActive = chip.Label == value;
            RefreshFiltered();
        }
    }

    // ── Resultado filtrado ────────────────────────────────────────────────
    public ObservableCollection<MoveOption> Filtered { get; } = [];

    // ── Métodos disponibles (para chips de filtro) ────────────────────────
    public List<MethodChip> AvailableMethods { get; }

    // ── Movimiento seleccionado ───────────────────────────────────────────
    private MoveOption? _selected;
    public MoveOption? Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); }
    }

    public bool HasSelection => Selected != null;

    // ── Constructor ───────────────────────────────────────────────────────
    public MoveSelectorViewModel(int slotIndex, string currentMove, int currentMoveId,
                                  IEnumerable<LearnsetEntry> learnset)
    {
        SlotIndex   = slotIndex;
        CurrentMove = currentMove;

        // Deduplicar por nombre de movimiento (puede aparecer en varios métodos)
        // Tomar la entrada de menor "prioridad" de método: levelup > evolution > tm > tutor > egg
        var methodPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["levelup"]   = 0, ["evolution"] = 1, ["tm"] = 2,
            ["tr"]        = 2, ["tutor"]     = 3, ["egg"] = 4,
            ["reminder"]  = 3, ["special"]   = 5,
        };

        _allMoves = learnset
            // Por id (no por nombre): el nombre depende del idioma de los datos.
            .GroupBy(e => e.MoveId)
            .Select(g =>
            {
                // Tomar la entrada de método de mayor prioridad
                var best = g.OrderBy(e => methodPriority.GetValueOrDefault(e.LearnMethod, 99)).First();
                return new MoveOption
                {
                    MoveId      = best.MoveId,
                    MoveName    = best.MoveName,
                    TypeName    = best.TypeName,
                    DamageClass = best.DamageClass,
                    Power       = best.Power,
                    LearnMethod = best.LearnMethod,
                    LearnLevel  = best.LearnLevel,
                    // TODOS los métodos con que lo aprende (nivel, MT, tutor, crianza...), no solo el de mayor
                    // prioridad: se muestran juntos y el filtro por método los considera a todos.
                    Methods     = g.Select(e => (Method: e.LearnMethod ?? string.Empty, Level: (int?)e.LearnLevel))
                                   .Distinct()
                                   .OrderBy(x => methodPriority.GetValueOrDefault(x.Method, 99))
                                   .ThenBy(x => x.Level ?? int.MaxValue)
                                   .ToList(),
                    IsCurrent   = currentMoveId > 0
                        ? best.MoveId == currentMoveId
                        : string.Equals(best.MoveName, currentMove, StringComparison.OrdinalIgnoreCase),
                };
            })
            // Movimiento actual primero, luego alphabético
            .OrderByDescending(m => m.IsCurrent)
            .ThenBy(m => m.MoveName)
            .ToList();

        // Métodos únicos disponibles
        var methods = _allMoves
            .SelectMany(m => m.Methods.Select(x => MethodChipLabel(x.Method)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        AvailableMethods = new[] { "All" }.Concat(methods).Select(l => new MethodChip(l) { IsActive = l == _methodFilter }).ToList();

        RefreshFiltered();
    }

    public void SelectMethod(string method)
    {
        MethodFilter = method;
    }

    private void RefreshFiltered()
    {
        Filtered.Clear();
        var q = _allMoves.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_search))
            q = q.Where(m => m.MoveName.Contains(_search, StringComparison.OrdinalIgnoreCase));

        if (_methodFilter != "All")
            q = q.Where(m => m.Methods.Any(x => MethodChipLabel(x.Method) == _methodFilter));

        foreach (var m in q)
            Filtered.Add(m);
    }

    private static string MethodChipLabel(string method) => method switch
    {
        "levelup"   => "Level up",
        "evolution" => "Evolution",
        "tm" or "tr"=> "TM",
        "tutor"     => "Tutor",
        "egg"       => "Egg",
        "reminder"  => "Reminder",
        _           => method,
    };
}

/// <summary>Chip de filtro por método; <see cref="IsActive"/> lo pinta de verde en el panel.</summary>
public sealed class MethodChip : INotifyPropertyChanged
{
    public MethodChip(string label) { Label = label; }
    public string Label { get; }
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Un movimiento disponible para seleccionar en el panel.</summary>
public sealed class MoveOption
{
    /// <summary>Id del movimiento (PKHeX/pokemon.db). 0 si el origen no lo trae.</summary>
    public int    MoveId      { get; set; }
    public string MoveName    { get; set; } = string.Empty;
    public string TypeName    { get; set; } = string.Empty;
    public string DamageClass { get; set; } = string.Empty;
    public int?   Power       { get; set; }
    public string LearnMethod { get; set; } = string.Empty;
    public int?   LearnLevel  { get; set; }
    public bool   IsCurrent   { get; set; }

    /// <summary>Todas las formas en que el Pokémon aprende este movimiento (método, nivel).</summary>
    public List<(string Method, int? Level)> Methods { get; set; } = [];

    // ── Propiedades calculadas para binding ───────────────────────────────

    public string PowerDisplay  => Power.HasValue ? Power.Value.ToString() : "—";

    /// <summary>Resumen corto de todos los métodos: "Lv 20 · TM · Egg".</summary>
    public string MethodDisplay => Methods.Count == 0 ? SingleLabel(LearnMethod, LearnLevel)
        : string.Join(" · ", Methods.Select(x => x.Method).Distinct(StringComparer.OrdinalIgnoreCase).Select(Group));

    /// <summary>Detalle para el tooltip: un método por línea, con todos los niveles.</summary>
    public string MethodTooltip => Methods.Count == 0 ? SingleLabel(LearnMethod, LearnLevel)
        : string.Join("\n", Methods.Select(x => x.Method).Distinct(StringComparer.OrdinalIgnoreCase).Select(Group));

    private string Group(string method)
    {
        if (!method.Equals("levelup", StringComparison.OrdinalIgnoreCase))
            return SingleLabel(method, null);
        var levels = Methods.Where(x => x.Method.Equals("levelup", StringComparison.OrdinalIgnoreCase) && x.Level.HasValue)
                            .Select(x => x.Level!.Value).Distinct().OrderBy(l => l).ToList();
        return levels.Count == 0 ? "Lv —" : "Lv " + string.Join("/", levels);
    }

    private static string SingleLabel(string method, int? level) => method switch
    {
        "levelup"   => level.HasValue ? $"Lv {level}" : "Lv —",
        "evolution" => "Evolution",
        "tm" or "tr"=> "TM",
        "tutor"     => "Tutor",
        "egg"       => "Egg",
        "reminder"  => "Reminder",
        _           => method,
    };

    public string TypeDisplay   => string.IsNullOrEmpty(TypeName) ? string.Empty
        : char.ToUpper(TypeName[0]) + TypeName[1..];

    /// <summary>Descripción del movimiento para el tooltip ⓘ (leída lazy desde la DB).</summary>
    private string? _tooltip;
    public string Tooltip => _tooltip ??= LoadTooltip();

    private string LoadTooltip()
    {
        var entry = MoveId > 0 ? MoveDatabase.Instance.Get(MoveId) : MoveDatabase.Instance.GetByName(MoveName);
        if (entry == null) return MoveName;
        var parts = new List<string>();
        if (entry.Accuracy.HasValue) parts.Add($"Acc {entry.Accuracy}");
        if (entry.Pp.HasValue)       parts.Add($"PP {entry.Pp}");
        var header = string.Join(" · ", parts);
        return string.IsNullOrEmpty(entry.EffectEntries) ? header
            : string.IsNullOrEmpty(header) ? entry.EffectEntries
            : $"{header}\n{entry.EffectEntries}";
    }
}
