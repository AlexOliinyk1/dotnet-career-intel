using System.Text.Json.Serialization;

namespace CareerIntel.Core.Models;

/// <summary>
/// Recommended action based on match analysis.
/// </summary>
public enum RecommendedAction
{
    Skip = 0,
    SkillUpFirst = 1,
    PrepareAndApply = 2,
    Apply = 3
}

/// <summary>
/// Detailed match score between a user profile and a job vacancy.
/// </summary>
public sealed class MatchScore
{
    /// <summary>
    /// Overall weighted score from 0 to 100.
    /// </summary>
    public double OverallScore { get; set; }

    /// <summary>
    /// Score for skill overlap (0-100).
    /// </summary>
    public double SkillMatchScore { get; set; }

    /// <summary>
    /// Score for seniority alignment (0-100).
    /// </summary>
    public double SeniorityMatchScore { get; set; }

    /// <summary>
    /// Score for salary range alignment (0-100).
    /// </summary>
    public double SalaryMatchScore { get; set; }

    /// <summary>
    /// Score for remote policy alignment (0-100).
    /// </summary>
    public double RemoteMatchScore { get; set; }

    /// <summary>
    /// Score for growth/upskilling opportunity (0-100).
    /// </summary>
    public double GrowthScore { get; set; }

    /// <summary>
    /// Required skills the candidate is missing.
    /// </summary>
    public List<string> MissingSkills { get; set; } = [];

    /// <summary>
    /// Skills that match between the vacancy and profile.
    /// </summary>
    public List<string> MatchingSkills { get; set; } = [];

    /// <summary>
    /// Extra skills the candidate has that are listed as preferred.
    /// </summary>
    public List<string> BonusSkills { get; set; } = [];

    /// <summary>
    /// Recommended action based on the match analysis.
    /// </summary>
    public RecommendedAction RecommendedAction { get; set; }

    /// <summary>
    /// Confidence score (0-1) indicating how reliable this match assessment is.
    /// Based on data completeness of both vacancy and profile.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Human-readable explanations for each scoring dimension.
    /// </summary>
    public ScoreExplanation Explanation { get; set; } = new();

    /// <summary>
    /// Estimated weeks to close gaps and reach Apply readiness.
    /// </summary>
    public int EstimatedWeeksToReady { get; set; }

    [JsonIgnore]
    public string ActionLabel => RecommendedAction switch
    {
        RecommendedAction.Apply => "Apply Now",
        RecommendedAction.PrepareAndApply => "Prepare & Apply",
        RecommendedAction.SkillUpFirst => "Skill Up First",
        RecommendedAction.Skip => "Skip",
        _ => "Unknown"
    };

    public override string ToString() =>
        $"Score: {OverallScore:F0}/100 | {ActionLabel} | " +
        $"Match: {MatchingSkills.Count}, Missing: {MissingSkills.Count}, Bonus: {BonusSkills.Count}";
}
