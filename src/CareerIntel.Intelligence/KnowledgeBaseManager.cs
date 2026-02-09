namespace CareerIntel.Intelligence;

using System.Text.Json;
using System.Text.Json.Serialization;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────
//  Result records
// ─────────────────────────────────────────────────────────────

public sealed record KnowledgeBase(
    IReadOnlyList<KnowledgeTopic> Topics,
    int StaticQuestionCount,
    int DynamicQuestionCount,
    DateTimeOffset LastUpdated);

public sealed record KnowledgeTopic(
    string TopicId,
    string TopicName,
    IReadOnlyList<KnowledgeQuestion> Questions,
    IReadOnlyList<string> KeyConcepts,
    int StaticCount,
    int DynamicCount);

public sealed record KnowledgeQuestion(
    string Question,
    string Answer,
    string Difficulty,
    IReadOnlyList<string> Tags,
    string Source,
    string SourceUrl,
    int Upvotes,
    string Company,
    DateTimeOffset? ScrapedDate);

public sealed record IngestResult(
    int TotalProcessed,
    int NewQuestionsAdded,
    int DuplicatesSkipped,
    int UnclassifiedSkipped,
    IReadOnlyList<string> TopicsEnriched);

public sealed record TrendingTopic(
    string TopicId,
    string TopicName,
    int NewQuestionsLast30Days,
    int TotalQuestions,
    double GrowthRate);

public sealed record KnowledgeBaseStats(
    int TotalTopics,
    int TotalQuestions,
    int StaticQuestions,
    int DynamicQuestions,
    IReadOnlyList<TopicQuestionCount> ByTopic,
    DateTimeOffset LastScrapedDate,
    IReadOnlyList<string> Sources);

public sealed record TopicQuestionCount(
    string TopicId,
    string TopicName,
    int Count);

// ─────────────────────────────────────────────────────────────
//  KnowledgeBaseManager — manages a combined knowledge base of
//  static (InterviewTopicBank) + dynamic (scraped) questions.
// ─────────────────────────────────────────────────────────────

public sealed class KnowledgeBaseManager(
    QuestionClassifier classifier,
    ILogger<KnowledgeBaseManager> logger)
{
    private const string DynamicStoreFileName = "knowledge-base.json";
    private const double MinClassificationConfidence = 30.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the full merged knowledge base (static + dynamic).
    /// </summary>
    public KnowledgeBase GetKnowledgeBase(string dataDirectory)
    {
        var staticTopics = InterviewTopicBank.GetAllTopics();
        var dynamicQuestions = LoadDynamicStore(dataDirectory);

        var topics = MergeTopics(staticTopics, dynamicQuestions);

        var staticCount = staticTopics.Sum(t => t.Questions.Count);
        var dynamicCount = dynamicQuestions.Count;

        var lastUpdated = dynamicQuestions.Count > 0
            ? dynamicQuestions.Max(q => q.Original.ScrapedDate)
            : DateTimeOffset.MinValue;

        logger.LogInformation(
            "Knowledge base loaded: {TopicCount} topics, {StaticCount} static + {DynamicCount} dynamic questions",
            topics.Count, staticCount, dynamicCount);

        return new KnowledgeBase(
            Topics: topics,
            StaticQuestionCount: staticCount,
            DynamicQuestionCount: dynamicCount,
            LastUpdated: lastUpdated);
    }

    /// <summary>
    /// Ingest new scraped questions: classify, deduplicate, add novel ones, and persist.
    /// </summary>
    public IngestResult IngestQuestions(
        IReadOnlyList<ScrapedInterviewQuestion> scraped,
        string dataDirectory)
    {
        logger.LogInformation("Ingesting {Count} scraped questions", scraped.Count);

        var staticTopics = InterviewTopicBank.GetAllTopics();
        var allStaticQuestions = staticTopics.SelectMany(t => t.Questions).ToList();
        var dynamicStore = LoadDynamicStore(dataDirectory);

        var newQuestionsAdded = 0;
        var duplicatesSkipped = 0;
        var unclassifiedSkipped = 0;
        var enrichedTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in scraped)
        {
            // Step 1: Classify the question.
            ClassifiedQuestion classified;
            try
            {
                classified = classifier.Classify(raw);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to classify question {QuestionId}, skipping", raw.Id);
                unclassifiedSkipped++;
                continue;
            }

            // Step 2: Skip questions with low confidence (unclassifiable).
            if (classified.Confidence < MinClassificationConfidence)
            {
                logger.LogDebug(
                    "Question {QuestionId} has low confidence ({Confidence:F1}), skipping",
                    raw.Id, classified.Confidence);
                unclassifiedSkipped++;
                continue;
            }

            // Step 3: Check for duplicates against static bank questions.
            if (classifier.IsDuplicate(raw, allStaticQuestions))
            {
                logger.LogDebug("Question {QuestionId} is a duplicate of a static bank question", raw.Id);
                duplicatesSkipped++;
                continue;
            }

            // Step 4: Check for duplicates against already-ingested dynamic questions.
            if (classifier.IsDuplicateOfClassified(raw, dynamicStore))
            {
                logger.LogDebug("Question {QuestionId} is a duplicate of an existing dynamic question", raw.Id);
                duplicatesSkipped++;
                continue;
            }

            // Step 5: Mark as novel and add to the dynamic store.
            var novelClassified = classified with { IsNovel = true };
            dynamicStore.Add(novelClassified);
            enrichedTopics.Add(classified.TopicId);
            newQuestionsAdded++;

            logger.LogDebug(
                "Added novel question {QuestionId} to topic {TopicId}",
                raw.Id, classified.TopicId);
        }

        // Step 6: Persist the updated dynamic store.
        SaveDynamicStore(dataDirectory, dynamicStore);

        var result = new IngestResult(
            TotalProcessed: scraped.Count,
            NewQuestionsAdded: newQuestionsAdded,
            DuplicatesSkipped: duplicatesSkipped,
            UnclassifiedSkipped: unclassifiedSkipped,
            TopicsEnriched: enrichedTopics.Order().ToList());

        logger.LogInformation(
            "Ingestion complete: {Processed} processed, {Added} added, {Duplicates} duplicates, {Unclassified} unclassified. Topics enriched: [{Topics}]",
            result.TotalProcessed, result.NewQuestionsAdded, result.DuplicatesSkipped,
            result.UnclassifiedSkipped, string.Join(", ", result.TopicsEnriched));

        return result;
    }

    /// <summary>
    /// Get questions for a specific topic (merged static + dynamic).
    /// </summary>
    public List<KnowledgeQuestion> GetQuestionsForTopic(string topicId, string dataDirectory)
    {
        var questions = new List<KnowledgeQuestion>();

        // Add static questions from the topic bank.
        var staticTopic = InterviewTopicBank.GetAllTopics()
            .FirstOrDefault(t => string.Equals(t.Id, topicId, StringComparison.OrdinalIgnoreCase));

        if (staticTopic is not null)
        {
            foreach (var q in staticTopic.Questions)
            {
                questions.Add(ConvertStaticQuestion(q));
            }
        }

        // Add dynamic questions for this topic.
        var dynamicStore = LoadDynamicStore(dataDirectory);

        foreach (var classified in dynamicStore)
        {
            if (string.Equals(classified.TopicId, topicId, StringComparison.OrdinalIgnoreCase))
            {
                questions.Add(ConvertDynamicQuestion(classified));
            }
        }

        logger.LogDebug(
            "Retrieved {Count} questions for topic {TopicId}",
            questions.Count, topicId);

        return questions;
    }

    /// <summary>
    /// Get trending topics — topics with the most new scraped questions recently.
    /// </summary>
    public List<TrendingTopic> GetTrendingTopics(string dataDirectory, int days = 30)
    {
        var dynamicStore = LoadDynamicStore(dataDirectory);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var staticTopics = InterviewTopicBank.GetAllTopics();

        // Count recent questions per topic.
        var recentByTopic = dynamicStore
            .Where(q => q.Original.ScrapedDate >= cutoff)
            .GroupBy(q => q.TopicId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Count total dynamic questions per topic.
        var totalDynamicByTopic = dynamicStore
            .GroupBy(q => q.TopicId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var trending = new List<TrendingTopic>();

        foreach (var (topicId, recentCount) in recentByTopic)
        {
            var staticTopic = staticTopics
                .FirstOrDefault(t => string.Equals(t.Id, topicId, StringComparison.OrdinalIgnoreCase));

            var topicName = staticTopic?.Name ?? topicId;

            var totalDynamic = totalDynamicByTopic.GetValueOrDefault(topicId, 0);
            var staticCount = staticTopic?.Questions.Count ?? 0;
            var totalQuestions = staticCount + totalDynamic;

            // Growth rate: recent new questions as a percentage of total (before the recent additions).
            var baseCount = totalQuestions - recentCount;
            var growthRate = baseCount > 0
                ? (double)recentCount / baseCount * 100.0
                : recentCount > 0 ? 100.0 : 0.0;

            trending.Add(new TrendingTopic(
                TopicId: topicId,
                TopicName: topicName,
                NewQuestionsLast30Days: recentCount,
                TotalQuestions: totalQuestions,
                GrowthRate: Math.Round(growthRate, 1)));
        }

        return trending
            .OrderByDescending(t => t.NewQuestionsLast30Days)
            .ThenByDescending(t => t.GrowthRate)
            .ToList();
    }

    /// <summary>
    /// Get statistics about the knowledge base.
    /// </summary>
    public KnowledgeBaseStats GetStats(string dataDirectory)
    {
        var kb = GetKnowledgeBase(dataDirectory);

        var byTopic = kb.Topics
            .Select(t => new TopicQuestionCount(t.TopicId, t.TopicName, t.Questions.Count))
            .OrderByDescending(t => t.Count)
            .ToList();

        var dynamicStore = LoadDynamicStore(dataDirectory);

        var lastScrapedDate = dynamicStore.Count > 0
            ? dynamicStore.Max(q => q.Original.ScrapedDate)
            : DateTimeOffset.MinValue;

        var sources = dynamicStore
            .Select(q => q.Original.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();

        // Always include "static" as a source.
        if (!sources.Contains("static", StringComparer.OrdinalIgnoreCase))
            sources.Insert(0, "static");

        return new KnowledgeBaseStats(
            TotalTopics: kb.Topics.Count,
            TotalQuestions: kb.StaticQuestionCount + kb.DynamicQuestionCount,
            StaticQuestions: kb.StaticQuestionCount,
            DynamicQuestions: kb.DynamicQuestionCount,
            ByTopic: byTopic,
            LastScrapedDate: lastScrapedDate,
            Sources: sources);
    }

    /// <summary>
    /// Get all unique skills/tags from scraped (dynamic) questions.
    /// </summary>
    public List<string> GetScrapedSkills(string dataDirectory)
    {
        var dynamicStore = LoadDynamicStore(dataDirectory);

        return dynamicStore
            .SelectMany(q => q.InferredTags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();
    }

    /// <summary>
    /// Get company mention counts from scraped questions.
    /// </summary>
    public Dictionary<string, int> GetCompanyMentions(string dataDirectory)
    {
        var dynamicStore = LoadDynamicStore(dataDirectory);

        return dynamicStore
            .Where(q => !string.IsNullOrWhiteSpace(q.Original.Company))
            .GroupBy(q => q.Original.Company, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────
    //  Merging static + dynamic into unified topics
    // ─────────────────────────────────────────────────────────────

    private static List<KnowledgeTopic> MergeTopics(
        IReadOnlyList<TopicArea> staticTopics,
        List<ClassifiedQuestion> dynamicQuestions)
    {
        // Group dynamic questions by topic.
        var dynamicByTopic = dynamicQuestions
            .GroupBy(q => q.TopicId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var mergedTopics = new List<KnowledgeTopic>();
        var coveredTopicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Start with static topics and merge in dynamic questions.
        foreach (var staticTopic in staticTopics)
        {
            coveredTopicIds.Add(staticTopic.Id);

            var questions = new List<KnowledgeQuestion>();

            // Add static questions.
            foreach (var q in staticTopic.Questions)
                questions.Add(ConvertStaticQuestion(q));

            var staticCount = questions.Count;

            // Add dynamic questions for this topic.
            var dynamicCount = 0;
            if (dynamicByTopic.TryGetValue(staticTopic.Id, out var dynamicForTopic))
            {
                foreach (var classified in dynamicForTopic)
                {
                    questions.Add(ConvertDynamicQuestion(classified));
                    dynamicCount++;
                }
            }

            mergedTopics.Add(new KnowledgeTopic(
                TopicId: staticTopic.Id,
                TopicName: staticTopic.Name,
                Questions: questions,
                KeyConcepts: staticTopic.KeyConcepts,
                StaticCount: staticCount,
                DynamicCount: dynamicCount));
        }

        // Add any dynamic-only topics (topics that exist only from scraped data).
        foreach (var (topicId, dynamicForTopic) in dynamicByTopic)
        {
            if (coveredTopicIds.Contains(topicId))
                continue;

            var questions = dynamicForTopic
                .Select(ConvertDynamicQuestion)
                .ToList();

            var topicName = dynamicForTopic.FirstOrDefault()?.TopicName ?? topicId;

            // Extract key concepts from dynamic question tags.
            var keyConcepts = dynamicForTopic
                .SelectMany(q => q.InferredTags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToList();

            mergedTopics.Add(new KnowledgeTopic(
                TopicId: topicId,
                TopicName: topicName,
                Questions: questions,
                KeyConcepts: keyConcepts,
                StaticCount: 0,
                DynamicCount: questions.Count));
        }

        return mergedTopics;
    }

    // ─────────────────────────────────────────────────────────────
    //  Conversion helpers
    // ─────────────────────────────────────────────────────────────

    private static KnowledgeQuestion ConvertStaticQuestion(InterviewQuestion q) =>
        new(
            Question: q.Question,
            Answer: q.ExpectedAnswer,
            Difficulty: q.Difficulty,
            Tags: q.Tags,
            Source: "static",
            SourceUrl: string.Empty,
            Upvotes: 0,
            Company: string.Empty,
            ScrapedDate: null);

    private static KnowledgeQuestion ConvertDynamicQuestion(ClassifiedQuestion classified) =>
        new(
            Question: classified.Original.Question,
            Answer: classified.Original.BestAnswer,
            Difficulty: classified.InferredDifficulty,
            Tags: classified.InferredTags,
            Source: classified.Original.Source,
            SourceUrl: classified.Original.SourceUrl,
            Upvotes: classified.Original.Upvotes,
            Company: classified.Original.Company,
            ScrapedDate: classified.Original.ScrapedDate);

    // ─────────────────────────────────────────────────────────────
    //  Persistence — JSON file for dynamic question store
    // ─────────────────────────────────────────────────────────────

    private List<ClassifiedQuestion> LoadDynamicStore(string dataDirectory)
    {
        var filePath = Path.Combine(dataDirectory, DynamicStoreFileName);

        if (!File.Exists(filePath))
        {
            logger.LogDebug("No dynamic store found at {Path}, starting with empty store", filePath);
            return [];
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var store = JsonSerializer.Deserialize<List<ClassifiedQuestion>>(json, JsonOptions);

            logger.LogDebug("Loaded {Count} dynamic questions from {Path}", store?.Count ?? 0, filePath);

            return store ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load dynamic store from {Path}, starting with empty store", filePath);
            return [];
        }
    }

    private void SaveDynamicStore(string dataDirectory, List<ClassifiedQuestion> store)
    {
        Directory.CreateDirectory(dataDirectory);

        var filePath = Path.Combine(dataDirectory, DynamicStoreFileName);

        try
        {
            var json = JsonSerializer.Serialize(store, JsonOptions);
            File.WriteAllText(filePath, json);

            logger.LogDebug("Saved {Count} dynamic questions to {Path}", store.Count, filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save dynamic store to {Path}", filePath);
            throw;
        }
    }
}
