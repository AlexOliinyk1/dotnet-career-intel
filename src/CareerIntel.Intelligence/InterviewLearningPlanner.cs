using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Generates interview-focused learning plans that maximize interview pass probability.
/// Orders learning by interview impact (not topic order), classifies gaps by effort,
/// and explicitly skips low-ROI mastery.
/// </summary>
public sealed class InterviewLearningPlanner(ILogger<InterviewLearningPlanner> logger)
{
    private const double LowROIThreshold = 15.0;
    private const double HighImpactThreshold = 60.0;
    private const int MaxPrepHoursRecommended = 60;

    /// <summary>
    /// Creates an interview-focused learning plan for a specific vacancy,
    /// using prep plan data, user profile, and historical interview performance.
    /// </summary>
    public InterviewLearningPlan CreatePlan(
        InterviewPrepPlan prepPlan,
        JobVacancy vacancy,
        UserProfile profile,
        IReadOnlyList<InterviewFeedback>? interviewHistory = null)
    {
        logger.LogInformation("Creating interview learning plan for '{Title}' at {Company}",
            vacancy.Title, vacancy.Company);

        var items = new List<InterviewLearningItem>();
        var historyWeakAreas = BuildWeakAreaFrequency(interviewHistory);

        foreach (var skillSet in prepPlan.SkillSets)
        {
            var userSkill = profile.Skills
                .FirstOrDefault(s => s.SkillName.Equals(skillSet.SkillName, StringComparison.OrdinalIgnoreCase));

            int userLevel = userSkill?.ProficiencyLevel ?? 0;
            int gap = Math.Max(0, skillSet.ExpectedDepth - userLevel);

            // No gap = no learning needed
            if (gap == 0 && !HasHistoricalWeakness(skillSet.SkillName, historyWeakAreas))
            {
                // Already at or above expected depth and no interview failures on this skill
                continue;
            }

            var item = BuildLearningItem(
                skillSet, userLevel, gap, vacancy.SeniorityLevel,
                historyWeakAreas, prepPlan.SkillSets.Count);

            items.Add(item);
        }

        // Sort by interview impact (descending) — the key ordering principle
        items = items
            .OrderByDescending(i => i.InterviewImpactScore)
            .ThenBy(i => i.EstimatedHours)
            .ToList();

        // Mark low-ROI items for skipping
        int skippedCount = MarkLowROISkips(items);

        // Compute pass probability estimate
        double passProbability = EstimatePassProbability(items, profile, vacancy);

        // Total hours (excluding skipped)
        int totalHours = items.Where(i => !i.SkipRecommended).Sum(i => i.EstimatedHours);

        // Generate verdict
        string verdict = GenerateVerdict(items, totalHours, passProbability);

        int criticalCount = items.Count(i => i.InterviewImpactScore >= HighImpactThreshold && !i.SkipRecommended);

        logger.LogInformation(
            "Learning plan: {ItemCount} items, {Critical} critical, {Skipped} skipped, {Hours}h total, {PassProb:F0}% estimated pass",
            items.Count, criticalCount, skippedCount, totalHours, passProbability);

        return new InterviewLearningPlan
        {
            VacancyId = vacancy.Id,
            Items = items,
            TotalEstimatedHours = totalHours,
            CriticalGapCount = criticalCount,
            SkippedLowROICount = skippedCount,
            EstimatedPassProbability = passProbability,
            Verdict = verdict
        };
    }

    /// <summary>
    /// Creates a consolidated learning plan across multiple vacancies,
    /// prioritizing skills with the highest aggregate interview impact.
    /// </summary>
    public InterviewLearningPlan CreateConsolidatedPlan(
        IReadOnlyList<InterviewPrepPlan> prepPlans,
        IReadOnlyList<JobVacancy> vacancies,
        UserProfile profile,
        IReadOnlyList<InterviewFeedback>? interviewHistory = null)
    {
        logger.LogInformation("Creating consolidated learning plan across {Count} vacancies", vacancies.Count);

        var historyWeakAreas = BuildWeakAreaFrequency(interviewHistory);
        var skillImpactMap = new Dictionary<string, AggregatedSkillData>(StringComparer.OrdinalIgnoreCase);

        // Aggregate impact across all prep plans
        foreach (var plan in prepPlans)
        {
            foreach (var skillSet in plan.SkillSets)
            {
                if (!skillImpactMap.TryGetValue(skillSet.SkillName, out var data))
                {
                    data = new AggregatedSkillData { SkillName = skillSet.SkillName };
                    skillImpactMap[skillSet.SkillName] = data;
                }

                data.AppearanceCount++;
                data.MaxExpectedDepth = Math.Max(data.MaxExpectedDepth, skillSet.ExpectedDepth);
                data.IsRequiredAnywhere = data.IsRequiredAnywhere || skillSet.IsRequired;
            }
        }

        var items = new List<InterviewLearningItem>();

        foreach (var (skillName, aggregated) in skillImpactMap)
        {
            var userSkill = profile.Skills
                .FirstOrDefault(s => s.SkillName.Equals(skillName, StringComparison.OrdinalIgnoreCase));

            int userLevel = userSkill?.ProficiencyLevel ?? 0;
            int gap = Math.Max(0, aggregated.MaxExpectedDepth - userLevel);

            if (gap == 0 && !HasHistoricalWeakness(skillName, historyWeakAreas))
                continue;

            // Interview impact scales with how many vacancies need this skill
            double vacancyCoverage = (double)aggregated.AppearanceCount / prepPlans.Count * 100;
            double depthImpact = gap * 20.0;
            double requiredBoost = aggregated.IsRequiredAnywhere ? 15.0 : 0.0;
            double historyBoost = historyWeakAreas.GetValueOrDefault(skillName, 0) * 10.0;

            double interviewImpact = Math.Min(100, vacancyCoverage * 0.4 + depthImpact + requiredBoost + historyBoost);

            var effort = ClassifyEffort(userLevel, aggregated.MaxExpectedDepth);
            int estimatedHours = EstimateHours(userLevel, aggregated.MaxExpectedDepth);
            string focusArea = DetermineFocusArea(userLevel, aggregated.MaxExpectedDepth);

            items.Add(new InterviewLearningItem
            {
                SkillName = skillName,
                Effort = effort,
                EffortLabel = EffortToLabel(effort),
                EstimatedHours = estimatedHours,
                InterviewImpactScore = interviewImpact,
                FocusArea = focusArea,
                PrepActions = GeneratePrepActions(skillName, userLevel, aggregated.MaxExpectedDepth, focusArea)
            });
        }

        // Sort by impact
        items = items.OrderByDescending(i => i.InterviewImpactScore).ThenBy(i => i.EstimatedHours).ToList();

        int skippedCount = MarkLowROISkips(items);
        int totalHours = items.Where(i => !i.SkipRecommended).Sum(i => i.EstimatedHours);

        // Estimate pass probability using highest-seniority vacancy as reference
        var referenceSeniority = vacancies.Max(v => v.SeniorityLevel);
        double passProbability = EstimateConsolidatedPassProbability(items, profile, referenceSeniority);

        string verdict = GenerateVerdict(items, totalHours, passProbability);
        int criticalCount = items.Count(i => i.InterviewImpactScore >= HighImpactThreshold && !i.SkipRecommended);

        return new InterviewLearningPlan
        {
            VacancyId = "consolidated",
            Items = items,
            TotalEstimatedHours = totalHours,
            CriticalGapCount = criticalCount,
            SkippedLowROICount = skippedCount,
            EstimatedPassProbability = passProbability,
            Verdict = verdict
        };
    }

    private InterviewLearningItem BuildLearningItem(
        SkillQuestionSet skillSet, int userLevel, int gap,
        SeniorityLevel seniority, Dictionary<string, int> historyWeakAreas, int totalSkillCount)
    {
        // Compute interview impact score (0-100)
        double interviewImpact = ComputeInterviewImpact(
            skillSet, gap, seniority, historyWeakAreas, totalSkillCount);

        var effort = ClassifyEffort(userLevel, skillSet.ExpectedDepth);
        int estimatedHours = EstimateHours(userLevel, skillSet.ExpectedDepth);
        string focusArea = DetermineFocusArea(userLevel, skillSet.ExpectedDepth);

        return new InterviewLearningItem
        {
            SkillName = skillSet.SkillName,
            Effort = effort,
            EffortLabel = EffortToLabel(effort),
            EstimatedHours = estimatedHours,
            InterviewImpactScore = interviewImpact,
            FocusArea = focusArea,
            PrepActions = GeneratePrepActions(skillSet.SkillName, userLevel, skillSet.ExpectedDepth, focusArea)
        };
    }

    private static double ComputeInterviewImpact(
        SkillQuestionSet skillSet, int gap, SeniorityLevel seniority,
        Dictionary<string, int> historyWeakAreas, int totalSkillCount)
    {
        double impact = 0;

        // Gap severity (0-40 points)
        impact += gap * 10.0;

        // Required vs preferred (0-20 points)
        impact += skillSet.IsRequired ? 20.0 : 5.0;

        // Expected depth — higher depth = more interview time spent on this (0-15 points)
        impact += skillSet.ExpectedDepth * 3.0;

        // Historical interview weakness — if failed on this skill before (0-20 points)
        int weaknessCount = historyWeakAreas.GetValueOrDefault(skillSet.SkillName, 0);
        impact += Math.Min(20, weaknessCount * 10.0);

        // Red flag count — more red flags = higher stakes (0-5 points)
        impact += Math.Min(5, skillSet.RedFlags.Count);

        return Math.Min(100, impact);
    }

    private static LearningEffort ClassifyEffort(int currentLevel, int targetDepth)
    {
        int gap = Math.Max(0, targetDepth - currentLevel);

        return gap switch
        {
            0 => LearningEffort.Fast, // review only (historical weakness)
            1 => LearningEffort.Fast,
            2 => LearningEffort.Medium,
            _ => LearningEffort.Deep
        };
    }

    private static int EstimateHours(int currentLevel, int targetDepth)
    {
        int gap = Math.Max(0, targetDepth - currentLevel);

        if (gap == 0) return 2; // quick review for historical weakness

        int hours = 0;
        for (int level = currentLevel; level < targetDepth; level++)
        {
            hours += level switch
            {
                0 => 3,  // awareness → usage
                1 => 4,  // usage → internals
                2 => 8,  // internals → tradeoffs
                3 => 12, // tradeoffs → expert
                4 => 16, // expert-level refinement
                _ => 8
            };
        }

        return hours;
    }

    private static string DetermineFocusArea(int currentLevel, int targetDepth)
    {
        if (currentLevel >= targetDepth) return "review";

        // Focus on the gap between current and target
        return currentLevel switch
        {
            0 => "fundamentals — learn core concepts and basic usage patterns",
            1 => "internals — understand how it works under the hood",
            2 => "failure modes — learn what breaks and how to handle it",
            3 => "tradeoffs — practice comparing alternatives and articulating decisions",
            4 => "edge cases — study advanced scenarios and security implications",
            _ => "general review"
        };
    }

    private static List<string> GeneratePrepActions(string skill, int currentLevel, int targetDepth, string focusArea)
    {
        var actions = new List<string>();

        if (currentLevel < 1)
        {
            actions.Add($"Complete a quick tutorial on {skill} basics (official docs or getting-started guide)");
            actions.Add($"Build a minimal working example using {skill}");
        }

        if (currentLevel < 2 && targetDepth >= 2)
        {
            actions.Add($"Practice writing code with {skill} — solve 3-5 exercises or small tasks");
            actions.Add($"Read {skill} API documentation for the most-used features");
        }

        if (currentLevel < 3 && targetDepth >= 3)
        {
            actions.Add($"Study {skill} internal architecture — how does it work under the hood?");
            actions.Add($"Read source code or architecture docs for {skill}'s core components");
            actions.Add($"Practice explaining {skill} internals out loud (mock interview style)");
        }

        if (currentLevel < 4 && targetDepth >= 4)
        {
            actions.Add($"Prepare a comparison of {skill} vs 2-3 alternatives with concrete tradeoffs");
            actions.Add($"Document {skill} failure modes you've seen or read about, with recovery strategies");
            actions.Add($"Practice answering 'when would you NOT use {skill}?' with specific scenarios");
        }

        if (currentLevel < 5 && targetDepth >= 5)
        {
            actions.Add($"Design a system that leverages {skill} — write up the architecture with diagrams");
            actions.Add($"Prepare to discuss {skill} security implications and hardening practices");
            actions.Add($"Practice live-coding a non-trivial component with {skill}");
        }

        if (actions.Count == 0)
        {
            actions.Add($"Quick review of {skill} — refresh your knowledge with focus on {focusArea}");
        }

        return actions;
    }

    private int MarkLowROISkips(List<InterviewLearningItem> items)
    {
        int skippedCount = 0;
        int accumulatedHours = 0;

        foreach (var item in items)
        {
            accumulatedHours += item.EstimatedHours;

            // Skip if: low impact AND either deep effort or total budget exceeded
            if (item.InterviewImpactScore < LowROIThreshold)
            {
                item.SkipRecommended = true;
                item.SkipReason = $"Low interview impact ({item.InterviewImpactScore:F0}%). Time is better spent on higher-impact skills.";
                skippedCount++;
                logger.LogDebug("Skipping low-ROI skill: {Skill} (impact={Impact:F0})", item.SkillName, item.InterviewImpactScore);
            }
            else if (item.Effort == LearningEffort.Deep && accumulatedHours > MaxPrepHoursRecommended)
            {
                item.SkipRecommended = true;
                item.SkipReason = $"Prep budget exceeded ({accumulatedHours}h). Focus on higher-impact skills first — apply before mastering everything.";
                skippedCount++;
                logger.LogDebug("Skipping over-budget skill: {Skill} (accumulated={Hours}h)", item.SkillName, accumulatedHours);
            }
        }

        return skippedCount;
    }

    private static double EstimatePassProbability(
        List<InterviewLearningItem> items, UserProfile profile, JobVacancy vacancy)
    {
        if (items.Count == 0) return 85.0; // no gaps = high probability

        // Base probability from profile strength
        double base_ = 50.0;

        // Boost for seniority match
        double maxExperience = profile.Skills
            .Where(s => s.YearsOfExperience > 0)
            .Select(s => s.YearsOfExperience)
            .DefaultIfEmpty(0)
            .Max();

        if (maxExperience >= 5) base_ += 10;
        if (maxExperience >= 8) base_ += 5;

        // Penalize for each critical gap (impact >= 60)
        int criticalGaps = items.Count(i => i.InterviewImpactScore >= HighImpactThreshold && !i.SkipRecommended);
        base_ -= criticalGaps * 12;

        // Penalize for deep effort items
        int deepItems = items.Count(i => i.Effort == LearningEffort.Deep && !i.SkipRecommended);
        base_ -= deepItems * 8;

        // Boost for fast-effort items (almost there)
        int fastItems = items.Count(i => i.Effort == LearningEffort.Fast && !i.SkipRecommended);
        base_ += fastItems * 3;

        return Math.Clamp(base_, 5, 95);
    }

    private static double EstimateConsolidatedPassProbability(
        List<InterviewLearningItem> items, UserProfile profile, SeniorityLevel seniority)
    {
        if (items.Count == 0) return 85.0;

        double base_ = 50.0;

        double maxExperience = profile.Skills
            .Where(s => s.YearsOfExperience > 0)
            .Select(s => s.YearsOfExperience)
            .DefaultIfEmpty(0)
            .Max();

        if (maxExperience >= 5) base_ += 10;
        if (maxExperience >= 8) base_ += 5;

        int criticalGaps = items.Count(i => i.InterviewImpactScore >= HighImpactThreshold && !i.SkipRecommended);
        base_ -= criticalGaps * 10;

        int deepItems = items.Count(i => i.Effort == LearningEffort.Deep && !i.SkipRecommended);
        base_ -= deepItems * 6;

        int fastItems = items.Count(i => i.Effort == LearningEffort.Fast && !i.SkipRecommended);
        base_ += fastItems * 2;

        return Math.Clamp(base_, 5, 95);
    }

    private static string GenerateVerdict(List<InterviewLearningItem> items, int totalHours, double passProbability)
    {
        int activeItems = items.Count(i => !i.SkipRecommended);
        int criticalItems = items.Count(i => i.InterviewImpactScore >= HighImpactThreshold && !i.SkipRecommended);

        if (activeItems == 0)
            return "Ready — no significant gaps detected. Focus on interview practice and confidence.";

        if (criticalItems == 0 && totalHours <= 10)
            return $"Nearly ready — {totalHours}h of light prep needed. Quick review on {activeItems} skill(s), then apply.";

        if (totalHours <= 20)
            return $"Prep needed ({totalHours}h) — {criticalItems} critical gap(s). Focused preparation then apply within 1-2 weeks.";

        if (totalHours <= MaxPrepHoursRecommended)
            return $"Significant prep needed ({totalHours}h) — {criticalItems} critical gap(s). Plan 2-4 weeks of focused study. Consider applying in parallel.";

        return $"Heavy prep needed ({totalHours}h) — {criticalItems} critical gap(s). Consider whether this role is the right target right now, or focus on the top {Math.Min(5, activeItems)} highest-impact items and apply anyway.";
    }

    private static Dictionary<string, int> BuildWeakAreaFrequency(IReadOnlyList<InterviewFeedback>? history)
    {
        if (history is null || history.Count == 0)
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return history
            .SelectMany(h => h.WeakAreas)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasHistoricalWeakness(string skillName, Dictionary<string, int> historyWeakAreas)
    {
        return historyWeakAreas.ContainsKey(skillName);
    }

    private static string EffortToLabel(LearningEffort effort) => effort switch
    {
        LearningEffort.Fast => "Fast (1-4h) — quick review, refresh existing knowledge",
        LearningEffort.Medium => "Medium (5-15h) — practice exercises, learn new patterns",
        LearningEffort.Deep => "Deep (16h+) — fundamentals work, build projects, deep study",
        _ => effort.ToString()
    };

    private sealed class AggregatedSkillData
    {
        public string SkillName { get; set; } = string.Empty;
        public int AppearanceCount { get; set; }
        public int MaxExpectedDepth { get; set; }
        public bool IsRequiredAnywhere { get; set; }
    }
}
