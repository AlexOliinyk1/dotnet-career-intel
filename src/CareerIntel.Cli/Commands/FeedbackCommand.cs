using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using CareerIntel.Persistence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that records interview feedback, saves it to a JSON file, and runs analysis.
/// Usage: career-intel feedback --company "X" --round "SystemDesign" --outcome "Rejected" --feedback "Weak on scalability" [--weak "scalability,caching"] [--strong "API design"]
/// </summary>
public static class FeedbackCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var companyOption = new Option<string>(
            "--company",
            description: "Company name for the interview")
        { IsRequired = true };

        var roundOption = new Option<string>(
            "--round",
            description: "Interview round (e.g., Recruiter, Technical, SystemDesign, Behavioral, Final)")
        { IsRequired = true };

        var outcomeOption = new Option<string>(
            "--outcome",
            description: "Interview outcome (Passed, Rejected, Ghosted, Withdrew)")
        { IsRequired = true };

        var feedbackOption = new Option<string>(
            "--feedback",
            description: "Feedback text from the interview")
        { IsRequired = true };

        var weakOption = new Option<string?>(
            "--weak",
            description: "Comma-separated list of weak areas identified");

        var strongOption = new Option<string?>(
            "--strong",
            description: "Comma-separated list of strong areas identified");

        var command = new Command("feedback", "Record interview feedback and analyze patterns")
        {
            companyOption,
            roundOption,
            outcomeOption,
            feedbackOption,
            weakOption,
            strongOption
        };

        command.SetHandler(ExecuteAsync, companyOption, roundOption, outcomeOption, feedbackOption, weakOption, strongOption);

        return command;
    }

    private static async Task ExecuteAsync(
        string company, string round, string outcome, string feedback, string? weak, string? strong)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.FeedbackCommand");
        var feedbackEngine = serviceProvider.GetRequiredService<InterviewFeedbackEngine>();

        // Build the InterviewFeedback record
        var interviewFeedback = new InterviewFeedback
        {
            Company = company,
            Round = round,
            Outcome = outcome,
            Feedback = feedback,
            WeakAreas = ParseCommaSeparated(weak),
            StrongAreas = ParseCommaSeparated(strong),
            InterviewDate = DateTimeOffset.UtcNow,
            RecordedDate = DateTimeOffset.UtcNow
        };

        // Load existing feedback from JSON file (append to array)
        var feedbackFilePath = Path.Combine(Program.DataDirectory, "interview-feedback.json");
        var allFeedback = await LoadFeedbackListAsync(feedbackFilePath);

        // Assign an incremental ID
        interviewFeedback.Id = allFeedback.Count > 0 ? allFeedback.Max(f => f.Id) + 1 : 1;
        allFeedback.Add(interviewFeedback);

        // Save updated list
        var json = JsonSerializer.Serialize(allFeedback, JsonOptions);
        await File.WriteAllTextAsync(feedbackFilePath, json);

        logger.LogInformation("Saved interview feedback #{Id} for {Company} ({Round})",
            interviewFeedback.Id, company, round);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Feedback #{interviewFeedback.Id} saved to: {feedbackFilePath}");
        Console.ResetColor();

        // Persist to SQLite + auto-enrich company profile
        try
        {
            await Program.EnsureDatabaseAsync(serviceProvider);
            var interviewRepo = serviceProvider.GetRequiredService<InterviewRepository>();
            var companyRepo = serviceProvider.GetRequiredService<CompanyRepository>();

            await interviewRepo.SaveFeedbackAsync(interviewFeedback);
            await companyRepo.UpdateFromFeedbackAsync(interviewFeedback);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Persisted to DB + updated company profile for '{company}'.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist feedback to database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning: DB persistence failed: {ex.Message}");
            Console.ResetColor();
        }

        // Load profile for analysis
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        var profile = new UserProfile();
        if (File.Exists(profilePath))
        {
            var profileJson = await File.ReadAllTextAsync(profilePath);
            profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UserProfile();
        }

        // Run analysis
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Feedback Analysis ===");
        Console.ResetColor();

        var analysis = feedbackEngine.AnalyzeFeedback(interviewFeedback, allFeedback, profile);

        // Summary
        Console.WriteLine();
        Console.WriteLine($"  Summary: {analysis.Summary}");

        // Priority adjustments
        if (analysis.PriorityAdjustments.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Priority Adjustments:");
            Console.ResetColor();
            foreach (var (skill, boost) in analysis.PriorityAdjustments)
            {
                var direction = boost > 0 ? "+" : "";
                Console.ForegroundColor = boost > 0 ? ConsoleColor.Red : ConsoleColor.Green;
                Console.WriteLine($"    {skill}: {direction}{boost:F1}");
                Console.ResetColor();
            }
        }

        // New prep tasks
        if (analysis.NewPrepTasks.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  New Prep Tasks:");
            Console.ResetColor();
            foreach (var task in analysis.NewPrepTasks)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"    [{task.Category}] ");
                Console.ResetColor();
                Console.WriteLine($"{task.Action} (~{task.EstimatedHours}h, priority {task.Priority})");
            }
        }

        // Repeating weaknesses
        if (analysis.RepeatingWeaknesses.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Repeating Weaknesses (address urgently!):");
            foreach (var weakness in analysis.RepeatingWeaknesses)
            {
                Console.WriteLine($"    - {weakness}");
            }
            Console.ResetColor();
        }

        // Strong areas
        if (interviewFeedback.StrongAreas.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Strong Areas:");
            foreach (var area in interviewFeedback.StrongAreas)
            {
                Console.WriteLine($"    + {area}");
            }
            Console.ResetColor();
        }
    }

    private static async Task<List<InterviewFeedback>> LoadFeedbackListAsync(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<InterviewFeedback>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }

    private static List<string> ParseCommaSeparated(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
