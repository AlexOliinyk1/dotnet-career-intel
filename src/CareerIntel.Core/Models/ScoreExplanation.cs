namespace CareerIntel.Core.Models;

/// <summary>
/// Human-readable explanations for each scoring dimension.
/// </summary>
public sealed class ScoreExplanation
{
    public string SkillMatch { get; set; } = string.Empty;
    public string SeniorityFit { get; set; } = string.Empty;
    public string SalaryAlignment { get; set; } = string.Empty;
    public string RemoteFit { get; set; } = string.Empty;
    public string GrowthOpportunity { get; set; } = string.Empty;
    public string OverallVerdict { get; set; } = string.Empty;
    public List<string> Strengths { get; set; } = [];
    public List<string> Risks { get; set; } = [];
}
