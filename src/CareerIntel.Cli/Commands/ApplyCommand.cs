using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for auto-apply pipeline management — identify candidates, prepare applications, show dashboard.
/// Usage: career-intel apply [--input path] [--min-score n] [--dry-run] [--status]
/// </summary>
public static class ApplyCommand
{
    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var minScoreOption = new Option<double>(
            "--min-score",
            getDefaultValue: () => 50,
            description: "Minimum match score for auto-apply candidates (0-100)");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            getDefaultValue: () => false,
            description: "Preview applications without submitting");

        var statusOption = new Option<bool>(
            "--status",
            getDefaultValue: () => false,
            description: "Show application tracking dashboard");

        var command = new Command("apply", "Auto-apply pipeline — identify candidates, prepare applications, track status")
        {
            inputOption,
            minScoreOption,
            dryRunOption,
            statusOption
        };

        command.SetHandler(ExecuteAsync, inputOption, minScoreOption, dryRunOption, statusOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input, double minScore, bool dryRun, bool status)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.ApplyCommand");
        var autoApplyEngine = serviceProvider.GetRequiredService<AutoApplyEngine>();
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();

        // Dashboard mode
        if (status)
        {
            await ShowDashboardAsync(autoApplyEngine);
            return;
        }

        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Profile not found at {profilePath}");
            Console.WriteLine("Please create your profile from the template in data/my-profile.json");
            Console.ResetColor();
            return;
        }

        await matchEngine.ReloadProfileAsync();

        var profileJson = await File.ReadAllTextAsync(profilePath);
        var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new UserProfile();

        // Load vacancies
        var inputPath = input ?? FindLatestVacanciesFile();
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Loading vacancies from: {inputPath}");
        Console.WriteLine($"Loading profile from: {profilePath}");

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

        // Match vacancies against profile
        Console.WriteLine($"Matching {vacancies.Count} vacancies (min score: {minScore})...\n");
        var ranked = matchEngine.RankVacancies(vacancies, minScore);

        // Exclude companies from profile preferences
        if (profile.Preferences.ExcludeCompanies.Count > 0)
        {
            autoApplyEngine.ExcludeCompanies(profile.Preferences.ExcludeCompanies);
        }

        // Identify candidates
        var candidates = autoApplyEngine.IdentifyCandidates(ranked, profile, minScore);

        if (candidates.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No candidates found above {minScore} match score threshold.");
            Console.ResetColor();
            return;
        }

        // Prepare applications
        var prepared = autoApplyEngine.PrepareApplications(candidates, profile, ranked);

        if (dryRun)
        {
            ShowDryRunPreview(prepared);
            return;
        }

        // Generate apply batch
        var batch = autoApplyEngine.GenerateApplyBatch(prepared);
        ShowBatchSummary(batch);
    }

    private static async Task ShowDashboardAsync(AutoApplyEngine autoApplyEngine)
    {
        // Load existing applications from data file
        var applicationsPath = Path.Combine(Program.DataDirectory, "applications.json");
        var applications = await LoadJsonListAsync<JobApplication>(applicationsPath);

        if (applications.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No applications tracked yet. Run 'apply' to start the pipeline.");
            Console.ResetColor();
            return;
        }

        var dashboard = autoApplyEngine.GetDashboard(applications);

        // Dashboard header
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("             APPLICATION TRACKING DASHBOARD                ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // Key metrics
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Total Applications: ");
        Console.ResetColor();
        Console.WriteLine(dashboard.TotalApplications);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Total Applied: ");
        Console.ResetColor();
        Console.WriteLine(dashboard.TotalApplied);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Response Rate: ");
        Console.ResetColor();
        Console.WriteLine($"{dashboard.ResponseRate}%");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Avg Days to Response: ");
        Console.ResetColor();
        Console.WriteLine($"{dashboard.AverageDaysToResponse} days");
        Console.WriteLine();

        // Status breakdown
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Status Breakdown:");
        Console.ResetColor();

        foreach (var (statusValue, count) in dashboard.StatusBreakdown.OrderByDescending(kv => kv.Value))
        {
            var statusColor = statusValue switch
            {
                ApplicationStatus.Applied or ApplicationStatus.ResumeReady => ConsoleColor.Green,
                ApplicationStatus.Screening or ApplicationStatus.Interview => ConsoleColor.Green,
                ApplicationStatus.Offer => ConsoleColor.Green,
                ApplicationStatus.Pending => ConsoleColor.Yellow,
                ApplicationStatus.Viewed => ConsoleColor.Yellow,
                ApplicationStatus.Rejected or ApplicationStatus.Ghosted => ConsoleColor.Red,
                ApplicationStatus.Withdrawn or ApplicationStatus.Expired => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            Console.ForegroundColor = statusColor;
            Console.WriteLine($"    {statusValue,-16} {count}");
            Console.ResetColor();
        }

        Console.WriteLine();

        // Top companies
        if (dashboard.TopCompanies.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Top Companies:");
            Console.ResetColor();

            foreach (var (company, count) in dashboard.TopCompanies)
            {
                Console.WriteLine($"    {company,-30} {count} applications");
            }

            Console.WriteLine();
        }

        // Weekly velocity
        if (dashboard.WeeklyVelocity.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Weekly Application Velocity (last 4 weeks):");
            Console.ResetColor();

            foreach (var week in dashboard.WeeklyVelocity)
            {
                var bar = new string('#', week.Count);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"    Week {week.Week,2}: ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(bar);
                Console.ResetColor();
                Console.WriteLine($" ({week.Count})");
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Dashboard generated: {dashboard.GeneratedDate:yyyy-MM-dd HH:mm}");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }

    private static void ShowDryRunPreview(List<JobApplication> prepared)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("              DRY RUN - APPLICATION PREVIEW                ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  The following applications would be prepared (no changes made):");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"#",-4} {"Title",-35} {"Company",-22} {"Score",6} {"Method",-10} {"Resume",-16}");
        Console.WriteLine($"  {new string('-', 95)}");
        Console.ResetColor();

        for (var i = 0; i < prepared.Count; i++)
        {
            var app = prepared[i];

            var scoreColor = app.MatchScore switch
            {
                >= 80 => ConsoleColor.Green,
                >= 60 => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };

            Console.Write($"  {i + 1,-4} ");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{Truncate(app.VacancyTitle, 34),-35} ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{Truncate(app.Company, 21),-22} ");

            Console.ForegroundColor = scoreColor;
            Console.Write($"{app.MatchScore,6:F0} ");
            Console.ResetColor();

            Console.Write($"{app.ApplyMethod,-10} ");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"{app.ResumeVersion,-16}");
            Console.ResetColor();

            Console.WriteLine();

            if (!string.IsNullOrEmpty(app.ApplyUrl))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"       {app.ApplyUrl}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  Total: {prepared.Count} applications ready to prepare");
        Console.WriteLine("  Run without --dry-run to generate the apply batch.");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void ShowBatchSummary(AutoApplyEngine.ApplyBatch batch)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("               APPLY BATCH GENERATED                       ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Applications in batch: ");
        Console.ResetColor();
        Console.WriteLine(batch.TotalCount);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Estimated time: ");
        Console.ResetColor();
        Console.WriteLine($"{batch.EstimatedMinutes} minutes");
        Console.WriteLine();

        // List applications with status colors
        for (var i = 0; i < batch.Applications.Count; i++)
        {
            var app = batch.Applications[i];

            var statusColor = app.Status switch
            {
                ApplicationStatus.ResumeReady => ConsoleColor.Green,
                ApplicationStatus.Pending => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };

            Console.ForegroundColor = statusColor;
            Console.Write($"  [{app.Status}] ");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{app.VacancyTitle}");
            Console.ResetColor();

            Console.Write($" at ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(app.Company);
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" (score: {app.MatchScore:F0})");
            Console.ResetColor();

            if (!string.IsNullOrEmpty(app.ApplyUrl))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"    URL: {app.ApplyUrl}");
                Console.ResetColor();
            }

            if (!string.IsNullOrEmpty(app.CoverLetterPath))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Cover letter: {app.CoverLetterPath}");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  Batch ready for review. Open the apply URLs above to submit.");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Generated: {batch.CreatedDate:yyyy-MM-dd HH:mm}");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 2), "..");

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

    private static string? FindLatestVacanciesFile()
    {
        if (!Directory.Exists(Program.DataDirectory))
            return null;

        return Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
