using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Scrapers;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for scraping and analyzing remote job opportunities.
/// Usage:
///   career-intel remote --scrape --stack ".NET" --location "EU"
///   career-intel remote --scrape --stack "Node.js" --location "US,EU" --min-salary 100000
///   career-intel remote --boards
///   career-intel remote --analyze-stack ".NET"
///   career-intel remote --compare ".NET,Node.js,Go"
/// </summary>
public static class RemoteJobsCommand
{
    public static Command Create()
    {
        var scrapeOption = new Option<bool>(
            "--scrape",
            description: "Scrape remote job listings from multiple boards");

        var stackOption = new Option<string?>(
            "--stack",
            description: "Technology stack (e.g., '.NET', 'Node.js', 'Go')");

        var locationOption = new Option<string?>(
            "--location",
            description: "Preferred locations (e.g., 'EU', 'US', 'Worldwide')");

        var minSalaryOption = new Option<int>(
            "--min-salary",
            getDefaultValue: () => 0,
            description: "Minimum salary filter");

        var boardsOption = new Option<bool>(
            "--boards",
            description: "List all available remote job boards with rankings");

        var analyzeOption = new Option<string?>(
            "--analyze-stack",
            description: "Analyze remote job opportunities for a stack");

        var compareOption = new Option<string?>(
            "--compare",
            description: "Compare stacks for remote opportunities (comma-separated)");

        var countOption = new Option<int>(
            "--count",
            getDefaultValue: () => 20,
            description: "Number of jobs to show");

        var command = new Command("remote", "Scrape and analyze remote job opportunities")
        {
            scrapeOption,
            stackOption,
            locationOption,
            minSalaryOption,
            boardsOption,
            analyzeOption,
            compareOption,
            countOption
        };

        command.SetHandler(async (context) =>
        {
            var scrape = context.ParseResult.GetValueForOption(scrapeOption);
            var stack = context.ParseResult.GetValueForOption(stackOption);
            var location = context.ParseResult.GetValueForOption(locationOption);
            var minSalary = context.ParseResult.GetValueForOption(minSalaryOption);
            var boards = context.ParseResult.GetValueForOption(boardsOption);
            var analyzeStack = context.ParseResult.GetValueForOption(analyzeOption);
            var compare = context.ParseResult.GetValueForOption(compareOption);
            var count = context.ParseResult.GetValueForOption(countOption);

            await ExecuteAsync(scrape, stack, location, minSalary, boards, analyzeStack, compare, count);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool scrape, string? stack, string? location, int minSalary,
        bool boards, string? analyzeStack, string? compare, int count)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.RemoteJobsCommand");

        if (boards)
        {
            ShowAvailableBoards();
        }
        else if (!string.IsNullOrEmpty(analyzeStack))
        {
            AnalyzeStackOpportunities(analyzeStack);
        }
        else if (!string.IsNullOrEmpty(compare))
        {
            CompareStacks(compare);
        }
        else if (scrape)
        {
            await ScrapeRemoteJobsAsync(stack, location, minSalary, count, serviceProvider, logger);
        }
        else
        {
            ShowUsageHelp();
        }
    }

    private static void ShowAvailableBoards()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Top Remote Job Boards ==");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine("Ranked by quality, .NET friendliness, and EU compatibility:\n");

        var boards = RemoteJobAggregator.TopRemoteBoards
            .OrderByDescending(b => b.Priority)
            .ToList();

        foreach (var board in boards)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{board.Priority,2}/10 ");
            Console.ResetColor();
            Console.WriteLine($"{board.Name}");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"      {board.Description}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"      .NET: ");
            Console.ResetColor();
            Console.Write($"{board.DotNetFriendly}/10   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"EU: ");
            Console.ResetColor();
            Console.WriteLine($"{board.EuFriendly}/10");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"      Tags: {string.Join(", ", board.Tags)}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"      {board.Url}");
            Console.ResetColor();

            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\nTotal: {boards.Count} remote job boards");
        Console.ResetColor();
    }

    private static async Task ScrapeRemoteJobsAsync(
        string? stack, string? location, int minSalary, int count,
        ServiceProvider serviceProvider, ILogger logger)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Scraping Remote Jobs ===");
        Console.ResetColor();
        Console.WriteLine();

        var stacks = string.IsNullOrEmpty(stack) ? new[] { ".NET" } : stack.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var locations = string.IsNullOrEmpty(location) ? new[] { "Worldwide" } : location.Split(',', StringSplitOptions.RemoveEmptyEntries);

        Console.WriteLine($"Stack: {string.Join(", ", stacks)}");
        Console.WriteLine($"Location: {string.Join(", ", locations)}");
        if (minSalary > 0)
            Console.WriteLine($"Min Salary: ${minSalary:N0}");
        Console.WriteLine();

        var httpClient = serviceProvider.GetRequiredService<HttpClient>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var aggregatorLogger = loggerFactory.CreateLogger<RemoteJobAggregator>();

        var aggregator = new RemoteJobAggregator(httpClient, aggregatorLogger);

        Console.WriteLine("Scraping from multiple boards in parallel...");
        Console.WriteLine();

        var result = await aggregator.ScrapeAllAsync(stacks, locations, minSalary);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Scraped {result.AllJobs.Count} total jobs");
        Console.ResetColor();

        Console.WriteLine($"\nBreakdown by source:");
        foreach (var (source, jobCount) in result.JobsBySource.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"  {source,-20} {jobCount,3} jobs");
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"After filtering: {result.FilteredJobs.Count} matching jobs");
        Console.ResetColor();
        Console.WriteLine();

        if (result.FilteredJobs.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No jobs found matching your criteria. Try:");
            Console.WriteLine("  - Broader stack (e.g., 'Backend' instead of specific tech)");
            Console.WriteLine("  - Worldwide location");
            Console.WriteLine("  - Lower or no salary minimum");
            Console.ResetColor();
            return;
        }

        // Show top matching jobs
        var topJobs = result.FilteredJobs.Take(count).ToList();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Top {topJobs.Count} Matching Jobs:");
        Console.ResetColor();
        Console.WriteLine();

        for (int i = 0; i < topJobs.Count; i++)
        {
            var job = topJobs[i];

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{i + 1}. {job.Title}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   {job.Company}");
            Console.ResetColor();

            Console.Write($"   📍 {job.Country}");
            if (job.RemotePolicy == RemotePolicy.FullyRemote)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($" • 🏠 Fully Remote");
                Console.ResetColor();
            }
            Console.WriteLine();

            if (job.SalaryMin.HasValue || job.SalaryMax.HasValue)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                if (job.SalaryMin.HasValue && job.SalaryMax.HasValue)
                    Console.WriteLine($"   💰 ${job.SalaryMin:N0} - ${job.SalaryMax:N0}");
                else if (job.SalaryMin.HasValue)
                    Console.WriteLine($"   💰 From ${job.SalaryMin:N0}");
                Console.ResetColor();
            }

            if (job.RequiredSkills.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"   Skills: {string.Join(", ", job.RequiredSkills.Take(5))}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   Source: {job.SourcePlatform} • Posted: {job.PostedDate:MMM dd, yyyy}");
            Console.WriteLine($"   {job.Url}");
            Console.ResetColor();

            Console.WriteLine();
        }

        // Show recommended boards for user's criteria
        var recommendedBoards = RemoteJobAggregator.GetRecommendedBoards(locations, stacks);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n📋 Recommended boards for your search:");
        Console.ResetColor();
        foreach (var board in recommendedBoards.Take(5))
        {
            Console.WriteLine($"  • {board.Name} (.NET: {board.DotNetFriendly}/10, EU: {board.EuFriendly}/10)");
        }
    }

    private static void AnalyzeStackOpportunities(string stack)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Remote Opportunities: {stack} ===");
        Console.ResetColor();
        Console.WriteLine();

        var isDotNet = stack.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                       stack.Contains("C#", StringComparison.OrdinalIgnoreCase);

        if (isDotNet)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(".NET Remote Job Market Analysis:");
            Console.ResetColor();
            Console.WriteLine();

            Console.WriteLine("✓ Strengths:");
            Console.WriteLine("  • Enterprise companies (Microsoft, financial services)");
            Console.WriteLine("  • High salaries ($100k-$180k)");
            Console.WriteLine("  • Mature ecosystem");
            Console.WriteLine("  • Strong in US market");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠ Challenges:");
            Console.ResetColor();
            Console.WriteLine("  • Fewer remote-first startups");
            Console.WriteLine("  • More US-timezone requirements");
            Console.WriteLine("  • Less common in EU startups");
            Console.WriteLine("  • 3-4x fewer remote listings than Node.js/Go");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Best boards for .NET remote:");
            Console.ResetColor();
            var dotNetBoards = RemoteJobAggregator.TopRemoteBoards
                .OrderByDescending(b => b.DotNetFriendly)
                .Take(5);
            foreach (var board in dotNetBoards)
            {
                Console.WriteLine($"  • {board.Name} (.NET: {board.DotNetFriendly}/10)");
            }
        }
        else
        {
            Console.WriteLine($"Remote job opportunities for {stack}:");
            Console.WriteLine("  • Use --scrape to see actual listings");
            Console.WriteLine("  • Use --compare to compare with other stacks");
        }
    }

    private static void CompareStacks(string stacksToCompare)
    {
        var stacks = stacksToCompare.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Stack Comparison for Remote Work ===");
        Console.ResetColor();
        Console.WriteLine();

        var comparisons = new[]
        {
            new
            {
                Stack = ".NET / C#",
                RemoteJobs = "Medium (⭐⭐⭐)",
                EuFriendly = "Medium (⭐⭐⭐)",
                AvgSalary = "$120k-$150k",
                LearningCurve = "You already know it!",
                BestBoards = "Arc.dev, Wellfound, Remote.co"
            },
            new
            {
                Stack = "Node.js / TypeScript",
                RemoteJobs = "Very High (⭐⭐⭐⭐⭐)",
                EuFriendly = "Very High (⭐⭐⭐⭐⭐)",
                AvgSalary = "$100k-$140k",
                LearningCurve = "2-3 months (similar to C#)",
                BestBoards = "RemoteOK, WeWorkRemotely, Arc.dev"
            },
            new
            {
                Stack = "Go",
                RemoteJobs = "High (⭐⭐⭐⭐)",
                EuFriendly = "High (⭐⭐⭐⭐)",
                AvgSalary = "$130k-$160k",
                LearningCurve = "3-4 months (different paradigm)",
                BestBoards = "RemoteOK, Arc.dev, WeWorkRemotely"
            },
            new
            {
                Stack = "Python",
                RemoteJobs = "Very High (⭐⭐⭐⭐⭐)",
                EuFriendly = "High (⭐⭐⭐⭐)",
                AvgSalary = "$110k-$140k",
                LearningCurve = "2-3 months (easier syntax)",
                BestBoards = "RemoteOK, WeWorkRemotely, JustRemote"
            },
            new
            {
                Stack = "Java",
                RemoteJobs = "High (⭐⭐⭐⭐)",
                EuFriendly = "Medium (⭐⭐⭐)",
                AvgSalary = "$120k-$150k",
                LearningCurve = "1-2 months (very similar to C#)",
                BestBoards = "Arc.dev, Wellfound, Remote.co"
            }
        };

        foreach (var comparison in comparisons)
        {
            var isRequested = stacks.Any(s => comparison.Stack.Contains(s, StringComparison.OrdinalIgnoreCase));
            if (isRequested || stacks.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"📊 {comparison.Stack}");
                Console.ResetColor();
                Console.WriteLine($"   Remote Jobs:    {comparison.RemoteJobs}");
                Console.WriteLine($"   EU-Friendly:    {comparison.EuFriendly}");
                Console.WriteLine($"   Avg Salary:     {comparison.AvgSalary}");
                Console.WriteLine($"   Learning Curve: {comparison.LearningCurve}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"   Best Boards:    {comparison.BestBoards}");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("💡 Recommendation for EU Remote Work:");
        Console.ResetColor();
        Console.WriteLine("   1. Node.js/TypeScript - Best remote job availability");
        Console.WriteLine("   2. Python - Great for startups and EU companies");
        Console.WriteLine("   3. Go - High salary, growing remote market");
        Console.WriteLine("   4. .NET - Good but fewer EU-friendly remote options");
        Console.WriteLine("   5. Java - Enterprise-heavy, less remote-first culture");
    }

    private static void ShowUsageHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  List boards:      career-intel remote --boards");
        Console.WriteLine("  Scrape jobs:      career-intel remote --scrape --stack \".NET\" --location \"EU\"");
        Console.WriteLine("  Analyze stack:    career-intel remote --analyze-stack \".NET\"");
        Console.WriteLine("  Compare stacks:   career-intel remote --compare \".NET,Node.js,Go\"");
        Console.WriteLine("  With salary:      career-intel remote --scrape --stack \"Node.js\" --min-salary 100000");
    }
}
