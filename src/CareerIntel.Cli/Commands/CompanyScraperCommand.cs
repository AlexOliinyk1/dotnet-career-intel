using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Scrapers;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for scraping jobs from individual companies.
/// Usage:
///   career-intel companies --scrape-all --source "remotive.csv"
///   career-intel companies --scrape "GitLab" --url "https://about.gitlab.com/jobs/"
///   career-intel companies --batch companies.json
/// </summary>
public static class CompanyScraperCommand
{
    public static Command Create()
    {
        var scrapeOption = new Option<string?>(
            "--scrape",
            description: "Scrape a single company by name");

        var urlOption = new Option<string?>(
            "--url",
            description: "Company careers page URL");

        var batchOption = new Option<string?>(
            "--batch",
            description: "Batch scrape from CSV/JSON file (like Remotive spreadsheet)");

        var limitOption = new Option<int>(
            "--limit",
            getDefaultValue: () => 50,
            description: "Maximum number of companies to scrape");

        var outputOption = new Option<string?>(
            "--output",
            description: "Output file for results (JSON)");

        var detectOption = new Option<bool>(
            "--detect-ats",
            description: "Only detect ATS, don't scrape jobs");

        var command = new Command("scrape-company", "Scrape jobs from individual company career pages")
        {
            scrapeOption,
            urlOption,
            batchOption,
            limitOption,
            outputOption,
            detectOption
        };

        command.SetHandler(async (context) =>
        {
            var scrape = context.ParseResult.GetValueForOption(scrapeOption);
            var url = context.ParseResult.GetValueForOption(urlOption);
            var batch = context.ParseResult.GetValueForOption(batchOption);
            var limit = context.ParseResult.GetValueForOption(limitOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var detectAts = context.ParseResult.GetValueForOption(detectOption);

            await ExecuteAsync(scrape, url, batch, limit, output, detectAts);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string? scrape, string? url, string? batch, int limit, string? output, bool detectAts)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.CompanyScraperCommand");

        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var scraper = new UniversalCompanyScraper(httpClient,
            loggerFactory.CreateLogger<UniversalCompanyScraper>());

        if (!string.IsNullOrEmpty(scrape) && !string.IsNullOrEmpty(url))
        {
            await ScrapeSingleCompanyAsync(scraper, scrape, url, detectAts);
        }
        else if (!string.IsNullOrEmpty(batch))
        {
            await ScrapeBatchAsync(scraper, batch, limit, output, detectAts);
        }
        else
        {
            ShowUsageHelp();
        }
    }

    private static async Task ScrapeSingleCompanyAsync(
        UniversalCompanyScraper scraper,
        string companyName,
        string url,
        bool detectOnly)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Scraping {companyName} ===");
        Console.ResetColor();
        Console.WriteLine();

        var result = await scraper.ScrapeCompanyAsync(companyName, url);

        Console.WriteLine($"Company: {result.CompanyName}");
        Console.WriteLine($"URL: {result.CareersUrl}");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"ATS: {result.ATSType}");
        Console.ResetColor();

        if (result.ATSIdentifier != null)
            Console.WriteLine($"ATS ID: {result.ATSIdentifier}");

        if (!detectOnly)
        {
            Console.WriteLine();
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Found {result.Jobs.Count} jobs");
                Console.ResetColor();
                Console.WriteLine();

                foreach (var job in result.Jobs.Take(10))
                {
                    Console.WriteLine($"• {job.Title}");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  {job.Country} | {job.RemotePolicy}");
                    if (job.RequiredSkills.Count > 0)
                        Console.WriteLine($"  Skills: {string.Join(", ", job.RequiredSkills.Take(5))}");
                    Console.WriteLine($"  {job.Url}");
                    Console.ResetColor();
                    Console.WriteLine();
                }

                if (result.Jobs.Count > 10)
                    Console.WriteLine($"... and {result.Jobs.Count - 10} more jobs");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Failed to scrape jobs");
                if (result.Error != null)
                    Console.WriteLine($"Error: {result.Error}");
                Console.ResetColor();
            }
        }
    }

    private static async Task ScrapeBatchAsync(
        UniversalCompanyScraper scraper,
        string batchFile,
        int limit,
        string? output,
        bool detectOnly)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Batch Scraping from {batchFile} ===");
        Console.ResetColor();
        Console.WriteLine();

        // Parse company list
        var companies = await ParseCompanyListAsync(batchFile);

        if (companies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No companies found in file");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Found {companies.Count} companies in file");
        Console.WriteLine($"Scraping up to {limit} companies...");
        Console.WriteLine();

        var results = new List<CompanyJobsResult>();
        var processed = 0;

        foreach (var company in companies.Take(limit))
        {
            processed++;
            Console.Write($"[{processed}/{Math.Min(limit, companies.Count)}] {company.Name}... ");

            var result = await scraper.ScrapeCompanyAsync(company.Name, company.CareersUrl);
            results.Add(result);

            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ {result.Jobs.Count} jobs ({result.ATSType})");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"✗ {result.ATSType}");
                Console.ResetColor();
            }

            // Rate limiting - be nice to servers
            await Task.Delay(1000);
        }

        // Summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Summary ===");
        Console.ResetColor();
        Console.WriteLine($"Companies scraped: {results.Count}");
        Console.WriteLine($"Successful: {results.Count(r => r.Success)}");
        Console.WriteLine($"Total jobs found: {results.Sum(r => r.Jobs.Count)}");
        Console.WriteLine();

        var atsCounts = results.GroupBy(r => r.ATSType)
            .OrderByDescending(g => g.Count())
            .ToList();

        Console.WriteLine("ATS Systems:");
        foreach (var ats in atsCounts)
        {
            Console.WriteLine($"  {ats.Key,-15} {ats.Count(),3} companies");
        }

        // Save to file if requested
        if (!string.IsNullOrEmpty(output))
        {
            var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(output, json);
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Results saved to {output}");
            Console.ResetColor();
        }
    }

    private static async Task<List<CompanyInfo>> ParseCompanyListAsync(string filePath)
    {
        var companies = new List<CompanyInfo>();

        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"File not found: {filePath}");
            Console.ResetColor();
            return companies;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".json")
        {
            // Parse JSON
            var json = await File.ReadAllTextAsync(filePath);
            companies = JsonSerializer.Deserialize<List<CompanyInfo>>(json) ?? [];
        }
        else if (ext == ".csv")
        {
            // Parse CSV (simple implementation)
            var lines = await File.ReadAllLinesAsync(filePath);
            foreach (var line in lines.Skip(1)) // Skip header
            {
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    companies.Add(new CompanyInfo
                    {
                        Name = parts[0].Trim().Trim('"'),
                        CareersUrl = parts[1].Trim().Trim('"')
                    });
                }
            }
        }

        return companies;
    }

    private static void ShowUsageHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Single company:   career-intel companies --scrape \"GitLab\" --url \"https://about.gitlab.com/jobs/\"");
        Console.WriteLine("  Detect ATS:       career-intel companies --scrape \"GitLab\" --url \"...\" --detect-ats");
        Console.WriteLine("  Batch scrape:     career-intel companies --batch companies.json --limit 50");
        Console.WriteLine("  Save results:     career-intel companies --batch companies.csv --output results.json");
        Console.WriteLine();
        Console.WriteLine("File formats:");
        Console.WriteLine("  JSON: [{\"Name\": \"GitLab\", \"CareersUrl\": \"https://...\"}]");
        Console.WriteLine("  CSV:  Name,CareersUrl");
        Console.WriteLine("        GitLab,https://about.gitlab.com/jobs/");
    }
}

public class CompanyInfo
{
    public string Name { get; set; } = string.Empty;
    public string CareersUrl { get; set; } = string.Empty;
}
