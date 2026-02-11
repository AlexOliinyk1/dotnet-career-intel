using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Adaptive learning engine (Phase 5). Adjusts learning priorities based on
/// interview performance, question confidence, and failure patterns.
/// Self-corrects: if you keep failing system design, it shifts focus there.
/// </summary>
public sealed class AdaptiveLearningEngine
{
    public AdaptiveLearningEngine()
    {
    }

    /// <summary>
    /// Generate an adaptive learning plan based on interview results and confidence gaps.
    /// </summary>
    public AdaptivePlan GeneratePlan(
        List<InterviewFeedback> feedback,
        List<InterviewQuestionConfidence> confidences)
    {
        var plan = new AdaptivePlan();

        // Identify weak areas from interview failures
        var failureTopics = AnalyzeFailureTopics(feedback);

        // Identify low-confidence topics
        var lowConfidenceTopics = confidences
            .Where(c => c.ConfidenceLevel < 60)
            .GroupBy(c => c.Category)
            .Select(g => new TopicGap
            {
                Topic = g.Key,
                AverageConfidence = g.Average(c => c.ConfidenceLevel),
                QuestionCount = g.Count(),
                Source = "Low confidence"
            })
            .OrderBy(t => t.AverageConfidence)
            .ToList();

        // Merge and prioritize
        var allGaps = MergeGaps(failureTopics, lowConfidenceTopics);

        // Generate learning sessions
        foreach (var gap in allGaps.Take(5))
        {
            var session = new LearningSession
            {
                Topic = gap.Topic,
                Priority = CalculatePriority(gap),
                EstimatedMinutes = EstimateLearningTime(gap),
                Reason = gap.Source,
                Confidence = gap.AverageConfidence,
                SuggestedActions = GenerateActions(gap)
            };
            plan.Sessions.Add(session);
        }

        // Calculate overall readiness
        plan.OverallReadiness = CalculateOverallReadiness(confidences);
        plan.WeakestArea = allGaps.FirstOrDefault()?.Topic ?? "None identified";
        plan.StrongestArea = confidences
            .GroupBy(c => c.Category)
            .OrderByDescending(g => g.Average(c => c.ConfidenceLevel))
            .FirstOrDefault()?.Key ?? "None identified";

        // Detect overlearning (spending too much time on already-strong areas)
        plan.OverlearningWarnings = DetectOverlearning(confidences, feedback);

        return plan;
    }

    /// <summary>
    /// Adjust priorities after a new interview result.
    /// </summary>
    public AdaptiveAdjustment AdjustAfterInterview(
        InterviewFeedback latestFeedback,
        AdaptivePlan currentPlan)
    {
        var adjustment = new AdaptiveAdjustment
        {
            InterviewCompany = latestFeedback.Company,
            InterviewRound = latestFeedback.Round,
            Outcome = latestFeedback.Outcome
        };

        var passed = latestFeedback.Outcome.Contains("Pass", StringComparison.OrdinalIgnoreCase) ||
                     latestFeedback.Outcome.Contains("Offer", StringComparison.OrdinalIgnoreCase);

        if (passed)
        {
            adjustment.Message = $"Passed {latestFeedback.Round} at {latestFeedback.Company} — current learning plan is working.";
            adjustment.Action = AdjustmentAction.Continue;
        }
        else
        {
            // Extract what failed
            var failedTopics = ExtractTopicsFromFeedback(latestFeedback);

            if (failedTopics.Count > 0)
            {
                adjustment.Message = $"Failed {latestFeedback.Round} at {latestFeedback.Company}. " +
                    $"Weak areas: {string.Join(", ", failedTopics)}. Shifting learning focus.";
                adjustment.Action = AdjustmentAction.Pivot;
                adjustment.NewFocusTopics = failedTopics;
            }
            else
            {
                adjustment.Message = $"Failed {latestFeedback.Round} at {latestFeedback.Company}. " +
                    "No specific topic identified — review full feedback.";
                adjustment.Action = AdjustmentAction.Review;
            }
        }

        return adjustment;
    }

    private List<TopicGap> AnalyzeFailureTopics(List<InterviewFeedback> feedback)
    {
        var failures = feedback.Where(f =>
            f.Outcome.Contains("Reject", StringComparison.OrdinalIgnoreCase) ||
            f.Outcome.Contains("Fail", StringComparison.OrdinalIgnoreCase));

        var topicCounts = new Dictionary<string, (int Count, List<string> Notes)>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in failures)
        {
            var topics = ExtractTopicsFromFeedback(f);
            foreach (var topic in topics)
            {
                if (!topicCounts.ContainsKey(topic))
                    topicCounts[topic] = (0, []);

                var current = topicCounts[topic];
                topicCounts[topic] = (current.Count + 1, current.Notes);
                topicCounts[topic].Notes.Add($"Failed at {f.Company} ({f.Round})");
            }
        }

        return topicCounts.Select(kv => new TopicGap
        {
            Topic = kv.Key,
            AverageConfidence = Math.Max(0, 50 - kv.Value.Count * 15),
            QuestionCount = kv.Value.Count,
            Source = $"Interview failures ({kv.Value.Count}x)"
        })
        .OrderBy(t => t.AverageConfidence)
        .ToList();
    }

    private static List<string> ExtractTopicsFromFeedback(InterviewFeedback feedback)
    {
        var topics = new List<string>();
        var text = $"{feedback.Feedback} {feedback.Round}".ToLowerInvariant();

        var topicKeywords = new Dictionary<string, string[]>
        {
            ["System Design"] = ["system design", "architecture", "scalability", "distributed"],
            ["Algorithms"] = ["algorithm", "leetcode", "coding", "data structure", "whiteboard"],
            [".NET Internals"] = [".net", "c#", "clr", "gc", "async", "memory"],
            ["Databases"] = ["sql", "database", "query", "indexing", "orm"],
            ["Cloud"] = ["azure", "aws", "cloud", "infrastructure", "devops"],
            ["Behavioral"] = ["behavioral", "culture", "leadership", "communication"],
            ["Concurrency"] = ["concurrency", "threading", "parallel", "async", "deadlock"],
            ["Testing"] = ["testing", "unit test", "tdd", "quality"]
        };

        foreach (var (topic, keywords) in topicKeywords)
        {
            if (keywords.Any(k => text.Contains(k)))
                topics.Add(topic);
        }

        return topics;
    }

    private static List<TopicGap> MergeGaps(List<TopicGap> failures, List<TopicGap> lowConfidence)
    {
        var merged = new Dictionary<string, TopicGap>(StringComparer.OrdinalIgnoreCase);

        foreach (var gap in failures)
        {
            merged[gap.Topic] = gap;
        }

        foreach (var gap in lowConfidence)
        {
            if (merged.TryGetValue(gap.Topic, out var existing))
            {
                // Interview failures + low confidence = critical
                existing.AverageConfidence = Math.Min(existing.AverageConfidence, gap.AverageConfidence);
                existing.Source = $"{existing.Source} + {gap.Source}";
            }
            else
            {
                merged[gap.Topic] = gap;
            }
        }

        return merged.Values.OrderBy(g => g.AverageConfidence).ToList();
    }

    private static string CalculatePriority(TopicGap gap)
    {
        if (gap.Source.Contains("failures") && gap.AverageConfidence < 40)
            return "CRITICAL";
        if (gap.AverageConfidence < 40)
            return "High";
        if (gap.AverageConfidence < 60)
            return "Medium";
        return "Low";
    }

    private static int EstimateLearningTime(TopicGap gap)
    {
        var baseMinutes = gap.AverageConfidence switch
        {
            < 20 => 120,
            < 40 => 90,
            < 60 => 60,
            _ => 30
        };

        if (gap.Source.Contains("failures"))
            baseMinutes = (int)(baseMinutes * 1.5);

        return baseMinutes;
    }

    private static List<string> GenerateActions(TopicGap gap)
    {
        var actions = new List<string>();

        if (gap.AverageConfidence < 30)
        {
            actions.Add($"Study fundamentals of {gap.Topic} from scratch");
            actions.Add($"Do 5+ practice problems on {gap.Topic}");
        }
        else if (gap.AverageConfidence < 60)
        {
            actions.Add($"Review key concepts in {gap.Topic}");
            actions.Add($"Practice 3 {gap.Topic} interview questions");
        }
        else
        {
            actions.Add($"Quick refresher on {gap.Topic} edge cases");
        }

        if (gap.Source.Contains("failures"))
            actions.Add($"Review failed interview notes for {gap.Topic} questions");

        return actions;
    }

    private static int CalculateOverallReadiness(List<InterviewQuestionConfidence> confidences)
    {
        if (confidences.Count == 0)
            return 0;

        return (int)confidences.Average(c => c.ConfidenceLevel);
    }

    private static List<string> DetectOverlearning(
        List<InterviewQuestionConfidence> confidences,
        List<InterviewFeedback> feedback)
    {
        var warnings = new List<string>();

        var highConfidenceTopics = confidences
            .GroupBy(c => c.Category)
            .Where(g => g.Average(c => c.ConfidenceLevel) > 85)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var failedTopics = feedback
            .Where(f => f.Outcome.Contains("Reject", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => ExtractTopicsFromFeedback(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Warn if high-confidence topics overlap with no failures but low-confidence topics have failures
        var lowConfidenceWithFailures = failedTopics
            .Where(t => !highConfidenceTopics.Contains(t))
            .ToList();

        if (highConfidenceTopics.Count > 0 && lowConfidenceWithFailures.Count > 0)
        {
            warnings.Add($"You're strong in {string.Join(", ", highConfidenceTopics.Take(3))} but keep failing " +
                $"{string.Join(", ", lowConfidenceWithFailures.Take(3))}. Shift study time.");
        }

        return warnings;
    }
}

public sealed class AdaptivePlan
{
    public List<LearningSession> Sessions { get; set; } = [];
    public int OverallReadiness { get; set; }
    public string WeakestArea { get; set; } = string.Empty;
    public string StrongestArea { get; set; } = string.Empty;
    public List<string> OverlearningWarnings { get; set; } = [];
}

public sealed class LearningSession
{
    public string Topic { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> SuggestedActions { get; set; } = [];
}

public sealed class TopicGap
{
    public string Topic { get; set; } = string.Empty;
    public double AverageConfidence { get; set; }
    public int QuestionCount { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class AdaptiveAdjustment
{
    public string InterviewCompany { get; set; } = string.Empty;
    public string InterviewRound { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public AdjustmentAction Action { get; set; }
    public List<string> NewFocusTopics { get; set; } = [];
}

public enum AdjustmentAction
{
    Continue,
    Pivot,
    Review
}
