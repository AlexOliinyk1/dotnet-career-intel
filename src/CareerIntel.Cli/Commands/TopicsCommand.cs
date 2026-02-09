using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that infers required topics from job descriptions and shows market demand.
/// Usage: career-intel topics [--input path] [--vacancy-id id]
/// </summary>
public static class TopicsCommand
{
    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var vacancyOption = new Option<string?>(
            "--vacancy-id",
            description: "Analyze a specific vacancy by ID to see its inferred topics.");

        var command = new Command("topics", "Infer interview topics from job descriptions and analyze market demand")
        {
            inputOption,
            vacancyOption
        };

        command.SetHandler(ExecuteAsync, inputOption, vacancyOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input, string? vacancyId)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.TopicsCommand");
        var topicEngine = serviceProvider.GetRequiredService<TopicInferenceEngine>();

        // Load vacancies
        var inputPath = input ?? FindLatestVacanciesFile();
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        var json = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        if (vacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No vacancies found in input file.");
            Console.ResetColor();
            return;
        }

        // Single vacancy analysis
        if (!string.IsNullOrEmpty(vacancyId))
        {
            var vacancy = vacancies.FirstOrDefault(v =>
                string.Equals(v.Id, vacancyId, StringComparison.OrdinalIgnoreCase));

            if (vacancy is null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Vacancy '{vacancyId}' not found.");
                Console.ResetColor();
                return;
            }

            var profile = topicEngine.InferTopics(vacancy);
            PrintVacancyTopics(vacancy, profile);
            return;
        }

        // Market demand analysis
        Console.WriteLine($"Analyzing {vacancies.Count} vacancies for topic demand...\n");

        var demand = topicEngine.AnalyzeMarketDemand(vacancies);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.WriteLine("              TOPIC DEMAND ANALYSIS                   ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        Console.WriteLine($"\n  Vacancies analyzed: {demand.TotalVacanciesAnalyzed}\n");

        // Topic rankings
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Topic Rankings by Market Demand:");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  {"#",-4} {"Topic",-30} {"Vacancies",10} {"%",6} {"Avg Salary",12}");
        Console.WriteLine($"  {new string('─', 65)}");
        Console.ResetColor();

        for (var i = 0; i < demand.TopicRankings.Count; i++)
        {
            var topic = demand.TopicRankings[i];
            var barLength = (int)(topic.PercentageOfVacancies / 5);
            var bar = new string('█', barLength);

            var color = topic.PercentageOfVacancies switch
            {
                >= 50 => ConsoleColor.Green,
                >= 25 => ConsoleColor.Yellow,
                _ => ConsoleColor.White
            };

            Console.Write($"  {i + 1,-4} ");
            Console.ForegroundColor = color;
            Console.Write($"{topic.TopicName,-30} ");
            Console.ResetColor();
            Console.Write($"{topic.VacancyCount,10} ");
            Console.Write($"{topic.PercentageOfVacancies,5:F0}% ");

            if (topic.AvgSalaryForTopic > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"${topic.AvgSalaryForTopic,11:N0}");
                Console.ResetColor();
            }

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"       {bar}");
            Console.ResetColor();

            if (topic.TopCompanies.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" ({string.Join(", ", topic.TopCompanies.Take(3))})");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // Hottest individual skills
        if (demand.HottestSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Hottest Individual Skills:");
            Console.ResetColor();

            for (var i = 0; i < Math.Min(demand.HottestSkills.Count, 20); i++)
            {
                var color = i < 5 ? ConsoleColor.Green : i < 10 ? ConsoleColor.Yellow : ConsoleColor.White;
                Console.ForegroundColor = color;
                Console.Write($"  {i + 1,2}. {demand.HottestSkills[i]}");
                Console.ResetColor();

                if ((i + 1) % 4 == 0)
                    Console.WriteLine();
                else
                    Console.Write("    ");
            }
            Console.WriteLine();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintVacancyTopics(JobVacancy vacancy, InferredTopicProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Topic Analysis: {vacancy.Title} at {vacancy.Company}");
        Console.WriteLine($"  {new string('─', 55)}");
        Console.ResetColor();

        Console.WriteLine($"  Estimated difficulty: {profile.EstimatedInterviewDifficulty}");

        if (!string.IsNullOrEmpty(profile.SalaryContext))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Salary: {profile.SalaryContext}");
            Console.ResetColor();
        }

        if (profile.SenioritySignals.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Seniority signals: {string.Join(", ", profile.SenioritySignals)}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  Inferred Topics:");
        Console.ResetColor();

        foreach (var topic in profile.InferredTopics.OrderByDescending(t => t.Confidence))
        {
            var confColor = topic.Confidence switch
            {
                >= 70 => ConsoleColor.Green,
                >= 40 => ConsoleColor.Yellow,
                _ => ConsoleColor.DarkGray
            };

            Console.ForegroundColor = confColor;
            Console.Write($"    [{topic.Confidence,3:F0}%] ");
            Console.ResetColor();
            Console.Write($"{topic.TopicName,-25} ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"({string.Join(", ", topic.DetectedKeywords.Take(5))})");
            Console.ResetColor();
        }

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
