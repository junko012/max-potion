namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Grupo de learnset por método de aprendizaje para binding en la vista.
/// </summary>
public sealed class LearnsetGroup
{
    public string              Method  { get; init; } = string.Empty;
    public List<LearnsetEntry> Moves   { get; init; } = new();
    public int                 Count   => Moves.Count;

    public string MethodDisplay => Method switch
    {
        "levelup"   => "📈 Level Up",
        "egg"       => "🥚 Egg Move",
        "tm"        => "💿 TM",
        "tr"        => "💿 TR",
        "tutor"     => "🧑 Tutor",
        "reminder"  => "💭 Move Reminder",
        "evolution" => "🔄 Evolution",
        "special"   => "⭐ Special",
        "oyabun"    => "👑 Alpha",
        "zmove"     => "⚡ Z-Move",
        _           => Method,
    };
}
