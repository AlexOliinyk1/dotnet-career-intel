using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes Reddit for .NET and C# interview questions using Reddit's public JSON
/// API (no authentication required). Searches across r/dotnet, r/csharp, and
/// r/ExperiencedDevs for interview-related posts and extracts questions from
/// titles, self-text, and top-voted comments.
/// </summary>
/// <remarks>
/// This scraper extends <see cref="BaseScraper"/> for HTTP infrastructure and
/// rate limiting but does NOT function as a job scraper. The abstract
/// <see cref="IJobScraper"/> methods return empty results. Instead, use
/// <see cref="ScrapeInterviewQuestionsAsync"/> to collect interview content.
/// <para>
/// Reddit requires a descriptive User-Agent header for non-authenticated API
/// access and rate-limits at approximately 60 requests per minute.
/// </para>
/// </remarks>
public sealed partial class RedditScraper(HttpClient httpClient, ILogger<RedditScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string RedditBase = "https://www.reddit.com";
    private const string UserAgent = "CareerIntel/1.0 (.NET Career Intelligence)";
    private const int MinPostScore = 5;

    public override string PlatformName => "Reddit";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Subreddits and their corresponding search queries for interview content.
    /// </summary>
    private static readonly (string Subreddit, string Query)[] SearchTargets =
    [
        ("dotnet", "interview questions"),
        ("dotnet", "interview tips senior"),
        ("csharp", "interview questions"),
        ("csharp", "technical interview"),
        ("ExperiencedDevs", ".NET interview"),
        ("ExperiencedDevs", "C# interview"),
    ];

    // ── IJobScraper — not applicable for interview question scraping ────

    public override Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        // This scraper collects interview questions, not job vacancies.
        return Task.FromResult<IReadOnlyList<JobVacancy>>([]);
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // Not applicable — interview questions have no vacancy detail page.
        return Task.FromResult<JobVacancy?>(null);
    }

    // ── Interview question scraping ────────────────────────────────────

    /// <summary>
    /// Searches Reddit for interview-related posts across .NET-focused subreddits,
    /// extracts questions from post titles and bodies, and fetches top comments
    /// for best answers.
    /// </summary>
    /// <param name="maxPages">Maximum number of result pages per search query (25 results each).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deduplicated list of scraped interview questions.</returns>
    public async Task<IReadOnlyList<ScrapedInterviewQuestion>> ScrapeInterviewQuestionsAsync(
        int maxPages = 3, CancellationToken ct = default)
    {
        ConfigureRedditHeaders();

        var allQuestions = new List<ScrapedInterviewQuestion>();
        var seenPostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (subreddit, query) in SearchTargets)
        {
            ct.ThrowIfCancellationRequested();

            string? after = null;

            for (var page = 0; page < maxPages; page++)
            {
                ct.ThrowIfCancellationRequested();

                var searchUrl = BuildSearchUrl(subreddit, query, after);

                logger.LogDebug("[{Platform}] Searching r/{Subreddit}: {Url}",
                    PlatformName, subreddit, searchUrl);

                var listing = await FetchJsonAsync<RedditListing>(searchUrl, ct);
                if (listing?.Data?.Children is null or { Count: 0 })
                    break;

                foreach (var child in listing.Data.Children)
                {
                    var post = child.Data;
                    if (post is null) continue;

                    // Skip duplicates (same post may appear in multiple searches)
                    if (!seenPostIds.Add(post.Id ?? string.Empty))
                        continue;

                    // Skip low-quality posts
                    if (post.Score < MinPostScore)
                        continue;

                    try
                    {
                        var questions = await ProcessPostAsync(post, subreddit, ct);
                        allQuestions.AddRange(questions);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex,
                            "[{Platform}] Failed to process post {Id} in r/{Subreddit}",
                            PlatformName, post.Id, subreddit);
                    }
                }

                // Pagination: Reddit uses "after" tokens
                after = listing.Data.After;
                if (string.IsNullOrEmpty(after))
                    break;
            }
        }

        // Deduplicate by question text similarity
        var deduplicated = DeduplicateQuestions(allQuestions);

        logger.LogInformation(
            "[{Platform}] Extracted {Total} questions, {Unique} unique after deduplication",
            PlatformName, allQuestions.Count, deduplicated.Count);

        return deduplicated;
    }

    // ── Private helpers ────────────────────────────────────────────────

    /// <summary>
    /// Sets the Reddit-required User-Agent header and Accept for JSON.
    /// </summary>
    private void ConfigureRedditHeaders()
    {
        // Reddit blocks requests with default or missing User-Agent headers.
        // Remove any existing User-Agent and set our custom one.
        if (httpClient.DefaultRequestHeaders.UserAgent.Count > 0)
            httpClient.DefaultRequestHeaders.UserAgent.Clear();

        httpClient.DefaultRequestHeaders.Remove("User-Agent");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);

        // Ensure we accept JSON
        if (!httpClient.DefaultRequestHeaders.Contains("Accept"))
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    private static string BuildSearchUrl(string subreddit, string query, string? after)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"{RedditBase}/r/{subreddit}/search.json?q={encoded}&sort=relevance&restrict_sr=on&limit=25";

        if (!string.IsNullOrEmpty(after))
            url += $"&after={after}";

        return url;
    }

    /// <summary>
    /// Fetches JSON from a URL using the shared HttpClient with rate limiting.
    /// </summary>
    private async Task<T?> FetchJsonAsync<T>(string url, CancellationToken ct) where T : class
    {
        await Task.Delay(RequestDelay, ct);

        try
        {
            var response = await httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[{Platform}] HTTP {Status} for {Url}",
                    PlatformName, response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[{Platform}] Request failed for {Url}", PlatformName, url);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[{Platform}] JSON parse error for {Url}", PlatformName, url);
            return null;
        }
    }

    /// <summary>
    /// Processes a single Reddit post: extracts questions from the title and body,
    /// then fetches top comments for best answers.
    /// </summary>
    private async Task<List<ScrapedInterviewQuestion>> ProcessPostAsync(
        RedditPostData post, string subreddit, CancellationToken ct)
    {
        var questions = new List<ScrapedInterviewQuestion>();
        var postUrl = $"{RedditBase}{post.Permalink}";
        var postedDate = post.CreatedUtc > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)post.CreatedUtc)
            : (DateTimeOffset?)null;

        var contextText = $"{post.Title} {post.Selftext}";

        // Extract questions from the title
        if (IsQuestion(post.Title))
        {
            var bestAnswer = await FetchBestCommentAsync(post, subreddit, ct);

            questions.Add(BuildQuestion(
                questionText: post.Title ?? string.Empty,
                sourceUrl: postUrl,
                contextText: contextText,
                subreddit: subreddit,
                upvotes: post.Score,
                bestAnswer: bestAnswer,
                postedDate: postedDate));
        }

        // Extract questions from the self-text body
        if (!string.IsNullOrWhiteSpace(post.Selftext))
        {
            var bodyQuestions = ExtractQuestionLines(post.Selftext);

            // Only fetch best comment once (reuse for all questions from same post)
            string? bestComment = null;
            if (bodyQuestions.Count > 0)
            {
                bestComment = await FetchBestCommentAsync(post, subreddit, ct);
            }

            foreach (var q in bodyQuestions)
            {
                questions.Add(BuildQuestion(
                    questionText: q,
                    sourceUrl: postUrl,
                    contextText: contextText,
                    subreddit: subreddit,
                    upvotes: post.Score,
                    bestAnswer: bestComment ?? string.Empty,
                    postedDate: postedDate));
            }
        }

        // If no explicit questions were found but the post is interview-related
        // and has a good title, treat the title as an implicit question
        if (questions.Count == 0 && IsInterviewRelated(post.Title))
        {
            var bestAnswer = await FetchBestCommentAsync(post, subreddit, ct);

            questions.Add(BuildQuestion(
                questionText: post.Title ?? "Untitled",
                sourceUrl: postUrl,
                contextText: contextText,
                subreddit: subreddit,
                upvotes: post.Score,
                bestAnswer: bestAnswer,
                postedDate: postedDate));
        }

        return questions;
    }

    /// <summary>
    /// Fetches the top-voted comment from a Reddit post's comment thread.
    /// </summary>
    private async Task<string> FetchBestCommentAsync(
        RedditPostData post, string subreddit, CancellationToken ct)
    {
        if (post.NumComments <= 0 || string.IsNullOrEmpty(post.Id))
            return string.Empty;

        var commentsUrl = $"{RedditBase}/r/{subreddit}/comments/{post.Id}.json?sort=top&limit=5";

        try
        {
            await Task.Delay(RequestDelay, ct);
            var httpResponse = await httpClient.GetAsync(commentsUrl, ct);
            if (!httpResponse.IsSuccessStatusCode)
                return string.Empty;

            var jsonString = await httpResponse.Content.ReadAsStringAsync(ct);
            var root = JsonSerializer.Deserialize<JsonElement>(jsonString);

            // Reddit comment API returns an array of two listings:
            // [0] = the post itself, [1] = comments
            if (root.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var arrayLength = root.GetArrayLength();
            if (arrayLength < 2)
                return string.Empty;

            var commentsListing = root[1];

            if (!commentsListing.TryGetProperty("data", out var commentsData) ||
                !commentsData.TryGetProperty("children", out var children))
                return string.Empty;

            // Find the top-scored comment
            string bestBody = string.Empty;
            int bestScore = 0;

            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetProperty("data", out var commentData))
                    continue;

                if (!commentData.TryGetProperty("body", out var bodyProp))
                    continue;

                var body = bodyProp.GetString();
                if (string.IsNullOrWhiteSpace(body))
                    continue;

                var score = 0;
                if (commentData.TryGetProperty("score", out var scoreProp) &&
                    scoreProp.ValueKind == JsonValueKind.Number)
                {
                    score = scoreProp.GetInt32();
                }

                if (score > bestScore || string.IsNullOrEmpty(bestBody))
                {
                    bestScore = score;
                    bestBody = body;
                }
            }

            // Truncate very long answers to keep data manageable
            if (bestBody.Length > 2000)
                bestBody = bestBody[..2000] + "...";

            return bestBody;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "[{Platform}] Could not fetch comments for post {Id}",
                PlatformName, post.Id);
            return string.Empty;
        }
    }

    /// <summary>
    /// Builds a <see cref="ScrapedInterviewQuestion"/> from raw extracted data.
    /// </summary>
    private ScrapedInterviewQuestion BuildQuestion(
        string questionText,
        string sourceUrl,
        string contextText,
        string subreddit,
        int upvotes,
        string bestAnswer,
        DateTimeOffset? postedDate)
    {
        var normalized = NormalizeWhitespace(questionText);

        return new ScrapedInterviewQuestion
        {
            Id = GenerateQuestionId(normalized),
            Source = $"reddit-{subreddit.ToLowerInvariant()}",
            SourceUrl = sourceUrl,
            Question = normalized,
            TopicArea = DetectTopicArea(normalized),
            Tags = DetectTags(normalized + " " + contextText),
            BestAnswer = bestAnswer,
            Upvotes = upvotes,
            Company = DetectCompany(contextText),
            SeniorityContext = DetectSeniorityContext(contextText),
            ScrapedDate = DateTimeOffset.UtcNow,
            PostedDate = postedDate,
        };
    }

    // ── Question extraction ────────────────────────────────────────────

    /// <summary>
    /// Extracts lines from text that appear to be questions.
    /// </summary>
    private static List<string> ExtractQuestionLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var questions = new List<string>();
        var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip very short or very long lines
            if (line.Length < 15 || line.Length > 500)
                continue;

            // Lines ending with "?" are likely questions
            if (line.EndsWith('?'))
            {
                questions.Add(line);
                continue;
            }

            // Common interview question patterns
            if (QuestionPatternRegex().IsMatch(line))
            {
                questions.Add(line);
            }
        }

        return questions;
    }

    /// <summary>
    /// Determines whether text is likely a question.
    /// </summary>
    private static bool IsQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        return trimmed.EndsWith('?') || QuestionPatternRegex().IsMatch(trimmed);
    }

    /// <summary>
    /// Checks if a post title is interview-related even if not phrased as a question.
    /// </summary>
    private static bool IsInterviewRelated(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        return lower.Contains("interview") || lower.Contains("hiring")
            || lower.Contains("technical screen") || lower.Contains("coding challenge")
            || lower.Contains("whiteboard") || lower.Contains("take-home")
            || lower.Contains("interview prep") || lower.Contains("interview question");
    }

    // ── Topic and tag detection ────────────────────────────────────────

    private static readonly Dictionary<string, string[]> TopicKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet-internals"] = ["GC", "garbage collector", "CLR", "JIT", "IL",
            "reflection", "AppDomain", "finalizer", "span", "memory",
            "boxing", "unboxing", "struct vs class"],
        ["async-parallel"] = ["async", "await", "Task", "ValueTask", "SynchronizationContext",
            "ConfigureAwait", "Parallel", "Semaphore", "thread", "lock",
            "concurrent", "deadlock", "race condition"],
        ["system-design"] = ["architecture", "microservices", "monolith", "CQRS", "event sourcing",
            "DDD", "domain driven", "scalability", "load balancer", "message queue",
            "distributed", "CAP theorem", "system design"],
        ["databases"] = ["SQL", "index", "transaction", "isolation level",
            "normalization", "stored procedure", "Entity Framework", "EF Core",
            "Dapper", "migration", "ACID", "JOIN"],
        ["web-api"] = ["REST", "HTTP", "middleware", "controller", "API", "gRPC",
            "SignalR", "WebSocket", "authentication", "authorization",
            "JWT", "OAuth", "CORS", "ASP.NET"],
        ["patterns-practices"] = ["SOLID", "dependency injection", "IoC", "pattern",
            "repository", "unit of work", "factory", "observer", "strategy",
            "decorator", "mediator", "clean architecture"],
        ["testing"] = ["unit test", "integration test", "mock", "stub", "xUnit",
            "NUnit", "MSTest", "TDD", "BDD", "code coverage"],
        ["csharp-language"] = ["LINQ", "delegate", "event", "expression tree", "generic",
            "covariance", "contravariance", "nullable", "record", "pattern matching",
            "interface", "abstract", "sealed", "extension method"],
    };

    /// <summary>
    /// Detects the primary topic area of a question based on keyword matching.
    /// </summary>
    private static string DetectTopicArea(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "general";

        var lower = text.ToLowerInvariant();
        var bestTopic = "general";
        var bestScore = 0;

        foreach (var (topic, keywords) in TopicKeywords)
        {
            var score = keywords.Count(kw => lower.Contains(kw.ToLowerInvariant()));
            if (score > bestScore)
            {
                bestScore = score;
                bestTopic = topic;
            }
        }

        return bestTopic;
    }

    /// <summary>
    /// Detects relevant tags from the combined question and context text.
    /// </summary>
    private static List<string> DetectTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lower = text.ToLowerInvariant();
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (lower.Contains("c#") || lower.Contains("csharp")) tags.Add("C#");
        if (lower.Contains(".net") || lower.Contains("dotnet")) tags.Add(".NET");
        if (lower.Contains("asp.net")) tags.Add("ASP.NET");
        if (lower.Contains("ef core") || lower.Contains("entity framework")) tags.Add("EF Core");
        if (lower.Contains("blazor")) tags.Add("Blazor");
        if (lower.Contains("azure")) tags.Add("Azure");
        if (lower.Contains("aws")) tags.Add("AWS");
        if (lower.Contains("docker")) tags.Add("Docker");
        if (lower.Contains("kubernetes") || lower.Contains("k8s")) tags.Add("Kubernetes");
        if (lower.Contains("sql")) tags.Add("SQL");
        if (lower.Contains("redis")) tags.Add("Redis");
        if (lower.Contains("rabbitmq") || lower.Contains("kafka")) tags.Add("Messaging");
        if (lower.Contains("grpc")) tags.Add("gRPC");
        if (lower.Contains("linq")) tags.Add("LINQ");
        if (lower.Contains("async") || lower.Contains("await")) tags.Add("async");
        if (lower.Contains("microservice")) tags.Add("microservices");
        if (lower.Contains("rest") || lower.Contains("web api")) tags.Add("REST");
        if (lower.Contains("signalr")) tags.Add("SignalR");
        if (lower.Contains("solid")) tags.Add("SOLID");
        if (lower.Contains("design pattern") || lower.Contains("pattern")) tags.Add("patterns");
        if (lower.Contains("unit test") || lower.Contains("testing")) tags.Add("testing");
        if (lower.Contains("react") || lower.Contains("angular") || lower.Contains("vue")) tags.Add("frontend");

        return [.. tags];
    }

    /// <summary>
    /// Attempts to detect a company name from context text.
    /// </summary>
    private static string DetectCompany(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Common patterns: "interview at Google", "got asked at Microsoft"
        var patterns = new[]
        {
            @"(?:interview|interviewed|asked)\s+(?:at|by|for|with)\s+([A-Z][\w\.\-]+)",
            @"([A-Z][\w\.\-]+)\s+(?:interview|technical screen|coding challenge)",
            @"(?:got an offer from|applied to|working at)\s+([A-Z][\w\.\-]+)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.None);
            if (match.Success && match.Groups[1].Value.Length >= 2)
            {
                var company = match.Groups[1].Value.Trim();
                if (!IsGenericWord(company))
                    return company;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Detects seniority context from the text.
    /// </summary>
    private static string DetectSeniorityContext(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff engineer"))
            return "principal";
        if (lower.Contains("architect"))
            return "architect";
        if (lower.Contains("lead") || lower.Contains("tech lead") || lower.Contains("team lead"))
            return "lead";
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr "))
            return "senior";
        if (lower.Contains("mid-level") || lower.Contains("mid level") || lower.Contains("middle"))
            return "middle";
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry level"))
            return "junior";
        if (lower.Contains("intern"))
            return "intern";

        return string.Empty;
    }

    private static bool IsGenericWord(string word)
    {
        var generics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "The", "This", "That", "Your", "What", "How", "Why", "Which",
            "NET", "Core", "Web", "API", "Job", "Work", "Dev", "Tech",
            "My", "Any", "Some", "Just", "Here", "About",
        };

        return generics.Contains(word);
    }

    /// <summary>
    /// Generates a deterministic ID for a question based on its normalized text.
    /// </summary>
    private string GenerateQuestionId(string questionText)
    {
        var normalized = questionText.ToLowerInvariant().Trim();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hashHex = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
        return $"{PlatformName.ToLowerInvariant()}:{hashHex}";
    }

    /// <summary>
    /// Deduplicates questions by their normalized text. Keeps the version with
    /// the highest upvote count.
    /// </summary>
    private static List<ScrapedInterviewQuestion> DeduplicateQuestions(
        List<ScrapedInterviewQuestion> questions)
    {
        var grouped = new Dictionary<string, ScrapedInterviewQuestion>(StringComparer.OrdinalIgnoreCase);

        foreach (var q in questions)
        {
            var key = NormalizeForDedup(q.Question);

            if (!grouped.TryGetValue(key, out var existing) || q.Upvotes > existing.Upvotes)
            {
                grouped[key] = q;
            }
        }

        return [.. grouped.Values];
    }

    /// <summary>
    /// Normalizes question text for deduplication.
    /// </summary>
    private static string NormalizeForDedup(string text)
    {
        var lower = text.ToLowerInvariant();
        var noPunctuation = Regex.Replace(lower, @"[^\w\s]", "");
        return NormalizeWhitespace(noPunctuation);
    }

    private static string NormalizeWhitespace(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    [GeneratedRegex(
        @"(?:explain|describe|how does|what is|difference between|tell me about|implement|compare|when would you|how would you|what are|can you explain|walk me through)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QuestionPatternRegex();

    // ── Reddit JSON API models ─────────────────────────────────────────

    private sealed class RedditListing
    {
        [JsonPropertyName("data")]
        public RedditListingData? Data { get; set; }
    }

    private sealed class RedditListingData
    {
        [JsonPropertyName("children")]
        public List<RedditChild>? Children { get; set; }

        [JsonPropertyName("after")]
        public string? After { get; set; }
    }

    private sealed class RedditChild
    {
        [JsonPropertyName("data")]
        public RedditPostData? Data { get; set; }
    }

    private sealed class RedditPostData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("selftext")]
        public string? Selftext { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("created_utc")]
        public double CreatedUtc { get; set; }

        [JsonPropertyName("num_comments")]
        public int NumComments { get; set; }

        [JsonPropertyName("subreddit")]
        public string? Subreddit { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }
    }
}
