using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that assesses your competitive positioning for job vacancies.
/// Produces "Top X%" ranking, response probability, and actionable tips.
/// Usage: career-intel assess [--input path] [--top n] [--min-score n] [--tier tier]
/// </summary>
public static class AssessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 15,
            description: "Number of top assessments to display");

        var minScoreOption = new Option<double>(
            "--min-score",
            getDefaultValue: () => 0,
            description: "Minimum competitiveness score to display (0-100)");

        var tierOption = new Option<string?>(
            "--tier",
            description: "Filter by tier: 'top', 'strong', 'competitive', 'average', 'longshot'");

        var verboseOption = new Option<bool>(
            "--verbose",
            getDefaultValue: () => false,
            description: "Show detailed breakdown for each vacancy");

        var command = new Command("assess",
            "AI-powered applicant competitiveness assessment — your chances of hearing back")
        {
            inputOption,
            topOption,
            minScoreOption,
            tierOption,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, inputOption, topOption, minScoreOption, tierOption, verboseOption);
        return command;
    }

    private static async Task ExecuteAsync(string? input, int top, double minScore, string? tier, bool verbose)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var engine = serviceProvider.GetRequiredService<ApplicantCompetitivenessEngine>();

        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n  Profile not found. Run 'career-intel profile create' first.");
            Console.ResetColor();
            return;
        }

        var profileJson = await File.ReadAllTextAsync(profilePath);
        var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, JsonOptions);
        if (profile is null || string.IsNullOrEmpty(profile.Personal.Name))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  Profile is empty or incomplete. Run 'career-intel profile create' to set it up.");
            Console.ResetColor();
            return;
        }

        // Load vacancies
        var inputPath = input ?? FindLatestVacanciesFile();
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n  No vacancies file found. Run 'career-intel scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        var vacancyJson = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(vacancyJson, JsonOptions) ?? [];

        if (vacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  No vacancies found in the input file.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.WriteLine("       APPLICANT COMPETITIVENESS ASSESSMENT           ");
        Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  Profile: {profile.Personal.Name} | {profile.Skills.Count} skills");
        Console.WriteLine($"  Vacancies: {vacancies.Count} from {Path.GetFileName(inputPath)}");
        Console.ResetColor();

        // Run batch assessment
        var report = engine.AssessAll(vacancies, profile);

        // Filter by tier if specified
        var filtered = report.Assessments.AsEnumerable();
        if (!string.IsNullOrEmpty(tier))
        {
            var tierNorm = tier.ToLowerInvariant().Trim();
            filtered = tierNorm switch
            {
                "top" => filtered.Where(a => a.Tier == "Top Candidate"),
                "strong" => filtered.Where(a => a.Tier == "Strong Contender"),
                "competitive" => filtered.Where(a => a.Tier == "Competitive"),
                "average" => filtered.Where(a => a.Tier == "Average"),
                "longshot" or "long-shot" => filtered.Where(a => a.Tier == "Long Shot"),
                _ => filtered
            };
        }

        // Filter by min score
        if (minScore > 0)
            filtered = filtered.Where(a => a.CompetitivenessScore >= minScore);

        var results = filtered.Take(top).ToList();

        if (results.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  No assessments match the specified filters.");
            Console.ResetColor();
            return;
        }

        // Display individual assessments
        for (var i = 0; i < results.Count; i++)
        {
            PrintAssessment(i + 1, results[i], verbose);
        }

        // Display report summary
        PrintReportSummary(report);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.ResetColor();
    }

    private static void PrintAssessment(int rank, CompetitivenessAssessment a, bool verbose)
    {
        var tierColor = a.Tier switch
        {
            "Top Candidate" => ConsoleColor.Green,
            "Strong Contender" => ConsoleColor.Cyan,
            "Competitive" => ConsoleColor.Yellow,
            "Average" => ConsoleColor.DarkYellow,
            _ => ConsoleColor.Red
        };

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"\n  #{rank} ");
        Console.ForegroundColor = tierColor;
        Console.Write($"[{a.CompetitivenessScore:F0}/100] {a.Tier}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" | Top {100 - a.EstimatedPercentile}%");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($" | Response: ");
        var responseColor = a.ResponseProbability switch
        {
            >= 50 => ConsoleColor.Green,
            >= 25 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.ForegroundColor = responseColor;
        Console.Write($"{a.ResponseProbability:F0}%");
        Console.ResetColor();
        Console.WriteLine();

        // Title & company
        Console.Write($"     {a.Vacancy.Title}");
        if (!string.IsNullOrEmpty(a.Vacancy.Company))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" at {a.Vacancy.Company}");
            Console.ResetColor();
        }
        Console.WriteLine();

        // Location & salary
        var location = !string.IsNullOrEmpty(a.Vacancy.City) && !string.IsNullOrEmpty(a.Vacancy.Country)
            ? $"{a.Vacancy.City}, {a.Vacancy.Country}"
            : !string.IsNullOrEmpty(a.Vacancy.Country) ? a.Vacancy.Country
            : !string.IsNullOrEmpty(a.Vacancy.City) ? a.Vacancy.City
            : null;

        var parts = new List<string>();
        if (location is not null) parts.Add(location);
        parts.Add(a.Vacancy.RemotePolicy.ToString());
        if (a.Vacancy.SalaryMin.HasValue || a.Vacancy.SalaryMax.HasValue)
        {
            var min = a.Vacancy.SalaryMin.HasValue ? $"${a.Vacancy.SalaryMin:N0}" : "?";
            var max = a.Vacancy.SalaryMax.HasValue ? $"${a.Vacancy.SalaryMax:N0}" : "?";
            parts.Add($"{min}-{max}");
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"     {string.Join(" | ", parts)}");
        Console.ResetColor();

        // Strengths
        if (a.StrengthFactors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            foreach (var s in a.StrengthFactors.Take(3))
                Console.WriteLine($"     + {s}");
            Console.ResetColor();
        }

        // Weaknesses
        if (a.WeaknessFactors.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var w in a.WeaknessFactors.Take(3))
                Console.WriteLine($"     - {w}");
            Console.ResetColor();
        }

        // Tips
        if (a.Tips.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var t in a.Tips.Take(2))
                Console.WriteLine($"     \u2192 {t}");
            Console.ResetColor();
        }

        // Verbose: detailed breakdown
        if (verbose)
        {
            var b = a.Breakdown;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"     Breakdown: Skill Depth {b.SkillDepthScore:F0} | Experience {b.ExperienceRelevanceScore:F0} | Seniority {b.SeniorityFitScore:F0} | Salary {b.SalaryPositioningScore:F0} | Fresh {b.FreshnessScore:F0} | Competition {b.MarketCompetitionScore:F0} | Platform {b.PlatformResponseScore:F0}");
            Console.ResetColor();
        }

        // URL
        if (!string.IsNullOrEmpty(a.Vacancy.Url))
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"     {a.Vacancy.Url}");
            Console.ResetColor();
        }
    }

    private static void PrintReportSummary(CompetitivenessReport report)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  \u2550\u2550\u2550 Assessment Summary \u2550\u2550\u2550");
        Console.ResetColor();

        Console.WriteLine($"  Assessed: {report.TotalVacancies} vacancies | Avg score: {report.AverageScore:F1}");
        Console.WriteLine();

        // Tier distribution
        PrintTierBar("Top Candidate",    report.TopCandidateCount,    report.TotalVacancies, ConsoleColor.Green);
        PrintTierBar("Strong Contender", report.StrongContenderCount, report.TotalVacancies, ConsoleColor.Cyan);
        PrintTierBar("Competitive",      report.CompetitiveCount,     report.TotalVacancies, ConsoleColor.Yellow);
        PrintTierBar("Average",          report.AverageCount,         report.TotalVacancies, ConsoleColor.DarkYellow);
        PrintTierBar("Long Shot",        report.LongShotCount,        report.TotalVacancies, ConsoleColor.Red);

        // Top contributing skills
        if (report.TopContributingSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\n  Your strongest skills: ");
            Console.ResetColor();
            Console.WriteLine(string.Join(", ", report.TopContributingSkills.Take(5)));
        }

        // Most needed skills
        if (report.MostNeededSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  Skills to invest in:   ");
            Console.ResetColor();
            Console.WriteLine(string.Join(", ", report.MostNeededSkills.Take(5)));
        }

        // Overall verdict
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"\n  {report.OverallVerdict}");
        Console.ResetColor();
    }

    private static void PrintTierBar(string label, int count, int total, ConsoleColor color)
    {
        var pct = total > 0 ? (double)count / total * 100 : 0;
        var barLen = total > 0 ? (int)Math.Round((double)count / total * 20) : 0;
        var bar = new string('\u2588', barLen) + new string('\u2591', 20 - barLen);

        Console.ForegroundColor = color;
        Console.Write($"  {label,-18}");
        Console.ResetColor();
        Console.Write($" {bar} ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{count,3}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($" ({pct:F0}%)");
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
