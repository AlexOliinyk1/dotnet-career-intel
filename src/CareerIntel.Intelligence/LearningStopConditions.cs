using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Prevents analysis paralysis and over-preparation by signaling when to STOP learning and START applying.
/// Enforces "ready enough" philosophy: 75% ready > 100% perfect.
/// </summary>
public sealed class LearningStopConditions
{
    public const int MinimumReadinessThreshold = 75; // Default "ready enough" threshold
    public const int MaxLearningWeeks = 4; // Maximum time to spend learning before forcing apply

    /// <summary>
    /// Evaluates if user should STOP learning and APPLY NOW.
    /// </summary>
    public StopConditionResult ShouldStopLearning(
        int currentReadiness,
        DateTimeOffset learningStartDate,
        List<InterviewQuestionConfidence> questions,
        JobVacancy targetVacancy)
    {
        var reasons = new List<string>();
        var signals = new List<StopSignal>();

        // Signal 1: Readiness threshold reached
        if (currentReadiness >= MinimumReadinessThreshold)
        {
            signals.Add(StopSignal.ReadinessThresholdReached);
            reasons.Add($"✓ {currentReadiness}% ready (≥{MinimumReadinessThreshold}% threshold)");
        }

        // Signal 2: Diminishing returns (learning slowed down)
        var learningVelocity = CalculateLearningVelocity(questions);
        if (learningVelocity < 5 && currentReadiness >= 60)
        {
            signals.Add(StopSignal.DiminishingReturns);
            reasons.Add($"⚠ Learning velocity low ({learningVelocity:F1}% per week) - further study has minimal ROI");
        }

        // Signal 3: Time limit exceeded
        var weeksSinceLearningStart = (DateTimeOffset.UtcNow - learningStartDate).TotalDays / 7.0;
        if (weeksSinceLearningStart >= MaxLearningWeeks)
        {
            signals.Add(StopSignal.TimeLimitExceeded);
            reasons.Add($"⚠ {weeksSinceLearningStart:F1} weeks of learning - TIME TO APPLY");
        }

        // Signal 4: Vacancy expiring soon
        var daysUntilExpiry = targetVacancy.ExpiresAt.HasValue
            ? (targetVacancy.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalDays
            : double.MaxValue;
        if (daysUntilExpiry <= 7 && daysUntilExpiry > 0)
        {
            signals.Add(StopSignal.VacancyExpiringSoon);
            reasons.Add($"⚠ Vacancy expires in {daysUntilExpiry:F0} days - APPLY NOW before it closes");
        }

        // Signal 5: Core questions confident (even if overall readiness low)
        var coreQuestionsReady = AreCoreQuestionsReady(questions);
        if (coreQuestionsReady && currentReadiness >= 65)
        {
            signals.Add(StopSignal.CoreQuestionsReady);
            reasons.Add("✓ Core interview questions mastered - you can handle the essentials");
        }

        // Signal 6: Opportunity cost (other better positions available)
        // This would require integration with vacancy ranking, placeholder for now
        // if (betterVacanciesWaiting)
        //     signals.Add(StopSignal.OpportunityCost);

        // Determine verdict
        var shouldStop = signals.Count >= 2 || // Multiple signals
                         currentReadiness >= MinimumReadinessThreshold || // Clear readiness
                         weeksSinceLearningStart >= MaxLearningWeeks; // Time limit

        var urgency = CalculateUrgency(signals, currentReadiness, daysUntilExpiry);

        return new StopConditionResult
        {
            ShouldStop = shouldStop,
            CurrentReadiness = currentReadiness,
            Signals = signals,
            Reasons = reasons,
            Urgency = urgency,
            RecommendedAction = GetRecommendedAction(shouldStop, currentReadiness, signals)
        };
    }

    private static double CalculateLearningVelocity(List<InterviewQuestionConfidence> questions)
    {
        if (questions.Count == 0)
            return 0;

        var recentlyPracticed = questions
            .Where(q => q.LastPracticed >= DateTimeOffset.UtcNow.AddDays(-7))
            .ToList();

        if (recentlyPracticed.Count == 0)
            return 0;

        // Avg confidence gain per week of recent practice
        var avgConfidenceGain = recentlyPracticed.Average(q =>
        {
            var weeksSincePractice = (DateTimeOffset.UtcNow - q.LastPracticed).TotalDays / 7.0;
            return weeksSincePractice > 0 ? q.ConfidenceLevel / weeksSincePractice : q.ConfidenceLevel;
        });

        return avgConfidenceGain;
    }

    private static bool AreCoreQuestionsReady(List<InterviewQuestionConfidence> questions)
    {
        // Core questions: SOLID, async/await, behavioral (most common in interviews)
        var coreQuestions = questions.Where(q =>
            q.Question.Contains("SOLID", StringComparison.OrdinalIgnoreCase) ||
            q.Question.Contains("async", StringComparison.OrdinalIgnoreCase) ||
            q.Question.Contains("disagree", StringComparison.OrdinalIgnoreCase) ||
            q.Question.Contains("challenging bug", StringComparison.OrdinalIgnoreCase)
        ).ToList();

        if (coreQuestions.Count == 0)
            return false;

        var avgCoreConfidence = coreQuestions.Average(q => q.ConfidenceLevel);
        return avgCoreConfidence >= 75;
    }

    private static StopConditionUrgency CalculateUrgency(
        List<StopSignal> signals,
        int readiness,
        double daysUntilExpiry)
    {
        if (signals.Contains(StopSignal.VacancyExpiringSoon) && daysUntilExpiry <= 3)
            return StopConditionUrgency.Critical;

        if (signals.Contains(StopSignal.TimeLimitExceeded))
            return StopConditionUrgency.High;

        if (readiness >= 85)
            return StopConditionUrgency.High;

        if (signals.Count >= 2)
            return StopConditionUrgency.Medium;

        return StopConditionUrgency.Low;
    }

    private static string GetRecommendedAction(bool shouldStop, int readiness, List<StopSignal> signals)
    {
        if (!shouldStop)
            return "Continue learning - not ready yet";

        if (readiness >= 85)
            return "🎯 APPLY NOW - you're well-prepared";

        if (readiness >= 75)
            return "✓ APPLY NOW - you're ready enough (75%+ threshold)";

        if (signals.Contains(StopSignal.VacancyExpiringSoon))
            return "⚠ APPLY NOW - vacancy expires soon, take the shot";

        if (signals.Contains(StopSignal.TimeLimitExceeded))
            return "⚠ APPLY NOW - time limit reached, learning more won't help significantly";

        if (signals.Contains(StopSignal.DiminishingReturns))
            return "⚠ APPLY NOW - further study has low ROI, learn on the job";

        return "APPLY NOW - multiple signals indicate readiness";
    }
}

/// <summary>
/// Result of evaluating stop conditions.
/// </summary>
public sealed class StopConditionResult
{
    public bool ShouldStop { get; set; }
    public int CurrentReadiness { get; set; }
    public List<StopSignal> Signals { get; set; } = [];
    public List<string> Reasons { get; set; } = [];
    public StopConditionUrgency Urgency { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
}

/// <summary>
/// Signals that indicate it's time to stop learning and apply.
/// </summary>
public enum StopSignal
{
    ReadinessThresholdReached,  // Hit 75%+ readiness
    DiminishingReturns,         // Learning velocity < 5% per week
    TimeLimitExceeded,          // More than 4 weeks of learning
    VacancyExpiringSoon,        // Vacancy expires within 7 days
    CoreQuestionsReady,         // Essential questions mastered
    OpportunityCost             // Better opportunities available
}

/// <summary>
/// Urgency level for stopping learning.
/// </summary>
public enum StopConditionUrgency
{
    Low,      // Continue learning if desired
    Medium,   // Consider applying soon
    High,     // Should apply within 1-2 days
    Critical  // Apply immediately (vacancy expiring)
}
