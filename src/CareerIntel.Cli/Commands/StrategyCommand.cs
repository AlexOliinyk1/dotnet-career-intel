using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for automated strategy analysis and recommendations.
/// Self-correcting system: analyzes outcomes and suggests pivots.
/// Usage: career-intel strategy
/// </summary>
public static class StrategyCommand
{
    public static Command Create()
    {
        var command = new Command("strategy", "Analyze application outcomes and get strategy recommendations");
        command.SetHandler(ExecuteAsync);
        return command;
    }

    private static async Task ExecuteAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ STRATEGY ANALYSIS & RECOMMENDATIONS ═══\n");
        Console.ResetColor();

        // Load application history
        var applicationsPath = Path.Combine(Program.DataDirectory, "applications.json");
        var applications = new List<JobApplication>();

        if (File.Exists(applicationsPath))
        {
            var json = await File.ReadAllTextAsync(applicationsPath);
            applications = JsonSerializer.Deserialize<List<JobApplication>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }

        // Load interview feedback
        var feedbackPath = Path.Combine(Program.DataDirectory, "interview-feedback.json");
        var feedback = new List<InterviewFeedback>();

        if (File.Exists(feedbackPath))
        {
            var json = await File.ReadAllTextAsync(feedbackPath);
            feedback = JsonSerializer.Deserialize<List<InterviewFeedback>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }

        if (applications.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No application data found. Start applying to positions:");
            Console.WriteLine("  career-intel apply --vacancy-id <id>");
            Console.ResetColor();
            return;
        }

        // Run strategy analysis
        var advisor = new StrategyAdvisor();
        var recommendation = advisor.AnalyzeStrategy(applications, feedback);

        // Display results
        PrintStrategyReport(recommendation, applications, feedback);
    }

    private static void PrintStrategyReport(
        StrategyRecommendation recommendation,
        List<JobApplication> applications,
        List<InterviewFeedback> feedback)
    {
        // Overall stats
        Console.WriteLine("Current Performance:");
        Console.WriteLine($"  Total Applications: {applications.Count}");
        Console.WriteLine($"  Response Rate: {CalculateRate(applications, ApplicationStatus.Viewed):P0}");
        Console.WriteLine($"  Interview Rate: {CalculateRate(applications, ApplicationStatus.Interview):P0}");
        Console.WriteLine($"  Offer Rate: {CalculateRate(applications, ApplicationStatus.Offer):P0}");

        var scoreColor = recommendation.StrategyEffectiveness switch
        {
            >= 70 => ConsoleColor.Green,
            >= 40 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.Write($"\n  Strategy Effectiveness: ");
        Console.ForegroundColor = scoreColor;
        Console.WriteLine($"{recommendation.StrategyEffectiveness}/100");
        Console.ResetColor();

        Console.WriteLine();

        // Identified pivots
        if (recommendation.Pivots.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("═══ IDENTIFIED PIVOTS ═══\n");
            Console.ResetColor();

            foreach (var pivot in recommendation.Pivots.OrderByDescending(p => GetImpactScore(p.Impact)))
            {
                var impactColor = pivot.Impact switch
                {
                    "Critical" => ConsoleColor.Red,
                    "High" => ConsoleColor.Yellow,
                    _ => ConsoleColor.DarkYellow
                };

                Console.ForegroundColor = impactColor;
                Console.WriteLine($"▌{pivot.Type} [{pivot.Impact} Impact, {pivot.Confidence} Confidence]");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Finding: {pivot.Finding}");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"  {pivot.Recommendation}");
                Console.ResetColor();

                Console.WriteLine();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ No major strategy issues detected");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Actionable advice
        if (recommendation.Advice.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("═══ ACTION PLAN ═══\n");
            Console.ResetColor();

            foreach (var advice in recommendation.Advice)
            {
                Console.WriteLine(advice);
            }

            Console.WriteLine();
        }

        // Next steps
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("───────────────────────────────────────");
        Console.ResetColor();
        Console.WriteLine("\nMonitor and adjust:");
        Console.WriteLine("  career-intel strategy    Re-run analysis after 10+ more applications");
        Console.WriteLine("  career-intel feedback    Record interview feedback for deeper insights");
    }

    private static double CalculateRate(List<JobApplication> applications, ApplicationStatus threshold)
    {
        if (applications.Count == 0)
            return 0;

        return applications.Count(a => a.Status >= threshold) / (double)applications.Count;
    }

    private static int GetImpactScore(string impact)
    {
        return impact switch
        {
            "Critical" => 4,
            "High" => 3,
            "Medium" => 2,
            "Low" => 1,
            _ => 0
        };
    }
}
