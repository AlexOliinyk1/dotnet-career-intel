using CareerIntel.Core.Enums;

namespace CareerIntel.Core.Models;

/// <summary>
/// Represents a single skill in the user's profile or in market analysis.
/// </summary>
public sealed class SkillProfile
{
    /// <summary>
    /// Canonical skill name (normalized).
    /// </summary>
    public string SkillName { get; set; } = string.Empty;

    /// <summary>
    /// Category the skill belongs to.
    /// </summary>
    public SkillCategory Category { get; set; } = SkillCategory.Unknown;

    /// <summary>
    /// Self-assessed proficiency level from 1 (beginner) to 5 (expert).
    /// </summary>
    public int ProficiencyLevel { get; set; }

    /// <summary>
    /// Total years of experience with this skill.
    /// </summary>
    public double YearsOfExperience { get; set; }

    /// <summary>
    /// When the skill was last actively used.
    /// </summary>
    public DateTimeOffset? LastUsedDate { get; set; }

    /// <summary>
    /// Computed market demand score (0-100). Set by the analysis engine.
    /// </summary>
    public double MarketDemandScore { get; set; }

    public override string ToString() =>
        $"{SkillName} (L{ProficiencyLevel}, {YearsOfExperience}y, demand: {MarketDemandScore:F0})";
}
