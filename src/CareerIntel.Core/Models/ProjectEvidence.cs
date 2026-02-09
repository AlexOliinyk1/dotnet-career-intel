namespace CareerIntel.Core.Models;

/// <summary>
/// Market-shaped project evidence that ties portfolio projects to specific
/// market signals and skill gaps, with interview-ready narratives.
/// </summary>
public sealed class ProjectEvidence
{
    public string ProjectTitle { get; set; } = string.Empty;

    /// <summary>Market signals that shaped this project choice.</summary>
    public List<MarketSignal> DrivingSignals { get; set; } = [];

    /// <summary>Specific skill gaps this project addresses.</summary>
    public List<string> AddressedGaps { get; set; } = [];

    /// <summary>Skill combinations from the market that this project demonstrates.</summary>
    public List<string> DemonstratedCombinations { get; set; } = [];

    /// <summary>Estimated salary impact of having this project on resume.</summary>
    public decimal EstimatedSalaryImpact { get; set; }

    /// <summary>Complexity tier based on market expectations.</summary>
    public string MarketAlignedComplexity { get; set; } = "Medium";

    /// <summary>Interview talking points for each skill demonstrated.</summary>
    public List<TalkingPoint> TalkingPoints { get; set; } = [];

    /// <summary>STAR-format stories ready for behavioral interviews.</summary>
    public List<StarStory> StarStories { get; set; } = [];
}

public sealed class MarketSignal
{
    public string SignalType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Strength { get; set; }
}

public sealed class TalkingPoint
{
    public string Skill { get; set; } = string.Empty;
    public string Point { get; set; } = string.Empty;
    public string ProofStatement { get; set; } = string.Empty;
}

public sealed class StarStory
{
    public string Theme { get; set; } = string.Empty;
    public string Situation { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
}
