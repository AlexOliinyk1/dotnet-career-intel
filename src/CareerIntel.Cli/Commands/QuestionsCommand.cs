using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for managing per-question interview confidence (0-100%).
/// Shows exactly which questions need practice and tracks improvement over time.
/// Usage: career-intel questions [--role role] [--update question-id confidence]
/// </summary>
public static class QuestionsCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static Command Create()
    {
        var roleOption = new Option<string?>(
            "--role",
            description: "Initialize questions for role (e.g., 'Senior .NET Developer')");

        var updateOption = new Option<string?>(
            "--update",
            description: "Update confidence for question ID (format: questionId:confidence)");

        var showAllOption = new Option<bool>(
            name: "--all",
            getDefaultValue: () => false,
            description: "Show all questions, not just ones needing work");

        var recordPerformanceOption = new Option<string?>(
            "--record",
            description: "Record interview performance (question-id)");

        var command = new Command("questions", "Track per-question interview confidence (0-100%)")
        {
            roleOption,
            updateOption,
            showAllOption,
            recordPerformanceOption
        };

        command.SetHandler(ExecuteAsync, roleOption, updateOption, showAllOption, recordPerformanceOption);

        return command;
    }

    private static async Task ExecuteAsync(string? role, string? update, bool showAll, string? record)
    {
        var dataPath = Path.Combine(Program.DataDirectory, "question-confidence.json");
        var tracker = new QuestionConfidenceTracker();

        // Load existing data
        if (File.Exists(dataPath))
        {
            var json = await File.ReadAllTextAsync(dataPath);
            var questions = JsonSerializer.Deserialize<List<InterviewQuestionConfidence>>(json, JsonOptions) ?? [];
            tracker.LoadQuestions(questions);
        }

        // Initialize questions for role
        if (!string.IsNullOrEmpty(role))
        {
            tracker.LoadQuestionsForRole(role);
            await SaveTracker(tracker, dataPath);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Initialized {tracker.GetAllQuestions().Count} common interview questions for {role}");
            Console.ResetColor();
            Console.WriteLine($"View with: career-intel questions");
            return;
        }

        // Update confidence for specific question
        if (!string.IsNullOrEmpty(update))
        {
            var parts = update.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var confidence))
            {
                var questionId = parts[0];
                tracker.RecordPractice(questionId, confidence, hoursSpent: 2);
                await SaveTracker(tracker, dataPath);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Updated confidence for question {questionId} to {confidence}%");
                Console.ResetColor();
                return;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Invalid format. Use --update questionId:confidence (e.g., --update q1:75)");
                Console.ResetColor();
                return;
            }
        }

        // Record interview performance
        if (!string.IsNullOrEmpty(record))
        {
            await RecordInterviewPerformance(tracker, record, dataPath);
            return;
        }

        // Show question dashboard
        ShowQuestionDashboard(tracker, showAll);
    }

    private static void ShowQuestionDashboard(QuestionConfidenceTracker tracker, bool showAll)
    {
        var questions = tracker.GetAllQuestions();

        if (questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No questions tracked yet. Initialize with:");
            Console.WriteLine("  career-intel questions --role \"Senior .NET Developer\"");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ INTERVIEW QUESTION CONFIDENCE TRACKER ═══\n");
        Console.ResetColor();

        var overallReadiness = tracker.CalculateOverallReadiness();
        var totalHoursNeeded = tracker.GetTotalHoursToReady(75);

        Console.WriteLine($"Overall Readiness: {overallReadiness}%");
        Console.WriteLine($"Hours to 75% Confidence: {totalHoursNeeded}h");
        Console.WriteLine();

        // Group by category
        var byCategory = questions.Values
            .GroupBy(q => q.Category)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var category in byCategory)
        {
            var categoryQuestions = showAll
                ? category.OrderBy(q => q.ConfidenceLevel).ToList()
                : category.Where(q => q.ConfidenceLevel < 75).OrderBy(q => q.ConfidenceLevel).ToList();

            if (categoryQuestions.Count == 0)
                continue;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"▌{category.Key}");
            Console.ResetColor();
            Console.WriteLine();

            foreach (var q in categoryQuestions)
            {
                var color = q.ConfidenceLevel switch
                {
                    >= 90 => ConsoleColor.Green,
                    >= 75 => ConsoleColor.Yellow,
                    >= 50 => ConsoleColor.DarkYellow,
                    _ => ConsoleColor.Red
                };

                Console.ForegroundColor = color;
                Console.Write($"  [{q.ConfidenceLevel,3}%]");
                Console.ResetColor();

                Console.Write($" {q.Question}");

                if (q.EstimatedHoursToMaster > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" (~{q.EstimatedHoursToMaster}h)");
                    Console.ResetColor();
                }

                if (q.TimesAsked > 0)
                {
                    var successRate = q.TimesAnsweredWell * 100 / q.TimesAsked;
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" | Asked {q.TimesAsked}x, {successRate}% success");
                    Console.ResetColor();
                }

                Console.WriteLine();

                // Show improvement areas if any
                if (q.ImprovementAreas.Count > 0 && q.ConfidenceLevel < 75)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"     Need: {string.Join(", ", q.ImprovementAreas.Take(3))}");
                    Console.ResetColor();
                }
            }
            Console.WriteLine();
        }

        // Show top priorities
        var priorities = tracker.GetTopPriorities(5);
        if (priorities.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("🎯 TOP PRIORITIES (Practice These First):");
            Console.ResetColor();
            Console.WriteLine();

            var rank = 1;
            foreach (var q in priorities)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"  {rank}. ");
                Console.ResetColor();
                Console.WriteLine($"{q.Question} ({q.ConfidenceLevel}% → needs {q.EstimatedHoursToMaster}h)");
                rank++;
            }
            Console.WriteLine();
        }

        // Usage instructions
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Commands:");
        Console.WriteLine("  career-intel questions --update q1:85    Update confidence after practice");
        Console.WriteLine("  career-intel questions --record q1       Record interview performance");
        Console.WriteLine("  career-intel questions --all             Show all questions");
        Console.ResetColor();
    }

    private static async Task RecordInterviewPerformance(QuestionConfidenceTracker tracker, string questionId, string dataPath)
    {
        var questions = tracker.GetAllQuestions();
        if (!questions.ContainsKey(questionId))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Question '{questionId}' not found.");
            Console.ResetColor();
            return;
        }

        var question = questions[questionId];

        Console.WriteLine($"\nRecording performance for: {question.Question}\n");

        Console.Write("How well did you answer? (0-100%): ");
        var performanceInput = Console.ReadLine();
        if (!int.TryParse(performanceInput, out var performance) || performance < 0 || performance > 100)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid performance score.");
            Console.ResetColor();
            return;
        }

        Console.Write("What went wrong? (press Enter to skip): ");
        var whatWentWrong = Console.ReadLine() ?? string.Empty;

        Console.Write("Missing concepts (comma-separated, press Enter to skip): ");
        var missingConceptsInput = Console.ReadLine();
        var missingConcepts = string.IsNullOrWhiteSpace(missingConceptsInput)
            ? []
            : missingConceptsInput.Split(',').Select(s => s.Trim()).ToList();

        Console.Write("Missing skills (comma-separated, press Enter to skip): ");
        var missingSkillsInput = Console.ReadLine();
        var missingSkills = string.IsNullOrWhiteSpace(missingSkillsInput)
            ? []
            : missingSkillsInput.Split(',').Select(s => s.Trim()).ToList();

        var questionPerformance = new QuestionPerformance
        {
            Question = question.Question,
            Category = question.Category,
            ConfidenceBefore = question.ConfidenceLevel,
            PerformanceDuring = performance,
            WhatWentWrong = whatWentWrong,
            MissingConcepts = missingConcepts,
            MissingSkills = missingSkills
        };

        tracker.RecordInterviewPerformance(questionId, questionPerformance);
        await SaveTracker(tracker, dataPath);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✓ Recorded performance. Updated confidence: {question.ConfidenceLevel}%");
        Console.ResetColor();

        if (missingConcepts.Count > 0 || missingSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\nRecommended learning: career-intel learn --skills \"{string.Join(",", missingSkills)}\"");
            Console.ResetColor();
        }
    }

    private static async Task SaveTracker(QuestionConfidenceTracker tracker, string dataPath)
    {
        var questions = tracker.GetAllQuestions().Values.ToList();
        var json = JsonSerializer.Serialize(questions, JsonOptions);
        await File.WriteAllTextAsync(dataPath, json);
    }
}
