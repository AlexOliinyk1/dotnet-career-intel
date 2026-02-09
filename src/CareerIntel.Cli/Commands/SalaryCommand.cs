using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that analyzes salary data across scraped vacancies.
/// Usage: career-intel salary [--input path] [--skills filter]
/// </summary>
public static class SalaryCommand
{
    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var skillsOption = new Option<string?>(
            "--skills",
            description: "Comma-separated skills to analyze salary for (e.g., \"Azure,Kubernetes\")");

        var command = new Command("salary", "Analyze salary intelligence across scraped vacancies")
        {
            inputOption,
            skillsOption
        };

        command.SetHandler(ExecuteAsync, inputOption, skillsOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input, string? skills)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.SalaryCommand");
        var salaryEngine = serviceProvider.GetRequiredService<SalaryIntelligenceEngine>();
        var matchEngine = serviceProvider.GetRequiredService<Core.Interfaces.IMatchEngine>();

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

        // Load profile if exists
        UserProfile? profile = null;
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (File.Exists(profilePath))
        {
            var profileJson = await File.ReadAllTextAsync(profilePath);
            profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        // Specific skills analysis
        if (!string.IsNullOrEmpty(skills))
        {
            var skillList = skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var skillData = salaryEngine.GetSalaryForSkills(vacancies, skillList);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n  Salary Analysis for: {string.Join(", ", skillList)}");
            Console.WriteLine($"  {new string('─', 50)}");
            Console.ResetColor();

            Console.WriteLine($"  Matching vacancies: {skillData.MatchingVacancies}");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Salary range: ${skillData.MinSalary:N0} - ${skillData.MaxSalary:N0}");
            Console.WriteLine($"  Median: ${skillData.MedianSalary:N0}");
            Console.ResetColor();

            if (skillData.TopCompanies.Count > 0)
            {
                Console.WriteLine($"  Top companies: {string.Join(", ", skillData.TopCompanies.Take(5))}");
            }

            Console.WriteLine();
            return;
        }

        // Full salary report
        var report = salaryEngine.GenerateReport(vacancies, profile);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("              SALARY INTELLIGENCE REPORT              ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        Console.WriteLine($"\n  Vacancies analyzed: {report.TotalVacanciesAnalyzed}");
        Console.WriteLine($"  With salary data:  {report.TotalVacanciesWithSalary}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n  Market Median: ${report.MedianSalaryMin:N0} - ${report.MedianSalaryMax:N0} ({report.PrimaryCurrency})");
        Console.ResetColor();

        // By seniority
        if (report.BySeniority.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  By Seniority:");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {"Level",-14} {"Min",10} {"Median",10} {"Max",10} {"Count",7}");
            Console.WriteLine($"  {new string('─', 55)}");
            Console.ResetColor();

            foreach (var band in report.BySeniority)
            {
                var color = band.Level switch
                {
                    "Senior" or "Lead" => ConsoleColor.Green,
                    "Middle" => ConsoleColor.Yellow,
                    _ => ConsoleColor.White
                };
                Console.ForegroundColor = color;
                Console.WriteLine($"  {band.Level,-14} ${band.MinSalary,9:N0} ${band.MedianSalary,9:N0} ${band.MaxSalary,9:N0} {band.VacancyCount,6}");
            }
            Console.ResetColor();
        }

        // Top skills by salary premium
        if (report.BySkill.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Top Skills by Salary Premium:");
            Console.ResetColor();

            var topSkills = report.BySkill
                .OrderByDescending(s => s.SalaryPremiumPercent)
                .Take(15);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {"Skill",-22} {"Avg Salary",12} {"Premium",9} {"Count",6}");
            Console.WriteLine($"  {new string('─', 55)}");
            Console.ResetColor();

            foreach (var skill in topSkills)
            {
                var premColor = skill.SalaryPremiumPercent switch
                {
                    > 20 => ConsoleColor.Green,
                    > 0 => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };
                Console.Write($"  {skill.Skill,-22} ");
                Console.Write($"${skill.MedianSalary,11:N0} ");
                Console.ForegroundColor = premColor;
                Console.Write($"{skill.SalaryPremiumPercent,+8:+0.0;-0.0}% ");
                Console.ResetColor();
                Console.WriteLine($"{skill.VacancyCount,5}");
            }
        }

        // Top companies by salary
        if (report.ByCompany.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Top Companies by Salary:");
            Console.ResetColor();

            var topCompanies = report.ByCompany
                .OrderByDescending(c => c.AverageSalaryMax)
                .Take(10);

            foreach (var company in topCompanies)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"  ${company.AverageSalaryMin:N0}-${company.AverageSalaryMax:N0}  ");
                Console.ResetColor();
                Console.Write($"{company.Company} ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"({company.VacancyCount} vacancies)");
                Console.ResetColor();
            }
        }

        // User market position
        if (report.UserPositionPercentile.HasValue)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Your Market Position:");
            Console.ResetColor();

            var percentile = report.UserPositionPercentile.Value;
            var posColor = percentile switch
            {
                >= 75 => ConsoleColor.Green,
                >= 50 => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            Console.ForegroundColor = posColor;
            Console.WriteLine($"  You are in the {percentile:F0}th percentile of the market");
            Console.ResetColor();
        }

        // Market summary
        if (!string.IsNullOrEmpty(report.MarketSummary))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"\n  {report.MarketSummary}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
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
