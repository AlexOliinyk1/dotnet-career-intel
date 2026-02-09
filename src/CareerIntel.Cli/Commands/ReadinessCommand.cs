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
/// CLI command that computes offer readiness for a specific vacancy or top N from latest scan.
/// Usage: career-intel readiness [--vacancy-id id] [--input path] [--top n]
/// </summary>
public static class ReadinessCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var vacancyIdOption = new Option<string?>(
            "--vacancy-id",
            description: "Specific vacancy ID to assess readiness for");

        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 5,
            description: "Number of top matches to assess readiness for (when no vacancy-id specified)");

        var command = new Command("readiness", "Compute offer readiness and preparation plan for target vacancies")
        {
            vacancyIdOption,
            inputOption,
            topOption
        };

        command.SetHandler(ExecuteAsync, vacancyIdOption, inputOption, topOption);

        return command;
    }

    private static async Task ExecuteAsync(string? vacancyId, string? input, int top)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.ReadinessCommand");
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        var readinessEngine = serviceProvider.GetRequiredService<OfferReadinessEngine>();

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

        // Load company profiles
        var companiesPath = Path.Combine(Program.DataDirectory, "companies.json");
        var companyProfiles = await LoadJsonListAsync<CompanyProfile>(companiesPath);

        // Determine target vacancies
        List<JobVacancy> targetVacancies;
        if (!string.IsNullOrEmpty(vacancyId))
        {
            var vacancy = vacancies.FirstOrDefault(v =>
                v.Id.Equals(vacancyId, StringComparison.OrdinalIgnoreCase));

            if (vacancy is null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Vacancy '{vacancyId}' not found.");
                Console.ResetColor();
                return;
            }

            targetVacancies = [vacancy];
        }
        else
        {
            // Use top N from ranked matches
            var ranked = matchEngine.RankVacancies(vacancies, 0);
            targetVacancies = ranked.Take(top).ToList();
        }

        if (targetVacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No vacancies to assess.");
            Console.ResetColor();
            return;
        }

        // Assess readiness for each target vacancy
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("               OFFER READINESS DASHBOARD                   ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        foreach (var vacancy in targetVacancies)
        {
            // Compute match score
            var matchScore = matchEngine.ComputeMatch(vacancy);
            vacancy.MatchScore = matchScore;

            // Find company profile if available
            var companyProfile = companyProfiles
                .FirstOrDefault(c => c.Name.Equals(vacancy.Company, StringComparison.OrdinalIgnoreCase));

            // Compute readiness
            var readiness = readinessEngine.Compute(
                vacancy, profile, matchScore, interviewHistory, companyProfile);

            PrintReadinessDashboard(vacancy, readiness);
        }
    }

    private static void PrintReadinessDashboard(JobVacancy vacancy, OfferReadiness readiness)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  --- {vacancy.Title} at {vacancy.Company} ---");
        Console.ResetColor();
        Console.WriteLine();

        // Readiness percentage with color
        var readinessColor = readiness.ReadinessPercent switch
        {
            >= 80 => ConsoleColor.Green,
            >= 50 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.Write("  Readiness:     ");
        Console.ForegroundColor = readinessColor;
        var bar = new string('#', (int)(readiness.ReadinessPercent / 5));
        var empty = new string('-', 20 - (int)(readiness.ReadinessPercent / 5));
        Console.Write($"[{bar}{empty}] {readiness.ReadinessPercent:F1}%");
        Console.ResetColor();
        Console.WriteLine();

        // Offer probability
        var probColor = readiness.OfferProbability switch
        {
            >= 0.7 => ConsoleColor.Green,
            >= 0.4 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.Write("  Offer Prob:    ");
        Console.ForegroundColor = probColor;
        Console.Write($"{readiness.OfferProbability:P0}");
        Console.ResetColor();
        Console.WriteLine();

        // Timing recommendation
        var timingColor = readiness.Timing switch
        {
            RecommendedTiming.ApplyNow => ConsoleColor.Green,
            RecommendedTiming.ApplyIn1To2Weeks => ConsoleColor.Yellow,
            RecommendedTiming.ApplyIn3To4Weeks => ConsoleColor.Yellow,
            RecommendedTiming.SkillUpFirst => ConsoleColor.Red,
            _ => ConsoleColor.Red
        };

        var timingLabel = readiness.Timing switch
        {
            RecommendedTiming.ApplyNow => "APPLY NOW",
            RecommendedTiming.ApplyIn1To2Weeks => "Apply in 1-2 weeks",
            RecommendedTiming.ApplyIn3To4Weeks => "Apply in 3-4 weeks",
            RecommendedTiming.SkillUpFirst => "Skill up first",
            RecommendedTiming.Skip => "SKIP",
            _ => "Unknown"
        };

        Console.Write("  Timing:        ");
        Console.ForegroundColor = timingColor;
        Console.Write(timingLabel);
        Console.ResetColor();
        if (readiness.EstimatedWeeksToReady > 0)
        {
            Console.Write($" (~{readiness.EstimatedWeeksToReady} weeks to ready)");
        }
        Console.WriteLine();

        // Critical gaps
        if (readiness.CriticalGaps.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Critical Gaps:");
            Console.ResetColor();
            foreach (var gap in readiness.CriticalGaps)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"    [{gap.CurrentLevel}/{gap.RequiredLevel}] ");
                Console.ResetColor();
                Console.Write(gap.SkillName);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" (impact: {gap.ImpactWeight:F2})");
                Console.ResetColor();
            }
        }

        // Strengths
        if (readiness.Strengths.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Strengths:");
            foreach (var strength in readiness.Strengths)
            {
                Console.WriteLine($"    + {strength}");
            }
            Console.ResetColor();
        }

        // Prep actions
        if (readiness.PrepActions.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Prep Actions:");
            Console.ResetColor();
            foreach (var action in readiness.PrepActions.Take(8))
            {
                var priorityColor = action.Priority switch
                {
                    1 => ConsoleColor.Red,
                    2 => ConsoleColor.Yellow,
                    _ => ConsoleColor.DarkGray
                };

                Console.ForegroundColor = priorityColor;
                Console.Write($"    P{action.Priority} ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"[{action.Category}] ");
                Console.ResetColor();
                Console.WriteLine($"{action.Action} (~{action.EstimatedHours}h)");
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ----------------------------------------------------------");
        Console.ResetColor();
        Console.WriteLine();
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
