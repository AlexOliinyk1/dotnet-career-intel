namespace CareerIntel.Intelligence;

using System.Text.Json;
using Microsoft.Extensions.Logging;

public sealed class LearningProgressTracker(ILogger<LearningProgressTracker> logger)
{
    private const string ProgressFileName = "learning-progress.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ───────────────────────────────────────────────────────────
    //  Persistence
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Load progress from the JSON file in <paramref name="dataDirectory"/>.
    /// Returns a fresh <see cref="LearningProgress"/> when the file does not exist.
    /// </summary>
    public LearningProgress LoadProgress(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, ProgressFileName);

        if (!File.Exists(path))
        {
            logger.LogInformation("No progress file found at {Path}, starting fresh", path);
            return new LearningProgress();
        }

        try
        {
            var json = File.ReadAllText(path);
            var progress = JsonSerializer.Deserialize<LearningProgress>(json, JsonOptions);

            if (progress is null)
            {
                logger.LogWarning("Deserialized progress was null from {Path}, starting fresh", path);
                return new LearningProgress();
            }

            logger.LogInformation(
                "Loaded learning progress: {Topics} topics, {Questions} questions studied, {Quizzes} quizzes taken",
                progress.TopicProgress.Count, progress.TotalQuestionsStudied, progress.TotalQuizzesTaken);

            return progress;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load progress from {Path}, starting fresh", path);
            return new LearningProgress();
        }
    }

    /// <summary>
    /// Save progress to the JSON file in <paramref name="dataDirectory"/>.
    /// </summary>
    public void SaveProgress(LearningProgress progress, string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, ProgressFileName);

        try
        {
            var json = JsonSerializer.Serialize(progress, JsonOptions);
            File.WriteAllText(path, json);
            logger.LogInformation("Saved learning progress to {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save progress to {Path}", path);
            throw;
        }
    }

    // ───────────────────────────────────────────────────────────
    //  Study tracking
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Mark a question as studied for the given topic.
    /// </summary>
    public void MarkStudied(LearningProgress progress, string topicId, string questionText)
    {
        var topic = EnsureTopicProgress(progress, topicId);

        if (topic.StudiedQuestions.Contains(questionText, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogDebug("Question already studied for topic {TopicId}: {Question}", topicId, questionText);
            return;
        }

        topic.StudiedQuestions.Add(questionText);
        topic.QuestionsStudied = topic.StudiedQuestions.Count;
        topic.LastStudied = DateTimeOffset.UtcNow;

        progress.TotalQuestionsStudied++;

        progress.StudyLog.Add(new StudyLogEntry
        {
            Date = DateTimeOffset.UtcNow,
            TopicId = topicId,
            Action = "studied",
            MinutesSpent = 0,
            Details = questionText,
        });

        UpdateStreaks(progress);
        UpdateTopicStatus(topic);

        logger.LogInformation(
            "Marked question as studied for topic {TopicId}. Total studied: {Count}/{Total}",
            topicId, topic.QuestionsStudied, topic.TotalQuestionsAvailable);
    }

    /// <summary>
    /// Record the user's self-assessed confidence for a topic on a 1-5 scale.
    /// </summary>
    public void RecordConfidence(LearningProgress progress, string topicId, int confidence)
    {
        confidence = Math.Clamp(confidence, 1, 5);
        var topic = EnsureTopicProgress(progress, topicId);
        topic.SelfConfidence = confidence;

        progress.StudyLog.Add(new StudyLogEntry
        {
            Date = DateTimeOffset.UtcNow,
            TopicId = topicId,
            Action = "confidence-update",
            MinutesSpent = 0,
            Details = $"Confidence set to {confidence}/5",
        });

        UpdateTopicStatus(topic);

        logger.LogInformation(
            "Recorded confidence {Confidence}/5 for topic {TopicId}",
            confidence, topicId);
    }

    /// <summary>
    /// Record a quiz result for the given topic.
    /// </summary>
    public void RecordQuizResult(LearningProgress progress, string topicId, int correct, int total)
    {
        if (total <= 0)
        {
            logger.LogWarning("Invalid quiz total {Total} for topic {TopicId}, ignoring", total, topicId);
            return;
        }

        var topic = EnsureTopicProgress(progress, topicId);
        var accuracy = (double)correct / total * 100.0;

        // Running average
        topic.QuizAccuracy = topic.QuizAttempts == 0
            ? accuracy
            : (topic.QuizAccuracy * topic.QuizAttempts + accuracy) / (topic.QuizAttempts + 1);

        topic.QuizAttempts++;
        topic.LastStudied = DateTimeOffset.UtcNow;

        progress.TotalQuizzesTaken++;

        progress.StudyLog.Add(new StudyLogEntry
        {
            Date = DateTimeOffset.UtcNow,
            TopicId = topicId,
            Action = "quiz",
            MinutesSpent = 0,
            Details = $"Quiz result: {correct}/{total} ({accuracy:F1}%)",
        });

        UpdateStreaks(progress);
        UpdateTopicStatus(topic);

        logger.LogInformation(
            "Recorded quiz result {Correct}/{Total} ({Accuracy:F1}%) for topic {TopicId}. Running average: {Avg:F1}%",
            correct, total, accuracy, topicId, topic.QuizAccuracy);
    }

    // ───────────────────────────────────────────────────────────
    //  Recommendations & assessment
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Get study recommendations based on progress, prioritizing weak and stale topics.
    /// </summary>
    public StudyRecommendation GetRecommendation(LearningProgress progress)
    {
        var allTopics = InterviewTopicBank.GetAllTopics();
        var focusTopics = new List<string>();
        var reviewTopics = new List<string>();
        var strongTopics = new List<string>();
        var weakTopics = new List<string>();

        var now = DateTimeOffset.UtcNow;

        // Walk topics in bank order (proxy for market demand priority)
        foreach (var bankTopic in allTopics)
        {
            var hasProgress = progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp);

            if (!hasProgress || tp!.QuestionsStudied == 0)
            {
                // Not started, and it's in the bank — focus on it
                focusTopics.Add(bankTopic.Name);
                continue;
            }

            var coverage = tp.TotalQuestionsAvailable > 0
                ? (double)tp.QuestionsStudied / tp.TotalQuestionsAvailable
                : 0;

            // Weak: quiz accuracy under 50%
            if (tp.QuizAttempts > 0 && tp.QuizAccuracy < 50)
            {
                weakTopics.Add(bankTopic.Name);
                continue;
            }

            // Review: studied but last study > 7 days ago
            if (tp.LastStudied != default && (now - tp.LastStudied).TotalDays > 7)
            {
                reviewTopics.Add(bankTopic.Name);
                continue;
            }

            // Low coverage with some progress — still needs focus
            if (coverage < 0.5)
            {
                focusTopics.Add(bankTopic.Name);
                continue;
            }

            // Otherwise it's strong
            strongTopics.Add(bankTopic.Name);
        }

        var advice = (weakTopics.Count, focusTopics.Count, reviewTopics.Count) switch
        {
            ( > 0, _, _) => $"You have {weakTopics.Count} weak topic(s) with low quiz accuracy. Focus on improving these before moving on.",
            (_, > 5, _) => $"You have {focusTopics.Count} topics that need attention. Start with the first few — they are ordered by market demand.",
            (_, _, > 3) => $"You have {reviewTopics.Count} topics due for review. Spaced repetition will solidify your knowledge.",
            _ when strongTopics.Count == allTopics.Count => "Excellent! You're well-prepared across all topics. Keep reviewing to stay sharp.",
            _ => "Good progress overall. Continue studying and take quizzes to solidify your knowledge.",
        };

        logger.LogInformation(
            "Study recommendation: {Focus} focus, {Review} review, {Strong} strong, {Weak} weak",
            focusTopics.Count, reviewTopics.Count, strongTopics.Count, weakTopics.Count);

        return new StudyRecommendation(focusTopics, reviewTopics, strongTopics, weakTopics, advice);
    }

    /// <summary>
    /// Assess overall readiness across all topics.
    /// Weighted score: 40% coverage + 30% quiz accuracy + 30% self-confidence.
    /// </summary>
    public ReadinessAssessment AssessReadiness(LearningProgress progress)
    {
        var allTopics = InterviewTopicBank.GetAllTopics();
        var topicReadinessList = new List<TopicReadiness>();
        var criticalGaps = new List<string>();
        double totalScore = 0;

        foreach (var bankTopic in allTopics)
        {
            var hasProgress = progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp);

            double coverageScore;
            double quizScore;
            double confidenceScore;

            if (!hasProgress || tp is null)
            {
                coverageScore = 0;
                quizScore = 0;
                confidenceScore = 0;
            }
            else
            {
                coverageScore = tp.TotalQuestionsAvailable > 0
                    ? (double)tp.QuestionsStudied / tp.TotalQuestionsAvailable * 100.0
                    : 0;

                quizScore = tp.QuizAttempts > 0 ? tp.QuizAccuracy : 0;
                confidenceScore = tp.SelfConfidence / 5.0 * 100.0;
            }

            var score = coverageScore * 0.4 + quizScore * 0.3 + confidenceScore * 0.3;
            score = Math.Min(score, 100);

            var status = score switch
            {
                < 25 => "Weak",
                < 50 => "Developing",
                < 75 => "Good",
                _ => "Strong",
            };

            topicReadinessList.Add(new TopicReadiness(bankTopic.Id, bankTopic.Name, Math.Round(score, 1), status));

            if (score < 30)
            {
                criticalGaps.Add(bankTopic.Name);
            }

            totalScore += score;
        }

        var overallScore = allTopics.Count > 0
            ? Math.Round(totalScore / allTopics.Count, 1)
            : 0;

        var verdict = overallScore switch
        {
            < 40 => "Not Ready",
            < 60 => "Almost Ready",
            < 80 => "Ready",
            _ => "Well Prepared",
        };

        logger.LogInformation(
            "Readiness assessment: {Score}/100 ({Verdict}), {Gaps} critical gaps",
            overallScore, verdict, criticalGaps.Count);

        return new ReadinessAssessment(overallScore, topicReadinessList, verdict, criticalGaps);
    }

    /// <summary>
    /// Generate a study session — the next N questions to study, prioritized by weakness.
    /// If <paramref name="focusTopic"/> is specified, 70% of questions come from that topic
    /// and 30% from mixed review.
    /// </summary>
    public StudySession GenerateStudySession(
        LearningProgress progress,
        string dataDirectory,
        int questionCount = 10,
        string? focusTopic = null)
    {
        var allTopics = InterviewTopicBank.GetAllTopics();

        // Build a ranked list of topics by weakness (weakest first)
        var rankedTopics = RankTopicsByWeakness(progress, allTopics);

        var questions = new List<StudyQuestion>();
        var sessionName = focusTopic is not null
            ? $"Focused session: {focusTopic}"
            : "Balanced study session";

        if (focusTopic is not null)
        {
            // 70% from focus topic, 30% from mixed review
            var focusCount = (int)Math.Ceiling(questionCount * 0.7);
            var mixedCount = questionCount - focusCount;

            var focusQuestions = PickQuestionsForTopic(progress, allTopics, focusTopic, focusCount);
            questions.AddRange(focusQuestions);

            var mixedQuestions = PickMixedQuestions(progress, allTopics, rankedTopics, mixedCount, focusTopic);
            questions.AddRange(mixedQuestions);
        }
        else
        {
            // Distribute across weak topics, weighted toward the weakest
            questions.AddRange(PickMixedQuestions(progress, allTopics, rankedTopics, questionCount, excludeTopic: null));
        }

        var estimatedMinutes = questions.Count * 5; // ~5 minutes per question
        var focusArea = focusTopic ?? (rankedTopics.Count > 0 ? rankedTopics[0].topicId : "general");

        logger.LogInformation(
            "Generated study session '{Name}' with {Count} questions, estimated {Minutes} minutes",
            sessionName, questions.Count, estimatedMinutes);

        return new StudySession(sessionName, questions, estimatedMinutes, focusArea);
    }

    // ───────────────────────────────────────────────────────────
    //  Private helpers
    // ───────────────────────────────────────────────────────────

    private static TopicProgress EnsureTopicProgress(LearningProgress progress, string topicId)
    {
        if (progress.TopicProgress.TryGetValue(topicId, out var existing))
        {
            return existing;
        }

        // Resolve topic name from the bank
        var bankTopic = InterviewTopicBank.GetAllTopics()
            .FirstOrDefault(t => string.Equals(t.Id, topicId, StringComparison.OrdinalIgnoreCase));

        var totalAvailable = bankTopic?.Questions.Count ?? 0;

        var tp = new TopicProgress
        {
            TopicId = topicId,
            TopicName = bankTopic?.Name ?? topicId,
            TotalQuestionsAvailable = totalAvailable,
        };

        progress.TopicProgress[topicId] = tp;
        return tp;
    }

    private static void UpdateStreaks(LearningProgress progress)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var lastDate = progress.LastStudyDate.Date;

        if (lastDate == today)
        {
            // Already studied today — no streak change
            return;
        }

        if (lastDate == today.AddDays(-1))
        {
            // Studied yesterday — increment streak
            progress.CurrentStreak++;
        }
        else
        {
            // Gap of more than a day — reset streak
            progress.CurrentStreak = 1;
        }

        if (progress.CurrentStreak > progress.LongestStreak)
        {
            progress.LongestStreak = progress.CurrentStreak;
        }

        progress.LastStudyDate = DateTimeOffset.UtcNow;
    }

    private static void UpdateTopicStatus(TopicProgress topic)
    {
        var coverage = topic.TotalQuestionsAvailable > 0
            ? (double)topic.QuestionsStudied / topic.TotalQuestionsAvailable
            : 0;

        topic.Status = (coverage, topic.SelfConfidence, topic.QuizAccuracy, topic.QuizAttempts) switch
        {
            // Mastered: >90% coverage, confidence 5, quiz >= 85%
            ( > 0.9, 5, >= 85, > 0) => "Mastered",

            // Confident: >70% coverage, confidence >= 4, quiz >= 70%
            ( > 0.7, >= 4, >= 70, > 0) => "Confident",

            // Review: >50% coverage, confidence >= 3
            ( > 0.5, >= 3, _, _) => "Review",

            // InProgress: at least some questions studied
            ( > 0, _, _, _) => "InProgress",

            // NotStarted: nothing studied yet
            _ => "NotStarted",
        };
    }

    private static List<(string topicId, string topicName, double weaknessScore)> RankTopicsByWeakness(
        LearningProgress progress,
        IReadOnlyList<TopicArea> allTopics)
    {
        var ranked = new List<(string topicId, string topicName, double weaknessScore)>();

        foreach (var bankTopic in allTopics)
        {
            double weaknessScore;

            if (!progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp))
            {
                // Never studied — maximum weakness
                weaknessScore = 100;
            }
            else
            {
                var coverage = tp.TotalQuestionsAvailable > 0
                    ? (double)tp.QuestionsStudied / tp.TotalQuestionsAvailable * 100.0
                    : 0;

                var quizScore = tp.QuizAttempts > 0 ? tp.QuizAccuracy : 0;
                var confidenceScore = tp.SelfConfidence / 5.0 * 100.0;

                // Weakness is the inverse of strength
                var strength = coverage * 0.4 + quizScore * 0.3 + confidenceScore * 0.3;
                weaknessScore = 100 - strength;
            }

            ranked.Add((bankTopic.Id, bankTopic.Name, weaknessScore));
        }

        // Sort weakest first (highest weakness score)
        ranked.Sort((a, b) => b.weaknessScore.CompareTo(a.weaknessScore));
        return ranked;
    }

    private static List<StudyQuestion> PickQuestionsForTopic(
        LearningProgress progress,
        IReadOnlyList<TopicArea> allTopics,
        string topicId,
        int count)
    {
        var bankTopic = allTopics.FirstOrDefault(t =>
            string.Equals(t.Id, topicId, StringComparison.OrdinalIgnoreCase));

        if (bankTopic is null)
        {
            return [];
        }

        var studiedSet = progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp)
            ? tp.StudiedQuestions.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var questions = new List<StudyQuestion>();

        // Prioritize unstudied questions
        foreach (var q in bankTopic.Questions)
        {
            if (questions.Count >= count) break;

            var isPreviouslyStudied = studiedSet.Contains(q.Question);
            if (!isPreviouslyStudied)
            {
                questions.Add(new StudyQuestion(
                    bankTopic.Id,
                    q.Question,
                    q.ExpectedAnswer,
                    q.Difficulty,
                    q.Tags,
                    "static",
                    IsPreviouslyStudied: false));
            }
        }

        // If not enough unstudied, fill with previously studied for review
        if (questions.Count < count)
        {
            foreach (var q in bankTopic.Questions)
            {
                if (questions.Count >= count) break;

                var isPreviouslyStudied = studiedSet.Contains(q.Question);
                if (isPreviouslyStudied)
                {
                    questions.Add(new StudyQuestion(
                        bankTopic.Id,
                        q.Question,
                        q.ExpectedAnswer,
                        q.Difficulty,
                        q.Tags,
                        "static",
                        IsPreviouslyStudied: true));
                }
            }
        }

        return questions;
    }

    private static List<StudyQuestion> PickMixedQuestions(
        LearningProgress progress,
        IReadOnlyList<TopicArea> allTopics,
        List<(string topicId, string topicName, double weaknessScore)> rankedTopics,
        int count,
        string? excludeTopic)
    {
        var questions = new List<StudyQuestion>();
        var topicsToUse = excludeTopic is not null
            ? rankedTopics.Where(t => !string.Equals(t.topicId, excludeTopic, StringComparison.OrdinalIgnoreCase)).ToList()
            : rankedTopics;

        if (topicsToUse.Count == 0) return questions;

        // Distribute questions weighted by weakness — weakest topics get more questions
        var totalWeakness = topicsToUse.Sum(t => t.weaknessScore);

        foreach (var (topicId, _, weaknessScore) in topicsToUse)
        {
            if (questions.Count >= count) break;

            var share = totalWeakness > 0
                ? (int)Math.Ceiling(count * (weaknessScore / totalWeakness))
                : 1;

            share = Math.Max(share, 1);
            var remaining = count - questions.Count;
            share = Math.Min(share, remaining);

            var topicQuestions = PickQuestionsForTopic(progress, allTopics, topicId, share);
            questions.AddRange(topicQuestions);
        }

        return questions.Take(count).ToList();
    }
}

// ─────────────────────────────────────────────────────────────────────────
//  Data models
// ─────────────────────────────────────────────────────────────────────────

public sealed class LearningProgress
{
    public Dictionary<string, TopicProgress> TopicProgress { get; set; } = [];
    public List<StudyLogEntry> StudyLog { get; set; } = [];
    public int TotalStudyMinutes { get; set; }
    public int TotalQuestionsStudied { get; set; }
    public int TotalQuizzesTaken { get; set; }
    public DateTimeOffset LastStudyDate { get; set; }
    public int CurrentStreak { get; set; }
    public int LongestStreak { get; set; }
}

public sealed class TopicProgress
{
    public string TopicId { get; set; } = string.Empty;
    public string TopicName { get; set; } = string.Empty;
    public int QuestionsStudied { get; set; }
    public int TotalQuestionsAvailable { get; set; }
    public int SelfConfidence { get; set; }
    public double QuizAccuracy { get; set; }
    public int QuizAttempts { get; set; }
    public List<string> StudiedQuestions { get; set; } = [];
    public DateTimeOffset LastStudied { get; set; }
    public string Status { get; set; } = "NotStarted";
}

public sealed class StudyLogEntry
{
    public DateTimeOffset Date { get; set; }
    public string TopicId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int MinutesSpent { get; set; }
    public string Details { get; set; } = string.Empty;
}

public sealed record StudyRecommendation(
    IReadOnlyList<string> FocusTopics,
    IReadOnlyList<string> ReviewTopics,
    IReadOnlyList<string> StrongTopics,
    IReadOnlyList<string> WeakTopics,
    string OverallAdvice);

public sealed record ReadinessAssessment(
    double OverallScore,
    IReadOnlyList<TopicReadiness> ByTopic,
    string Verdict,
    IReadOnlyList<string> CriticalGaps);

public sealed record TopicReadiness(
    string TopicId,
    string TopicName,
    double Score,
    string Status);

public sealed record StudySession(
    string SessionName,
    IReadOnlyList<StudyQuestion> Questions,
    int EstimatedMinutes,
    string FocusArea);

public sealed record StudyQuestion(
    string TopicId,
    string Question,
    string ExpectedAnswer,
    string Difficulty,
    IReadOnlyList<string> Tags,
    string Source,
    bool IsPreviouslyStudied);
