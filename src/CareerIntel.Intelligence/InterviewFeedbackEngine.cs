using CareerIntel.Core.Models;
using CareerIntel.Intelligence.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class InterviewFeedbackEngine(ILogger<InterviewFeedbackEngine> logger)
{
    private static readonly HashSet<string> PassOutcomes =
        new(["Pass", "Passed", "Advance", "Advanced", "Offer"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FailOutcomes =
        new(["Reject", "Rejected", "Fail", "Failed", "Declined"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ingest new feedback and return learning priority adjustments.
    /// </summary>
    public FeedbackAnalysis AnalyzeFeedback(
        InterviewFeedback newFeedback,
        IReadOnlyList<InterviewFeedback> allFeedback,
        UserProfile profile)
    {
        logger.LogInformation("Analyzing feedback from {Company}, round {Round}, outcome {Outcome}",
            newFeedback.Company, newFeedback.Round, newFeedback.Outcome);

        // 1. Extract weak areas from the new feedback
        var newWeakAreas = ExtractWeakAreas(newFeedback);

        // 2. Cross-reference with historical weak areas
        var historicalWeakAreas = allFeedback
            .SelectMany(f => f.WeakAreas)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // 3. Identify repeating patterns (same weak area in 2+ interviews = CRITICAL)
        var repeatingWeaknesses = new List<string>();
        foreach (var area in newWeakAreas)
        {
            int historicalCount = historicalWeakAreas.GetValueOrDefault(area, 0);
            // Count includes all feedback; if it appeared in 2+ total interviews it's repeating
            if (historicalCount >= 2)
            {
                repeatingWeaknesses.Add(area);
            }
        }

        // Also include any historical area that has appeared 2+ times
        foreach (var (area, count) in historicalWeakAreas)
        {
            if (count >= 2 && !repeatingWeaknesses.Contains(area, StringComparer.OrdinalIgnoreCase))
            {
                repeatingWeaknesses.Add(area);
            }
        }

        // 4. Generate skill priority adjustments
        var priorityAdjustments = ComputePriorityAdjustments(newWeakAreas, historicalWeakAreas, profile);

        // 5. Generate targeted prep tasks
        var prepTasks = GeneratePrepTasks(newFeedback, newWeakAreas, repeatingWeaknesses);

        // 6. Build summary
        string summary = BuildAnalysisSummary(newFeedback, repeatingWeaknesses, priorityAdjustments);

        logger.LogInformation("Feedback analysis complete: {RepeatingCount} repeating weaknesses, {AdjustmentCount} priority adjustments",
            repeatingWeaknesses.Count, priorityAdjustments.Count);

        return new FeedbackAnalysis
        {
            PriorityAdjustments = priorityAdjustments,
            NewPrepTasks = prepTasks,
            RepeatingWeaknesses = repeatingWeaknesses,
            Summary = summary
        };
    }

    /// <summary>
    /// Get aggregated insights across all interviews.
    /// </summary>
    public InterviewInsights GetInsights(IReadOnlyList<InterviewFeedback> allFeedback)
    {
        logger.LogInformation("Generating interview insights from {Count} feedback entries", allFeedback.Count);

        if (allFeedback.Count == 0)
        {
            return new InterviewInsights
            {
                TotalInterviews = 0,
                OverallPassRate = 0.0,
                Trend = "Stable"
            };
        }

        // 1. Pass rate by round type
        var passRateByRound = ComputePassRateByRound(allFeedback);

        // 2. Most common rejection reasons
        var topRejectionReasons = ComputeTopRejectionReasons(allFeedback);

        // 3. Repeating weak areas sorted by frequency
        var repeatingWeakAreas = allFeedback
            .SelectMany(f => f.WeakAreas)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Select(g => (Area: g.Key, Count: g.Count()))
            .ToList();

        // 4. Overall pass rate
        int totalWithOutcome = allFeedback.Count(f => !string.IsNullOrWhiteSpace(f.Outcome));
        int totalPassed = allFeedback.Count(f => PassOutcomes.Contains(f.Outcome));
        double overallPassRate = totalWithOutcome > 0 ? (double)totalPassed / totalWithOutcome : 0.0;

        // 5. Trend analysis: compare first half vs second half performance
        string trend = ComputeTrend(allFeedback);

        return new InterviewInsights
        {
            PassRateByRound = passRateByRound,
            TopRejectionReasons = topRejectionReasons,
            RepeatingWeakAreas = repeatingWeakAreas,
            OverallPassRate = overallPassRate,
            Trend = trend,
            TotalInterviews = allFeedback.Count
        };
    }

    private static List<string> ExtractWeakAreas(InterviewFeedback feedback)
    {
        var weakAreas = new List<string>(feedback.WeakAreas);

        // Also extract keywords from feedback text if present
        if (!string.IsNullOrWhiteSpace(feedback.Feedback))
        {
            var keywordMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["system design"] = "System Design",
                ["algorithms"] = "Algorithms",
                ["data structures"] = "Data Structures",
                ["communication"] = "Communication",
                ["coding"] = "Coding",
                ["architecture"] = "Architecture",
                ["testing"] = "Testing",
                ["debugging"] = "Debugging",
                ["concurrency"] = "Concurrency",
                ["database"] = "Database",
                ["sql"] = "SQL",
                ["api design"] = "API Design",
                ["behavioral"] = "Behavioral",
                ["leadership"] = "Leadership",
                ["problem solving"] = "Problem Solving",
                ["time management"] = "Time Management",
                ["scalability"] = "Scalability",
                ["security"] = "Security",
                ["performance"] = "Performance",
                ["cloud"] = "Cloud",
                ["devops"] = "DevOps",
                ["ci/cd"] = "CI/CD"
            };

            string feedbackLower = feedback.Feedback.ToLowerInvariant();
            foreach (var (keyword, canonical) in keywordMap)
            {
                if (feedbackLower.Contains(keyword)
                    && !weakAreas.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                {
                    weakAreas.Add(canonical);
                }
            }
        }

        return weakAreas.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<(string Skill, double PriorityBoost)> ComputePriorityAdjustments(
        List<string> newWeakAreas,
        Dictionary<string, int> historicalWeakAreas,
        UserProfile profile)
    {
        var adjustments = new List<(string Skill, double PriorityBoost)>();

        foreach (var area in newWeakAreas)
        {
            int historicalCount = historicalWeakAreas.GetValueOrDefault(area, 0);

            // Base boost for appearing in feedback
            double boost = 0.1;

            // Repeating weakness gets a larger boost
            if (historicalCount >= 2)
            {
                boost = 0.3; // CRITICAL priority
            }
            else if (historicalCount == 1)
            {
                boost = 0.2; // HIGH priority
            }

            // Check if user already has this skill at a decent level
            var existingSkill = profile.Skills
                .FirstOrDefault(s => s.SkillName.Equals(area, StringComparison.OrdinalIgnoreCase));

            if (existingSkill is not null && existingSkill.ProficiencyLevel >= 4)
            {
                // Skill is already strong - might be an interview technique issue, not a skill issue
                boost *= 0.5;
            }

            adjustments.Add((area, boost));
        }

        return adjustments.OrderByDescending(a => a.PriorityBoost).ToList();
    }

    private static List<PrepAction> GeneratePrepTasks(
        InterviewFeedback newFeedback,
        List<string> weakAreas,
        List<string> repeatingWeaknesses)
    {
        var tasks = new List<PrepAction>();
        int priority = 1;

        // Critical: repeating weaknesses get highest priority
        foreach (var weakness in repeatingWeaknesses)
        {
            tasks.Add(new PrepAction
            {
                Action = $"CRITICAL: Address repeating weakness in {weakness} - dedicate focused practice sessions",
                Category = "Critical Gap",
                Priority = priority++,
                EstimatedHours = 10
            });
        }

        // New weak areas that are not yet repeating
        foreach (var area in weakAreas.Where(a =>
            !repeatingWeaknesses.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            tasks.Add(new PrepAction
            {
                Action = $"Practice {area} with targeted exercises and mock scenarios",
                Category = "Skill Improvement",
                Priority = priority++,
                EstimatedHours = 5
            });
        }

        // Round-specific prep based on feedback difficulty
        if (newFeedback.DifficultyRating >= 4)
        {
            tasks.Add(new PrepAction
            {
                Action = $"Practice high-difficulty {newFeedback.Round} rounds - seek harder practice problems",
                Category = "Interview Prep",
                Priority = priority++,
                EstimatedHours = 8
            });
        }

        // If the outcome was a rejection, add a debrief task
        if (FailOutcomes.Contains(newFeedback.Outcome))
        {
            tasks.Add(new PrepAction
            {
                Action = $"Debrief {newFeedback.Company} {newFeedback.Round} rejection: analyze what went wrong and create mitigation plan",
                Category = "Post-Mortem",
                Priority = priority++,
                EstimatedHours = 2
            });
        }

        return tasks;
    }

    private static string BuildAnalysisSummary(
        InterviewFeedback newFeedback,
        List<string> repeatingWeaknesses,
        List<(string Skill, double PriorityBoost)> adjustments)
    {
        var parts = new List<string>();

        string outcomeLabel = PassOutcomes.Contains(newFeedback.Outcome) ? "passed" : "did not pass";
        parts.Add($"Interview at {newFeedback.Company} ({newFeedback.Round}): {outcomeLabel}.");

        if (repeatingWeaknesses.Count > 0)
        {
            parts.Add($"ALERT: {repeatingWeaknesses.Count} repeating weakness(es) detected: {string.Join(", ", repeatingWeaknesses)}. These require immediate attention.");
        }

        if (adjustments.Count > 0)
        {
            var topAdjustments = adjustments.Take(3).Select(a => $"{a.Skill} (+{a.PriorityBoost:F1})");
            parts.Add($"Top priority adjustments: {string.Join(", ", topAdjustments)}.");
        }

        if (newFeedback.StrongAreas.Count > 0)
        {
            parts.Add($"Strengths confirmed: {string.Join(", ", newFeedback.StrongAreas)}.");
        }

        return string.Join(" ", parts);
    }

    private static Dictionary<string, double> ComputePassRateByRound(IReadOnlyList<InterviewFeedback> allFeedback)
    {
        return allFeedback
            .GroupBy(f => f.Round, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    int total = g.Count();
                    int passed = g.Count(f => PassOutcomes.Contains(f.Outcome));
                    return total > 0 ? (double)passed / total : 0.0;
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<(string Reason, int Count)> ComputeTopRejectionReasons(
        IReadOnlyList<InterviewFeedback> allFeedback)
    {
        return allFeedback
            .Where(f => FailOutcomes.Contains(f.Outcome))
            .SelectMany(f => f.WeakAreas)
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => (Reason: g.Key, Count: g.Count()))
            .ToList();
    }

    private string ComputeTrend(IReadOnlyList<InterviewFeedback> allFeedback)
    {
        if (allFeedback.Count < 4)
        {
            logger.LogDebug("Not enough interviews ({Count}) to determine trend, defaulting to Stable",
                allFeedback.Count);
            return "Stable";
        }

        int midpoint = allFeedback.Count / 2;
        var firstHalf = allFeedback.Take(midpoint).ToList();
        var secondHalf = allFeedback.Skip(midpoint).ToList();

        double firstPassRate = ComputeHalfPassRate(firstHalf);
        double secondPassRate = ComputeHalfPassRate(secondHalf);

        double delta = secondPassRate - firstPassRate;

        if (delta > 0.1)
            return "Improving";
        if (delta < -0.1)
            return "Declining";
        return "Stable";
    }

    private static double ComputeHalfPassRate(List<InterviewFeedback> feedback)
    {
        int total = feedback.Count(f => !string.IsNullOrWhiteSpace(f.Outcome));
        int passed = feedback.Count(f => PassOutcomes.Contains(f.Outcome));
        return total > 0 ? (double)passed / total : 0.0;
    }
}
