using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;

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

        // Technology demand rates
        var techReport = TechDemandAnalyzer.Analyze(vacancies);
        if (techReport.Items.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Technology Demand Rates ===");
            Console.ResetColor();

            foreach (var item in techReport.Items.Take(30))
            {
                var bar = new string('#', (int)(item.Percentage / 3));
                Console.Write($"  {item.Technology,-22}");
                Console.ForegroundColor = item.Percentage >= 30 ? ConsoleColor.Green :
                    item.Percentage >= 15 ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
                Console.Write($" {item.Percentage,5:F1}%");
                Console.ResetColor();
                Console.Write($" ({item.Count,3} of {techReport.TotalVacancies}) ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[{item.Category}]");
                Console.ResetColor();
                Console.Write($" {bar}");
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Demand by Category ===");
            Console.ResetColor();

            foreach (var cat in techReport.ByCategory)
            {
                Console.WriteLine($"  {cat.Category,-16} {cat.TotalMentions,4} mentions across {cat.TechCount} technologies (top: {cat.TopTech})");
            }
        }

        // Time-series trend analysis
        var historicalSnapshots = LoadHistoricalSnapshots();
        if (historicalSnapshots.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Time-Series Skill Trends ===");
            Console.ResetColor();

            var previousSnapshot = historicalSnapshots.OrderByDescending(s => s.Date).FirstOrDefault();
            if (previousSnapshot != null)
            {
                var trends = analyzer.IdentifyTrends(previousSnapshot, snapshot);
                var topTrends = trends.Take(15).ToList();

                Console.WriteLine($"Comparing with snapshot from {previousSnapshot.Date:yyyy-MM-dd} ({previousSnapshot.TotalVacancies} vacancies)\n");

                // Show top growing skills
                var growing = topTrends.Where(t => t.GrowthRate > 0).Take(10).ToList();
                if (growing.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("📈 Top Growing Skills:");
                    Console.ResetColor();
                    foreach (var (skill, rate) in growing)
                    {
                        var arrow = rate > 50 ? "🚀" : rate > 20 ? "⬆️ " : "↗️ ";
                        Console.WriteLine($"  {arrow} {skill,-30} +{rate:F1}%");
                    }
                }

                // Show top declining skills
                var declining = trends.Where(t => t.GrowthRate < -10).Take(5).ToList();
                if (declining.Count > 0)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("📉 Declining Skills:");
                    Console.ResetColor();
                    foreach (var (skill, rate) in declining)
                    {
                        Console.WriteLine($"  ↘️  {skill,-30} {rate:F1}%");
                    }
                }

                // Show trend summary across all historical data
                if (historicalSnapshots.Count >= 2)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"📊 Historical Data: {historicalSnapshots.Count} snapshots from {historicalSnapshots.Min(s => s.Date):yyyy-MM-dd} to {historicalSnapshots.Max(s => s.Date):yyyy-MM-dd}");
                    Console.ResetColor();
                }
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

    private static List<MarketSnapshot> LoadHistoricalSnapshots()
    {
        if (!Directory.Exists(Program.DataDirectory))
            return [];

        var snapshots = new List<MarketSnapshot>();
        var snapshotFiles = Directory.GetFiles(Program.DataDirectory, "snapshot-*.json")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var file in snapshotFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var snapshot = JsonSerializer.Deserialize<MarketSnapshot>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }
            catch
            {
                // Skip invalid snapshot files
            }
        }

        return snapshots;
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
