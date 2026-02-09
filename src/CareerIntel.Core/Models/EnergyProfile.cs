namespace CareerIntel.Core.Models;

/// <summary>
/// Tracks user's weekly learning capacity, energy levels, and burnout risk.
/// </summary>
public sealed class EnergyProfile
{
    public double WeeklyAvailableHours { get; set; } = 10.0;
    public int ConsecutiveWeeksStudying { get; set; }
    public double AverageHoursPerWeek { get; set; }
    public List<WeeklyLog> RecentWeeks { get; set; } = [];
    public BurnoutRisk BurnoutRiskLevel { get; set; } = BurnoutRisk.Low;
    public string BurnoutWarning { get; set; } = string.Empty;
    public List<string> RecoveryRecommendations { get; set; } = [];
}

public sealed class WeeklyLog
{
    public DateTimeOffset WeekStart { get; set; }
    public double HoursStudied { get; set; }
    public double HoursPlanned { get; set; }
    public double CompletionRate => HoursPlanned > 0 ? HoursStudied / HoursPlanned * 100 : 0;
    public string EnergyLevel { get; set; } = "Normal"; // "High", "Normal", "Low", "Exhausted"
}

public enum BurnoutRisk
{
    Low = 0,
    Moderate = 1,
    High = 2,
    Critical = 3
}

public sealed class WeeklyCapacityPlan
{
    public DateTimeOffset WeekStart { get; set; }
    public double TotalHoursAllocated { get; set; }
    public double CapacityUtilizationPercent { get; set; }
    public List<SkillAllocation> SkillAllocations { get; set; } = [];
    public double ApplicationHours { get; set; } // time reserved for job applications
    public double RestHours { get; set; } // mandatory rest
    public string PlanSummary { get; set; } = string.Empty;
}

public sealed class SkillAllocation
{
    public string SkillName { get; set; } = string.Empty;
    public double HoursAllocated { get; set; }
    public string ActivityType { get; set; } = string.Empty; // "Course", "Project", "Practice", "Interview Prep"
    public int Priority { get; set; }
}
