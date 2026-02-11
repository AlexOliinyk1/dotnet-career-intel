using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Interface for scrapers that collect documentation from official sources.
/// </summary>
public interface IDocumentationScraper
{
    /// <summary>
    /// Scrape documentation articles for a specific topic.
    /// </summary>
    Task<List<DocumentationArticle>> ScrapeTopicAsync(string topic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrape documentation for a technology (e.g., "C#", "ASP.NET Core").
    /// </summary>
    Task<List<DocumentationArticle>> ScrapeTechnologyAsync(string technology, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search documentation for specific keywords.
    /// </summary>
    Task<List<DocumentationArticle>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the source name for this scraper.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Technologies supported by this documentation source.
    /// </summary>
    IReadOnlyList<string> SupportedTechnologies { get; }
}

/// <summary>
/// Base class for documentation scrapers with common functionality.
/// </summary>
public abstract class BaseDocumentationScraper : IDocumentationScraper
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;

    protected BaseDocumentationScraper(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public abstract string SourceName { get; }
    public abstract IReadOnlyList<string> SupportedTechnologies { get; }

    public abstract Task<List<DocumentationArticle>> ScrapeTopicAsync(string topic, CancellationToken cancellationToken = default);
    public abstract Task<List<DocumentationArticle>> ScrapeTechnologyAsync(string technology, CancellationToken cancellationToken = default);
    public abstract Task<List<DocumentationArticle>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate reading time based on content length (200 words per minute).
    /// </summary>
    protected static int EstimateReadingTime(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var wordCount = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var minutes = (int)Math.Ceiling(wordCount / 200.0);
        return Math.Max(1, minutes);
    }

    /// <summary>
    /// Extract topics/keywords from article content.
    /// </summary>
    protected static List<string> ExtractTopics(string title, string content)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = $"{title} {content}".ToLowerInvariant();

        // Common .NET topics
        var knownTopics = new Dictionary<string, string[]>
        {
            ["Async/Await"] = ["async", "await", "task", "asynchronous"],
            ["Dependency Injection"] = ["dependency injection", "di container", "ioc"],
            ["LINQ"] = ["linq", "query syntax", "lambda"],
            ["Entity Framework"] = ["entity framework", "ef core", "orm"],
            ["ASP.NET Core"] = ["asp.net core", "minimal api", "web api"],
            ["Blazor"] = ["blazor", "webassembly", "razor components"],
            ["SignalR"] = ["signalr", "real-time", "websocket"],
            ["gRPC"] = ["grpc", "protocol buffers", "rpc"],
            ["Authentication"] = ["authentication", "authorization", "jwt", "oauth"],
            ["Performance"] = ["performance", "optimization", "caching", "profiling"],
            ["Testing"] = ["unit test", "integration test", "xunit", "nunit"],
            ["Docker"] = ["docker", "container", "dockerfile"],
            ["Kubernetes"] = ["kubernetes", "k8s", "orchestration"],
            ["Microservices"] = ["microservice", "distributed system", "service mesh"],
            ["SOLID Principles"] = ["solid", "single responsibility", "open/closed"],
            ["Design Patterns"] = ["design pattern", "factory", "singleton", "strategy"]
        };

        foreach (var (topic, keywords) in knownTopics)
        {
            if (keywords.Any(k => text.Contains(k)))
            {
                topics.Add(topic);
            }
        }

        return [.. topics];
    }

    /// <summary>
    /// Detect difficulty level from content markers.
    /// </summary>
    protected static DifficultyLevel DetectDifficulty(string title, string content, List<string> tags)
    {
        var text = $"{title} {content} {string.Join(" ", tags)}".ToLowerInvariant();

        if (text.Contains("advanced") || text.Contains("expert") || text.Contains("deep dive"))
            return DifficultyLevel.Hard;

        if (text.Contains("intermediate") || text.Contains("tutorial") || text.Contains("guide"))
            return DifficultyLevel.Medium;

        if (text.Contains("beginner") || text.Contains("introduction") || text.Contains("getting started") || text.Contains("quickstart"))
            return DifficultyLevel.Easy;

        return DifficultyLevel.Medium; // Default
    }

    /// <summary>
    /// Clean HTML content to plain text.
    /// </summary>
    protected static string CleanHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // Remove script and style tags
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove HTML tags
        html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ");

        // Decode HTML entities
        html = System.Net.WebUtility.HtmlDecode(html);

        // Normalize whitespace
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ");

        return html.Trim();
    }

    /// <summary>
    /// Extract code samples from content.
    /// </summary>
    protected static List<CodeSample> ExtractCodeSamples(string content)
    {
        var samples = new List<CodeSample>();

        // Match code blocks in markdown format (```language ... ```)
        var codeBlockPattern = @"```(\w+)\s*\n([\s\S]*?)```";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, codeBlockPattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count >= 3)
            {
                samples.Add(new CodeSample
                {
                    Language = match.Groups[1].Value.ToLowerInvariant(),
                    Code = match.Groups[2].Value.Trim(),
                    IsRunnable = IsRunnableLanguage(match.Groups[1].Value)
                });
            }
        }

        return samples;
    }

    private static bool IsRunnableLanguage(string language)
    {
        var runnable = new[] { "csharp", "javascript", "python", "java", "go", "typescript" };
        return runnable.Contains(language.ToLowerInvariant());
    }
}
