using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Scrapers;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for scraping and managing interview questions.
/// Usage:
///   career-intel interview-questions --scrape --company "Microsoft" --role "Senior .NET Developer"
///   career-intel interview-questions --list --company "Google"
///   career-intel interview-questions --practice --company "Amazon" --weak "async/await,SOLID"
/// </summary>
public static class InterviewQuestionsCommand
{
    public static Command Create()
    {
        var scrapeOption = new Option<bool>(
            "--scrape",
            description: "Scrape interview questions from sources");

        var listOption = new Option<bool>(
            "--list",
            description: "List available interview questions");

        var practiceOption = new Option<bool>(
            "--practice",
            description: "Generate practice question set");

        var statsOption = new Option<bool>(
            "--stats",
            description: "Show database statistics");

        var companyOption = new Option<string?>(
            "--company",
            description: "Company name (e.g., 'Microsoft', 'Google')");

        var roleOption = new Option<string?>(
            "--role",
            description: "Role name (e.g., 'Senior .NET Developer')");

        var weakOption = new Option<string?>(
            "--weak",
            description: "Comma-separated weak concepts for targeted practice");

        var countOption = new Option<int>(
            "--count",
            getDefaultValue: () => 20,
            description: "Number of questions to show/practice");

        var categoryOption = new Option<string?>(
            "--category",
            description: "Filter by category (Coding, SystemDesign, Behavioral, etc.)");

        var difficultyOption = new Option<string?>(
            "--difficulty",
            description: "Filter by difficulty (Easy, Medium, Hard)");

        var command = new Command("interview-questions", "Scrape and manage real interview questions")
        {
            scrapeOption,
            listOption,
            practiceOption,
            statsOption,
            companyOption,
            roleOption,
            weakOption,
            countOption,
            categoryOption,
            difficultyOption
        };

        command.SetHandler(async (context) =>
        {
            var scrape = context.ParseResult.GetValueForOption(scrapeOption);
            var list = context.ParseResult.GetValueForOption(listOption);
            var practice = context.ParseResult.GetValueForOption(practiceOption);
            var stats = context.ParseResult.GetValueForOption(statsOption);
            var company = context.ParseResult.GetValueForOption(companyOption);
            var role = context.ParseResult.GetValueForOption(roleOption);
            var weak = context.ParseResult.GetValueForOption(weakOption);
            var count = context.ParseResult.GetValueForOption(countOption);
            var category = context.ParseResult.GetValueForOption(categoryOption);
            var difficulty = context.ParseResult.GetValueForOption(difficultyOption);

            await ExecuteAsync(scrape, list, practice, stats, company, role, weak, count, category, difficulty);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool scrape, bool list, bool practice, bool stats,
        string? company, string? role, string? weak, int count,
        string? category, string? difficulty)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.InterviewQuestionsCommand");

        var databasePath = Path.Combine(Program.DataDirectory, "interview-questions.json");
        var database = new InterviewQuestionDatabase(databasePath);

        if (scrape)
        {
            await ScrapeQuestionsAsync(company, role, database, serviceProvider, logger);
        }
        else if (list)
        {
            ListQuestions(company, role, category, difficulty, count, database);
        }
        else if (practice)
        {
            GeneratePracticeSet(company, role, weak, count, database);
        }
        else if (stats)
        {
            ShowStatistics(database);
        }
        else
        {
            ShowUsageHelp();
        }
    }

    private static async Task ScrapeQuestionsAsync(
        string? company,
        string? role,
        InterviewQuestionDatabase database,
        ServiceProvider serviceProvider,
        ILogger logger)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Scraping Interview Questions ===");
        Console.ResetColor();
        Console.WriteLine();

        if (string.IsNullOrEmpty(company) && string.IsNullOrEmpty(role))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Please specify --company and/or --role");
            Console.ResetColor();
            return;
        }

        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        // Create scrapers
        var scrapers = new List<IInterviewQuestionScraper>
        {
            new LeetCodeQuestionScraper(httpClient, loggerFactory.CreateLogger<LeetCodeQuestionScraper>()),
            new GlassdoorQuestionScraper(httpClient, loggerFactory.CreateLogger<GlassdoorQuestionScraper>())
        };

        var allQuestions = new List<InterviewQuestion>();

        foreach (var scraper in scrapers)
        {
            try
            {
                Console.WriteLine($"Scraping from {scraper.SourceName}...");

                List<InterviewQuestion> questions;
                if (!string.IsNullOrEmpty(company) && !string.IsNullOrEmpty(role))
                {
                    questions = await scraper.ScrapeQuestionsAsync(company, role);
                }
                else if (!string.IsNullOrEmpty(company))
                {
                    questions = await scraper.ScrapeCompanyQuestionsAsync(company);
                }
                else
                {
                    questions = await scraper.ScrapeRoleQuestionsAsync(role!);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ Found {questions.Count} questions from {scraper.SourceName}");
                Console.ResetColor();

                allQuestions.AddRange(questions);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape from {Source}", scraper.SourceName);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠ Failed to scrape from {scraper.SourceName}: {ex.Message}");
                Console.ResetColor();
            }
        }

        if (allQuestions.Count > 0)
        {
            database.AddQuestions(allQuestions);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Scraped and saved {allQuestions.Count} interview questions");
            Console.ResetColor();

            // Show breakdown
            var bySource = allQuestions.GroupBy(q => q.Source).ToList();
            Console.WriteLine("\nBreakdown by source:");
            foreach (var group in bySource)
            {
                Console.WriteLine($"  {group.Key}: {group.Count()} questions");
            }

            var byCategory = allQuestions.GroupBy(q => q.Category).ToList();
            Console.WriteLine("\nBreakdown by category:");
            foreach (var group in byCategory)
            {
                Console.WriteLine($"  {group.Key}: {group.Count()} questions");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No questions found. The scrapers might need configuration.");
            Console.ResetColor();
        }
    }

    private static void ListQuestions(
        string? company,
        string? role,
        string? category,
        string? difficulty,
        int count,
        InterviewQuestionDatabase database)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Interview Questions ===");
        Console.ResetColor();
        Console.WriteLine();

        List<InterviewQuestion> questions;

        if (!string.IsNullOrEmpty(company) && !string.IsNullOrEmpty(role))
        {
            questions = database.GetQuestions(company, role);
        }
        else if (!string.IsNullOrEmpty(company))
        {
            questions = database.GetCompanyQuestions(company);
        }
        else if (!string.IsNullOrEmpty(role))
        {
            questions = database.GetRoleQuestions(role);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Please specify --company and/or --role");
            Console.ResetColor();
            return;
        }

        // Apply filters
        if (!string.IsNullOrEmpty(category) && Enum.TryParse<QuestionCategory>(category, true, out var cat))
        {
            questions = questions.Where(q => q.Category == cat).ToList();
        }

        if (!string.IsNullOrEmpty(difficulty) && Enum.TryParse<DifficultyLevel>(difficulty, true, out var diff))
        {
            questions = questions.Where(q => q.Difficulty == diff).ToList();
        }

        questions = questions.Take(count).ToList();

        if (questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No questions found. Try scraping first:");
            Console.WriteLine($"  career-intel interview-questions --scrape --company \"{company ?? "Microsoft"}\" --role \"{role ?? "Senior .NET Developer"}\"");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Found {questions.Count} questions:\n");

        for (int i = 0; i < questions.Count; i++)
        {
            var q = questions[i];

            Console.ForegroundColor = GetDifficultyColor(q.Difficulty);
            Console.Write($"{i + 1}. [{q.Difficulty}] ");
            Console.ResetColor();

            Console.ForegroundColor = GetCategoryColor(q.Category);
            Console.Write($"[{q.Category}] ");
            Console.ResetColor();

            Console.WriteLine(q.Question);

            if (q.KeyConcepts.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"   Concepts: {string.Join(", ", q.KeyConcepts)}");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(q.InterviewerTips))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"   💡 Tip: {q.InterviewerTips}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   Source: {q.Source} | Asked {q.TimesAsked}x | {q.EstimatedMinutes} min");
            Console.ResetColor();

            Console.WriteLine();
        }
    }

    private static void GeneratePracticeSet(
        string? company,
        string? role,
        string? weak,
        int count,
        InterviewQuestionDatabase database)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Practice Question Set ===");
        Console.ResetColor();
        Console.WriteLine();

        if (string.IsNullOrEmpty(company) || string.IsNullOrEmpty(role))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Please specify both --company and --role for practice");
            Console.ResetColor();
            return;
        }

        var weakConcepts = string.IsNullOrEmpty(weak)
            ? new List<string>()
            : weak.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        List<InterviewQuestion> questions;

        if (weakConcepts.Count > 0)
        {
            Console.WriteLine($"Generating practice set targeting: {string.Join(", ", weakConcepts)}\n");
            questions = database.GetPracticeQuestions(company, role, weakConcepts, count: count);
        }
        else
        {
            Console.WriteLine($"Generating balanced practice set (no weak areas specified)\n");
            questions = database.GenerateStudyPlan(company, role, count);
        }

        if (questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No questions available. Scrape first!");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Practice Set: {questions.Count} questions\n");

        var byDifficulty = questions.GroupBy(q => q.Difficulty).OrderBy(g => g.Key).ToList();

        foreach (var group in byDifficulty)
        {
            Console.ForegroundColor = GetDifficultyColor(group.Key);
            Console.WriteLine($"{group.Key} ({group.Count()}):");
            Console.ResetColor();

            foreach (var q in group)
            {
                Console.WriteLine($"  • {q.Question}");
                if (q.KeyConcepts.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    ({string.Join(", ", q.KeyConcepts)})");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Total estimated time: {questions.Sum(q => q.EstimatedMinutes)} minutes");
        Console.ResetColor();
    }

    private static void ShowStatistics(InterviewQuestionDatabase database)
    {
        var stats = database.GetStatistics();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Interview Questions Database Statistics ===");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine($"Total Questions: {stats.TotalQuestions}");
        Console.WriteLine($"Total Companies: {stats.TotalCompanies}");
        Console.WriteLine($"Total Roles: {stats.TotalRoles}");
        Console.WriteLine($"Last Updated: {stats.LastUpdated:yyyy-MM-dd HH:mm}");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("By Category:");
        Console.ResetColor();
        foreach (var (cat, count) in stats.QuestionsByCategory.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {cat,-20} {count,5} questions");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("By Difficulty:");
        Console.ResetColor();
        foreach (var (diff, count) in stats.QuestionsByDifficulty.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  {diff,-20} {count,5} questions");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("By Source:");
        Console.ResetColor();
        foreach (var (source, count) in stats.QuestionsBySource.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {source,-20} {count,5} questions");
        }
    }

    private static void ShowUsageHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Scrape questions:  career-intel interview-questions --scrape --company \"Microsoft\" --role \"Senior .NET Developer\"");
        Console.WriteLine("  List questions:    career-intel interview-questions --list --company \"Google\"");
        Console.WriteLine("  Practice set:      career-intel interview-questions --practice --company \"Amazon\" --weak \"async/await,SOLID\"");
        Console.WriteLine("  Show statistics:   career-intel interview-questions --stats");
    }

    private static ConsoleColor GetDifficultyColor(DifficultyLevel difficulty)
    {
        return difficulty switch
        {
            DifficultyLevel.Easy => ConsoleColor.Green,
            DifficultyLevel.Medium => ConsoleColor.Yellow,
            DifficultyLevel.Hard => ConsoleColor.Red,
            DifficultyLevel.Expert => ConsoleColor.Magenta,
            _ => ConsoleColor.White
        };
    }

    private static ConsoleColor GetCategoryColor(QuestionCategory category)
    {
        return category switch
        {
            QuestionCategory.Coding => ConsoleColor.Cyan,
            QuestionCategory.SystemDesign => ConsoleColor.Blue,
            QuestionCategory.Behavioral => ConsoleColor.Green,
            _ => ConsoleColor.White
        };
    }
}
