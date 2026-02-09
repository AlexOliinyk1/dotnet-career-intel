using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Intelligence.Models;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that analyzes all interview feedback and displays aggregated insights.
/// Usage: career-intel insights
/// </summary>
public static class InsightsCommand
{
    public static Command Create()
    {
        var command = new Command("insights", "Analyze all interview feedback and display aggregated insights");

        command.SetHandler(ExecuteAsync);

        return command;
    }

    private static async Task ExecuteAsync()
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.InsightsCommand");
        var feedbackEngine = serviceProvider.GetRequiredService<InterviewFeedbackEngine>();

        // Load all feedback
        var feedbackPath = Path.Combine(Program.DataDirectory, "interview-feedback.json");
        if (!File.Exists(feedbackPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No interview feedback found. Use 'feedback' command to record your first interview.");
            Console.ResetColor();
            return;
        }

        var json = await File.ReadAllTextAsync(feedbackPath);
        var allFeedback = JsonSerializer.Deserialize<List<InterviewFeedback>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        if (allFeedback.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No interview feedback records found.");
            Console.ResetColor();
            return;
        }

        logger.LogInformation("Analyzing {Count} interview feedback records", allFeedback.Count);

        // Generate insights
        var insights = feedbackEngine.GetInsights(allFeedback);

        // Print dashboard
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("              INTERVIEW INSIGHTS DASHBOARD                 ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // Overall stats
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Overall Statistics:");
        Console.ResetColor();
        Console.WriteLine($"  Total Interviews: {insights.TotalInterviews}");

        var passColor = insights.OverallPassRate switch
        {
            >= 60 => ConsoleColor.Green,
            >= 40 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.Write("  Overall Pass Rate: ");
        Console.ForegroundColor = passColor;
        Console.WriteLine($"{insights.OverallPassRate:F1}%");
        Console.ResetColor();

        Console.Write("  Trend: ");
        var trendColor = insights.Trend switch
        {
            "Improving" => ConsoleColor.Green,
            "Stable" => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.ForegroundColor = trendColor;
        Console.WriteLine(insights.Trend);
        Console.ResetColor();

        // Pass rate by round (bar chart)
        if (insights.PassRateByRound.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Pass Rate by Round:");
            Console.ResetColor();

            var maxLabelLen = insights.PassRateByRound.Keys
                .Max(k => k.Length);

            foreach (var (round, rate) in insights.PassRateByRound.OrderByDescending(kvp => kvp.Value))
            {
                var barLength = (int)(rate / 5);
                var barStr = new string('#', barLength);
                var roundColor = rate switch
                {
                    >= 70 => ConsoleColor.Green,
                    >= 40 => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };

                Console.Write($"    {round.PadRight(maxLabelLen)} ");
                Console.ForegroundColor = roundColor;
                Console.Write($"{barStr.PadRight(20)} ");
                Console.ResetColor();
                Console.WriteLine($"{rate:F0}%");
            }
        }

        // Top rejection reasons (bar chart)
        if (insights.TopRejectionReasons.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Top Rejection Reasons:");
            Console.ResetColor();

            var maxCount = insights.TopRejectionReasons.Max(r => r.Count);
            var maxReasonLen = insights.TopRejectionReasons
                .Max(r => r.Reason.Length);

            foreach (var (reason, count) in insights.TopRejectionReasons)
            {
                var barLength = maxCount > 0 ? (int)((double)count / maxCount * 15) : 0;
                var barStr = new string('#', Math.Max(1, barLength));

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"    {reason.PadRight(maxReasonLen)} ");
                Console.Write(barStr.PadRight(15));
                Console.ResetColor();
                Console.WriteLine($" ({count})");
            }
        }

        // Repeating weak areas (bar chart)
        if (insights.RepeatingWeakAreas.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Repeating Weak Areas:");
            Console.ResetColor();

            var maxCount = insights.RepeatingWeakAreas.Max(r => r.Count);
            var maxAreaLen = insights.RepeatingWeakAreas
                .Max(r => r.Area.Length);

            foreach (var (area, count) in insights.RepeatingWeakAreas)
            {
                var barLength = maxCount > 0 ? (int)((double)count / maxCount * 15) : 0;
                var barStr = new string('#', Math.Max(1, barLength));

                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"    {area.PadRight(maxAreaLen)} ");
                Console.Write(barStr.PadRight(15));
                Console.ResetColor();
                Console.WriteLine($" ({count}x)");
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }
}
