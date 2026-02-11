using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// Adaptive learning command. Analyzes interview failures and confidence gaps
/// to generate a self-correcting learning plan.
///
/// Usage:
///   career-intel adaptive-learn                    -- Generate adaptive learning plan
///   career-intel adaptive-learn --after-interview   -- Adjust plan after latest interview
/// </summary>
public static class AdaptiveLearnCommand
{
    public static Command Create()
    {
        var afterInterviewOption = new Option<bool>(
            "--after-interview",
            description: "Adjust learning plan after a new interview result");

        var command = new Command("adaptive-learn",
            "Generate adaptive learning plan based on interview performance and confidence gaps")
        {
            afterInterviewOption
        };

        command.SetHandler(async (context) =>
        {
            var afterInterview = context.ParseResult.GetValueForOption(afterInterviewOption);
            await ExecuteAsync(afterInterview);
        });

        return command;
    }

    private static async Task ExecuteAsync(bool afterInterview)
    {
        var engine = new AdaptiveLearningEngine();

        // Load feedback and confidences
        var feedback = await LoadFeedbackAsync();
        var confidences = await LoadConfidencesAsync();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Adaptive Learning Engine ===");
        Console.ResetColor();
        Console.WriteLine();

        var plan = engine.GeneratePlan(feedback, confidences);

        // Overall readiness
        Console.Write("Overall Readiness: ");
        Console.ForegroundColor = plan.OverallReadiness >= 70 ? ConsoleColor.Green :
            plan.OverallReadiness >= 50 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"{plan.OverallReadiness}%");
        Console.ResetColor();
        Console.WriteLine($"Weakest Area: {plan.WeakestArea}");
        Console.WriteLine($"Strongest Area: {plan.StrongestArea}");
        Console.WriteLine();

        // Overlearning warnings
        if (plan.OverlearningWarnings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("OVERLEARNING WARNINGS:");
            Console.ResetColor();
            foreach (var warning in plan.OverlearningWarnings)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ! {warning}");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // Learning sessions
        if (plan.Sessions.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- Recommended Learning Sessions ---");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var session in plan.Sessions)
            {
                Console.ForegroundColor = session.Priority switch
                {
                    "CRITICAL" => ConsoleColor.Red,
                    "High" => ConsoleColor.Yellow,
                    "Medium" => ConsoleColor.White,
                    _ => ConsoleColor.DarkGray
                };
                Console.Write($"  [{session.Priority}]");
                Console.ResetColor();
                Console.Write($" {session.Topic}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({session.EstimatedMinutes} min, confidence: {session.Confidence:F0}%)");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Reason: {session.Reason}");
                Console.ResetColor();

                foreach (var action in session.SuggestedActions)
                {
                    Console.WriteLine($"    - {action}");
                }
                Console.WriteLine();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("No gaps detected — you're well-prepared!");
            Console.ResetColor();
        }

        // Post-interview adjustment
        if (afterInterview && feedback.Count > 0)
        {
            var latest = feedback.OrderByDescending(f => f.InterviewDate).First();
            var adjustment = engine.AdjustAfterInterview(latest, plan);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- Post-Interview Adjustment ---");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = adjustment.Action switch
            {
                AdjustmentAction.Continue => ConsoleColor.Green,
                AdjustmentAction.Pivot => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            Console.WriteLine($"  {adjustment.Message}");
            Console.ResetColor();

            if (adjustment.NewFocusTopics.Count > 0)
            {
                Console.WriteLine($"  New focus: {string.Join(", ", adjustment.NewFocusTopics)}");
            }
        }
    }

    private static async Task<List<InterviewFeedback>> LoadFeedbackAsync()
    {
        var path = Path.Combine(Program.DataDirectory, "interview-feedback.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<InterviewFeedback>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static async Task<List<InterviewQuestionConfidence>> LoadConfidencesAsync()
    {
        var path = Path.Combine(Program.DataDirectory, "question-confidence.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<InterviewQuestionConfidence>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
