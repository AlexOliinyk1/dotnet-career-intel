using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Notifications;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that matches vacancies against the user profile and displays ranked results.
/// Usage: career-intel match [--input path] [--min-score n] [--top n] [--notify]
/// Deal-breaker filters: --remote-only, --min-salary, --no-relocation, --allow-geo
/// </summary>
public static class MatchCommand
{
    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var minScoreOption = new Option<double>(
            "--min-score",
            getDefaultValue: () => 30,
            description: "Minimum match score threshold (0-100)");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 15,
            description: "Number of top matches to display");

        var notifyOption = new Option<bool>(
            "--notify",
            getDefaultValue: () => false,
            description: "Send results via configured notification channels (Telegram/Email)");

        // Deal-breaker filters
        var remoteOnlyOption = new Option<bool>(
            "--remote-only",
            getDefaultValue: () => false,
            description: "DEAL-BREAKER: Only show fully remote or hybrid positions");

        var minSalaryOption = new Option<decimal?>(
            "--min-salary",
            description: "DEAL-BREAKER: Minimum acceptable salary (will filter out lower or unspecified)");

        var noRelocationOption = new Option<bool>(
            "--no-relocation",
            getDefaultValue: () => false,
            description: "DEAL-BREAKER: Exclude jobs requiring relocation");

        var allowGeoOption = new Option<string?>(
            "--allow-geo",
            description: "DEAL-BREAKER: Only show jobs allowing this location (e.g., 'Ukraine', 'Worldwide', 'Europe')");

        var excludeB2BOption = new Option<bool>(
            "--exclude-b2b",
            getDefaultValue: () => false,
            description: "DEAL-BREAKER: Exclude B2B/contract positions");

        var command = new Command("match", "Match vacancies against your profile and rank results")
        {
            inputOption,
            minScoreOption,
            topOption,
            notifyOption,
            remoteOnlyOption,
            minSalaryOption,
            noRelocationOption,
            allowGeoOption,
            excludeB2BOption
        };

        command.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption);
            var minScore = context.ParseResult.GetValueForOption(minScoreOption);
            var top = context.ParseResult.GetValueForOption(topOption);
            var notify = context.ParseResult.GetValueForOption(notifyOption);
            var remoteOnly = context.ParseResult.GetValueForOption(remoteOnlyOption);
            var minSalary = context.ParseResult.GetValueForOption(minSalaryOption);
            var noRelocation = context.ParseResult.GetValueForOption(noRelocationOption);
            var allowGeo = context.ParseResult.GetValueForOption(allowGeoOption);
            var excludeB2B = context.ParseResult.GetValueForOption(excludeB2BOption);

            await ExecuteAsync(input, minScore, top, notify, remoteOnly, minSalary, noRelocation, allowGeo, excludeB2B);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string? input,
        double minScore,
        int top,
        bool notify,
        bool remoteOnly,
        decimal? minSalary,
        bool noRelocation,
        string? allowGeo,
        bool excludeB2B)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CareerIntel.Cli.MatchCommand");
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();

        // Resolve input file
        var inputPath = input ?? FindLatestVacanciesFile();

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        // Check profile exists
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Profile not found at {profilePath}");
            Console.WriteLine("Please create your profile from the template in data/my-profile.json");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Loading vacancies from: {inputPath}");
        Console.WriteLine($"Loading profile from: {profilePath}");

        // Load profile
        await matchEngine.ReloadProfileAsync();

        // Load vacancies
        var json = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        if (vacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: No vacancies found in the input file.");
            Console.ResetColor();
            return;
        }

        var originalCount = vacancies.Count;

        // Apply deal-breaker filters
        var filtered = ApplyDealBreakerFilters(
            vacancies,
            remoteOnly,
            minSalary,
            noRelocation,
            allowGeo,
            excludeB2B);

        if (filtered.Count < originalCount)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Deal-breaker filters: {originalCount} -> {filtered.Count} vacancies");
            Console.ResetColor();

            if (remoteOnly)
                Console.WriteLine("  ✓ Remote-only filter applied");
            if (minSalary.HasValue)
                Console.WriteLine($"  ✓ Minimum salary: {minSalary:N0}");
            if (noRelocation)
                Console.WriteLine("  ✓ No relocation required");
            if (!string.IsNullOrEmpty(allowGeo))
                Console.WriteLine($"  ✓ Geo filter: {allowGeo}");
            if (excludeB2B)
                Console.WriteLine("  ✓ Excluding B2B/contract positions");

            Console.WriteLine();
        }

        if (filtered.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No vacancies passed deal-breaker filters.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Matching {filtered.Count} vacancies (min score: {minScore})...\n");

        // Rank vacancies (use filtered list)
        var ranked = matchEngine.RankVacancies(filtered, minScore);
        var topMatches = ranked.Take(top).ToList();

        if (topMatches.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No matches found above the minimum score threshold.");
            Console.ResetColor();
            return;
        }

        // Display results
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"=== Top {topMatches.Count} Matches ===");
        Console.ResetColor();
        Console.WriteLine();

        for (var i = 0; i < topMatches.Count; i++)
        {
            var vacancy = topMatches[i];
            var score = vacancy.MatchScore!;

            PrintMatch(i + 1, vacancy, score);
        }

        // Summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Total matches above {minScore}: {ranked.Count}");
        Console.WriteLine($"Showing top {topMatches.Count}");
        Console.ResetColor();

        var applyCount = ranked.Count(v =>
            v.MatchScore?.RecommendedAction == RecommendedAction.Apply);
        var prepareCount = ranked.Count(v =>
            v.MatchScore?.RecommendedAction == RecommendedAction.PrepareAndApply);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Apply Now: {applyCount}");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Prepare & Apply: {prepareCount}");
        Console.ResetColor();

        // Notify if requested
        if (notify)
        {
            await SendNotificationsAsync(serviceProvider, topMatches, logger);
        }
    }

    private static void PrintMatch(int rank, JobVacancy vacancy, MatchScore score)
    {
        var actionColor = score.RecommendedAction switch
        {
            RecommendedAction.Apply => ConsoleColor.Green,
            RecommendedAction.PrepareAndApply => ConsoleColor.Yellow,
            RecommendedAction.SkillUpFirst => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Gray
        };

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"  #{rank} ");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"[{score.OverallScore:F0}/100] ");
        Console.ForegroundColor = actionColor;
        Console.Write($"({score.ActionLabel}) ");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine($"     {vacancy.Title} at {vacancy.Company}");

        if (vacancy.SalaryMin.HasValue || vacancy.SalaryMax.HasValue)
        {
            var salary = FormatSalary(vacancy);
            Console.WriteLine($"     Salary: {salary}");
        }

        if (score.MatchingSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"     Matching: {string.Join(", ", score.MatchingSkills.Take(6))}");
            Console.ResetColor();
        }

        if (score.MissingSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"     Missing:  {string.Join(", ", score.MissingSkills.Take(5))}");
            Console.ResetColor();
        }

        if (score.BonusSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"     Bonus:    {string.Join(", ", score.BonusSkills.Take(4))}");
            Console.ResetColor();
        }

        if (!string.IsNullOrEmpty(vacancy.Url))
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"     {vacancy.Url}");
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    private static string FormatSalary(JobVacancy vacancy)
    {
        if (vacancy.SalaryMin.HasValue && vacancy.SalaryMax.HasValue)
            return $"{vacancy.SalaryCurrency} {vacancy.SalaryMin:N0} - {vacancy.SalaryMax:N0}";
        if (vacancy.SalaryMax.HasValue)
            return $"Up to {vacancy.SalaryCurrency} {vacancy.SalaryMax:N0}";
        if (vacancy.SalaryMin.HasValue)
            return $"From {vacancy.SalaryCurrency} {vacancy.SalaryMin:N0}";
        return "Not specified";
    }

    private static async Task SendNotificationsAsync(
        ServiceProvider serviceProvider,
        List<JobVacancy> matches,
        ILogger logger)
    {
        var config = serviceProvider.GetRequiredService<NotificationConfig>();

        if (config.Telegram?.Enabled == true)
        {
            try
            {
                var httpFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpFactory.CreateClient("telegram");
                var telegramLogger = serviceProvider.GetRequiredService<ILogger<TelegramNotifier>>();

                var notifier = new TelegramNotifier(
                    httpClient,
                    config.Telegram.BotToken,
                    config.Telegram.ChatId,
                    telegramLogger);

                var filtered = matches
                    .Where(m => m.MatchScore?.OverallScore >= config.MinScoreToNotify)
                    .ToList();

                if (filtered.Count > 0)
                {
                    await notifier.NotifyMatchesAsync(filtered);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nSent {filtered.Count} matches via Telegram");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send Telegram notifications");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nWarning: Telegram notification failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        if (config.Email?.Enabled == true)
        {
            try
            {
                var emailLogger = serviceProvider.GetRequiredService<ILogger<EmailNotifier>>();

                var notifier = new EmailNotifier(
                    config.Email.SmtpHost,
                    config.Email.SmtpPort,
                    config.Email.Username,
                    config.Email.Password,
                    config.Email.FromAddress,
                    config.Email.ToAddress,
                    emailLogger);

                var filtered = matches
                    .Where(m => m.MatchScore?.OverallScore >= config.MinScoreToNotify)
                    .ToList();

                if (filtered.Count > 0)
                {
                    await notifier.NotifyMatchesAsync(filtered);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\nSent {filtered.Count} matches via Email");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send Email notifications");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\nWarning: Email notification failed: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static string? FindLatestVacanciesFile()
    {
        if (!Directory.Exists(Program.DataDirectory))
            return null;

        return Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }

    private static List<JobVacancy> ApplyDealBreakerFilters(
        List<JobVacancy> vacancies,
        bool remoteOnly,
        decimal? minSalary,
        bool noRelocation,
        string? allowGeo,
        bool excludeB2B)
    {
        var filtered = vacancies.AsEnumerable();

        // Remote-only filter
        if (remoteOnly)
        {
            filtered = filtered.Where(v =>
                v.RemotePolicy == RemotePolicy.FullyRemote ||
                v.RemotePolicy == RemotePolicy.Hybrid);
        }

        // Minimum salary filter
        if (minSalary.HasValue)
        {
            filtered = filtered.Where(v =>
                v.SalaryMin.HasValue && v.SalaryMin.Value >= minSalary.Value);
        }

        // No relocation filter (exclude jobs with relocation in title or description)
        if (noRelocation)
        {
            filtered = filtered.Where(v =>
            {
                var text = $"{v.Title} {v.Description}".ToLowerInvariant();
                return !text.Contains("relocation") &&
                       !text.Contains("relocate") &&
                       !text.Contains("on-site only") &&
                       !text.Contains("onsite only");
            });
        }

        // Geo restriction filter
        if (!string.IsNullOrWhiteSpace(allowGeo))
        {
            var geoLower = allowGeo.ToLowerInvariant();
            filtered = filtered.Where(v =>
            {
                // If no geo restrictions mentioned, assume worldwide
                if (v.GeoRestrictions.Count == 0)
                    return true;

                var restrictions = string.Join(" ", v.GeoRestrictions).ToLowerInvariant();

                // Check for explicit "worldwide" or "remote" (no restrictions)
                if (restrictions.Contains("worldwide") || restrictions.Contains("any location"))
                    return true;

                var country = v.Country?.ToLowerInvariant() ?? "";
                var title = v.Title?.ToLowerInvariant() ?? "";
                var description = v.Description?.ToLowerInvariant() ?? "";
                var combined = $"{country} {title} {description} {restrictions}";

                // Check if the allowed geo is mentioned
                if (combined.Contains(geoLower))
                    return true;

                // Special cases for Ukraine
                if (geoLower == "ukraine" || geoLower == "ua")
                {
                    return combined.Contains("ukraine") ||
                           combined.Contains("eastern europe") ||
                           combined.Contains("europe") ||
                           combined.Contains("eu only") ||
                           (v.GeoRestrictions.Count == 0); // No restrictions = worldwide
                }

                // Special cases for Europe
                if (geoLower == "europe" || geoLower == "eu")
                {
                    return combined.Contains("europe") ||
                           combined.Contains("eu only") ||
                           (v.GeoRestrictions.Count == 0);
                }

                return false;
            });
        }

        // Exclude B2B filter
        if (excludeB2B)
        {
            filtered = filtered.Where(v =>
                v.EngagementType != EngagementType.ContractB2B &&
                v.EngagementType != EngagementType.Freelance);
        }

        return filtered.ToList();
    }
}
