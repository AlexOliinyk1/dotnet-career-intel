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

        // Application Funnel Visualization
        PrintApplicationFunnel(applications);

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

    private static void PrintApplicationFunnel(List<JobApplication> applications)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Application Funnel:");
        Console.ResetColor();

        // Define funnel stages
        var stages = new[]
        {
            ("Applied", new[] { ApplicationStatus.Applied, ApplicationStatus.ResumeReady, ApplicationStatus.Pending }),
            ("Viewed", new[] { ApplicationStatus.Viewed }),
            ("Screening", new[] { ApplicationStatus.Screening }),
            ("Interview", new[] { ApplicationStatus.Interview }),
            ("Offer", new[] { ApplicationStatus.Offer })
        };

        var stageCounts = stages
            .Select(stage => new
            {
                Name = stage.Item1,
                Count = applications.Count(a => stage.Item2.Contains(a.Status))
            })
            .ToList();

        // Calculate total at top of funnel (all applications)
        var totalApplied = stageCounts.FirstOrDefault()?.Count ?? 0;

        if (totalApplied == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    No applications to visualize");
            Console.ResetColor();
            return;
        }

        // Print funnel stages
        Console.WriteLine();
        var maxWidth = 50;

        for (var i = 0; i < stageCounts.Count; i++)
        {
            var stage = stageCounts[i];
            var pct = totalApplied > 0 ? (double)stage.Count / totalApplied * 100 : 0;
            var barWidth = totalApplied > 0 ? (int)Math.Round((double)stage.Count / totalApplied * maxWidth) : 0;

            // Calculate conversion rate from previous stage
            string conversionInfo = "";
            if (i > 0)
            {
                var previousStage = stageCounts[i - 1];
                var conversionRate = previousStage.Count > 0 ? (double)stage.Count / previousStage.Count * 100 : 0;
                conversionInfo = $" ({conversionRate:F1}% conversion)";
            }

            // Stage name and count
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"    {stage.Name,-12}");
            Console.ResetColor();

            // Funnel bar
            var color = stage.Name switch
            {
                "Applied" => ConsoleColor.Cyan,
                "Viewed" => ConsoleColor.Blue,
                "Screening" => ConsoleColor.Yellow,
                "Interview" => ConsoleColor.Green,
                "Offer" => ConsoleColor.Green,
                _ => ConsoleColor.White
            };

            Console.ForegroundColor = color;
            var bar = new string('█', barWidth);
            var padding = new string(' ', maxWidth - barWidth);
            Console.Write($" {bar}{padding}");
            Console.ResetColor();

            // Count and percentage
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($" {stage.Count,3}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" ({pct:F0}%)");
            Console.ResetColor();

            // Conversion rate
            if (!string.IsNullOrEmpty(conversionInfo))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(conversionInfo);
                Console.ResetColor();
            }

            Console.WriteLine();
        }

        Console.WriteLine();

        // Calculate funnel metrics
        var viewed = stageCounts.FirstOrDefault(s => s.Name == "Viewed")?.Count ?? 0;
        var screening = stageCounts.FirstOrDefault(s => s.Name == "Screening")?.Count ?? 0;
        var interview = stageCounts.FirstOrDefault(s => s.Name == "Interview")?.Count ?? 0;
        var offer = stageCounts.FirstOrDefault(s => s.Name == "Offer")?.Count ?? 0;

        var appliedToViewedRate = totalApplied > 0 ? (double)viewed / totalApplied * 100 : 0;
        var screeningToInterviewRate = screening > 0 ? (double)interview / screening * 100 : 0;
        var interviewToOfferRate = interview > 0 ? (double)offer / interview * 100 : 0;

        // Funnel insights
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Funnel Insights:");
        Console.ResetColor();

        if (viewed == 0 && totalApplied > 5)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    ⚠ Low visibility: {totalApplied} applications but none viewed yet");
            Console.WriteLine("       → Focus on personalized outreach to recruiters");
            Console.ResetColor();
        }
        else if (appliedToViewedRate > 0)
        {
            var viewColor = appliedToViewedRate >= 30 ? ConsoleColor.Green : appliedToViewedRate >= 15 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.ForegroundColor = viewColor;
            Console.WriteLine($"    View rate: {appliedToViewedRate:F0}% of applications are being viewed");
            Console.ResetColor();
        }

        if (screening > 0 && interview == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    ⚠ Screening bottleneck: {screening} screening(s) but no interviews yet");
            Console.WriteLine("       → Review and improve your interview prep materials");
            Console.ResetColor();
        }
        else if (screeningToInterviewRate > 0)
        {
            var screeningColor = screeningToInterviewRate >= 40 ? ConsoleColor.Green : screeningToInterviewRate >= 20 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.ForegroundColor = screeningColor;
            Console.WriteLine($"    Screening → Interview: {screeningToInterviewRate:F0}% pass rate");
            Console.ResetColor();
        }

        if (interview > 0 && interviewToOfferRate > 0)
        {
            var offerColor = interviewToOfferRate >= 25 ? ConsoleColor.Green : interviewToOfferRate >= 10 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.ForegroundColor = offerColor;
            Console.WriteLine($"    Interview → Offer: {interviewToOfferRate:F0}% conversion rate");
            Console.ResetColor();
        }

        // Overall funnel health
        if (offer > 0)
        {
            var overallRate = totalApplied > 0 ? (double)offer / totalApplied * 100 : 0;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    ✓ Overall success: {overallRate:F1}% of applications resulted in offers");
            Console.ResetColor();
        }

        // Identify biggest drop-off
        var dropOffs = new List<(string Stage, double DropOff)>();

        for (var i = 1; i < stageCounts.Count; i++)
        {
            var previousCount = stageCounts[i - 1].Count;
            var currentCount = stageCounts[i].Count;

            if (previousCount > 0)
            {
                var dropOff = (double)(previousCount - currentCount) / previousCount * 100;
                dropOffs.Add(($"{stageCounts[i - 1].Name} → {stageCounts[i].Name}", dropOff));
            }
        }

        if (dropOffs.Count > 0)
        {
            var biggestDropOff = dropOffs.OrderByDescending(d => d.DropOff).First();
            if (biggestDropOff.DropOff >= 60)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"    ⚠ Biggest drop-off: {biggestDropOff.Stage} ({biggestDropOff.DropOff:F0}% loss)");
                Console.ResetColor();

                // Suggest actions based on drop-off stage
                Console.ForegroundColor = ConsoleColor.Yellow;
                if (biggestDropOff.Stage.Contains("Applied → Viewed"))
                {
                    Console.WriteLine("       → Improve resume headlines and keywords for ATS systems");
                }
                else if (biggestDropOff.Stage.Contains("Viewed → Screening"))
                {
                    Console.WriteLine("       → Strengthen your profile summary and key achievements");
                }
                else if (biggestDropOff.Stage.Contains("Screening → Interview"))
                {
                    Console.WriteLine("       → Practice behavioral questions and technical prep");
                }
                else if (biggestDropOff.Stage.Contains("Interview → Offer"))
                {
                    Console.WriteLine("       → Work on salary negotiation and closing skills");
                }
                Console.ResetColor();
            }
        }
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
