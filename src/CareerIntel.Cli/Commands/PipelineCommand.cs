using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using CareerIntel.Persistence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that runs the full career intelligence pipeline:
/// scan -> analyze -> match -> readiness in a single invocation.
/// Usage: career-intel run-all [--max-pages n] [--min-score n] [--top n] [--notify]
/// </summary>
public static class PipelineCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var maxPagesOption = new Option<int>(
            "--max-pages",
            getDefaultValue: () => 5,
            description: "Maximum number of pages to scrape per platform");

        var minScoreOption = new Option<double>(
            "--min-score",
            getDefaultValue: () => 30,
            description: "Minimum match score threshold (0-100)");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 10,
            description: "Number of top matches to display and assess readiness for");

        var command = new Command("run-all", "Run the full pipeline: scan -> analyze -> match -> readiness")
        {
            maxPagesOption,
            minScoreOption,
            topOption
        };

        command.SetHandler(ExecuteAsync, maxPagesOption, minScoreOption, topOption);

        return command;
    }

    private static async Task ExecuteAsync(int maxPages, double minScore, int top)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.PipelineCommand");

        await Program.EnsureDatabaseAsync(serviceProvider);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("         CAREER INTELLIGENCE PIPELINE                      ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // ─── STEP 1: SCAN ────────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[1/4] SCANNING job boards...");
        Console.ResetColor();

        var scrapers = serviceProvider.GetServices<IJobScraper>().ToList();
        var allVacancies = new List<JobVacancy>();

        foreach (var scraper in scrapers)
        {
            Console.Write($"  {scraper.PlatformName}... ");
            try
            {
                var vacancies = await scraper.ScrapeAsync(maxPages: maxPages);
                allVacancies.AddRange(vacancies);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{vacancies.Count} vacancies");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to scrape {Platform}", scraper.PlatformName);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"FAILED: {ex.Message}");
                Console.ResetColor();
            }
        }

        if (allVacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nNo vacancies found. Pipeline cannot continue.");
            Console.ResetColor();
            return;
        }

        // ─── ELIGIBILITY GATE ──────────────────────────────────────────
        // Hard filter: B2B/contractor only, remote from Ukraine, no geo restrictions
        var totalScraped = allVacancies.Count;
        var eligible = EligibilityGate.Filter(allVacancies);
        allVacancies = eligible.ToList();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Eligibility gate: {allVacancies.Count}/{totalScraped} vacancies passed (B2B/contractor, remote, no geo restrictions)");
        Console.ResetColor();

        if (allVacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nNo eligible vacancies after B2B/contractor filter. Pipeline cannot continue.");
            Console.ResetColor();
            return;
        }

        // Save scan results
        var vacanciesPath = Path.Combine(
            Program.DataDirectory,
            $"vacancies-{DateTime.Now:yyyy-MM-dd}.json");

        var vacanciesJson = JsonSerializer.Serialize(allVacancies, JsonOptions);
        await File.WriteAllTextAsync(vacanciesPath, vacanciesJson);

        // Persist to DB
        try
        {
            var vacancyRepo = serviceProvider.GetRequiredService<VacancyRepository>();
            await vacancyRepo.SaveVacanciesAsync(allVacancies);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DB persistence failed for vacancies");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Total: {allVacancies.Count} vacancies scraped\n");
        Console.ResetColor();

        // ─── STEP 2: ANALYZE ─────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[2/4] ANALYZING market trends...");
        Console.ResetColor();

        var analyzer = serviceProvider.GetRequiredService<ISkillAnalyzer>();
        var skills = await analyzer.AnalyzeSkillDemandAsync(allVacancies);
        var snapshot = await analyzer.GenerateSnapshotAsync(allVacancies);

        // Print top 10 skills
        var topSkills = skills.Take(10).ToList();
        for (var i = 0; i < topSkills.Count; i++)
        {
            var skill = topSkills[i];
            var bar = new string('#', (int)(skill.MarketDemandScore / 10));
            Console.WriteLine($"  {i + 1,2}. {skill.SkillName,-25} {skill.MarketDemandScore,5:F1}% {bar}");
        }

        // Save snapshot
        var snapshotPath = Path.Combine(
            Program.DataDirectory,
            $"snapshot-{DateTime.Now:yyyy-MM-dd}.json");
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(snapshotPath, snapshotJson);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Snapshot saved. {snapshot.TopSkillCombinations.Count} skill combos identified.\n");
        Console.ResetColor();

        // ─── STEP 3: MATCH ───────────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[3/4] MATCHING against your profile...");
        Console.ResetColor();

        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        await matchEngine.ReloadProfileAsync();

        var ranked = matchEngine.RankVacancies(allVacancies, minScore);
        var topMatches = ranked.Take(top).ToList();

        if (topMatches.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No matches above threshold.\n");
            Console.ResetColor();
        }
        else
        {
            foreach (var vacancy in topMatches)
            {
                var score = vacancy.MatchScore!;
                var actionColor = score.RecommendedAction switch
                {
                    RecommendedAction.Apply => ConsoleColor.Green,
                    RecommendedAction.PrepareAndApply => ConsoleColor.Yellow,
                    _ => ConsoleColor.Gray
                };

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  [{score.OverallScore:F0}] ");
                Console.ForegroundColor = actionColor;
                Console.Write($"({score.ActionLabel}) ");
                Console.ResetColor();
                Console.WriteLine($"{vacancy.Title} at {vacancy.Company}");
            }

            var applyCount = ranked.Count(v =>
                v.MatchScore?.RecommendedAction == RecommendedAction.Apply);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  {applyCount} ready to apply, {ranked.Count} total matches\n");
            Console.ResetColor();
        }

        // ─── STEP 4: READINESS ───────────────────────────────────────────
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[4/4] ASSESSING offer readiness...");
        Console.ResetColor();

        if (topMatches.Count > 0)
        {
            var readinessEngine = serviceProvider.GetRequiredService<OfferReadinessEngine>();

            var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
            var profileJson = await File.ReadAllTextAsync(profilePath);
            var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UserProfile();

            var feedbackPath = Path.Combine(Program.DataDirectory, "interview-feedback.json");
            var interviewHistory = await LoadJsonListAsync<InterviewFeedback>(feedbackPath);

            foreach (var vacancy in topMatches.Take(5))
            {
                var matchScore = vacancy.MatchScore ?? matchEngine.ComputeMatch(vacancy);
                var readiness = readinessEngine.Compute(vacancy, profile, matchScore, interviewHistory, null);

                var readinessColor = readiness.ReadinessPercent switch
                {
                    >= 80 => ConsoleColor.Green,
                    >= 50 => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };

                Console.Write($"  {vacancy.Title,-40} ");
                Console.ForegroundColor = readinessColor;
                Console.Write($"{readiness.ReadinessPercent:F0}% ready ");
                Console.ResetColor();

                var timingLabel = readiness.Timing switch
                {
                    RecommendedTiming.ApplyNow => "APPLY NOW",
                    RecommendedTiming.ApplyIn1To2Weeks => "1-2 weeks",
                    RecommendedTiming.ApplyIn3To4Weeks => "3-4 weeks",
                    RecommendedTiming.SkillUpFirst => "Skill up",
                    _ => "Skip"
                };

                Console.ForegroundColor = readinessColor;
                Console.WriteLine($"[{timingLabel}]");
                Console.ResetColor();
            }
        }

        // ─── SUMMARY ─────────────────────────────────────────────────────
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("  PIPELINE COMPLETE");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine($"  Vacancies scraped:  {allVacancies.Count}");
        Console.WriteLine($"  Matches found:      {ranked.Count}");
        Console.WriteLine($"  Data saved to:      {Program.DataDirectory}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }

    private static async Task<List<T>> LoadJsonListAsync<T>(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }
}
