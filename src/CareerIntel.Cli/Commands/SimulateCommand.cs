using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using CareerIntel.Resume;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that simulates how a generated resume performs against ATS, recruiter, and tech lead review.
/// Usage: career-intel simulate --vacancy-id id [--input path]
/// </summary>
public static class SimulateCommand
{
    public static Command Create()
    {
        var vacancyIdOption = new Option<string>(
            "--vacancy-id",
            description: "ID of the vacancy to simulate the resume for")
        { IsRequired = true };

        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var command = new Command("simulate", "Simulate resume performance against ATS, recruiter, and tech lead review")
        {
            vacancyIdOption,
            inputOption
        };

        command.SetHandler(ExecuteAsync, vacancyIdOption, inputOption);

        return command;
    }

    private static async Task ExecuteAsync(string vacancyId, string? input)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.SimulateCommand");
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        var resumeBuilder = serviceProvider.GetRequiredService<ResumeBuilder>();
        var simulator = serviceProvider.GetRequiredService<ResumeSimulator>();

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

        // Find target vacancy
        var vacancy = vacancies.FirstOrDefault(v =>
            v.Id.Equals(vacancyId, StringComparison.OrdinalIgnoreCase));

        if (vacancy is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Vacancy '{vacancyId}' not found in {inputPath}");
            Console.ResetColor();

            Console.WriteLine("\nAvailable vacancy IDs:");
            foreach (var v in vacancies.Take(20))
            {
                Console.WriteLine($"  {v.Id} - {v.Title} at {v.Company}");
            }
            if (vacancies.Count > 20)
            {
                Console.WriteLine($"  ... and {vacancies.Count - 20} more");
            }
            return;
        }

        // Generate resume
        Console.WriteLine($"Generating resume for: {vacancy.Title} at {vacancy.Company}");
        var resumeMarkdown = resumeBuilder.Build(profile, vacancy);

        // Run simulation
        Console.WriteLine("Running resume simulation...\n");
        var simulation = simulator.Simulate(resumeMarkdown, vacancy, profile);

        // Print dashboard
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("               RESUME SIMULATION RESULTS                   ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Target: {vacancy.Title} at {vacancy.Company}");
        Console.ResetColor();
        Console.WriteLine();

        // Scores overview
        PrintScoreBar("ATS Score", simulation.AtsScore.Score);
        PrintScoreBar("Recruiter Score", simulation.RecruiterScore.Score);
        PrintScoreBar("Tech Lead Score", simulation.TechLeadScore.Score);
        Console.WriteLine();

        // Conversion probability
        var convColor = simulation.OverallConversionProbability switch
        {
            >= 0.7 => ConsoleColor.Green,
            >= 0.4 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.Write("  Conversion Probability: ");
        Console.ForegroundColor = convColor;
        Console.WriteLine($"{simulation.OverallConversionProbability:P0}");
        Console.ResetColor();

        // ATS Details
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  --- ATS Analysis ---");
        Console.ResetColor();
        Console.Write("  Keyword Match: ");
        var kwColor = simulation.AtsScore.KeywordMatchPercent >= 70 ? ConsoleColor.Green :
                      simulation.AtsScore.KeywordMatchPercent >= 50 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.ForegroundColor = kwColor;
        Console.WriteLine($"{simulation.AtsScore.KeywordMatchPercent:F0}%");
        Console.ResetColor();

        Console.Write("  Passes Filter: ");
        Console.ForegroundColor = simulation.AtsScore.PassesFilter ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(simulation.AtsScore.PassesFilter ? "YES" : "NO");
        Console.ResetColor();

        if (simulation.AtsScore.MissingKeywords.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Missing Keywords: {string.Join(", ", simulation.AtsScore.MissingKeywords)}");
            Console.ResetColor();
        }

        if (simulation.AtsScore.FormatIssues.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Format Issues:");
            foreach (var issue in simulation.AtsScore.FormatIssues)
            {
                Console.WriteLine($"    - {issue}");
            }
            Console.ResetColor();
        }

        // Recruiter Details
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  --- Recruiter Review ---");
        Console.ResetColor();
        Console.WriteLine($"  Estimated Read Time: {simulation.RecruiterScore.ReadTimeSeconds:F0}s");
        Console.WriteLine($"  First Impression: {simulation.RecruiterScore.FirstImpression}");

        // Heatmap
        if (simulation.RecruiterScore.Heatmap.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Section Heatmap:");
            Console.ResetColor();

            var maxSectionLen = simulation.RecruiterScore.Heatmap
                .Max(h => h.Section.Length);

            foreach (var section in simulation.RecruiterScore.Heatmap)
            {
                var attentionColor = section.Attention switch
                {
                    "High" => ConsoleColor.Green,
                    "Medium" => ConsoleColor.Yellow,
                    "Low" => ConsoleColor.Red,
                    "Skipped" => ConsoleColor.DarkGray,
                    _ => ConsoleColor.White
                };

                Console.Write($"    {section.Section.PadRight(maxSectionLen)} ");
                Console.ForegroundColor = attentionColor;
                Console.Write($"[{section.Attention,-7}]");
                Console.ResetColor();

                if (!string.IsNullOrEmpty(section.Recommendation))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" {section.Recommendation}");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
        }

        if (simulation.RecruiterScore.Concerns.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Recruiter Concerns:");
            foreach (var concern in simulation.RecruiterScore.Concerns)
            {
                Console.WriteLine($"    - {concern}");
            }
            Console.ResetColor();
        }

        // Tech Lead Details
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  --- Tech Lead Review ---");
        Console.ResetColor();
        Console.WriteLine($"  Technical Depth: {simulation.TechLeadScore.DepthPercent:F0}%");

        if (simulation.TechLeadScore.ImpressivePoints.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Impressive Points:");
            foreach (var point in simulation.TechLeadScore.ImpressivePoints)
            {
                Console.WriteLine($"    + {point}");
            }
            Console.ResetColor();
        }

        if (simulation.TechLeadScore.RedFlags.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Red Flags:");
            foreach (var flag in simulation.TechLeadScore.RedFlags)
            {
                Console.WriteLine($"    ! {flag}");
            }
            Console.ResetColor();
        }

        if (simulation.TechLeadScore.QuestionsWouldAsk.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Questions They'd Ask:");
            foreach (var question in simulation.TechLeadScore.QuestionsWouldAsk)
            {
                Console.WriteLine($"    ? {question}");
            }
            Console.ResetColor();
        }

        // Critical issues
        if (simulation.CriticalIssues.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  CRITICAL ISSUES:");
            foreach (var issue in simulation.CriticalIssues)
            {
                Console.WriteLine($"    [!] {issue}");
            }
            Console.ResetColor();
        }

        // Recommendations
        if (simulation.Recommendations.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Recommendations:");
            foreach (var rec in simulation.Recommendations)
            {
                Console.WriteLine($"    -> {rec}");
            }
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }

    private static void PrintScoreBar(string label, double score)
    {
        var color = score switch
        {
            >= 80 => ConsoleColor.Green,
            >= 60 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.Write($"  {label,-18} ");
        Console.ForegroundColor = color;

        var filled = (int)(score / 5);
        var empty = 20 - filled;
        Console.Write($"[{new string('#', filled)}{new string('-', empty)}] {score:F0}/100");

        Console.ResetColor();
        Console.WriteLine();
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
