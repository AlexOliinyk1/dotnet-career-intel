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

        // Display market positioning insights
        PrintMarketPositioning(report, profile);

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

    private static void PrintMarketPositioning(CompetitivenessReport report, UserProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  ═══ Market Positioning Analysis ═══");
        Console.ResetColor();

        // 1. Geographic Competitiveness
        var geoAnalysis = report.Assessments
            .Where(a => !string.IsNullOrEmpty(a.Vacancy.Country))
            .GroupBy(a => a.Vacancy.Country!)
            .Select(g => new
            {
                Country = g.Key,
                AvgScore = g.Average(a => a.CompetitivenessScore),
                TopCandidateCount = g.Count(a => a.Tier == "Top Candidate"),
                TotalCount = g.Count()
            })
            .OrderByDescending(x => x.AvgScore)
            .Take(5)
            .ToList();

        if (geoAnalysis.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n  Geographic Competitiveness (Top 5 Markets):");
            Console.ResetColor();

            foreach (var geo in geoAnalysis)
            {
                var pct = geo.TotalCount > 0 ? (double)geo.TopCandidateCount / geo.TotalCount * 100 : 0;
                var color = geo.AvgScore >= 65 ? ConsoleColor.Green : geo.AvgScore >= 50 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;

                Console.ForegroundColor = color;
                Console.Write($"    {geo.Country,-20}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" Avg: {geo.AvgScore:F0}/100");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  |  {geo.TopCandidateCount}/{geo.TotalCount} top ({pct:F0}%)");
                Console.ResetColor();
            }
        }

        // 2. Skill Gap Impact Analysis
        var skillImpact = AnalyzeSkillGapImpact(report);
        if (skillImpact.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n  High-Impact Skill Gaps (learn these to jump tiers):");
            Console.ResetColor();

            foreach (var skill in skillImpact.Take(5))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"    {skill.SkillName,-25}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" Missing in {skill.MissingCount} roles");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" | Avg tier score: {skill.AvgScoreWithoutSkill:F0} → {skill.PotentialScore:F0}");
                Console.ResetColor();
            }
        }

        // 3. Salary Band Positioning
        var salaryBands = report.Assessments
            .Where(a => a.Vacancy.SalaryMax.HasValue)
            .GroupBy(a => ClassifySalaryBand(a.Vacancy.SalaryMax!.Value))
            .Select(g => new
            {
                Band = g.Key,
                AvgScore = g.Average(a => a.CompetitivenessScore),
                TopCandidateCount = g.Count(a => a.Tier == "Top Candidate"),
                TotalCount = g.Count()
            })
            .OrderBy(x => x.Band)
            .ToList();

        if (salaryBands.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n  Competitiveness by Salary Band:");
            Console.ResetColor();

            foreach (var band in salaryBands)
            {
                var bandLabel = band.Band switch
                {
                    0 => "< $60k",
                    1 => "$60k - $100k",
                    2 => "$100k - $150k",
                    3 => "$150k - $200k",
                    _ => "$200k+"
                };

                var pct = band.TotalCount > 0 ? (double)band.TopCandidateCount / band.TotalCount * 100 : 0;
                var color = band.AvgScore >= 65 ? ConsoleColor.Green : band.AvgScore >= 50 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;

                Console.ForegroundColor = color;
                Console.Write($"    {bandLabel,-20}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" Avg: {band.AvgScore:F0}/100");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  |  {band.TopCandidateCount}/{band.TotalCount} top ({pct:F0}%)");
                Console.ResetColor();
            }
        }

        // 4. Remote Policy Positioning
        var remoteAnalysis = report.Assessments
            .GroupBy(a => a.Vacancy.RemotePolicy)
            .Select(g => new
            {
                Policy = g.Key,
                AvgScore = g.Average(a => a.CompetitivenessScore),
                TopCandidateCount = g.Count(a => a.Tier == "Top Candidate"),
                TotalCount = g.Count()
            })
            .OrderByDescending(x => x.AvgScore)
            .ToList();

        if (remoteAnalysis.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n  Competitiveness by Work Arrangement:");
            Console.ResetColor();

            foreach (var remote in remoteAnalysis)
            {
                var pct = remote.TotalCount > 0 ? (double)remote.TopCandidateCount / remote.TotalCount * 100 : 0;
                var color = remote.AvgScore >= 65 ? ConsoleColor.Green : remote.AvgScore >= 50 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;

                Console.ForegroundColor = color;
                Console.Write($"    {remote.Policy,-20}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" Avg: {remote.AvgScore:F0}/100");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  |  {remote.TopCandidateCount}/{remote.TotalCount} top ({pct:F0}%)");
                Console.ResetColor();
            }
        }

        // 5. Seniority Sweet Spot
        var seniorityAnalysis = report.Assessments
            .Where(a => a.Vacancy.SeniorityLevel != Core.Enums.SeniorityLevel.Unknown)
            .GroupBy(a => a.Vacancy.SeniorityLevel)
            .Select(g => new
            {
                Level = g.Key,
                AvgScore = g.Average(a => a.CompetitivenessScore),
                TopCandidateCount = g.Count(a => a.Tier == "Top Candidate"),
                TotalCount = g.Count()
            })
            .OrderByDescending(x => x.AvgScore)
            .ToList();

        if (seniorityAnalysis.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n  Seniority Sweet Spot:");
            Console.ResetColor();

            foreach (var seniority in seniorityAnalysis)
            {
                var pct = seniority.TotalCount > 0 ? (double)seniority.TopCandidateCount / seniority.TotalCount * 100 : 0;
                var color = seniority.AvgScore >= 65 ? ConsoleColor.Green : seniority.AvgScore >= 50 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;

                Console.ForegroundColor = color;
                Console.Write($"    {seniority.Level,-20}");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($" Avg: {seniority.AvgScore:F0}/100");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  |  {seniority.TopCandidateCount}/{seniority.TotalCount} top ({pct:F0}%)");
                Console.ResetColor();
            }

            var bestLevel = seniorityAnalysis.FirstOrDefault();
            if (bestLevel != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n    → Focus on {bestLevel.Level} roles for highest success rate");
                Console.ResetColor();
            }
        }

        // 6. Recommended Actions
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  Market Positioning Recommendations:");
        Console.ResetColor();

        var topGeo = geoAnalysis.FirstOrDefault();
        if (topGeo != null && topGeo.AvgScore >= 65)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    ✓ You're strongest in {topGeo.Country} — prioritize roles there");
            Console.ResetColor();
        }

        if (skillImpact.Count > 0)
        {
            var topSkill = skillImpact.First();
            var potentialIncrease = topSkill.PotentialScore - topSkill.AvgScoreWithoutSkill;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    → Learning {topSkill.SkillName} could boost your avg score by ~{potentialIncrease:F0} points");
            Console.ResetColor();
        }

        var bestBand = salaryBands.OrderByDescending(b => b.AvgScore).FirstOrDefault();
        if (bestBand != null)
        {
            var bandLabel = bestBand.Band switch
            {
                0 => "under $60k",
                1 => "$60k-$100k",
                2 => "$100k-$150k",
                3 => "$150k-$200k",
                _ => "over $200k"
            };
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"    ℹ You're most competitive in the {bandLabel} salary range");
            Console.ResetColor();
        }
    }

    private static List<SkillGapImpact> AnalyzeSkillGapImpact(CompetitivenessReport report)
    {
        // Find skills that appear frequently in "missing" but user doesn't have
        var missingSkillFrequency = report.Assessments
            .Where(a => a.WeaknessFactors.Any(w => w.StartsWith("Missing required skill:", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(a => a.WeaknessFactors
                .Where(w => w.StartsWith("Missing required skill:", StringComparison.OrdinalIgnoreCase))
                .Select(w => w.Replace("Missing required skill:", "").Trim()))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Skill = g.Key,
                MissingCount = g.Count()
            })
            .Where(x => x.MissingCount >= 3)
            .ToList();

        var impacts = new List<SkillGapImpact>();

        foreach (var missing in missingSkillFrequency)
        {
            var rolesWithoutSkill = report.Assessments
                .Where(a => a.WeaknessFactors.Any(w => w.Contains(missing.Skill, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (rolesWithoutSkill.Count == 0)
                continue;

            var avgScore = rolesWithoutSkill.Average(a => a.CompetitivenessScore);

            // Estimate potential score increase (assume skill would add 15-25 points)
            var potentialScore = Math.Min(100, avgScore + 20);

            impacts.Add(new SkillGapImpact(
                SkillName: missing.Skill,
                MissingCount: missing.MissingCount,
                AvgScoreWithoutSkill: avgScore,
                PotentialScore: potentialScore));
        }

        return impacts
            .OrderByDescending(i => i.PotentialScore - i.AvgScoreWithoutSkill)
            .ThenByDescending(i => i.MissingCount)
            .ToList();
    }

    private static int ClassifySalaryBand(decimal salaryMax)
    {
        return salaryMax switch
        {
            < 60_000 => 0,
            < 100_000 => 1,
            < 150_000 => 2,
            < 200_000 => 3,
            _ => 4
        };
    }

    private sealed record SkillGapImpact(
        string SkillName,
        int MissingCount,
        double AvgScoreWithoutSkill,
        double PotentialScore);

    private static string? FindLatestVacanciesFile()
    {
        if (!Directory.Exists(Program.DataDirectory))
            return null;

        return Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
