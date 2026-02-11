using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Scrapers;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for scraping and managing documentation.
/// Usage:
///   career-intel docs --scrape --topic "async/await"
///   career-intel docs --search "dependency injection"
///   career-intel docs --technology "C#"
///   career-intel docs --learning-path "LINQ" --count 5
/// </summary>
public static class DocumentationCommand
{
    public static Command Create()
    {
        var scrapeOption = new Option<bool>(
            "--scrape",
            description: "Scrape documentation from sources");

        var searchOption = new Option<string?>(
            "--search",
            description: "Search for documentation articles");

        var topicOption = new Option<string?>(
            "--topic",
            description: "Topic to scrape/search (e.g., 'async/await', 'SOLID')");

        var technologyOption = new Option<string?>(
            "--technology",
            description: "Technology (e.g., 'C#', 'ASP.NET Core')");

        var learningPathOption = new Option<bool>(
            "--learning-path",
            description: "Generate progressive learning path for a topic");

        var countOption = new Option<int>(
            "--count",
            getDefaultValue: () => 10,
            description: "Number of articles to show");

        var difficultyOption = new Option<string?>(
            "--difficulty",
            description: "Filter by difficulty (Easy, Medium, Hard)");

        var statsOption = new Option<bool>(
            "--stats",
            description: "Show database statistics");

        var command = new Command("docs", "Scrape and manage documentation articles")
        {
            scrapeOption,
            searchOption,
            topicOption,
            technologyOption,
            learningPathOption,
            countOption,
            difficultyOption,
            statsOption
        };

        command.SetHandler(async (context) =>
        {
            var scrape = context.ParseResult.GetValueForOption(scrapeOption);
            var search = context.ParseResult.GetValueForOption(searchOption);
            var topic = context.ParseResult.GetValueForOption(topicOption);
            var technology = context.ParseResult.GetValueForOption(technologyOption);
            var learningPath = context.ParseResult.GetValueForOption(learningPathOption);
            var count = context.ParseResult.GetValueForOption(countOption);
            var difficulty = context.ParseResult.GetValueForOption(difficultyOption);
            var stats = context.ParseResult.GetValueForOption(statsOption);

            await ExecuteAsync(scrape, search, topic, technology, learningPath, count, difficulty, stats);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool scrape, string? search, string? topic, string? technology,
        bool learningPath, int count, string? difficulty, bool stats)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.DocumentationCommand");

        var databasePath = Path.Combine(Program.DataDirectory, "documentation.json");
        var database = new DocumentationDatabase(databasePath);

        if (scrape)
        {
            await ScrapeDocumentationAsync(topic, technology, database, serviceProvider, logger);
        }
        else if (!string.IsNullOrEmpty(search))
        {
            SearchDocumentation(search, count, difficulty, database);
        }
        else if (learningPath && !string.IsNullOrEmpty(topic))
        {
            ShowLearningPath(topic, count, database);
        }
        else if (!string.IsNullOrEmpty(technology))
        {
            ShowTechnologyArticles(technology, count, difficulty, database);
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

    private static async Task ScrapeDocumentationAsync(
        string? topic,
        string? technology,
        DocumentationDatabase database,
        ServiceProvider serviceProvider,
        ILogger logger)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Scraping Documentation ===");
        Console.ResetColor();
        Console.WriteLine();

        if (string.IsNullOrEmpty(topic) && string.IsNullOrEmpty(technology))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Please specify --topic or --technology");
            Console.ResetColor();
            return;
        }

        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        // Create scrapers
        var scrapers = new List<IDocumentationScraper>
        {
            new MicrosoftDocsScraper(httpClient, loggerFactory.CreateLogger<MicrosoftDocsScraper>())
        };

        var allArticles = new List<DocumentationArticle>();

        foreach (var scraper in scrapers)
        {
            try
            {
                Console.WriteLine($"Scraping from {scraper.SourceName}...");

                List<DocumentationArticle> articles;
                if (!string.IsNullOrEmpty(topic))
                {
                    articles = await scraper.ScrapeTopicAsync(topic);
                }
                else
                {
                    articles = await scraper.ScrapeTechnologyAsync(technology!);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ Found {articles.Count} articles from {scraper.SourceName}");
                Console.ResetColor();

                allArticles.AddRange(articles);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape from {Source}", scraper.SourceName);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠ Failed to scrape from {scraper.SourceName}: {ex.Message}");
                Console.ResetColor();
            }
        }

        if (allArticles.Count > 0)
        {
            database.AddArticles(allArticles);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Scraped and saved {allArticles.Count} documentation articles");
            Console.ResetColor();

            // Show breakdown
            var bySource = allArticles.GroupBy(a => a.Source).ToList();
            Console.WriteLine("\nBreakdown by source:");
            foreach (var group in bySource)
            {
                Console.WriteLine($"  {group.Key}: {group.Count()} articles");
            }

            var byDifficulty = allArticles.GroupBy(a => a.Difficulty).ToList();
            Console.WriteLine("\nBreakdown by difficulty:");
            foreach (var group in byDifficulty)
            {
                Console.WriteLine($"  {group.Key}: {group.Count()} articles");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No articles found.");
            Console.ResetColor();
        }
    }

    private static void SearchDocumentation(
        string query,
        int count,
        string? difficulty,
        DocumentationDatabase database)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Searching Documentation: \"{query}\" ===");
        Console.ResetColor();
        Console.WriteLine();

        var articles = database.Search(query);

        // Apply difficulty filter
        if (!string.IsNullOrEmpty(difficulty) && Enum.TryParse<DifficultyLevel>(difficulty, true, out var diff))
        {
            articles = articles.Where(a => a.Difficulty == diff).ToList();
        }

        articles = articles.Take(count).ToList();

        if (articles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No articles found for \"{query}\". Try scraping first:");
            Console.WriteLine($"  career-intel docs --scrape --topic \"{query}\"");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Found {articles.Count} articles:\n");

        DisplayArticles(articles);
    }

    private static void ShowLearningPath(string topic, int count, DocumentationDatabase database)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Learning Path: {topic} ===");
        Console.ResetColor();
        Console.WriteLine();

        var articles = database.GetLearningPath(topic, count);

        if (articles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No articles found for \"{topic}\". Try scraping first:");
            Console.WriteLine($"  career-intel docs --scrape --topic \"{topic}\"");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Progressive learning path with {articles.Count} articles:\n");

        var byDifficulty = articles.GroupBy(a => a.Difficulty).OrderBy(g => g.Key);

        foreach (var group in byDifficulty)
        {
            Console.ForegroundColor = GetDifficultyColor(group.Key);
            Console.WriteLine($"{group.Key} ({group.Count()}):");
            Console.ResetColor();

            foreach (var article in group)
            {
                Console.WriteLine($"  • {article.Title} ({article.EstimatedMinutes} min)");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    {article.Summary}");
                Console.WriteLine($"    {article.Url}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Total estimated time: {articles.Sum(a => a.EstimatedMinutes)} minutes");
        Console.ResetColor();
    }

    private static void ShowTechnologyArticles(
        string technology,
        int count,
        string? difficulty,
        DocumentationDatabase database)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Documentation: {technology} ===");
        Console.ResetColor();
        Console.WriteLine();

        var articles = database.GetTechnologyArticles(technology);

        // Apply difficulty filter
        if (!string.IsNullOrEmpty(difficulty) && Enum.TryParse<DifficultyLevel>(difficulty, true, out var diff))
        {
            articles = articles.Where(a => a.Difficulty == diff).ToList();
        }

        articles = articles.Take(count).ToList();

        if (articles.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No articles found for {technology}. Try scraping first:");
            Console.WriteLine($"  career-intel docs --scrape --technology \"{technology}\"");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Found {articles.Count} articles:\n");

        DisplayArticles(articles);
    }

    private static void DisplayArticles(List<DocumentationArticle> articles)
    {
        for (int i = 0; i < articles.Count; i++)
        {
            var article = articles[i];

            Console.ForegroundColor = GetDifficultyColor(article.Difficulty);
            Console.Write($"{i + 1}. [{article.Difficulty}] ");
            Console.ResetColor();

            Console.Write($"[{article.Technology}] ");
            Console.WriteLine(article.Title);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   {article.Summary}");
            Console.ResetColor();

            if (article.Topics.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"   Topics: {string.Join(", ", article.Topics)}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   Source: {article.Source} | {article.EstimatedMinutes} min | {article.Category}");
            Console.WriteLine($"   {article.Url}");
            Console.ResetColor();

            Console.WriteLine();
        }
    }

    private static void ShowStatistics(DocumentationDatabase database)
    {
        var stats = database.GetStatistics();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Documentation Database Statistics ===");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine($"Total Articles: {stats.TotalArticles}");
        Console.WriteLine($"Total Sources: {stats.TotalSources}");
        Console.WriteLine($"Total Technologies: {stats.TotalTechnologies}");
        Console.WriteLine($"Last Updated: {stats.LastUpdated:yyyy-MM-dd HH:mm}");
        Console.WriteLine();

        if (stats.ArticlesBySource.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("By Source:");
            Console.ResetColor();
            foreach (var (source, count) in stats.ArticlesBySource.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"  {source,-25} {count,5} articles");
            }
            Console.WriteLine();
        }

        if (stats.ArticlesByTechnology.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("By Technology:");
            Console.ResetColor();
            foreach (var (tech, count) in stats.ArticlesByTechnology.OrderByDescending(kv => kv.Value))
            {
                Console.WriteLine($"  {tech,-25} {count,5} articles");
            }
            Console.WriteLine();
        }

        if (stats.ArticlesByDifficulty.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("By Difficulty:");
            Console.ResetColor();
            foreach (var (diff, count) in stats.ArticlesByDifficulty.OrderBy(kv => kv.Key))
            {
                Console.WriteLine($"  {diff,-25} {count,5} articles");
            }
        }
    }

    private static void ShowUsageHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Scrape docs:      career-intel docs --scrape --topic \"async/await\"");
        Console.WriteLine("  Search docs:      career-intel docs --search \"dependency injection\"");
        Console.WriteLine("  Learning path:    career-intel docs --learning-path --topic \"LINQ\" --count 5");
        Console.WriteLine("  By technology:    career-intel docs --technology \"C#\"");
        Console.WriteLine("  Show statistics:  career-intel docs --stats");
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
}
