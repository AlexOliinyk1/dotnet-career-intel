namespace CareerIntel.Intelligence;

using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

public sealed class DynamicContentPipeline(
    KnowledgeBaseManager knowledgeBase,
    TopicInferenceEngine topicInference,
    LearningProgressTracker progressTracker,
    ResourceRecommendationEngine resourceEngine,
    ILogger<DynamicContentPipeline> logger)
{
    private const int DefaultDailyQuestions = 3;
    private const int MaxDailyQuestions = 5;
    private const double AssumedStudyHoursPerDay = 2.0;
    private const int RecentScrapeWindowDays = 30;
    private const int MinutesPerQuestion = 5;

    // ───────────────────────────────────────────────────────────
    //  Full pipeline: scrape -> classify -> ingest -> analyze -> recommend
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Run the full dynamic content pipeline. Ingests scraped data, analyzes market demand,
    /// detects trends, and produces an adaptive study plan combining all signals.
    /// </summary>
    public async Task<PipelineResult> RunAsync(
        PipelineInput input,
        CancellationToken ct = default)
    {
        logger.LogInformation("Starting dynamic content pipeline for data directory {Dir}", input.DataDirectory);

        // Step 1: Ingest scraped questions if provided
        IngestResult ingestion;
        if (input.ScrapedQuestions is { Count: > 0 })
        {
            logger.LogInformation("Ingesting {Count} scraped questions", input.ScrapedQuestions.Count);
            ingestion = knowledgeBase.IngestQuestions(input.ScrapedQuestions, input.DataDirectory);
        }
        else
        {
            logger.LogInformation("No scraped questions provided, skipping ingestion");
            ingestion = new IngestResult(0, 0, 0, 0, []);
        }

        // Step 2: Analyze market demand from vacancies if provided
        MarketTopicDemand? marketDemand = null;
        if (input.Vacancies is { Count: > 0 })
        {
            logger.LogInformation("Analyzing market demand from {Count} vacancies", input.Vacancies.Count);
            marketDemand = topicInference.AnalyzeMarketDemand(input.Vacancies);
        }
        else
        {
            logger.LogInformation("No vacancies provided, skipping market demand analysis");
        }

        // Step 3: Get trending topics from knowledge base
        var trendingTopics = knowledgeBase.GetTrendingTopics(input.DataDirectory, RecentScrapeWindowDays);

        // Step 4: Generate adaptive study plan
        var studyPlan = GetAdaptiveStudyPlan(input.DataDirectory, input.Profile, marketDemand, trendingTopics);

        // Step 5: Get knowledge base stats
        var stats = knowledgeBase.GetStats(input.DataDirectory);

        var result = new PipelineResult(
            ingestion,
            marketDemand,
            trendingTopics,
            studyPlan,
            stats,
            DateTimeOffset.UtcNow);

        logger.LogInformation(
            "Pipeline complete. Ingested: {Ingested}, Trending: {Trending}, Readiness: {Readiness} ({Verdict})",
            ingestion.NewQuestionsAdded,
            trendingTopics.Count,
            studyPlan.Readiness.OverallScore,
            studyPlan.Readiness.Verdict);

        return result;
    }

    // ───────────────────────────────────────────────────────────
    //  Quick analysis without scraping
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Analyze existing data without performing any scraping. Uses the knowledge base
    /// and progress file already on disk.
    /// </summary>
    public DynamicInsights AnalyzeExisting(string dataDirectory)
    {
        logger.LogInformation("Analyzing existing data in {Dir}", dataDirectory);

        // Load knowledge base stats
        var stats = knowledgeBase.GetStats(dataDirectory);

        // Get trending topics
        var trendingTopics = knowledgeBase.GetTrendingTopics(dataDirectory, RecentScrapeWindowDays);

        // Detect emerging skills — skills in dynamic/scraped questions not in static bank key concepts
        var emergingSkills = DetectEmergingSkills(dataDirectory);

        // Extract companies most frequently mentioned in scraped questions
        var topCompanies = ExtractTopCompanies(dataDirectory);

        // Load and assess readiness if progress exists
        ReadinessAssessment? readiness = null;
        try
        {
            var progress = progressTracker.LoadProgress(dataDirectory);
            if (progress.TopicProgress.Count > 0 || progress.TotalQuestionsStudied > 0)
            {
                readiness = progressTracker.AssessReadiness(progress);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load progress for readiness assessment");
        }

        logger.LogInformation(
            "Existing data analysis complete: {Emerging} emerging skills, {Companies} top companies, readiness={Readiness}",
            emergingSkills.Count, topCompanies.Count, readiness?.OverallScore.ToString("F1") ?? "N/A");

        return new DynamicInsights(stats, trendingTopics, emergingSkills, topCompanies, readiness);
    }

    // ───────────────────────────────────────────────────────────
    //  Adaptive study plan — the key intelligence method
    // ───────────────────────────────────────────────────────────

    /// <summary>
    /// Produces an adaptive study plan that combines all available signals:
    /// market demand (40%), user weakness (30%), trending topics (20%), coverage gaps (10%).
    /// </summary>
    public AdaptiveStudyPlan GetAdaptiveStudyPlan(
        string dataDirectory,
        UserProfile? profile = null)
    {
        return GetAdaptiveStudyPlan(dataDirectory, profile, marketDemand: null, trendingTopics: null);
    }

    private AdaptiveStudyPlan GetAdaptiveStudyPlan(
        string dataDirectory,
        UserProfile? profile,
        MarketTopicDemand? marketDemand,
        IReadOnlyList<TrendingTopic>? trendingTopics)
    {
        logger.LogInformation("Generating adaptive study plan");

        var allTopics = InterviewTopicBank.GetAllTopics();
        var progress = progressTracker.LoadProgress(dataDirectory);

        // If no trending topics provided, try loading from KB
        trendingTopics ??= knowledgeBase.GetTrendingTopics(dataDirectory, RecentScrapeWindowDays);

        // Build priority scores per topic
        var priorities = new List<StudyPriority>();

        for (var i = 0; i < allTopics.Count; i++)
        {
            var bankTopic = allTopics[i];

            // Market demand weight (40%)
            double marketScore;
            if (marketDemand is not null)
            {
                var demandEntry = marketDemand.TopicRankings
                    .FirstOrDefault(d => string.Equals(d.TopicId, bankTopic.Id, StringComparison.OrdinalIgnoreCase));

                marketScore = demandEntry is not null
                    ? demandEntry.PercentageOfVacancies
                    : 0;
            }
            else
            {
                // Use static ordering as proxy — earlier topics have higher demand
                marketScore = (double)(allTopics.Count - i) / allTopics.Count * 100.0;
            }

            // Weakness weight (30%) — inverse of topic strength
            double weaknessScore;
            if (progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp))
            {
                var coverage = tp.TotalQuestionsAvailable > 0
                    ? (double)tp.QuestionsStudied / tp.TotalQuestionsAvailable * 100.0
                    : 0;

                var quizScore = tp.QuizAttempts > 0 ? tp.QuizAccuracy : 0;
                var confidenceScore = tp.SelfConfidence / 5.0 * 100.0;
                var strength = coverage * 0.4 + quizScore * 0.3 + confidenceScore * 0.3;
                weaknessScore = 100 - strength;
            }
            else
            {
                weaknessScore = 100; // Never studied — maximum weakness
            }

            // Trending weight (20%) — how many new questions scraped recently
            var trendingEntry = trendingTopics
                .FirstOrDefault(t => string.Equals(t.TopicId, bankTopic.Id, StringComparison.OrdinalIgnoreCase));

            var trendingScore = trendingEntry is not null
                ? Math.Min(trendingEntry.NewQuestionsLast30Days * 10.0, 100.0) // cap at 100
                : 0;

            // Coverage gap weight (10%) — percentage of questions not yet studied
            double coverageGapScore;
            if (progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp2))
            {
                coverageGapScore = tp2.TotalQuestionsAvailable > 0
                    ? (1.0 - (double)tp2.QuestionsStudied / tp2.TotalQuestionsAvailable) * 100.0
                    : 100;
            }
            else
            {
                coverageGapScore = 100;
            }

            // Combined priority score
            var priorityScore =
                marketScore * 0.4 +
                weaknessScore * 0.3 +
                trendingScore * 0.2 +
                coverageGapScore * 0.1;

            priorityScore = Math.Min(priorityScore, 100);

            // Determine reason
            var reason = DeterminePriorityReason(marketScore, weaknessScore, trendingScore, coverageGapScore);

            // Questions remaining
            var questionsRemaining = bankTopic.Questions.Count;
            if (progress.TopicProgress.TryGetValue(bankTopic.Id, out var tp3))
            {
                questionsRemaining = Math.Max(0, tp3.TotalQuestionsAvailable - tp3.QuestionsStudied);
            }

            // Estimated hours: ~5 minutes per question
            var estimatedHours = (int)Math.Ceiling(questionsRemaining * MinutesPerQuestion / 60.0);

            priorities.Add(new StudyPriority(
                bankTopic.Id,
                bankTopic.Name,
                Math.Round(priorityScore, 1),
                reason,
                questionsRemaining,
                estimatedHours));
        }

        // Sort by priority score descending
        priorities.Sort((a, b) => b.PriorityScore.CompareTo(a.PriorityScore));

        // Generate study session for the top priority topic
        var topTopic = priorities.Count > 0 ? priorities[0].TopicId : null;
        var nextSession = progressTracker.GenerateStudySession(
            progress, dataDirectory, questionCount: 10, focusTopic: topTopic);

        // Assess readiness
        var readiness = progressTracker.AssessReadiness(progress);

        // Get resource recommendations for top 3 priority topics
        var topGapSkills = priorities
            .Take(3)
            .Select(p => p.TopicName)
            .ToList();

        var resourcePlan = resourceEngine.RecommendForGaps(topGapSkills);
        var resources = resourcePlan.Recommendations;

        // Calculate estimated days to ready
        var totalRemainingHours = priorities.Sum(p => p.EstimatedHours);
        var estimatedDaysToReady = totalRemainingHours > 0
            ? (int)Math.Ceiling(totalRemainingHours / AssumedStudyHoursPerDay)
            : 0;

        // Daily goal
        var dailyQuestions = priorities.Count > 0 && priorities[0].QuestionsRemaining > 0
            ? Math.Min(MaxDailyQuestions, Math.Max(DefaultDailyQuestions, priorities[0].QuestionsRemaining / 10))
            : DefaultDailyQuestions;

        var dailyTopicName = priorities.Count > 0 ? priorities[0].TopicName : "general review";
        var dailyGoal = $"Study {dailyQuestions} questions on '{dailyTopicName}' today";

        logger.LogInformation(
            "Adaptive study plan generated: {Priorities} topics prioritized, top={Top}, readiness={Score} ({Verdict}), est. {Days} days to ready",
            priorities.Count, topTopic ?? "none", readiness.OverallScore, readiness.Verdict, estimatedDaysToReady);

        return new AdaptiveStudyPlan(
            priorities,
            nextSession,
            readiness,
            resources,
            dailyGoal,
            estimatedDaysToReady);
    }

    // ───────────────────────────────────────────────────────────
    //  Private helpers
    // ───────────────────────────────────────────────────────────

    private List<string> DetectEmergingSkills(string dataDirectory)
    {
        // Get all key concepts from the static bank
        var staticConcepts = InterviewTopicBank.GetAllTopics()
            .SelectMany(t => t.KeyConcepts)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Get skills/tags from scraped questions in the knowledge base
        var scrapedSkills = knowledgeBase.GetScrapedSkills(dataDirectory);

        // Emerging = in scraped data but NOT in static bank
        var emerging = scrapedSkills
            .Where(skill => !staticConcepts.Contains(skill))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogInformation("Detected {Count} emerging skills not in static bank", emerging.Count);
        return emerging;
    }

    private List<string> ExtractTopCompanies(string dataDirectory)
    {
        var companyCounts = knowledgeBase.GetCompanyMentions(dataDirectory);

        var topCompanies = companyCounts
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => kv.Key)
            .ToList();

        logger.LogInformation("Top companies from scraped questions: {Companies}",
            string.Join(", ", topCompanies));

        return topCompanies;
    }

    private static string DeterminePriorityReason(
        double marketScore,
        double weaknessScore,
        double trendingScore,
        double coverageGapScore)
    {
        var maxSignal = Math.Max(Math.Max(marketScore, weaknessScore), Math.Max(trendingScore, coverageGapScore));

        return maxSignal switch
        {
            _ when maxSignal == marketScore && marketScore > 50 =>
                "High market demand — frequently required in job vacancies",
            _ when maxSignal == weaknessScore && weaknessScore > 70 =>
                "Significant weakness — low quiz accuracy or confidence",
            _ when maxSignal == trendingScore && trendingScore > 50 =>
                "Trending topic — many new questions scraped recently",
            _ when maxSignal == coverageGapScore && coverageGapScore > 70 =>
                "Low coverage — many questions remain unstudied",
            _ when weaknessScore > 80 =>
                "Not yet started — needs initial study",
            _ => "Balanced priority across demand, weakness, and coverage",
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────
//  Pipeline data models
// ─────────────────────────────────────────────────────────────────────────

public sealed record PipelineInput(
    string DataDirectory,
    IReadOnlyList<ScrapedInterviewQuestion>? ScrapedQuestions = null,
    IReadOnlyList<JobVacancy>? Vacancies = null,
    UserProfile? Profile = null);

public sealed record PipelineResult(
    IngestResult Ingestion,
    MarketTopicDemand? MarketDemand,
    IReadOnlyList<TrendingTopic> TrendingTopics,
    AdaptiveStudyPlan StudyPlan,
    KnowledgeBaseStats Stats,
    DateTimeOffset GeneratedDate);

public sealed record DynamicInsights(
    KnowledgeBaseStats Stats,
    IReadOnlyList<TrendingTopic> TrendingTopics,
    IReadOnlyList<string> EmergingSkills,
    IReadOnlyList<string> TopCompaniesAsking,
    ReadinessAssessment? Readiness);

public sealed record AdaptiveStudyPlan(
    IReadOnlyList<StudyPriority> Priorities,
    StudySession NextSession,
    ReadinessAssessment Readiness,
    IReadOnlyList<ResourceRecommendation> Resources,
    string DailyGoal,
    int EstimatedDaysToReady);

public sealed record StudyPriority(
    string TopicId,
    string TopicName,
    double PriorityScore,
    string Reason,
    int QuestionsRemaining,
    int EstimatedHours);
