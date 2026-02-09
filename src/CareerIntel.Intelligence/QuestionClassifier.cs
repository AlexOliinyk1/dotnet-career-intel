namespace CareerIntel.Intelligence;

using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

// ─────────────────────────────────────────────────────────────
//  Result records
// ─────────────────────────────────────────────────────────────

public sealed record ClassifiedQuestion(
    ScrapedInterviewQuestion Original,
    string TopicId,
    string TopicName,
    double Confidence,
    string InferredDifficulty,
    IReadOnlyList<string> InferredTags,
    bool IsNovel);

// ─────────────────────────────────────────────────────────────
//  QuestionClassifier — classifies scraped interview questions
//  into topic areas using keyword matching.
// ─────────────────────────────────────────────────────────────

public sealed class QuestionClassifier(ILogger<QuestionClassifier> logger)
{
    // ─────────────────────────────────────────────────────────────
    //  Keyword-to-topic mapping (focused on question text analysis)
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, QuestionTopicEntry> TopicKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["algorithms"] = new("Algorithms & Data Structures",
        [
            "algorithm", "big-o", "data structure", "binary search", "sorting",
            "hash", "tree", "graph", "leetcode", "dynamic programming",
            "linked list", "array"
        ]),

        ["system-design"] = new("System Design",
        [
            "system design", "distributed", "microservice", "CQRS", "event sourcing",
            "load balancer", "scalab", "message queue", "kafka", "rabbitmq"
        ]),

        ["dotnet-internals"] = new(".NET Internals",
        [
            "CLR", "GC", "garbage collection", "JIT", "IL", "Span", "memory",
            "ref struct", "boxing", "value type", "assembly", "reflection",
            "expression tree"
        ]),

        ["backend-architecture"] = new("Backend Architecture",
        [
            "ASP.NET", "middleware", "minimal API", "controller", "REST", "gRPC",
            "GraphQL", "clean architecture", "DDD", "MediatR", "API gateway"
        ]),

        ["databases"] = new("Databases",
        [
            "SQL", "PostgreSQL", "index", "query", "transaction", "ACID", "NoSQL",
            "MongoDB", "Redis", "deadlock", "normalization", "stored procedure"
        ]),

        ["orm-data-access"] = new("ORM & Data Access",
        [
            "Entity Framework", "EF Core", "Dapper", "LINQ", "IQueryable",
            "DbContext", "migration", "lazy loading", "repository", "unit of work",
            "N+1"
        ]),

        ["performance"] = new("Performance",
        [
            "performance", "profiling", "benchmark", "memory leak", "allocation",
            "caching", "Span", "ArrayPool", "hot path"
        ]),

        ["concurrency"] = new("Concurrency",
        [
            "async", "await", "Task", "thread", "parallel", "lock", "semaphore",
            "channel", "concurrent", "deadlock async"
        ]),

        ["cloud"] = new("Cloud & Infrastructure",
        [
            "Azure", "AWS", "cloud", "Kubernetes", "Docker", "serverless",
            "Service Bus", "Key Vault", "Terraform"
        ]),

        ["security"] = new("Security",
        [
            "OWASP", "authentication", "authorization", "OAuth", "JWT", "CSRF",
            "XSS", "SQL injection", "encryption"
        ]),

        ["testing"] = new("Testing",
        [
            "test", "unit test", "integration test", "xUnit", "Moq", "TDD",
            "BDD", "code coverage", "Testcontainers"
        ]),

        ["frontend"] = new("Frontend",
        [
            "Blazor", "React", "Angular", "TypeScript", "SignalR", "JavaScript", "CSS"
        ]),

        ["devops"] = new("DevOps",
        [
            "Docker", "CI/CD", "Kubernetes", "GitHub Actions", "monitoring",
            "logging", "OpenTelemetry", "Prometheus"
        ]),

        ["behavioral"] = new("Behavioral",
        [
            "leadership", "mentoring", "agile", "scrum", "estimation", "conflict",
            "code review", "team"
        ]),
    };

    // ─────────────────────────────────────────────────────────────
    //  Difficulty keywords
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, List<string>> DifficultyKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Senior"] = ["senior", "lead", "principal", "architect", "staff"],
        ["Junior"] = ["junior", "entry", "beginner", "intern", "graduate"],
    };

    private const double DuplicateJaccardThreshold = 0.6;

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Classify a single scraped question into a topic area, infer difficulty and tags.
    /// </summary>
    public ClassifiedQuestion Classify(ScrapedInterviewQuestion raw)
    {
        var corpus = BuildCorpus(raw);

        // Find the best matching topic.
        var (topicId, topicName, confidence, matchedKeywords) = FindBestTopic(corpus);

        // Infer difficulty level from question text and seniority context.
        var difficulty = InferDifficulty(corpus, raw.SeniorityContext);

        // Extract tags — all matched keywords plus any existing tags from the raw question.
        var tags = ExtractTags(matchedKeywords, raw.Tags);

        logger.LogDebug(
            "Classified question {QuestionId} -> topic={TopicId} confidence={Confidence:F1} difficulty={Difficulty}",
            raw.Id, topicId, confidence, difficulty);

        return new ClassifiedQuestion(
            Original: raw,
            TopicId: topicId,
            TopicName: topicName,
            Confidence: confidence,
            InferredDifficulty: difficulty,
            InferredTags: tags,
            IsNovel: true); // Novelty is determined externally via IsDuplicate
    }

    /// <summary>
    /// Batch classify a list of scraped questions.
    /// </summary>
    public List<ClassifiedQuestion> ClassifyBatch(IReadOnlyList<ScrapedInterviewQuestion> questions)
    {
        logger.LogInformation("Classifying batch of {Count} scraped questions", questions.Count);

        var results = new List<ClassifiedQuestion>(questions.Count);

        foreach (var question in questions)
        {
            try
            {
                results.Add(Classify(question));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to classify question {QuestionId}, skipping", question.Id);
            }
        }

        logger.LogInformation(
            "Batch classification complete: {Classified}/{Total} classified",
            results.Count, questions.Count);

        return results;
    }

    /// <summary>
    /// Detect if a scraped question is a duplicate of any existing InterviewTopicBank question,
    /// using Jaccard similarity of word tokens (threshold = 0.6).
    /// </summary>
    public bool IsDuplicate(ScrapedInterviewQuestion question, IReadOnlyList<InterviewQuestion> existing)
    {
        var questionWords = Tokenize(question.Question);

        if (questionWords.Count == 0)
            return false;

        foreach (var existingQuestion in existing)
        {
            var existingWords = Tokenize(existingQuestion.Question);

            if (existingWords.Count == 0)
                continue;

            var similarity = JaccardSimilarity(questionWords, existingWords);

            if (similarity >= DuplicateJaccardThreshold)
            {
                logger.LogDebug(
                    "Duplicate detected: scraped={ScrapedId} matches existing question (Jaccard={Similarity:F3})",
                    question.Id, similarity);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a scraped question is a duplicate of any classified (dynamic) question,
    /// using Jaccard similarity of word tokens (threshold = 0.6).
    /// </summary>
    public bool IsDuplicateOfClassified(ScrapedInterviewQuestion question, IReadOnlyList<ClassifiedQuestion> existing)
    {
        var questionWords = Tokenize(question.Question);

        if (questionWords.Count == 0)
            return false;

        foreach (var classified in existing)
        {
            var existingWords = Tokenize(classified.Original.Question);

            if (existingWords.Count == 0)
                continue;

            var similarity = JaccardSimilarity(questionWords, existingWords);

            if (similarity >= DuplicateJaccardThreshold)
            {
                logger.LogDebug(
                    "Duplicate detected: scraped={ScrapedId} matches dynamic question (Jaccard={Similarity:F3})",
                    question.Id, similarity);
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────

    private static string BuildCorpus(ScrapedInterviewQuestion raw)
    {
        return string.Join(" ",
            raw.Question,
            raw.TopicArea,
            raw.BestAnswer,
            string.Join(" ", raw.Tags),
            raw.SeniorityContext,
            raw.Company).Trim();
    }

    private static (string TopicId, string TopicName, double Confidence, List<string> MatchedKeywords) FindBestTopic(string corpus)
    {
        var bestTopicId = "unknown";
        var bestTopicName = "Unclassified";
        var bestScore = 0;
        var bestKeywords = new List<string>();

        foreach (var (topicId, entry) in TopicKeywords)
        {
            var matchedKeywords = new List<string>();

            foreach (var keyword in entry.Keywords)
            {
                if (ContainsKeyword(corpus, keyword))
                    matchedKeywords.Add(keyword);
            }

            if (matchedKeywords.Count > bestScore)
            {
                bestScore = matchedKeywords.Count;
                bestTopicId = topicId;
                bestTopicName = entry.DisplayName;
                bestKeywords = matchedKeywords;
            }
        }

        // Confidence: ratio of matched keywords to a reasonable threshold, scaled to 0-100.
        var confidence = bestScore == 0
            ? 0.0
            : Math.Min((double)bestScore / Math.Min(bestKeywords.Count + 2, 8) * 100.0, 100.0);

        return (bestTopicId, bestTopicName, confidence, bestKeywords);
    }

    private static string InferDifficulty(string corpus, string seniorityContext)
    {
        // Check explicit seniority context first.
        var combinedText = string.IsNullOrWhiteSpace(seniorityContext)
            ? corpus
            : $"{seniorityContext} {corpus}";

        // Check for senior-level signals.
        foreach (var keyword in DifficultyKeywords["Senior"])
        {
            if (ContainsKeyword(combinedText, keyword))
                return "Senior";
        }

        // Check for junior-level signals.
        foreach (var keyword in DifficultyKeywords["Junior"])
        {
            if (ContainsKeyword(combinedText, keyword))
                return "Junior";
        }

        // Default to Mid level.
        return "Mid";
    }

    private static IReadOnlyList<string> ExtractTags(List<string> matchedKeywords, List<string> existingTags)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in matchedKeywords)
            tags.Add(keyword);

        foreach (var tag in existingTags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                tags.Add(tag);
        }

        return tags.Order().ToList();
    }

    private static bool ContainsKeyword(string corpus, string keyword)
    {
        if (string.IsNullOrWhiteSpace(corpus) || string.IsNullOrWhiteSpace(keyword))
            return false;

        // For short keywords (3 chars or less like "GC", "IL", "DI"),
        // use word-boundary matching to avoid false positives.
        if (keyword.Length <= 3)
        {
            var pattern = $@"\b{Regex.Escape(keyword)}\b";
            return Regex.IsMatch(corpus, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }

        return corpus.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Split on non-alphanumeric characters, lowercase, and deduplicate.
        var words = Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(w => w.Length > 1) // Skip single-char tokens
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return words;
    }

    private static double JaccardSimilarity(HashSet<string> setA, HashSet<string> setB)
    {
        var intersectionCount = setA.Count(word => setB.Contains(word));
        var unionCount = setA.Count + setB.Count - intersectionCount;

        return unionCount == 0 ? 0.0 : (double)intersectionCount / unionCount;
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal record
    // ─────────────────────────────────────────────────────────────

    private sealed record QuestionTopicEntry(string DisplayName, List<string> Keywords);
}
