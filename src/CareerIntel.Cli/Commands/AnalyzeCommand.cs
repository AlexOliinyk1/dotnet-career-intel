using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that analyzes scraped vacancies for skill demand and market trends.
/// Usage: career-intel analyze [--input path] [--output path]
/// </summary>
public static class AnalyzeCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var outputOption = new Option<string?>(
            "--output",
            description: "Output file path. Defaults to data/snapshot-{date}.json");

        var command = new Command("analyze", "Analyze skill demand and market trends from scraped vacancies")
        {
            inputOption,
            outputOption
        };

        command.SetHandler(ExecuteAsync, inputOption, outputOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input, string? output)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CareerIntel.Cli.AnalyzeCommand");
        var analyzer = serviceProvider.GetRequiredService<ISkillAnalyzer>();

        // Resolve input file
        var inputPath = input ?? FindLatestVacanciesFile();

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Loading vacancies from: {inputPath}");

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

        Console.WriteLine($"Analyzing {vacancies.Count} vacancies...\n");

        // Skill demand analysis
        var skills = await analyzer.AnalyzeSkillDemandAsync(vacancies);
        var snapshot = await analyzer.GenerateSnapshotAsync(vacancies);

        // Print top 30 skills
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Top 30 Skills by Demand ===");
        Console.ResetColor();

        var topSkills = skills.Take(30).ToList();
        for (var i = 0; i < topSkills.Count; i++)
        {
            var skill = topSkills[i];
            var bar = new string('#', (int)(skill.MarketDemandScore / 5));
            Console.WriteLine($"  {i + 1,2}. {skill.SkillName,-30} {skill.MarketDemandScore,5:F1}% {bar}");
        }

        // Print top 10 skill combinations
        if (snapshot.TopSkillCombinations.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Top 10 Skill Bundles ===");
            Console.ResetColor();

            foreach (var combo in snapshot.TopSkillCombinations.Take(10))
            {
                Console.WriteLine($"  {combo}");
            }
        }

        // Salary by seniority
        if (snapshot.AverageSalaryByLevel.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Average Salary by Seniority ===");
            Console.ResetColor();

            foreach (var (level, salary) in snapshot.AverageSalaryByLevel.OrderBy(kvp => kvp.Key))
            {
                Console.WriteLine($"  {level,-12} ${salary:N0}");
            }
        }

        // Remote policy distribution
        if (snapshot.RemotePolicyDistribution.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Remote Policy Distribution ===");
            Console.ResetColor();

            foreach (var (policy, count) in snapshot.RemotePolicyDistribution.OrderByDescending(kvp => kvp.Value))
            {
                var pct = (double)count / vacancies.Count * 100;
                Console.WriteLine($"  {policy,-16} {count,4} ({pct:F1}%)");
            }
        }

        // Save snapshot
        var outputPath = output ?? Path.Combine(
            Program.DataDirectory,
            $"snapshot-{DateTime.Now:yyyy-MM-dd}.json");

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(outputPath, snapshotJson);

        Console.WriteLine($"\nSnapshot saved to: {outputPath}");
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
