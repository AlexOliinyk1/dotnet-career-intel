namespace CareerIntel.Core.Models;

public sealed class OfferReadiness
{
    public double ReadinessPercent { get; set; } // 0-100
    public int EstimatedWeeksToReady { get; set; }
    public RecommendedTiming Timing { get; set; }
    public List<SkillGap> CriticalGaps { get; set; } = [];
    public List<string> Strengths { get; set; } = [];
    public List<PrepAction> PrepActions { get; set; } = [];
    public double OfferProbability { get; set; } // 0-1.0
}

public enum RecommendedTiming
{
    ApplyNow,
    ApplyIn1To2Weeks,
    ApplyIn3To4Weeks,
    SkillUpFirst,
    Skip
}

public sealed class SkillGap
{
    public string SkillName { get; set; } = string.Empty;
    public int CurrentLevel { get; set; } // 0-5
    public int RequiredLevel { get; set; } // 1-5
    public double ImpactWeight { get; set; } // how much this gap hurts offer probability
    public string RecommendedAction { get; set; } = string.Empty;
    public bool IsCritical { get; set; } // Is this a required skill vs. nice-to-have?
    public int HoursToLearn { get; set; } // Estimated hours to bridge gap

    // Alias for backwards compatibility
    public int TargetLevel
    {
        get => RequiredLevel;
        set => RequiredLevel = value;
    }
}

public sealed class PrepAction
{
    public string Action { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // "Technical", "SystemDesign", "Behavioral", "Portfolio"
    public int Priority { get; set; } // 1-5
    public int EstimatedHours { get; set; }
}
