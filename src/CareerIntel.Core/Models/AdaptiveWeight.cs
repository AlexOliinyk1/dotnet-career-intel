namespace CareerIntel.Core.Models;

/// <summary>
/// Adaptive scoring weight derived from interview outcome data.
/// Skills that cause rejections get boosted priority; overhyped skills get dampened.
/// </summary>
public sealed class AdaptiveWeight
{
    /// <summary>Skill name this weight applies to.</summary>
    public string SkillName { get; set; } = string.Empty;

    /// <summary>Number of times this skill appeared in rejection weak areas.</summary>
    public int RejectionCount { get; set; }

    /// <summary>Number of times this skill appeared in pass strong areas.</summary>
    public int PassCount { get; set; }

    /// <summary>Interview stages where this skill caused failures.</summary>
    public List<string> FailureStages { get; set; } = [];

    /// <summary>
    /// Priority multiplier: >1 means boost (caused rejections), less than 1 means dampen (overhyped).
    /// Range: 0.5 to 2.0
    /// </summary>
    public double PriorityMultiplier { get; set; } = 1.0;

    /// <summary>Confidence in this weight based on sample size.</summary>
    public double Confidence { get; set; }
}
