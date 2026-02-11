using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Scrapers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// Detects intermediary (outsourcing/staffing) job postings and checks
/// if the end client has the same position posted directly.
/// Skip the middleman — apply direct for better comp.
///
/// Usage:
///   career-intel bridge-check                              -- Analyze latest vacancies
///   career-intel bridge-check --client "Spotify"           -- Force client name
///   career-intel bridge-check --client "Spotify" --client-url "https://..."
///   career-intel bridge-check --detect-only                -- Only detect, don't scrape
///   career-intel bridge-check --list-intermediaries        -- Show known intermediaries
/// </summary>
public static class BridgeCheckCommand
{
    public static Command Create()
    {
        var clientOption = new Option<string?>(
            "--client",
            description: "Override detected client name (skip auto-detection)");

        var clientUrlOption = new Option<string?>(
            "--client-url",
            description: "Client's careers page URL (skip URL discovery)");

        var detectOnlyOption = new Option<bool>(
            "--detect-only",
            description: "Only detect intermediaries and clients, don't scrape direct postings");

        var listOption = new Option<bool>(
            "--list-intermediaries",
            description: "List all known intermediary companies");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 20,
            description: "Number of vacancies to analyze");

        var command = new Command("bridge-check",
            "Detect intermediary postings and find direct openings at end clients")
        {
            clientOption,
            clientUrlOption,
            detectOnlyOption,
            listOption,
            topOption
        };

        command.SetHandler(async (context) =>
        {
            var client = context.ParseResult.GetValueForOption(clientOption);
            var clientUrl = context.ParseResult.GetValueForOption(clientUrlOption);
            var detectOnly = context.ParseResult.GetValueForOption(detectOnlyOption);
            var list = context.ParseResult.GetValueForOption(listOption);
            var top = context.ParseResult.GetValueForOption(topOption);

            await ExecuteAsync(client, clientUrl, detectOnly, list, top);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string? client, string? clientUrl, bool detectOnly, bool list, int top)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("CareerIntel.Cli.BridgeCheckCommand");

        var detector = new EndClientDetector(
            loggerFactory.CreateLogger<EndClientDetector>(),
            Program.DataDirectory);

        if (list)
        {
            ShowIntermediaries(detector);
            return;
        }

        // If user specified --client, do a direct check for that client
        if (!string.IsNullOrEmpty(client))
        {
            await CheckSpecificClientAsync(client, clientUrl, detector, loggerFactory, serviceProvider);
            return;
        }

        // Otherwise, analyze latest vacancies
        await AnalyzeVacanciesAsync(detector, detectOnly, top, clientUrl, loggerFactory, serviceProvider);
    }

    private static void ShowIntermediaries(EndClientDetector detector)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Known Intermediary Companies ===");
        Console.ResetColor();
        Console.WriteLine();

        var grouped = detector.KnownIntermediaries
            .GroupBy(i => i.Type)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{group.Key}]");
            Console.ResetColor();

            foreach (var company in group.OrderBy(c => c.Name))
            {
                Console.Write($"    {company.Name,-30}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" {company.Region,-20}");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($" ~{company.EstimatedMarkup:P0} markup");
                Console.ResetColor();
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Total: {detector.KnownIntermediaries.Count} intermediaries tracked");
    }

    private static async Task CheckSpecificClientAsync(
        string clientName,
        string? clientUrl,
        EndClientDetector detector,
        ILoggerFactory loggerFactory,
        ServiceProvider serviceProvider)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Bridge Check: {clientName} ===");
        Console.ResetColor();
        Console.WriteLine();

        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var companyScraper = new UniversalCompanyScraper(httpClient,
            loggerFactory.CreateLogger<UniversalCompanyScraper>());
        var checker = new DirectPositionChecker(httpClient, companyScraper,
            loggerFactory.CreateLogger<DirectPositionChecker>());

        // Create a synthetic detection for the specified client
        var detection = new EndClientDetection
        {
            DetectedClientName = clientName,
            Method = DetectionMethod.UserProvided,
            Confidence = 1.0,
            Intermediary = new IntermediaryCompany { Name = "Manual check" },
            OriginalVacancy = new JobVacancy { Title = "Manual check", Company = "N/A" }
        };

        Console.WriteLine("Discovering careers page...");
        var result = await checker.CheckForDirectPostingsAsync(detection, clientUrl);

        DisplayDirectCheckResult(result, null);
    }

    private static async Task AnalyzeVacanciesAsync(
        EndClientDetector detector,
        bool detectOnly,
        int top,
        string? clientUrl,
        ILoggerFactory loggerFactory,
        ServiceProvider serviceProvider)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Bridge Check: Skip the Middleman ===");
        Console.ResetColor();
        Console.WriteLine();

        // Load latest vacancies
        var vacancies = LoadLatestVacancies();
        if (vacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No vacancies found. Run 'career-intel scan' first.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Analyzing {Math.Min(top, vacancies.Count)} of {vacancies.Count} vacancies...");
        Console.WriteLine();

        var detections = detector.AnalyzeBatch(vacancies.Take(top));

        var withClient = detections.Where(d => !string.IsNullOrEmpty(d.DetectedClientName)).ToList();
        var withoutClient = detections.Where(d => string.IsNullOrEmpty(d.DetectedClientName)).ToList();

        // Summary
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Found {detections.Count} intermediary postings");
        if (withClient.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  {withClient.Count} with detected end client");
        }
        if (withoutClient.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {withoutClient.Count} with hidden/anonymous client");
        }
        Console.ResetColor();
        Console.WriteLine();

        // Show intermediary breakdown
        var byIntermediary = detections.GroupBy(d => d.Intermediary.Name)
            .OrderByDescending(g => g.Count());

        foreach (var group in byIntermediary)
        {
            Console.Write($"  {group.Key}: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"{group.Count()} postings");
            Console.ResetColor();
        }
        Console.WriteLine();

        // Show detected clients
        if (withClient.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- Detected End Clients ---");
            Console.ResetColor();
            Console.WriteLine();

            var index = 0;
            foreach (var detection in withClient)
            {
                index++;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {index}. ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(detection.DetectedClientName);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" (via {detection.Intermediary.Name})");
                Console.ResetColor();
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     Role: {detection.OriginalVacancy.Title}");
                Console.Write($"     Detection: {detection.Method} ");
                Console.ForegroundColor = detection.Confidence >= 0.9 ? ConsoleColor.Green :
                    detection.Confidence >= 0.7 ? ConsoleColor.Yellow : ConsoleColor.Red;
                Console.WriteLine($"({detection.Confidence:P0} confidence)");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     Snippet: \"{detection.MatchedSnippet}\"");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"     Estimated intermediary markup: ~{detection.EstimatedUpliftPercent:F0}%");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        // If detect-only, stop here
        if (detectOnly)
        {
            if (withClient.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Tip: Run without --detect-only to scrape direct postings from these clients.");
                Console.WriteLine("     Or: career-intel bridge-check --client \"CompanyName\" --client-url \"https://...\"");
                Console.ResetColor();
            }
            return;
        }

        // Scrape direct postings for detected clients
        if (withClient.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- Checking Direct Postings ---");
            Console.ResetColor();
            Console.WriteLine();

            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            var companyScraper = new UniversalCompanyScraper(httpClient,
                loggerFactory.CreateLogger<UniversalCompanyScraper>());
            var checker = new DirectPositionChecker(httpClient, companyScraper,
                loggerFactory.CreateLogger<DirectPositionChecker>());

            foreach (var detection in withClient)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  Checking {detection.DetectedClientName}... ");
                Console.ResetColor();

                var result = await checker.CheckForDirectPostingsAsync(detection, clientUrl);
                DisplayDirectCheckResult(result, detection);

                // Rate limiting
                await Task.Delay(1500);
            }
        }
    }

    private static void DisplayDirectCheckResult(DirectCheckResult result, EndClientDetection? detection)
    {
        if (result.Error is not null && !result.DirectPostingFound)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(result.Error);
            Console.ResetColor();

            if (result.AllDirectPostings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Found {result.AllDirectPostings.Count} direct postings (no close match to intermediary role):");
                Console.ResetColor();

                foreach (var job in result.AllDirectPostings.Take(5))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"      - {job.Title}");
                    if (!string.IsNullOrEmpty(job.Url))
                        Console.WriteLine($"        {job.Url}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            return;
        }

        if (result.CareersUrl is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Careers: {result.CareersUrl}");
            Console.WriteLine($"  ATS: {result.ATSType ?? "Unknown"}");
            Console.ResetColor();
        }

        if (result.DirectPostingFound)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine($"  >>> DIRECT POSTING FOUND! <<<");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var match in result.Matches.Take(3))
            {
                // Side by side comparison
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("  +-------------------------------+-------------------------------+");
                Console.Write("  | ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"{"INTERMEDIARY",-29}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" | ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{"DIRECT",-29}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(" |");
                Console.WriteLine("  +-------------------------------+-------------------------------+");

                var intTitle = Truncate(match.IntermediaryPosting.Title ?? "N/A", 29);
                var dirTitle = Truncate(match.DirectPosting.Title ?? "N/A", 29);
                Console.Write($"  | {intTitle,-29} | {dirTitle,-29} |");
                Console.WriteLine();

                var intCompany = Truncate(match.IntermediaryPosting.Company ?? "N/A", 29);
                var dirCompany = Truncate(match.DirectPosting.Company ?? result.ClientName, 29);
                Console.Write($"  | {intCompany,-29} | {dirCompany,-29} |");
                Console.WriteLine();

                var intSkills = Truncate(string.Join(", ", (match.IntermediaryPosting.RequiredSkills ?? []).Take(4)), 29);
                var dirSkills = Truncate(string.Join(", ", (match.DirectPosting.RequiredSkills ?? []).Take(4)), 29);
                Console.Write($"  | {intSkills,-29} | {dirSkills,-29} |");
                Console.WriteLine();

                if (match.IntermediaryPosting.SalaryMin.HasValue || match.DirectPosting.SalaryMin.HasValue)
                {
                    var intSalary = FormatSalary(match.IntermediaryPosting);
                    var dirSalary = FormatSalary(match.DirectPosting);
                    Console.Write($"  | {Truncate(intSalary, 29),-29} | {Truncate(dirSalary, 29),-29} |");
                    Console.WriteLine();
                }

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("  +-------------------------------+-------------------------------+");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Title match: {match.TitleSimilarity:P0} | Skill overlap: {match.SkillOverlap:P0} | Overall: {match.OverallConfidence:P0}");
                Console.ResetColor();

                if (detection is not null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  Estimated savings: ~{detection.EstimatedUpliftPercent:F0}% markup avoided");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  APPLY DIRECT: ");
                Console.ResetColor();
                Console.WriteLine(match.DirectPosting.Url ?? result.CareersUrl ?? "check careers page");
                Console.WriteLine();
            }

            if (result.Matches.Count > 3)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  ... and {result.Matches.Count - 3} more potential matches");
                Console.ResetColor();
            }
        }
        else if (result.AllDirectPostings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  No close match, but {result.AllDirectPostings.Count} other openings found:");
            Console.ResetColor();

            foreach (var job in result.AllDirectPostings.Take(5))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    - {job.Title}");
                if (!string.IsNullOrEmpty(job.Url))
                    Console.WriteLine($"      {job.Url}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  No direct postings found");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private static List<JobVacancy> LoadLatestVacancies()
    {
        var dataDir = Program.DataDirectory;
        if (!Directory.Exists(dataDir))
            return [];

        // Find the most recent vacancies file
        var files = Directory.GetFiles(dataDir, "vacancies-*.json")
            .OrderByDescending(f => f)
            .ToList();

        if (files.Count == 0)
            return [];

        try
        {
            var json = File.ReadAllText(files[0]);
            return JsonSerializer.Deserialize<List<JobVacancy>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string FormatSalary(JobVacancy v)
    {
        if (!v.SalaryMin.HasValue && !v.SalaryMax.HasValue)
            return "Not disclosed";

        var currency = v.SalaryCurrency ?? "USD";
        if (v.SalaryMin.HasValue && v.SalaryMax.HasValue)
            return $"{currency} {v.SalaryMin:N0}-{v.SalaryMax:N0}";
        if (v.SalaryMin.HasValue)
            return $"{currency} {v.SalaryMin:N0}+";
        return $"{currency} up to {v.SalaryMax:N0}";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLength ? text : text[..(maxLength - 2)] + "..";
    }
}
