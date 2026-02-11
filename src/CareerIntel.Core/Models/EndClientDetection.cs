namespace CareerIntel.Core.Models;

public sealed class EndClientDetection
{
    public JobVacancy OriginalVacancy { get; set; } = null!;
    public IntermediaryCompany Intermediary { get; set; } = null!;
    public string DetectedClientName { get; set; } = string.Empty;
    public DetectionMethod Method { get; set; }
    public double Confidence { get; set; }
    public string MatchedSnippet { get; set; } = string.Empty;
    public double EstimatedUpliftPercent { get; set; }
}

public enum DetectionMethod
{
    ExplicitMention,
    TitleBrackets,
    TitleParentheses,
    ContextualHint,
    UserProvided
}

public sealed class PositionMatch
{
    public JobVacancy IntermediaryPosting { get; set; } = null!;
    public JobVacancy DirectPosting { get; set; } = null!;
    public double TitleSimilarity { get; set; }
    public double SkillOverlap { get; set; }
    public bool LocationMatch { get; set; }
    public double OverallConfidence { get; set; }
}

public sealed class DirectCheckResult
{
    public string ClientName { get; set; } = string.Empty;
    public string? CareersUrl { get; set; }
    public string? ATSType { get; set; }
    public List<PositionMatch> Matches { get; set; } = [];
    public List<JobVacancy> AllDirectPostings { get; set; } = [];
    public bool DirectPostingFound => Matches.Count > 0;
    public string? Error { get; set; }
}
