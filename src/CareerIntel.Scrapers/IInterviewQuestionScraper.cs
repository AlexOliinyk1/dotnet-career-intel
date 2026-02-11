using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Interface for scrapers that collect real interview questions from various sources.
/// </summary>
public interface IInterviewQuestionScraper
{
    /// <summary>
    /// Scrape interview questions for a specific company.
    /// </summary>
    Task<List<InterviewQuestion>> ScrapeCompanyQuestionsAsync(string company, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrape interview questions for a specific role.
    /// </summary>
    Task<List<InterviewQuestion>> ScrapeRoleQuestionsAsync(string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrape interview questions for a company + role combination.
    /// </summary>
    Task<List<InterviewQuestion>> ScrapeQuestionsAsync(string company, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the source name for this scraper.
    /// </summary>
    string SourceName { get; }
}

/// <summary>
/// Base class for interview question scrapers with common functionality.
/// </summary>
public abstract class BaseInterviewQuestionScraper : IInterviewQuestionScraper
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;

    protected BaseInterviewQuestionScraper(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public abstract string SourceName { get; }

    public abstract Task<List<InterviewQuestion>> ScrapeCompanyQuestionsAsync(string company, CancellationToken cancellationToken = default);

    public abstract Task<List<InterviewQuestion>> ScrapeRoleQuestionsAsync(string role, CancellationToken cancellationToken = default);

    public abstract Task<List<InterviewQuestion>> ScrapeQuestionsAsync(string company, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalize company name for consistent matching.
    /// </summary>
    protected static string NormalizeCompanyName(string company)
    {
        return company.Trim()
            .Replace("Inc.", "")
            .Replace("Corp.", "")
            .Replace("Corporation", "")
            .Replace("LLC", "")
            .Replace("Ltd", "")
            .Trim();
    }

    /// <summary>
    /// Parse difficulty from text.
    /// </summary>
    protected static DifficultyLevel ParseDifficulty(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("easy"))
            return DifficultyLevel.Easy;
        if (lower.Contains("medium") || lower.Contains("moderate"))
            return DifficultyLevel.Medium;
        if (lower.Contains("hard") || lower.Contains("difficult"))
            return DifficultyLevel.Hard;
        if (lower.Contains("expert") || lower.Contains("very hard"))
            return DifficultyLevel.Expert;

        return DifficultyLevel.Medium; // Default
    }

    /// <summary>
    /// Detect question category from question text.
    /// </summary>
    protected static QuestionCategory DetectCategory(string question)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("system design") || lower.Contains("scalability") || lower.Contains("architecture"))
            return QuestionCategory.SystemDesign;

        if (lower.Contains("tell me about a time") || lower.Contains("describe a situation") || lower.Contains("behavioral"))
            return QuestionCategory.Behavioral;

        if (lower.Contains("code") || lower.Contains("algorithm") || lower.Contains("leetcode") || lower.Contains("implement"))
            return QuestionCategory.Coding;

        if (lower.Contains("sql") || lower.Contains("database") || lower.Contains("query"))
            return QuestionCategory.Database;

        if (lower.Contains("c#") || lower.Contains("csharp"))
            return QuestionCategory.CSharp;

        if (lower.Contains(".net") || lower.Contains("dotnet") || lower.Contains("asp.net"))
            return QuestionCategory.DotNet;

        if (lower.Contains("solid") || lower.Contains("design pattern") || lower.Contains("dependency injection"))
            return QuestionCategory.Architecture;

        if (lower.Contains("debug") || lower.Contains("troubleshoot") || lower.Contains("fix"))
            return QuestionCategory.Troubleshooting;

        return QuestionCategory.Technical;
    }

    /// <summary>
    /// Extract key concepts from question text.
    /// </summary>
    protected static List<string> ExtractConcepts(string question)
    {
        var concepts = new List<string>();
        var lower = question.ToLowerInvariant();

        var knownConcepts = new Dictionary<string, string[]>
        {
            ["SOLID"] = ["solid", "single responsibility", "open/closed", "liskov", "interface segregation", "dependency inversion"],
            ["Async/Await"] = ["async", "await", "task", "asynchronous"],
            ["Dependency Injection"] = ["dependency injection", "di", "ioc", "inversion of control"],
            ["Design Patterns"] = ["design pattern", "factory", "singleton", "strategy", "observer", "decorator"],
            ["Garbage Collection"] = ["garbage collection", "gc", "memory management", "dispose"],
            ["LINQ"] = ["linq", "query syntax", "lambda"],
            ["Entity Framework"] = ["entity framework", "ef core", "orm"],
            ["Microservices"] = ["microservices", "distributed system", "service mesh"],
            ["Docker/Kubernetes"] = ["docker", "kubernetes", "container", "k8s"],
            ["Authentication"] = ["authentication", "authorization", "jwt", "oauth", "identity"],
            ["REST API"] = ["rest", "api", "http", "endpoint"],
            ["SQL Optimization"] = ["sql", "query optimization", "index", "explain"],
            ["System Design"] = ["system design", "scalability", "load balancing", "caching"]
        };

        foreach (var (concept, keywords) in knownConcepts)
        {
            if (keywords.Any(k => lower.Contains(k)))
            {
                concepts.Add(concept);
            }
        }

        return concepts.Distinct().ToList();
    }
}
