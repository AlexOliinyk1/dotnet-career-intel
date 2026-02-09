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

        // Enhanced Skill Premium Calculator
        if (report.BySkill.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  ═══ SKILL PREMIUM CALCULATOR ═══");
            Console.ResetColor();

            // 1. Top individual skills by premium
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  Top Individual Skills:");
            Console.ResetColor();

            var topSkills = report.BySkill
                .OrderByDescending(s => s.SalaryPremiumPercent)
                .Take(10);

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

            // 2. Skill combinations with highest premiums
            var skillCombos = CalculateSkillCombinationPremiums(vacancies, report);
            if (skillCombos.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n  Top Skill Combinations (Stacking Bonus):");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  {"Combination",-35} {"Avg",12} {"Premium",9} {"Jobs",5}");
                Console.WriteLine($"  {new string('─', 65)}");
                Console.ResetColor();

                foreach (var combo in skillCombos.Take(5))
                {
                    var premColor = combo.Premium switch
                    {
                        > 30 => ConsoleColor.Green,
                        > 15 => ConsoleColor.Yellow,
                        _ => ConsoleColor.White
                    };
                    Console.Write($"  {combo.Skills,-35} ");
                    Console.Write($"${combo.AverageSalary,11:N0} ");
                    Console.ForegroundColor = premColor;
                    Console.Write($"{combo.Premium,+8:+0.0}% ");
                    Console.ResetColor();
                    Console.WriteLine($"{combo.Count,4}");
                }
            }

            // 3. Demand-adjusted premium scores (sweet spot analysis)
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  Sweet Spot Analysis (Premium × Demand):");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  (High score = good premium with many opportunities)");
            Console.ResetColor();

            var sweetSpot = report.BySkill
                .Select(s => new
                {
                    s.Skill,
                    s.MedianSalary,
                    s.SalaryPremiumPercent,
                    s.VacancyCount,
                    Score = s.SalaryPremiumPercent * Math.Log10(s.VacancyCount + 1) * 10
                })
                .OrderByDescending(s => s.Score)
                .Take(10);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {"Skill",-22} {"Premium",9} {"Jobs",6} {"Score",8}");
            Console.WriteLine($"  {new string('─', 50)}");
            Console.ResetColor();

            foreach (var skill in sweetSpot)
            {
                var scoreColor = skill.Score switch
                {
                    > 100 => ConsoleColor.Green,
                    > 50 => ConsoleColor.Yellow,
                    _ => ConsoleColor.White
                };
                Console.Write($"  {skill.Skill,-22} ");
                Console.ForegroundColor = skill.SalaryPremiumPercent > 0 ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write($"{skill.SalaryPremiumPercent,+8:+0.0}% ");
                Console.ResetColor();
                Console.Write($"{skill.VacancyCount,5} ");
                Console.ForegroundColor = scoreColor;
                Console.WriteLine($"{skill.Score,7:F0}");
                Console.ResetColor();
            }

            // 4. Personalized recommendations (if profile exists)
            if (profile != null && profile.Skills.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n  💡 Personalized Learning ROI:");
                Console.ResetColor();

                var userSkills = new HashSet<string>(
                    profile.Skills.Select(s => s.SkillName),
                    StringComparer.OrdinalIgnoreCase);

                var recommendations = report.BySkill
                    .Where(s => !userSkills.Contains(s.Skill))
                    .Where(s => s.SalaryPremiumPercent > 5 && s.VacancyCount >= 3)
                    .Select(s => new
                    {
                        s.Skill,
                        s.SalaryPremiumPercent,
                        s.MedianSalary,
                        s.VacancyCount,
                        ROI = s.SalaryPremiumPercent * Math.Log10(s.VacancyCount + 1) * 5
                    })
                    .OrderByDescending(s => s.ROI)
                    .Take(5);

                if (recommendations.Any())
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  (Skills you don't have yet with best ROI)");
                    Console.WriteLine();
                    Console.ResetColor();

                    foreach (var rec in recommendations)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write($"  → {rec.Skill}");
                        Console.ResetColor();
                        Console.Write($": +{rec.SalaryPremiumPercent:F0}% premium, ");
                        Console.Write($"{rec.VacancyCount} jobs, ");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"ROI Score: {rec.ROI:F0}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ✓ You already have the most valuable skills!");
                    Console.ResetColor();
                }
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

    private static List<SkillComboPremium> CalculateSkillCombinationPremiums(
        List<JobVacancy> vacancies,
        SalaryReport report)
    {
        var combos = new List<SkillComboPremium>();

        // Find vacancies with salary data
        var withSalary = vacancies
            .Where(v => v.SalaryMin.HasValue && v.SalaryMax.HasValue)
            .ToList();

        if (withSalary.Count == 0)
            return combos;

        var baseline = (decimal)report.MedianSalaryMax;
        if (baseline == 0)
            return combos;

        // Group by skill pairs that appear together
        var skillPairs = new Dictionary<string, (List<decimal> Salaries, int Count)>();

        foreach (var vacancy in withSalary)
        {
            var skills = vacancy.RequiredSkills
                .Concat(vacancy.PreferredSkills)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            // Get all pairs
            for (int i = 0; i < skills.Count; i++)
            {
                for (int j = i + 1; j < skills.Count && j < i + 3; j++) // Limit to avoid explosion
                {
                    var combo = $"{skills[i]} + {skills[j]}";
                    var avgSalary = (vacancy.SalaryMin!.Value + vacancy.SalaryMax!.Value) / 2m;

                    if (!skillPairs.ContainsKey(combo))
                    {
                        skillPairs[combo] = (new List<decimal>(), 0);
                    }

                    var existing = skillPairs[combo];
                    existing.Salaries.Add(avgSalary);
                    existing.Count++;
                    skillPairs[combo] = existing;
                }
            }
        }

        // Calculate premiums for combinations that appear frequently enough
        foreach (var (combo, (salaries, count)) in skillPairs.Where(kv => kv.Value.Count >= 3))
        {
            var avgSalary = salaries.OrderBy(s => s).Skip(salaries.Count / 2).First(); // Median
            var premium = (double)(avgSalary - baseline) / (double)baseline * 100;

            combos.Add(new SkillComboPremium
            {
                Skills = combo,
                AverageSalary = avgSalary,
                Premium = premium,
                Count = count
            });
        }

        return combos.OrderByDescending(c => c.Premium).ToList();
    }

    private sealed class SkillComboPremium
    {
        public string Skills { get; set; } = string.Empty;
        public decimal AverageSalary { get; set; }
        public double Premium { get; set; }
        public int Count { get; set; }
    }
}
