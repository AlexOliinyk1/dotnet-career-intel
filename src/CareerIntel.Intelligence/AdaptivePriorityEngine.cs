using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Computes adaptive skill priority weights from interview outcome data.
/// Skills that repeatedly cause rejections get boosted; overhyped skills get dampened.
/// </summary>
public sealed class AdaptivePriorityEngine(ILogger<AdaptivePriorityEngine> logger)
{
    /// <summary>
    /// Analyze all interview feedback to produce adaptive weights per skill.
    /// Skills that cause rejections are boosted; skills that are strong but not differentiating are dampened.
    /// </summary>
    public List<AdaptiveWeight> ComputeAdaptiveWeights(IReadOnlyList<InterviewFeedback> allFeedback)
    {
        logger.LogInformation("Computing adaptive weights from {Count} feedback entries", allFeedback.Count);

        // Group feedback by outcome
        var rejected = allFeedback
            .Where(f => string.Equals(f.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var passed = allFeedback
            .Where(f => string.Equals(f.Outcome, "Passed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // For rejected interviews: count weak area frequency and track failure stages
        var rejectionSkillCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rejectionSkillStages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var feedback in rejected)
        {
            foreach (var weakArea in feedback.WeakAreas)
            {
                rejectionSkillCounts[weakArea] = rejectionSkillCounts.GetValueOrDefault(weakArea, 0) + 1;

                if (!rejectionSkillStages.TryGetValue(weakArea, out var stages))
                {
                    stages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    rejectionSkillStages[weakArea] = stages;
                }

                stages.Add(feedback.Round);
            }
        }

        // For passed interviews: count strong area frequency
        var passSkillCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var feedback in passed)
        {
            foreach (var strongArea in feedback.StrongAreas)
            {
                passSkillCounts[strongArea] = passSkillCounts.GetValueOrDefault(strongArea, 0) + 1;
            }
        }

        // Collect all unique skills across both sets
        var allSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in rejectionSkillCounts.Keys)
            allSkills.Add(skill);
        foreach (var skill in passSkillCounts.Keys)
            allSkills.Add(skill);

        // Build adaptive weights
        var weights = new List<AdaptiveWeight>();

        foreach (var skill in allSkills)
        {
            int rejectionCount = rejectionSkillCounts.GetValueOrDefault(skill, 0);
            int passCount = passSkillCounts.GetValueOrDefault(skill, 0);

            var failureStages = rejectionSkillStages.TryGetValue(skill, out var stages)
                ? stages.ToList()
                : [];

            double priorityMultiplier;
            if (rejectionCount >= 3)
                priorityMultiplier = 2.0; // critical
            else if (rejectionCount >= 2)
                priorityMultiplier = 1.5; // important
            else if (rejectionCount == 1 && passCount == 0)
                priorityMultiplier = 1.2; // emerging concern
            else if (passCount > rejectionCount)
                priorityMultiplier = 0.7; // overhyped - strong but not differentiating
            else
                priorityMultiplier = 1.0;

            double confidence = Math.Min(1.0, (rejectionCount + passCount) / 5.0);

            weights.Add(new AdaptiveWeight
            {
                SkillName = skill,
                RejectionCount = rejectionCount,
                PassCount = passCount,
                FailureStages = failureStages,
                PriorityMultiplier = priorityMultiplier,
                Confidence = confidence
            });
        }

        var result = weights
            .OrderByDescending(w => w.PriorityMultiplier)
            .Take(30)
            .ToList();

        logger.LogInformation("Computed {Count} adaptive weights ({Rejected} rejected, {Passed} passed interviews analyzed)",
            result.Count, rejected.Count, passed.Count);

        return result;
    }

    /// <summary>
    /// Group rejected feedback by interview round and collect the weak areas for each.
    /// Returns a taxonomy of rejection reasons organized by stage.
    /// </summary>
    public Dictionary<string, List<string>> GetRejectionTaxonomy(IReadOnlyList<InterviewFeedback> allFeedback)
    {
        logger.LogInformation("Building rejection taxonomy from {Count} feedback entries", allFeedback.Count);

        var taxonomy = allFeedback
            .Where(f => string.Equals(f.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.Round, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(f => f.WeakAreas)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);

        logger.LogInformation("Rejection taxonomy contains {Count} stages", taxonomy.Count);

        return taxonomy;
    }

    /// <summary>
    /// Analyze failure rates per interview stage.
    /// Returns stages ordered by fail rate descending.
    /// </summary>
    public List<(string Stage, int TotalInterviews, int Rejections, double FailRate)> GetFailureStageAnalysis(
        IReadOnlyList<InterviewFeedback> allFeedback)
    {
        logger.LogInformation("Analyzing failure stages from {Count} feedback entries", allFeedback.Count);

        var analysis = allFeedback
            .GroupBy(f => f.Round, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                int total = g.Count();
                int rejections = g.Count(f => string.Equals(f.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase));
                double failRate = total > 0 ? (double)rejections / total : 0.0;
                return (Stage: g.Key, TotalInterviews: total, Rejections: rejections, FailRate: failRate);
            })
            .OrderByDescending(x => x.FailRate)
            .ToList();

        logger.LogInformation("Failure stage analysis: {Count} stages analyzed", analysis.Count);

        return analysis;
    }
}
