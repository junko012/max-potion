using Exxeguttor.UI.Services;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Grupo de sets recomendados por (Source, Generation) para binding en el tab Sets.
/// Equivalente a LearnsetGroup para el tab Learnsets.
/// </summary>
public sealed class RecommendedSetGroup
{
    public string               Source     { get; init; } = string.Empty;
    public int                  Generation { get; init; }
    public List<RecommendedSet> Sets       { get; init; } = [];
    public int                  Count      => Sets.Count;

    /// <summary>Encabezado de sección: "Smogon  ·  Gen 9"</summary>
    public string Header => $"{Source}  ·  Gen {Generation}";

    public string SourceDisplay => Source switch
    {
        "Smogon"             => "Smogon",
        "Showdown"           => "Showdown",
        "Showdown Most Used" => "Usage",
        "Randbats"           => "Randbats",
        _                    => Source,
    };
}
