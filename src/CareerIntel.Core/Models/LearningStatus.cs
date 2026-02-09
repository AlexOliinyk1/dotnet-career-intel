namespace CareerIntel.Core.Models;

public sealed class LearningStatus
{
    public string SkillName { get; set; } = string.Empty;
    public double MarketDemandScore { get; set; }
    public double PersonalGapScore { get; set; } // how much this gap hurts you
    public double LearningROI { get; set; } // computed: demand * gap * salary_impact
    public int EstimatedHoursToClose { get; set; }
    public string CurrentAction { get; set; } = string.Empty; // "Learning", "Practicing", "Done", "Deprioritized"
    public bool ShouldStop { get; set; } // fatigue/overlearning signal
    public string StopReason { get; set; } = string.Empty;
}

public sealed class LearningPlan
{
    public List<LearningStatus> Skills { get; set; } = [];
    public bool OverlearningDetected { get; set; }
    public string GlobalRecommendation { get; set; } = string.Empty; // "Keep learning", "Start applying NOW", "You're over-prepared"
    public int TotalEstimatedHours { get; set; }
    public DateTimeOffset GeneratedDate { get; set; } = DateTimeOffset.UtcNow;
}
