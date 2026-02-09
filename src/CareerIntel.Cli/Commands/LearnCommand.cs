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
/// CLI command that generates a learning plan with ROI-ranked skills.
/// Usage: career-intel learn [--input path]
/// </summary>
public static class LearnCommand
{
    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var command = new Command("learn", "Generate a learning plan with ROI-ranked skills and overlearning detection")
        {
            inputOption
        };

        command.SetHandler(ExecuteAsync, inputOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.LearnCommand");
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        var learningEngine = serviceProvider.GetRequiredService<LearningROIEngine>();

        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Profile not found at {profilePath}");
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

        var vacanciesJson = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(vacanciesJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        // Load interview history
        var feedbackPath = Path.Combine(Program.DataDirectory, "interview-feedback.json");
        var interviewHistory = await LoadJsonListAsync<InterviewFeedback>(feedbackPath);

        // Generate learning plan
        logger.LogInformation("Generating learning plan from {Count} vacancies", vacancies.Count);
        var plan = learningEngine.GeneratePlan(profile, vacancies, interviewHistory);

        // OVERLEARNING WARNING
        if (plan.OverlearningDetected)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.BackgroundColor = ConsoleColor.White;
            Console.WriteLine("  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!  ");
            Console.WriteLine("  !!                                                 !!  ");
            Console.WriteLine("  !!        STOP LEARNING. START APPLYING.           !!  ");
            Console.WriteLine("  !!                                                 !!  ");
            Console.WriteLine("  !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!  ");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  {plan.GlobalRecommendation}");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Dashboard header
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("                 LEARNING ROI DASHBOARD                    ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // Global stats
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Total Estimated Hours: ");
        Console.ResetColor();
        Console.WriteLine($"{plan.TotalEstimatedHours}h");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  Recommendation: ");
        Console.ResetColor();
        Console.WriteLine(plan.GlobalRecommendation);
        Console.WriteLine();

        // Top 10 skills by ROI
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Top 10 Skills by ROI:");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"#",-4} {"Skill",-25} {"ROI",7} {"Hours",6} {"Status",-14} {"Action",-20}");
        Console.WriteLine($"  {new string('-', 80)}");
        Console.ResetColor();

        var topSkills = plan.Skills
            .OrderByDescending(s => s.LearningROI)
            .Take(10)
            .ToList();

        for (var i = 0; i < topSkills.Count; i++)
        {
            var skill = topSkills[i];

            // ROI color
            var roiColor = skill.LearningROI switch
            {
                >= 70 => ConsoleColor.Green,
                >= 40 => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };

            // Stop signal
            if (skill.ShouldStop)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"  {i + 1,-4} ");
                Console.Write($"{skill.SkillName,-25} ");
                Console.Write($"{"STOP",7} ");
                Console.Write($"{skill.EstimatedHoursToClose + "h",6} ");
                Console.Write($"{skill.CurrentAction,-14} ");
                Console.WriteLine(skill.StopReason);
                Console.ResetColor();
                continue;
            }

            Console.Write($"  {i + 1,-4} ");

            // Skill name
            var nameColor = skill.PersonalGapScore switch
            {
                >= 0.7 => ConsoleColor.Red,
                >= 0.4 => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };
            Console.ForegroundColor = nameColor;
            Console.Write($"{skill.SkillName,-25} ");

            // ROI score
            Console.ForegroundColor = roiColor;
            Console.Write($"{skill.LearningROI,7:F1} ");
            Console.ResetColor();

            // Hours
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{skill.EstimatedHoursToClose + "h",6} ");
            Console.ResetColor();

            // Status
            var statusColor = skill.CurrentAction switch
            {
                "Done" => ConsoleColor.Green,
                "Learning" or "Practicing" => ConsoleColor.Yellow,
                "Deprioritized" => ConsoleColor.DarkGray,
                _ => ConsoleColor.White
            };
            Console.ForegroundColor = statusColor;
            Console.Write($"{skill.CurrentAction,-14} ");
            Console.ResetColor();

            // Market demand bar
            var demandBar = new string('#', (int)(skill.MarketDemandScore / 10));
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(demandBar);
            Console.ResetColor();
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ROI = MarketDemand x PersonalGap x SalaryImpact");
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

    private static string? FindLatestVacanciesFile()
    {
        if (!Directory.Exists(Program.DataDirectory))
            return null;

        return Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
