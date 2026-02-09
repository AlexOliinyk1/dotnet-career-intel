using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes DOU.ua forum threads for real interview questions discussed by the
/// Ukrainian developer community. Targets the interview category and search
/// results for .NET/C# interview-related discussions.
/// </summary>
/// <remarks>
/// This scraper extends <see cref="BaseScraper"/> for HTTP infrastructure and
/// rate limiting but does NOT function as a job scraper. The abstract
/// <see cref="IJobScraper"/> methods return empty results. Instead, use
/// <see cref="ScrapeInterviewQuestionsAsync"/> to collect interview content.
/// </remarks>
public sealed partial class DouForumScraper(HttpClient httpClient, ILogger<DouForumScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string ForumBaseUrl = "https://dou.ua/forums";

    public override string PlatformName => "DouForum";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(4);

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
    /// Scrapes DOU.ua forum for interview-related threads and extracts
    /// individual questions from thread titles, bodies, and top comments.
    /// </summary>
    /// <param name="maxPages">Maximum number of listing pages to process per search query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deduplicated list of scraped interview questions.</returns>
    public async Task<IReadOnlyList<ScrapedInterviewQuestion>> ScrapeInterviewQuestionsAsync(
        int maxPages = 3, CancellationToken ct = default)
    {
        var allQuestions = new List<ScrapedInterviewQuestion>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect thread URLs from multiple entry points
        var threadUrls = new List<string>();

        var searchQueries = new[]
        {
            $"{ForumBaseUrl}/topic/?from=fp&category=interview",
            $"{ForumBaseUrl}/search/?q={HttpUtility.UrlEncode(".NET interview")}",
            $"{ForumBaseUrl}/search/?q={HttpUtility.UrlEncode("C# собеседование")}",
            $"{ForumBaseUrl}/search/?q={HttpUtility.UrlEncode("співбесіда .NET")}",
        };

        foreach (var baseQuery in searchQueries)
        {
            ct.ThrowIfCancellationRequested();

            for (var page = 0; page < maxPages; page++)
            {
                ct.ThrowIfCancellationRequested();

                var pageUrl = page == 0
                    ? baseQuery
                    : $"{baseQuery}&page={page}";

                var urls = await FetchThreadUrlsFromListingAsync(pageUrl, ct);
                if (urls.Count == 0) break;

                foreach (var url in urls)
                {
                    if (seenUrls.Add(url))
                        threadUrls.Add(url);
                }
            }
        }

        logger.LogInformation("[{Platform}] Collected {Count} unique thread URLs to process",
            PlatformName, threadUrls.Count);

        // Process each thread to extract questions
        foreach (var threadUrl in threadUrls)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var questions = await ExtractQuestionsFromThreadAsync(threadUrl, ct);
                allQuestions.AddRange(questions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[{Platform}] Failed to process thread: {Url}",
                    PlatformName, threadUrl);
            }
        }

        // Deduplicate by normalized question text
        var deduplicated = DeduplicateQuestions(allQuestions);

        logger.LogInformation(
            "[{Platform}] Extracted {Total} questions, {Unique} unique after deduplication",
            PlatformName, allQuestions.Count, deduplicated.Count);

        return deduplicated;
    }

    // ── Private helpers ────────────────────────────────────────────────

    /// <summary>
    /// Fetches a forum listing or search results page and extracts thread URLs.
    /// </summary>
    private async Task<List<string>> FetchThreadUrlsFromListingAsync(
        string listingUrl, CancellationToken ct)
    {
        var urls = new List<string>();

        var doc = await FetchPageAsync(listingUrl, ct);
        if (doc is null) return urls;

        // DOU forum listing: threads appear as links within topic list items.
        // Try multiple selectors to handle both category listings and search results.
        var threadNodes = SelectNodes(doc, "//a[contains(@class, 'topic')]")
            ?? SelectNodes(doc, "//article//a[contains(@href, '/forums/topic/')]")
            ?? SelectNodes(doc, "//div[contains(@class, 'b-forum-topic')]//a[@href]");

        if (threadNodes is null or { Count: 0 })
        {
            logger.LogDebug("[{Platform}] No thread links found on {Url}", PlatformName, listingUrl);
            return urls;
        }

        foreach (var node in threadNodes)
        {
            var href = ExtractAttribute(node, "href");
            if (string.IsNullOrEmpty(href)) continue;

            // Normalize to absolute URL
            var fullUrl = href.StartsWith("http")
                ? href
                : $"https://dou.ua{href}";

            // Only include actual forum topic links
            if (fullUrl.Contains("/forums/topic/"))
                urls.Add(fullUrl);
        }

        logger.LogDebug("[{Platform}] Found {Count} thread URLs on {Url}",
            PlatformName, urls.Count, listingUrl);

        return urls;
    }

    /// <summary>
    /// Fetches a single forum thread and extracts interview questions from
    /// the title, body text, and top-level comments.
    /// </summary>
    private async Task<List<ScrapedInterviewQuestion>> ExtractQuestionsFromThreadAsync(
        string threadUrl, CancellationToken ct)
    {
        var questions = new List<ScrapedInterviewQuestion>();

        var doc = await FetchPageAsync(threadUrl, ct);
        if (doc is null) return questions;

        // Extract thread metadata
        var titleNode = SelectSingleNode(doc, "//h1") ?? SelectSingleNode(doc, "//title");
        var threadTitle = ExtractText(titleNode);

        var bodyNode = SelectSingleNode(doc, "//div[contains(@class, 'b-typo')]")
            ?? SelectSingleNode(doc, "//div[contains(@class, 'text')]")
            ?? SelectSingleNode(doc, "//article//div[contains(@class, 'content')]");
        var bodyText = ExtractText(bodyNode);

        var dateNode = SelectSingleNode(doc, "//time") ?? SelectSingleNode(doc, "//span[contains(@class, 'date')]");
        var dateText = ExtractText(dateNode);
        var postedDate = TryParseDate(dateText);

        // Extract questions from the thread title itself (if it's a question)
        if (IsQuestion(threadTitle))
        {
            questions.Add(BuildQuestion(threadTitle, threadUrl, bodyText, postedDate));
        }

        // Extract questions from the thread body (lines ending with "?")
        var bodyQuestions = ExtractQuestionLines(bodyText);
        foreach (var q in bodyQuestions)
        {
            questions.Add(BuildQuestion(q, threadUrl, bodyText, postedDate));
        }

        // Extract questions from comments
        var commentNodes = SelectNodes(doc, "//div[contains(@class, 'comment')]//div[contains(@class, 'text')]")
            ?? SelectNodes(doc, "//div[contains(@class, 'b-comment')]//div[contains(@class, 'text')]");

        if (commentNodes is not null)
        {
            // Process top comments (limit to first 20 to stay respectful)
            var topComments = commentNodes.Take(20);

            foreach (var commentNode in topComments)
            {
                var commentText = ExtractText(commentNode);
                var commentQuestions = ExtractQuestionLines(commentText);

                foreach (var q in commentQuestions)
                {
                    questions.Add(BuildQuestion(q, threadUrl, commentText, postedDate));
                }
            }
        }

        return questions;
    }

    /// <summary>
    /// Builds a <see cref="ScrapedInterviewQuestion"/> from raw extracted text.
    /// </summary>
    private ScrapedInterviewQuestion BuildQuestion(
        string questionText, string sourceUrl, string contextText, DateTimeOffset? postedDate)
    {
        var normalized = NormalizeWhitespace(questionText);

        return new ScrapedInterviewQuestion
        {
            Id = GenerateQuestionId(normalized),
            Source = "dou-forum",
            SourceUrl = sourceUrl,
            Question = normalized,
            TopicArea = DetectTopicArea(normalized),
            Tags = DetectTags(normalized + " " + contextText),
            BestAnswer = string.Empty, // DOU forum does not have a structured "best answer"
            Upvotes = 0,
            Company = DetectCompany(contextText),
            SeniorityContext = DetectSeniorityContext(contextText),
            ScrapedDate = DateTimeOffset.UtcNow,
            PostedDate = postedDate,
        };
    }

    /// <summary>
    /// Extracts lines from text that appear to be questions. Matches lines ending
    /// with "?" as well as common interview question patterns.
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

            // Common interview question patterns (Ukrainian + English)
            if (QuestionPatternRegex().IsMatch(line))
            {
                questions.Add(line);
            }
        }

        return questions;
    }

    /// <summary>
    /// Determines whether a piece of text is likely a question.
    /// </summary>
    private static bool IsQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        return trimmed.EndsWith('?') || QuestionPatternRegex().IsMatch(trimmed);
    }

    // ── Topic and tag detection ────────────────────────────────────────

    private static readonly Dictionary<string, string[]> TopicKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet-internals"] = ["GC", "garbage collector", "CLR", "JIT", "IL", "CIL", "MSIL",
            "reflection", "assembly", "AppDomain", "finalizer", "span", "memory",
            "ValueType", "boxing", "unboxing", "struct vs class", "збірка сміття"],
        ["async-parallel"] = ["async", "await", "Task", "ValueTask", "SynchronizationContext",
            "ConfigureAwait", "Parallel", "Semaphore", "thread", "lock", "concurrent",
            "deadlock", "race condition", "асинхронність", "багатопоточність"],
        ["system-design"] = ["architecture", "microservices", "monolith", "CQRS", "event sourcing",
            "DDD", "domain driven", "scalability", "load balancer", "message queue",
            "distributed", "CAP theorem", "архітектура", "мікросервіси"],
        ["databases"] = ["SQL", "index", "transaction", "isolation level", "deadlock",
            "normalization", "stored procedure", "Entity Framework", "EF Core",
            "Dapper", "migration", "ACID", "JOIN", "база даних"],
        ["web-api"] = ["REST", "HTTP", "middleware", "controller", "API", "gRPC",
            "SignalR", "WebSocket", "authentication", "authorization",
            "JWT", "OAuth", "CORS", "rate limit", "ASP.NET"],
        ["patterns-practices"] = ["SOLID", "DI", "dependency injection", "IoC", "pattern",
            "repository", "unit of work", "factory", "observer", "strategy",
            "decorator", "mediator", "clean architecture", "патерн"],
        ["testing"] = ["unit test", "integration test", "mock", "stub", "xUnit",
            "NUnit", "MSTest", "TDD", "BDD", "code coverage", "тестування"],
        ["csharp-language"] = ["LINQ", "delegate", "event", "expression tree", "generic",
            "covariance", "contravariance", "nullable", "record", "pattern matching",
            "interface", "abstract", "sealed", "extension method"],
    };

    /// <summary>
    /// Detects the primary topic area of a question based on keyword matching.
    /// Returns the topic with the most keyword hits.
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

        // Technology tags
        if (lower.Contains("c#") || lower.Contains("csharp")) tags.Add("C#");
        if (lower.Contains(".net") || lower.Contains("dotnet")) tags.Add(".NET");
        if (lower.Contains("asp.net")) tags.Add("ASP.NET");
        if (lower.Contains("ef core") || lower.Contains("entity framework")) tags.Add("EF Core");
        if (lower.Contains("blazor")) tags.Add("Blazor");
        if (lower.Contains("azure")) tags.Add("Azure");
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
        if (lower.Contains("design pattern") || lower.Contains("патерн")) tags.Add("patterns");
        if (lower.Contains("unit test") || lower.Contains("тест")) tags.Add("testing");

        return [.. tags];
    }

    /// <summary>
    /// Attempts to detect a company name from context text. Looks for common
    /// patterns like "at Company", "в Company", "Company interview".
    /// </summary>
    private static string DetectCompany(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Common patterns: "interview at Google", "співбесіда в EPAM"
        var patterns = new[]
        {
            @"(?:interview|собеседовани[еия]|співбесід[аи])\s+(?:at|в|у|@)\s+([A-ZА-ЯІЇЄҐ][\w\.\-]+)",
            @"(?:at|в|у|@)\s+([A-ZА-ЯІЇЄҐ][\w\.\-]+)\s+(?:interview|собеседовани[еия]|співбесід[аи])",
            @"([A-Z][\w\.\-]+)\s+(?:interview|собеседовани[еия]|співбесід[аи])",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups[1].Value.Length >= 2)
            {
                var company = match.Groups[1].Value.Trim();
                // Filter out generic words that are not company names
                if (!IsGenericWord(company))
                    return company;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Detects seniority context from the text (e.g., "senior", "lead", "middle").
    /// </summary>
    private static string DetectSeniorityContext(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff"))
            return "principal";
        if (lower.Contains("architect") || lower.Contains("архітектор"))
            return "architect";
        if (lower.Contains("lead") || lower.Contains("лід") || lower.Contains("тімлід"))
            return "lead";
        if (lower.Contains("senior") || lower.Contains("сеніор") || lower.Contains("синьйор"))
            return "senior";
        if (lower.Contains("middle") || lower.Contains("мідл") || lower.Contains("mid-level"))
            return "middle";
        if (lower.Contains("junior") || lower.Contains("джуніор"))
            return "junior";

        return string.Empty;
    }

    private static bool IsGenericWord(string word)
    {
        var generics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "The", "This", "That", "Your", "What", "How", "Why", "Which",
            "NET", "Core", "Web", "API", "Job", "Work", "Dev", "Tech",
            "Це", "Що", "Як", "Чому", "Де", "Ваш",
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
    /// Deduplicates questions by their normalized text. Keeps the first occurrence.
    /// </summary>
    private static List<ScrapedInterviewQuestion> DeduplicateQuestions(
        List<ScrapedInterviewQuestion> questions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ScrapedInterviewQuestion>();

        foreach (var q in questions)
        {
            var key = NormalizeForDedup(q.Question);
            if (seen.Add(key))
                result.Add(q);
        }

        return result;
    }

    /// <summary>
    /// Normalizes question text for deduplication by lowering case, removing
    /// punctuation, and collapsing whitespace.
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

    private static DateTimeOffset? TryParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return DateTimeOffset.TryParse(text, out var date) ? date : null;
    }

    [GeneratedRegex(
        @"(?:розкажіть|поясніть|як працює|в чому різниця|що таке|навіщо|як реалізувати|explain|describe|how does|what is|difference between|tell me about|implement|compare)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex QuestionPatternRegex();
}
