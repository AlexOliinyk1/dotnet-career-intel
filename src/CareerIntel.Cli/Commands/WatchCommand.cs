using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using CareerIntel.Notifications;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that runs automated scan, match, and notify cycles on a configurable interval.
/// Usage: career-intel watch [--interval 60] [--min-score 60] [--max-pages 3] [--cycles 0] [--quiet]
/// </summary>
public static class WatchCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var intervalOption = new Option<int>(
            "--interval",
            getDefaultValue: () => 60,
            description: "Minutes between scan cycles");

        var minScoreOption = new Option<double>(
            "--min-score",
            getDefaultValue: () => 60,
            description: "Minimum match score to trigger notification");

        var maxPagesOption = new Option<int>(
            "--max-pages",
            getDefaultValue: () => 3,
            description: "Pages to scrape per platform per cycle");

        var cyclesOption = new Option<int>(
            "--cycles",
            getDefaultValue: () => 0,
            description: "Number of cycles to run (0 = infinite until Ctrl+C)");

        var quietOption = new Option<bool>(
            "--quiet",
            getDefaultValue: () => false,
            description: "Suppress per-vacancy output, only show summaries");

        var command = new Command("watch", "Monitor job boards and auto-notify when new matching vacancies appear")
        {
            intervalOption,
            minScoreOption,
            maxPagesOption,
            cyclesOption,
            quietOption
        };

        command.SetHandler(ExecuteAsync, intervalOption, minScoreOption, maxPagesOption, cyclesOption, quietOption);

        return command;
    }

    private static async Task ExecuteAsync(int interval, double minScore, int maxPages, int cycles, bool quiet)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CareerIntel.Cli.WatchCommand");

        // ── Build notifiers from config ──────────────────────────────────
        var config = serviceProvider.GetRequiredService<NotificationConfig>();
        var notifiers = BuildNotifiers(serviceProvider, config);

        if (notifiers.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No notification channels are enabled.");
            Console.WriteLine("Configure Telegram or Email in your settings and set Enabled = true.");
            Console.ResetColor();
            return;
        }

        // ── Test notification connectivity ───────────────────────────────
        Console.WriteLine("Testing notification channels...");
        var healthyNotifiers = new List<INotificationService>();

        foreach (var notifier in notifiers)
        {
            try
            {
                var ok = await notifier.TestConnectionAsync();
                if (ok)
                {
                    healthyNotifiers.Add(notifier);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  {notifier.ChannelName}: connected");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  {notifier.ChannelName}: test returned false, skipping");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification channel {Channel} connectivity test failed", notifier.ChannelName);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  {notifier.ChannelName}: failed ({ex.Message}), skipping");
                Console.ResetColor();
            }
        }

        if (healthyNotifiers.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nError: All notification channels failed connectivity test. Aborting.");
            Console.ResetColor();
            return;
        }

        // ── Resolve services ─────────────────────────────────────────────
        var scrapers = serviceProvider.GetServices<IJobScraper>().ToList();
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        await matchEngine.ReloadProfileAsync();
        await Program.EnsureDatabaseAsync(serviceProvider);

        // ── Load seen IDs ────────────────────────────────────────────────
        var seenIdsPath = Path.Combine(Program.DataDirectory, "watch-seen-ids.json");
        var seenIds = await LoadSeenIdsAsync(seenIdsPath);

        // ── Load custom alert rules ──────────────────────────────────────
        var rulesPath = Path.Combine(Program.DataDirectory, "watch-rules.json");
        var alertRules = await LoadAlertRulesAsync(rulesPath);
        if (alertRules.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Loaded {alertRules.Count} custom alert rule(s)");
            Console.ResetColor();
            foreach (var rule in alertRules.Where(r => r.Enabled))
            {
                Console.WriteLine($"  ✓ {rule.Name} (priority: {rule.Priority})");
            }
        }

        // ── Cancellation via Ctrl+C ──────────────────────────────────────
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // ── Print banner ─────────────────────────────────────────────────
        var telegramStatus = config.Telegram?.Enabled == true
            ? (healthyNotifiers.Any(n => n.ChannelName.Equals("Telegram", StringComparison.OrdinalIgnoreCase)) ? "yes" : "fail")
            : "off";
        var emailStatus = config.Email?.Enabled == true
            ? (healthyNotifiers.Any(n => n.ChannelName.Equals("Email", StringComparison.OrdinalIgnoreCase)) ? "yes" : "fail")
            : "off";

        Console.WriteLine();
        Console.WriteLine(new string('=', 54));
        Console.WriteLine("          CAREER INTEL WATCH MODE");
        Console.WriteLine(new string('=', 54));
        Console.WriteLine();
        Console.WriteLine($"  Interval: {interval} min | Min score: {minScore} | Notifications: Telegram {telegramStatus}, Email {emailStatus}");
        Console.WriteLine();

        // ── Main loop ────────────────────────────────────────────────────
        var cycleNumber = 0;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                cycleNumber++;

                if (cycles > 0 && cycleNumber > cycles)
                    break;

                var cycleStart = DateTime.Now;
                Console.WriteLine($"  -- Cycle {cycleNumber} ({cycleStart:yyyy-MM-dd HH:mm}) --");

                // (a) Scrape all platforms in parallel
                var allVacancies = await ScrapeAllPlatformsAsync(scrapers, maxPages, logger, cts.Token);

                Console.WriteLine($"  Scanning {scrapers.Count} platforms... {allVacancies.Count} vacancies");

                // (b) Apply eligibility gate
                var eligible = EligibilityGate.Filter(allVacancies);
                Console.WriteLine($"  Eligibility gate: {eligible.Count}/{allVacancies.Count} passed");

                // (c) Deduplicate against seen IDs
                var newVacancies = eligible.Where(v => !seenIds.Contains(v.Id)).ToList();
                var previouslySeenCount = eligible.Count - newVacancies.Count;
                Console.WriteLine($"  New vacancies: {newVacancies.Count} ({previouslySeenCount} previously seen)");

                // (d) Match new vacancies against profile
                var ranked = matchEngine.RankVacancies(newVacancies, minScore);
                Console.WriteLine($"  Matches above {minScore}: {ranked.Count} new");

                // (d2) Evaluate custom alert rules
                var ruleMatches = new List<(JobVacancy Vacancy, WatchRule Rule)>();
                if (alertRules.Count > 0)
                {
                    foreach (var vacancy in newVacancies)
                    {
                        foreach (var rule in alertRules.Where(r => r.Enabled))
                        {
                            if (EvaluateRule(rule, vacancy))
                            {
                                ruleMatches.Add((vacancy, rule));
                            }
                        }
                    }

                    if (ruleMatches.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"  Custom rule matches: {ruleMatches.Count}");
                        Console.ResetColor();

                        var byRule = ruleMatches.GroupBy(m => m.Rule.Name);
                        foreach (var group in byRule)
                        {
                            Console.WriteLine($"    {group.Key}: {group.Count()} match(es)");
                        }
                    }
                }

                // (e) Print per-vacancy detail unless --quiet
                if (!quiet && ranked.Count > 0)
                {
                    Console.WriteLine();
                    for (var i = 0; i < ranked.Count; i++)
                    {
                        var v = ranked[i];
                        var score = v.MatchScore;
                        Console.WriteLine($"    #{i + 1}  [{score?.OverallScore:F0}]  {v.Title} at {v.Company}");
                    }
                    Console.WriteLine();
                }

                // (f) Notify if there are new high-score matches or rule matches
                var allNotifications = ranked.ToList();

                // Add rule-matched vacancies that aren't already in ranked list
                foreach (var (vacancy, rule) in ruleMatches)
                {
                    if (!allNotifications.Any(v => v.Id == vacancy.Id))
                    {
                        // Annotate with rule information
                        vacancy.MatchScore = new MatchScore
                        {
                            OverallScore = rule.Priority * 10, // Use priority as pseudo-score
                            MatchingSkills = [],
                            MissingSkills = [],
                            BonusSkills = [],
                            RecommendedAction = RecommendedAction.Apply
                        };
                        allNotifications.Add(vacancy);
                    }
                }

                if (allNotifications.Count > 0)
                {
                    foreach (var notifier in healthyNotifiers)
                    {
                        try
                        {
                            await notifier.NotifyMatchesAsync(allNotifications, cts.Token);
                            Console.ForegroundColor = ConsoleColor.Green;
                            var profileCount = ranked.Count;
                            var ruleCount = allNotifications.Count - profileCount;
                            var summary = ruleCount > 0
                                ? $"{profileCount} profile + {ruleCount} rule match(es)"
                                : $"{profileCount} match(es)";
                            Console.WriteLine($"  Notified {summary} via {notifier.ChannelName}");
                            Console.ResetColor();
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to notify via {Channel}", notifier.ChannelName);
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  Warning: {notifier.ChannelName} notification failed: {ex.Message}");
                            Console.ResetColor();
                        }
                    }
                }

                // Update seen IDs with all eligible vacancies (not just matched)
                foreach (var v in eligible)
                    seenIds.Add(v.Id);

                await SaveSeenIdsAsync(seenIdsPath, seenIds);

                // (g) Save all vacancies to dated JSON file
                var outputPath = Path.Combine(
                    Program.DataDirectory,
                    $"vacancies-{DateTime.Now:yyyy-MM-dd}.json");

                var json = JsonSerializer.Serialize(allVacancies, JsonOptions);
                await File.WriteAllTextAsync(outputPath, json, cts.Token);
                Console.WriteLine($"  Saved to: {outputPath}");

                // (h) Wait for next cycle
                if (cycles > 0 && cycleNumber >= cycles)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  Completed {cycles} cycle(s). Exiting.");
                    Console.ResetColor();
                    break;
                }

                var nextCycleTime = cycleStart.AddMinutes(interval);
                Console.WriteLine();
                Console.WriteLine($"  Next cycle in {interval} minutes ({nextCycleTime:HH:mm})... Press Ctrl+C to stop.");
                Console.WriteLine();

                await Task.Delay(TimeSpan.FromMinutes(interval), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C
        }

        // ── Graceful shutdown ────────────────────────────────────────────
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Watch mode stopped after {cycleNumber} cycle(s). Saving state...");
        Console.ResetColor();

        await SaveSeenIdsAsync(seenIdsPath, seenIds);

        Console.WriteLine($"  Seen IDs saved ({seenIds.Count} total). Goodbye.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Notifier construction
    // ─────────────────────────────────────────────────────────────────────────

    private static List<INotificationService> BuildNotifiers(
        ServiceProvider serviceProvider,
        NotificationConfig config)
    {
        var notifiers = new List<INotificationService>();

        if (config.Telegram?.Enabled == true)
        {
            var httpFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpFactory.CreateClient("telegram");
            var tgLogger = serviceProvider.GetRequiredService<ILogger<TelegramNotifier>>();
            notifiers.Add(new TelegramNotifier(httpClient, config.Telegram.BotToken, config.Telegram.ChatId, tgLogger));
        }

        if (config.Email?.Enabled == true)
        {
            var emailLogger = serviceProvider.GetRequiredService<ILogger<EmailNotifier>>();
            notifiers.Add(new EmailNotifier(
                config.Email.SmtpHost,
                config.Email.SmtpPort,
                config.Email.Username,
                config.Email.Password,
                config.Email.FromAddress,
                config.Email.ToAddress,
                emailLogger));
        }

        return notifiers;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Parallel scraping
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<List<JobVacancy>> ScrapeAllPlatformsAsync(
        List<IJobScraper> scrapers,
        int maxPages,
        ILogger logger,
        CancellationToken ct)
    {
        var tasks = scrapers.Select(async scraper =>
        {
            try
            {
                return await scraper.ScrapeAsync(maxPages: maxPages);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scraper {Platform} failed during watch cycle", scraper.PlatformName);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Warning: {scraper.PlatformName} scraping failed: {ex.Message}");
                Console.ResetColor();
                return (IReadOnlyList<JobVacancy>)[];
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Seen-IDs persistence
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<HashSet<string>> LoadSeenIdsAsync(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<SeenIdsData>(json, JsonOptions);
            return data?.SeenIds is not null
                ? new HashSet<string>(data.SeenIds, StringComparer.OrdinalIgnoreCase)
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task SaveSeenIdsAsync(string path, HashSet<string> seenIds)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var data = new SeenIdsData
        {
            SeenIds = [.. seenIds],
            LastUpdated = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private sealed class SeenIdsData
    {
        public List<string> SeenIds { get; set; } = [];
        public DateTime LastUpdated { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Custom Alert Rules
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<List<WatchRule>> LoadAlertRulesAsync(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var data = JsonSerializer.Deserialize<WatchRulesData>(json, JsonOptions);
            return data?.Rules ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool EvaluateRule(WatchRule rule, JobVacancy vacancy)
    {
        // Company match
        if (rule.Companies?.Count > 0)
        {
            var match = rule.Companies.Any(c =>
                vacancy.Company.Equals(c, StringComparison.OrdinalIgnoreCase));
            if (!match) return false;
        }

        // Skills match (must have ALL specified skills)
        if (rule.MustHaveSkills?.Count > 0)
        {
            var vacancySkills = vacancy.RequiredSkills
                .Concat(vacancy.PreferredSkills)
                .Select(s => s.ToLowerInvariant())
                .ToHashSet();

            var hasAll = rule.MustHaveSkills.All(skill =>
                vacancySkills.Contains(skill.ToLowerInvariant()));

            if (!hasAll) return false;
        }

        // Skills match (must have ANY of specified skills)
        if (rule.MustHaveAnySkill?.Count > 0)
        {
            var vacancySkills = vacancy.RequiredSkills
                .Concat(vacancy.PreferredSkills)
                .Select(s => s.ToLowerInvariant())
                .ToHashSet();

            var hasAny = rule.MustHaveAnySkill.Any(skill =>
                vacancySkills.Contains(skill.ToLowerInvariant()));

            if (!hasAny) return false;
        }

        // Minimum salary
        if (rule.MinSalary.HasValue)
        {
            if (!vacancy.SalaryMin.HasValue || vacancy.SalaryMin.Value < rule.MinSalary.Value)
                return false;
        }

        // Remote policy
        if (rule.RemoteOnly)
        {
            if (vacancy.RemotePolicy != Core.Enums.RemotePolicy.FullyRemote &&
                vacancy.RemotePolicy != Core.Enums.RemotePolicy.Hybrid)
                return false;
        }

        // Seniority level
        if (rule.SeniorityLevels?.Count > 0)
        {
            var seniorityMatch = rule.SeniorityLevels.Contains(vacancy.SeniorityLevel.ToString());
            if (!seniorityMatch) return false;
        }

        // Title keywords (must contain ANY)
        if (rule.TitleKeywords?.Count > 0)
        {
            var titleLower = vacancy.Title.ToLowerInvariant();
            var hasKeyword = rule.TitleKeywords.Any(kw =>
                titleLower.Contains(kw.ToLowerInvariant()));
            if (!hasKeyword) return false;
        }

        // Exclusions - company
        if (rule.ExcludeCompanies?.Count > 0)
        {
            var excluded = rule.ExcludeCompanies.Any(c =>
                vacancy.Company.Equals(c, StringComparison.OrdinalIgnoreCase));
            if (excluded) return false;
        }

        // Exclusions - keywords in title
        if (rule.ExcludeTitleKeywords?.Count > 0)
        {
            var titleLower = vacancy.Title.ToLowerInvariant();
            var hasExcluded = rule.ExcludeTitleKeywords.Any(kw =>
                titleLower.Contains(kw.ToLowerInvariant()));
            if (hasExcluded) return false;
        }

        return true; // Passed all conditions
    }

    private sealed class WatchRulesData
    {
        public List<WatchRule> Rules { get; set; } = [];
    }

    private sealed class WatchRule
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; } = 5; // 1-10, higher = more important

        // Positive conditions (must match)
        public List<string>? Companies { get; set; }
        public List<string>? MustHaveSkills { get; set; } // AND logic
        public List<string>? MustHaveAnySkill { get; set; } // OR logic
        public decimal? MinSalary { get; set; }
        public bool RemoteOnly { get; set; }
        public List<string>? SeniorityLevels { get; set; } // e.g., ["Senior", "Lead"]
        public List<string>? TitleKeywords { get; set; } // OR logic

        // Negative conditions (exclusions)
        public List<string>? ExcludeCompanies { get; set; }
        public List<string>? ExcludeTitleKeywords { get; set; }
    }
}
