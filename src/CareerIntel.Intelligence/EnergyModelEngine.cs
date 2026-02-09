using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Assesses learning fatigue, burnout risk, and generates capacity-aware weekly plans.
/// </summary>
public sealed class EnergyModelEngine(ILogger<EnergyModelEngine> logger)
{
    private const int CriticalConsecutiveWeeks = 8;
    private const int HighConsecutiveWeeks = 6;
    private const int ModerateConsecutiveWeeks = 4;
    private const double CriticalLoadFactor = 0.8;
    private const double HighLoadFactor = 0.9;
    private const double ModerateLoadFactor = 0.7;
    private const double HighBurnoutCapacityMultiplier = 0.6;
    private const double CriticalBurnoutCapacityMultiplier = 0.4;
    private const double ApplicationTimePercent = 0.15;
    private const double RestTimePercent = 0.10;
    private const int DecliningWeeksThreshold = 3;

    public EnergyProfile AssessEnergy(
        LearningPlan plan,
        List<WeeklyLog> recentWeeks,
        double weeklyAvailableHours)
    {
        logger.LogInformation(
            "Assessing energy for plan with {SkillCount} skills, {WeekCount} recent weeks, {Available}h/week available",
            plan.Skills.Count, recentWeeks.Count, weeklyAvailableHours);

        int consecutiveWeeks = ComputeConsecutiveWeeksStudying(recentWeeks);
        double averageHours = recentWeeks.Count > 0
            ? recentWeeks.Average(w => w.HoursStudied)
            : 0.0;

        var burnoutRisk = ComputeBurnoutRisk(
            consecutiveWeeks, averageHours, weeklyAvailableHours, recentWeeks);

        string burnoutWarning = GenerateBurnoutWarning(burnoutRisk, consecutiveWeeks, averageHours);
        var recoveryRecommendations = GenerateRecoveryRecommendations(burnoutRisk, consecutiveWeeks, averageHours, weeklyAvailableHours);

        logger.LogInformation(
            "Energy assessment complete: {ConsecutiveWeeks} consecutive weeks, {AvgHours:F1}h/week avg, burnout risk={Risk}",
            consecutiveWeeks, averageHours, burnoutRisk);

        return new EnergyProfile
        {
            WeeklyAvailableHours = weeklyAvailableHours,
            ConsecutiveWeeksStudying = consecutiveWeeks,
            AverageHoursPerWeek = averageHours,
            RecentWeeks = recentWeeks,
            BurnoutRiskLevel = burnoutRisk,
            BurnoutWarning = burnoutWarning,
            RecoveryRecommendations = recoveryRecommendations
        };
    }

    public WeeklyCapacityPlan CreateWeeklyPlan(LearningPlan learningPlan, EnergyProfile energy)
    {
        logger.LogInformation(
            "Creating weekly capacity plan with burnout risk={Risk}, available={Hours}h/week",
            energy.BurnoutRiskLevel, energy.WeeklyAvailableHours);

        double effectiveHours = energy.BurnoutRiskLevel switch
        {
            BurnoutRisk.Critical => energy.WeeklyAvailableHours * CriticalBurnoutCapacityMultiplier,
            BurnoutRisk.High => energy.WeeklyAvailableHours * HighBurnoutCapacityMultiplier,
            _ => energy.WeeklyAvailableHours
        };

        // Reserve time for job applications and rest
        double applicationHours = effectiveHours * ApplicationTimePercent;
        double restHours = effectiveHours * RestTimePercent;
        double learningBudget = effectiveHours - applicationHours - restHours;

        // Sort skills by ROI descending, excluding stopped skills
        var activeSkills = learningPlan.Skills
            .Where(s => !s.ShouldStop && s.PersonalGapScore > 0)
            .OrderByDescending(s => s.LearningROI)
            .ToList();

        var allocations = AllocateSkillHours(activeSkills, learningBudget);

        double totalAllocated = allocations.Sum(a => a.HoursAllocated) + applicationHours + restHours;
        double utilizationPercent = energy.WeeklyAvailableHours > 0
            ? totalAllocated / energy.WeeklyAvailableHours * 100
            : 0;

        string summary = GeneratePlanSummary(energy, allocations, applicationHours, restHours, effectiveHours);

        logger.LogInformation(
            "Weekly plan created: {SkillCount} skills, {TotalHours:F1}h allocated, {Utilization:F0}% capacity",
            allocations.Count, totalAllocated, utilizationPercent);

        return new WeeklyCapacityPlan
        {
            WeekStart = GetNextWeekStart(),
            TotalHoursAllocated = totalAllocated,
            CapacityUtilizationPercent = utilizationPercent,
            SkillAllocations = allocations,
            ApplicationHours = applicationHours,
            RestHours = restHours,
            PlanSummary = summary
        };
    }

    public (bool FatigueDetected, string Reason, List<string> Actions) DetectLearningFatigue(
        List<WeeklyLog> recentWeeks)
    {
        logger.LogInformation("Running fatigue detection on {WeekCount} recent weeks", recentWeeks.Count);

        if (recentWeeks.Count < 2)
            return (false, string.Empty, []);

        var ordered = recentWeeks.OrderBy(w => w.WeekStart).ToList();

        // Check 1: Declining completion rate over 3+ weeks
        if (ordered.Count >= DecliningWeeksThreshold)
        {
            var recentSlice = ordered.TakeLast(DecliningWeeksThreshold).ToList();
            bool completionDeclining = true;
            for (int i = 1; i < recentSlice.Count; i++)
            {
                if (recentSlice[i].CompletionRate >= recentSlice[i - 1].CompletionRate)
                {
                    completionDeclining = false;
                    break;
                }
            }

            if (completionDeclining)
            {
                logger.LogWarning("Fatigue detected: completion rate declining over {Weeks} consecutive weeks", DecliningWeeksThreshold);
                return (
                    true,
                    $"Your completion rate has been declining for {DecliningWeeksThreshold}+ consecutive weeks, dropping from {recentSlice[0].CompletionRate:F0}% to {recentSlice[^1].CompletionRate:F0}%.",
                    [
                        "Take a 1-week break from structured learning",
                        "Switch to project-based learning instead of courses",
                        "Reduce planned hours by 40% next week"
                    ]);
            }
        }

        // Check 2: Energy level "Low" or "Exhausted" for 2+ consecutive weeks
        int consecutiveLowEnergy = 0;
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].EnergyLevel is "Low" or "Exhausted")
                consecutiveLowEnergy++;
            else
                break;
        }

        if (consecutiveLowEnergy >= 2)
        {
            logger.LogWarning("Fatigue detected: {Weeks} consecutive weeks of low/exhausted energy", consecutiveLowEnergy);
            return (
                true,
                $"You've reported low or exhausted energy for {consecutiveLowEnergy} consecutive weeks.",
                [
                    "Take a complete week off from studying",
                    "Focus on sleep, exercise, and recovery",
                    "When you resume, start at 50% of your previous capacity"
                ]);
        }

        // Check 3: Hours studied declining while hours planned stays same
        if (ordered.Count >= DecliningWeeksThreshold)
        {
            var recentSlice = ordered.TakeLast(DecliningWeeksThreshold).ToList();
            bool hoursStudiedDeclining = true;
            bool hoursPlannedStable = true;

            for (int i = 1; i < recentSlice.Count; i++)
            {
                if (recentSlice[i].HoursStudied >= recentSlice[i - 1].HoursStudied)
                    hoursStudiedDeclining = false;

                double plannedDelta = Math.Abs(recentSlice[i].HoursPlanned - recentSlice[i - 1].HoursPlanned);
                if (plannedDelta > recentSlice[i - 1].HoursPlanned * 0.2)
                    hoursPlannedStable = false;
            }

            if (hoursStudiedDeclining && hoursPlannedStable)
            {
                logger.LogWarning("Fatigue detected: hours studied declining while planned hours remain stable");
                return (
                    true,
                    "Your actual study hours are declining while your planned hours remain the same. This is a classic fatigue pattern.",
                    [
                        "Reduce planned hours to match your actual capacity",
                        "Schedule shorter, more focused study sessions",
                        "Add variety: mix courses, projects, and interview prep"
                    ]);
            }
        }

        logger.LogDebug("No learning fatigue detected");
        return (false, string.Empty, []);
    }

    private static int ComputeConsecutiveWeeksStudying(List<WeeklyLog> recentWeeks)
    {
        if (recentWeeks.Count == 0)
            return 0;

        var ordered = recentWeeks.OrderByDescending(w => w.WeekStart).ToList();
        int consecutive = 0;

        foreach (var week in ordered)
        {
            if (week.HoursStudied > 0)
                consecutive++;
            else
                break;
        }

        return consecutive;
    }

    private BurnoutRisk ComputeBurnoutRisk(
        int consecutiveWeeks,
        double averageHours,
        double availableHours,
        List<WeeklyLog> recentWeeks)
    {
        // Critical: consecutive weeks >= 8 AND average hours > 0.8 * available hours
        if (consecutiveWeeks >= CriticalConsecutiveWeeks && averageHours > CriticalLoadFactor * availableHours)
        {
            logger.LogWarning("Burnout risk: CRITICAL ({Weeks} consecutive weeks, {AvgHours:F1}h avg vs {Available:F1}h available)",
                consecutiveWeeks, averageHours, availableHours);
            return BurnoutRisk.Critical;
        }

        // High: consecutive weeks >= 6 OR (average hours > 0.9 * available AND completion rate declining over 3+ weeks)
        bool completionDeclining = IsCompletionRateDeclining(recentWeeks);
        if (consecutiveWeeks >= HighConsecutiveWeeks
            || (averageHours > HighLoadFactor * availableHours && completionDeclining))
        {
            logger.LogWarning("Burnout risk: HIGH ({Weeks} consecutive weeks, declining={Declining})",
                consecutiveWeeks, completionDeclining);
            return BurnoutRisk.High;
        }

        // Moderate: consecutive weeks >= 4 OR average hours > 0.7 * available
        if (consecutiveWeeks >= ModerateConsecutiveWeeks || averageHours > ModerateLoadFactor * availableHours)
        {
            logger.LogInformation("Burnout risk: MODERATE ({Weeks} consecutive weeks, {AvgHours:F1}h avg)",
                consecutiveWeeks, averageHours);
            return BurnoutRisk.Moderate;
        }

        return BurnoutRisk.Low;
    }

    private static bool IsCompletionRateDeclining(List<WeeklyLog> recentWeeks)
    {
        if (recentWeeks.Count < DecliningWeeksThreshold)
            return false;

        var ordered = recentWeeks.OrderBy(w => w.WeekStart).ToList();
        var lastThree = ordered.TakeLast(DecliningWeeksThreshold).ToList();

        for (int i = 1; i < lastThree.Count; i++)
        {
            if (lastThree[i].CompletionRate >= lastThree[i - 1].CompletionRate)
                return false;
        }

        return true;
    }

    private static string GenerateBurnoutWarning(BurnoutRisk risk, int consecutiveWeeks, double averageHours) =>
        risk switch
        {
            BurnoutRisk.Critical =>
                $"CRITICAL: You've been studying for {consecutiveWeeks} consecutive weeks at {averageHours:F1}h/week. "
                + "You are at serious risk of burnout. Take an immediate break and reduce your study load by at least 60%.",
            BurnoutRisk.High =>
                $"HIGH RISK: {consecutiveWeeks} consecutive weeks of study with {averageHours:F1}h/week average. "
                + "Reduce your workload soon or take a break to avoid burnout.",
            BurnoutRisk.Moderate =>
                $"MODERATE RISK: You've been studying for {consecutiveWeeks} weeks. "
                + "Consider scheduling a lighter week or taking a short break to maintain long-term sustainability.",
            _ => string.Empty
        };

    private static List<string> GenerateRecoveryRecommendations(
        BurnoutRisk risk,
        int consecutiveWeeks,
        double averageHours,
        double availableHours) =>
        risk switch
        {
            BurnoutRisk.Critical =>
            [
                "Take a 1-week complete break from all structured learning",
                $"When resuming, reduce to {availableHours * CriticalBurnoutCapacityMultiplier:F0} hours/week (40% capacity)",
                "Switch to project-based learning instead of courses",
                "Focus on applying to jobs rather than acquiring new skills",
                "Prioritize sleep, exercise, and social activities"
            ],
            BurnoutRisk.High =>
            [
                "Take 2-3 days off from studying this week",
                $"Reduce to {availableHours * HighBurnoutCapacityMultiplier:F0} hours/week (60% capacity) next week",
                "Mix learning formats: alternate between courses, projects, and interview prep",
                "Start allocating more time to job applications"
            ],
            BurnoutRisk.Moderate =>
            [
                "Schedule a lighter week with 70% of your usual hours",
                "Add one full rest day per week with no study",
                "Switch to project-based learning instead of courses for variety"
            ],
            _ => []
        };

    private static List<SkillAllocation> AllocateSkillHours(
        List<LearningStatus> activeSkills,
        double learningBudget)
    {
        if (activeSkills.Count == 0)
            return [];

        // Weight each skill by its ROI for proportional allocation
        double totalRoi = activeSkills.Sum(s => s.LearningROI);
        if (totalRoi <= 0)
            totalRoi = activeSkills.Count; // fallback: equal distribution

        var allocations = new List<SkillAllocation>();
        int priority = 1;

        foreach (var skill in activeSkills)
        {
            double weight = totalRoi > 0 ? skill.LearningROI / totalRoi : 1.0 / activeSkills.Count;
            double hours = learningBudget * weight;

            // Minimum 0.5h allocation to be meaningful, skip otherwise
            if (hours < 0.5)
                continue;

            string activityType = DetermineActivityType(skill);

            allocations.Add(new SkillAllocation
            {
                SkillName = skill.SkillName,
                HoursAllocated = Math.Round(hours, 1),
                ActivityType = activityType,
                Priority = priority++
            });
        }

        return allocations;
    }

    private static string DetermineActivityType(LearningStatus skill)
    {
        if (skill.PersonalGapScore >= 60)
            return "Course";
        if (skill.PersonalGapScore >= 30)
            return "Project";
        if (skill.PersonalGapScore > 0)
            return "Practice";
        return "Interview Prep";
    }

    private static string GeneratePlanSummary(
        EnergyProfile energy,
        List<SkillAllocation> allocations,
        double applicationHours,
        double restHours,
        double effectiveHours)
    {
        var parts = new List<string>();

        if (energy.BurnoutRiskLevel >= BurnoutRisk.High)
        {
            parts.Add($"REDUCED CAPACITY: Operating at {effectiveHours:F0}h/week due to {energy.BurnoutRiskLevel} burnout risk.");
        }

        if (allocations.Count > 0)
        {
            var topSkills = allocations.Take(3).Select(a => $"{a.SkillName} ({a.HoursAllocated:F1}h)");
            parts.Add($"Focus areas: {string.Join(", ", topSkills)}.");
        }

        parts.Add($"Job applications: {applicationHours:F1}h. Rest: {restHours:F1}h.");

        if (energy.BurnoutRiskLevel == BurnoutRisk.Critical)
        {
            parts.Add("Consider pausing all learning and focusing entirely on applications and recovery.");
        }

        return string.Join(" ", parts);
    }

    private static DateTimeOffset GetNextWeekStart()
    {
        var now = DateTimeOffset.UtcNow;
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0)
            daysUntilMonday = 7;
        return now.Date.AddDays(daysUntilMonday);
    }
}
