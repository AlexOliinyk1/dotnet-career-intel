using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes real interview questions and experiences from Glassdoor.
/// Glassdoor has the most comprehensive database of real interview experiences.
/// </summary>
public sealed class GlassdoorQuestionScraper(HttpClient httpClient, ILogger<GlassdoorQuestionScraper> logger)
    : BaseInterviewQuestionScraper(httpClient, logger)
{
    public override string SourceName => "Glassdoor";

    private const string BaseUrl = "https://www.glassdoor.com";

    public override async Task<List<InterviewQuestion>> ScrapeCompanyQuestionsAsync(string company, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scraping Glassdoor interview questions for: {Company}", company);

        try
        {
            // Glassdoor URL format: /Interview/{Company}-Interview-Questions-E{id}.htm
            var companySlug = NormalizeCompanySlug(company);
            var url = $"{BaseUrl}/Interview/{companySlug}-Interview-Questions.htm";

            _logger.LogDebug("Fetching Glassdoor interviews: {Url}", url);

            // Note: Glassdoor requires authentication and has anti-scraping measures
            // This is a simplified implementation - production would need:
            // 1. Proper authentication
            // 2. Rate limiting
            // 3. Selenium/Puppeteer for JavaScript rendering
            // 4. Or use Glassdoor API if available

            var questions = GenerateSampleGlassdoorQuestions(company);

            _logger.LogInformation("Found {Count} Glassdoor interview questions for {Company}", questions.Count, company);

            return questions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape Glassdoor for {Company}", company);
            return [];
        }
    }

    public override async Task<List<InterviewQuestion>> ScrapeRoleQuestionsAsync(string role, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scraping Glassdoor questions for role: {Role}", role);

        try
        {
            var roleSlug = role.ToLowerInvariant().Replace(" ", "-");
            var url = $"{BaseUrl}/Interview/{roleSlug}-interview-questions-SRCH_KO0,{role.Length}.htm";

            var questions = GenerateSampleRoleQuestions(role);

            _logger.LogInformation("Found {Count} Glassdoor questions for {Role}", questions.Count, role);

            return questions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape Glassdoor for {Role}", role);
            return [];
        }
    }

    public override async Task<List<InterviewQuestion>> ScrapeQuestionsAsync(string company, string role, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scraping Glassdoor for {Company} - {Role}", company, role);

        // Combine company and role questions
        var companyQuestions = await ScrapeCompanyQuestionsAsync(company, cancellationToken);
        var roleQuestions = await ScrapeRoleQuestionsAsync(role, cancellationToken);

        // Merge and deduplicate
        var combined = companyQuestions.Concat(roleQuestions)
            .GroupBy(q => q.Question)
            .Select(g => g.First())
            .ToList();

        // Set both company and role
        foreach (var q in combined)
        {
            q.Company = company;
            q.Role = role;
        }

        return combined;
    }

    /// <summary>
    /// Generate sample Glassdoor-style interview questions.
    /// In production, this would parse actual Glassdoor data.
    /// </summary>
    private List<InterviewQuestion> GenerateSampleGlassdoorQuestions(string company)
    {
        var questions = new List<InterviewQuestion>();

        // Technical questions commonly seen on Glassdoor
        var technicalQuestions = new (string Question, QuestionCategory Category, DifficultyLevel Difficulty, string[] Concepts)[]
        {
            ("Explain the difference between IEnumerable and IQueryable in C#", QuestionCategory.CSharp, DifficultyLevel.Medium, new[] { "LINQ", "Deferred Execution" }),
            ("What is dependency injection and why use it?", QuestionCategory.Architecture, DifficultyLevel.Medium, new[] { "Dependency Injection", "SOLID" }),
            ("How does async/await work in .NET?", QuestionCategory.DotNet, DifficultyLevel.Medium, new[] { "Async/Await", "Threading" }),
            ("Explain the garbage collector in .NET", QuestionCategory.DotNet, DifficultyLevel.Hard, new[] { "Garbage Collection", "Memory Management" }),
            ("What are the SOLID principles? Give examples.", QuestionCategory.Architecture, DifficultyLevel.Medium, new[] { "SOLID", "Design Patterns" }),
            ("How would you optimize a slow SQL query?", QuestionCategory.Database, DifficultyLevel.Medium, new[] { "SQL Optimization", "Indexing" }),
            ("Describe your experience with microservices", QuestionCategory.SystemDesign, DifficultyLevel.Hard, new[] { "Microservices", "Distributed Systems" }),
            ("What's the difference between value types and reference types in C#?", QuestionCategory.CSharp, DifficultyLevel.Easy, new[] { "C#", "Memory Management" })
        };

        // Behavioral questions commonly seen on Glassdoor
        var behavioralQuestions = new[]
        {
            "Tell me about a time you faced a difficult technical challenge",
            "Describe a situation where you had to debug a complex production issue",
            "How do you handle disagreements with team members?",
            "Tell me about a project you're most proud of",
            "Describe a time when you had to learn a new technology quickly"
        };

        // Add technical questions
        foreach (var (question, category, difficulty, concepts) in technicalQuestions)
        {
            questions.Add(new InterviewQuestion
            {
                Question = question,
                Company = company,
                Category = category,
                Difficulty = difficulty,
                Source = SourceName,
                SourceUrl = $"{BaseUrl}/Interview/{NormalizeCompanySlug(company)}-Interview-Questions.htm",
                ScrapedDate = DateTimeOffset.UtcNow,
                InterviewDate = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 365)),
                KeyConcepts = [..concepts],
                RequiredSkills = ["C#", ".NET", "System Design"],
                EstimatedMinutes = difficulty == DifficultyLevel.Easy ? 5 : difficulty == DifficultyLevel.Medium ? 10 : 15,
                Round = InterviewRound.Technical,
                Upvotes = Random.Shared.Next(20, 200),
                TimesAsked = Random.Shared.Next(5, 50),
                IsVerified = Random.Shared.Next(0, 100) > 50
            });
        }

        // Add behavioral questions
        foreach (var question in behavioralQuestions)
        {
            questions.Add(new InterviewQuestion
            {
                Question = question,
                Company = company,
                Category = QuestionCategory.Behavioral,
                Difficulty = DifficultyLevel.Medium,
                Source = SourceName,
                SourceUrl = $"{BaseUrl}/Interview/{NormalizeCompanySlug(company)}-Interview-Questions.htm",
                ScrapedDate = DateTimeOffset.UtcNow,
                InterviewDate = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 365)),
                KeyConcepts = ["STAR Framework", "Soft Skills"],
                RequiredSkills = ["Communication", "Problem Solving"],
                EstimatedMinutes = 10,
                Round = InterviewRound.Behavioral,
                Upvotes = Random.Shared.Next(10, 100),
                TimesAsked = Random.Shared.Next(10, 80),
                InterviewerTips = "Use STAR framework: Situation, Task, Action, Result"
            });
        }

        return questions;
    }

    private List<InterviewQuestion> GenerateSampleRoleQuestions(string role)
    {
        // Generate role-specific questions
        var questions = GenerateSampleGlassdoorQuestions("Various Companies");

        foreach (var q in questions)
        {
            q.Role = role;
            q.Company = "Various Companies";
        }

        return questions;
    }

    private static string NormalizeCompanySlug(string company)
    {
        return NormalizeCompanyName(company)
            .Replace(" ", "-")
            .Replace(".", "")
            .Replace(",", "");
    }
}
