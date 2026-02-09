using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Persistence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that runs job scrapers and saves results to a JSON file.
/// Usage: career-intel scan [--platform name] [--max-pages n] [--output path]
/// </summary>
public static class ScanCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var platformOption = new Option<string?>(
            "--platform",
            description: "Specific platform to scrape (e.g., djinni). Omit to scrape all.");

        var maxPagesOption = new Option<int>(
            "--max-pages",
            getDefaultValue: () => 5,
            description: "Maximum number of pages to scrape per platform");

        var outputOption = new Option<string?>(
            "--output",
            description: "Output file path. Defaults to data/vacancies-{date}.json");

        var command = new Command("scan", "Scrape job vacancies from supported platforms")
        {
            platformOption,
            maxPagesOption,
            outputOption
        };

        command.SetHandler(ExecuteAsync, platformOption, maxPagesOption, outputOption);

        return command;
    }

    private static async Task ExecuteAsync(string? platform, int maxPages, string? output)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CareerIntel.Cli.ScanCommand");
        var scrapers = serviceProvider.GetServices<IJobScraper>().ToList();

        logger.LogInformation("Starting scan with {Count} registered scrapers", scrapers.Count);

        if (!string.IsNullOrEmpty(platform))
        {
            scrapers = scrapers
                .Where(s => s.PlatformName.Equals(platform, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (scrapers.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: No scraper found for platform '{platform}'");
                Console.ResetColor();
                return;
            }
        }

        var allVacancies = new List<JobVacancy>();

        foreach (var scraper in scrapers)
        {
            Console.WriteLine($"\nScraping {scraper.PlatformName}...");

            try
            {
                var vacancies = await scraper.ScrapeAsync(maxPages: maxPages);
                allVacancies.AddRange(vacancies);
                Console.WriteLine($"  Found {vacancies.Count} vacancies on {scraper.PlatformName}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape {Platform}", scraper.PlatformName);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Warning: {scraper.PlatformName} scraping failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Determine output path
        var outputPath = output ?? Path.Combine(
            Program.DataDirectory,
            $"vacancies-{DateTime.Now:yyyy-MM-dd}.json");

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Save results to JSON
        var json = JsonSerializer.Serialize(allVacancies, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);

        // Persist to SQLite database
        if (allVacancies.Count > 0)
        {
            try
            {
                await Program.EnsureDatabaseAsync(serviceProvider);
                var vacancyRepo = serviceProvider.GetRequiredService<VacancyRepository>();
                await vacancyRepo.SaveVacanciesAsync(allVacancies);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Persisted {allVacancies.Count} vacancies to SQLite database.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist vacancies to database");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Warning: DB persistence failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Print summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Scan Summary ===");
        Console.ResetColor();
        Console.WriteLine($"Total vacancies found: {allVacancies.Count}");

        // By platform
        var byPlatform = allVacancies.GroupBy(v => v.SourcePlatform);
        foreach (var group in byPlatform)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        // By seniority
        var bySeniority = allVacancies
            .GroupBy(v => v.SeniorityLevel)
            .OrderByDescending(g => g.Count());

        Console.WriteLine("\nBy seniority:");
        foreach (var group in bySeniority)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        Console.WriteLine($"\nResults saved to: {outputPath}");
    }
}
