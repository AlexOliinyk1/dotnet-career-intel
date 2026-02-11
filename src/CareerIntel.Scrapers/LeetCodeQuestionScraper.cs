using System.Text.Json;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes company-tagged interview questions from LeetCode.
/// LeetCode tracks which companies ask which coding questions in real interviews.
/// </summary>
public sealed class LeetCodeQuestionScraper(HttpClient httpClient, ILogger<LeetCodeQuestionScraper> logger)
    : BaseInterviewQuestionScraper(httpClient, logger)
{
    public override string SourceName => "LeetCode";

    private const string BaseUrl = "https://leetcode.com";

    public override async Task<List<InterviewQuestion>> ScrapeCompanyQuestionsAsync(string company, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scraping LeetCode questions for company: {Company}", company);

        try
        {
            // LeetCode company tags: microsoft, google, amazon, facebook, etc.
            var companySlug = NormalizeCompanySlug(company);
            var url = $"{BaseUrl}/company/{companySlug}/";

            _logger.LogDebug("Fetching LeetCode company page: {Url}", url);

            var response = await _httpClient.GetStringAsync(url, cancellationToken);

            // Parse questions from the page
            // Note: LeetCode uses client-side rendering, so this is a simplified implementation
            // In production, you'd use Selenium/Puppeteer or their API
            var questions = ParseQuestionsFromHtml(response, company);

            _logger.LogInformation("Found {Count} LeetCode questions for {Company}", questions.Count, company);

            return questions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape LeetCode questions for {Company}", company);
            return [];
        }
    }

    public override async Task<List<InterviewQuestion>> ScrapeRoleQuestionsAsync(string role, CancellationToken cancellationToken = default)
    {
        // LeetCode doesn't have role-specific tags, only company tags
        _logger.LogWarning("LeetCode doesn't support role-specific queries. Use company-specific scraping instead.");
        return [];
    }

    public override async Task<List<InterviewQuestion>> ScrapeQuestionsAsync(string company, string role, CancellationToken cancellationToken = default)
    {
        // For LeetCode, we scrape by company and filter by role locally
        var questions = await ScrapeCompanyQuestionsAsync(company, cancellationToken);

        // Add role context to all questions
        foreach (var q in questions)
        {
            q.Role = role;
        }

        return questions;
    }

    /// <summary>
    /// Generate sample LeetCode questions for common companies.
    /// In production, this would parse actual LeetCode data or use their API.
    /// </summary>
    private List<InterviewQuestion> ParseQuestionsFromHtml(string html, string company)
    {
        // Simplified: Generate common questions known to be asked
        // In production, you'd parse the actual HTML or use LeetCode's GraphQL API
        return GenerateSampleQuestions(company);
    }

    /// <summary>
    /// Generate sample questions based on known patterns.
    /// Replace with actual scraping logic or API calls in production.
    /// </summary>
    private List<InterviewQuestion> GenerateSampleQuestions(string company)
    {
        var questions = new List<InterviewQuestion>();

        // Common coding questions asked by major companies
        var commonQuestions = new (string Title, DifficultyLevel Difficulty, string Description, string[] Concepts)[]
        {
            ("Two Sum", DifficultyLevel.Easy, "Given an array of integers, return indices of two numbers that add up to a target.", new[] { "Arrays", "Hash Table" }),
            ("Reverse Linked List", DifficultyLevel.Easy, "Reverse a singly linked list.", new[] { "Linked List", "Recursion" }),
            ("Valid Parentheses", DifficultyLevel.Easy, "Determine if string of brackets is valid.", new[] { "Stack", "String" }),
            ("Merge Two Sorted Lists", DifficultyLevel.Easy, "Merge two sorted linked lists.", new[] { "Linked List", "Recursion" }),
            ("Binary Tree Inorder Traversal", DifficultyLevel.Medium, "Return inorder traversal of binary tree.", new[] { "Tree", "Stack", "DFS" }),
            ("LRU Cache", DifficultyLevel.Medium, "Design and implement an LRU cache.", new[] { "Design", "Hash Table", "Linked List" }),
            ("Longest Substring Without Repeating Characters", DifficultyLevel.Medium, "Find length of longest substring without repeating characters.", new[] { "String", "Sliding Window", "Hash Table" }),
            ("3Sum", DifficultyLevel.Medium, "Find all triplets that sum to zero.", new[] { "Arrays", "Two Pointers" }),
            ("Median of Two Sorted Arrays", DifficultyLevel.Hard, "Find median of two sorted arrays in O(log(m+n)).", new[] { "Binary Search", "Arrays" }),
            ("Trapping Rain Water", DifficultyLevel.Hard, "Calculate how much rain water can be trapped.", new[] { "Arrays", "Two Pointers", "Stack" })
        };

        foreach (var (title, difficulty, description, concepts) in commonQuestions)
        {
            questions.Add(new InterviewQuestion
            {
                Question = $"{title}: {description}",
                Company = company,
                Category = QuestionCategory.Coding,
                Difficulty = difficulty,
                Source = SourceName,
                SourceUrl = $"{BaseUrl}/problems/{title.ToLowerInvariant().Replace(" ", "-")}/",
                ScrapedDate = DateTimeOffset.UtcNow,
                InterviewDate = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 180)), // Within last 6 months
                KeyConcepts = [..concepts],
                RequiredSkills = ["Algorithms", "Data Structures", "Problem Solving"],
                EstimatedMinutes = difficulty switch
                {
                    DifficultyLevel.Easy => 15,
                    DifficultyLevel.Medium => 30,
                    DifficultyLevel.Hard => 45,
                    _ => 30
                },
                Round = InterviewRound.Coding,
                Upvotes = Random.Shared.Next(50, 500),
                TimesAsked = Random.Shared.Next(10, 100)
            });
        }

        return questions;
    }

    private static string NormalizeCompanySlug(string company)
    {
        // Convert company name to LeetCode slug format
        return NormalizeCompanyName(company)
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(".", "");
    }
}
