using System.CommandLine;
using CareerIntel.Intelligence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// Mock interview simulator command. Interactive practice with scoring and feedback.
///
/// Usage:
///   career-intel mock-interview                              -- Random topics, all difficulties
///   career-intel mock-interview --topics "System Design"     -- Focus on specific topic
///   career-intel mock-interview --difficulty Senior --count 5 -- 5 senior-level questions
/// </summary>
public static class MockInterviewCommand
{
    public static Command Create()
    {
        var topicsOption = new Option<string?>(
            "--topics",
            description: "Comma-separated topic names to focus on (e.g., 'System Design,Algorithms')");

        var difficultyOption = new Option<string>(
            "--difficulty",
            getDefaultValue: () => "all",
            description: "Question difficulty: Junior, Mid, Senior, or all");

        var countOption = new Option<int>(
            "--count",
            getDefaultValue: () => 5,
            description: "Number of questions");

        var command = new Command("mock-interview",
            "Interactive mock interview simulator with scoring")
        {
            topicsOption,
            difficultyOption,
            countOption
        };

        command.SetHandler(async (context) =>
        {
            var topics = context.ParseResult.GetValueForOption(topicsOption);
            var difficulty = context.ParseResult.GetValueForOption(difficultyOption);
            var count = context.ParseResult.GetValueForOption(countOption);

            await ExecuteAsync(topics, difficulty ?? "all", count);
        });

        return command;
    }

    private static async Task ExecuteAsync(string? topics, string difficulty, int count)
    {
        var simulator = new MockInterviewSimulator();

        var config = new MockInterviewConfig
        {
            Topics = string.IsNullOrEmpty(topics)
                ? []
                : topics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Difficulty = difficulty,
            TotalQuestions = count,
            QuestionsPerTopic = 3
        };

        var session = simulator.CreateSession(config);

        if (session.Questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No questions available for the specified criteria.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Mock Interview Simulator ===");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"Session: {session.Id}");
        Console.WriteLine($"Questions: {session.Questions.Count}");
        Console.WriteLine($"Topics: {string.Join(", ", session.Questions.Select(q => q.Topic).Distinct())}");
        Console.WriteLine($"Difficulty: {difficulty}");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Type your answer for each question. Type 'hint' for a hint, 'skip' to skip, 'quit' to end.");
        Console.ResetColor();
        Console.WriteLine();

        for (var i = 0; i < session.Questions.Count; i++)
        {
            var question = session.Questions[i];

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Q{i + 1}/{session.Questions.Count} ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{question.Topic}] [{question.Difficulty}]");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(question.Question);
            Console.ResetColor();
            Console.WriteLine();

            var hintIndex = 0;
            string? userAnswer = null;

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Your answer> ");
                Console.ResetColor();

                var input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                    continue;

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine();
                    goto scoring;
                }

                if (input.Equals("skip", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("Skipped.");
                    Console.ResetColor();
                    break;
                }

                if (input.Equals("hint", StringComparison.OrdinalIgnoreCase))
                {
                    if (hintIndex < question.Hints.Count)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"  Hint: {question.Hints[hintIndex]}");
                        Console.ResetColor();
                        hintIndex++;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("  No more hints available.");
                        Console.ResetColor();
                    }
                    continue;
                }

                userAnswer = input;
                break;
            }

            if (userAnswer != null)
            {
                question.UserAnswer = userAnswer;
                var score = simulator.ScoreAnswer(question, userAnswer);
                question.Score = score;

                Console.WriteLine();
                Console.ForegroundColor = score.Score >= 70 ? ConsoleColor.Green :
                    score.Score >= 40 ? ConsoleColor.Yellow : ConsoleColor.Red;
                Console.Write($"  Score: {score.Score}/100");
                Console.ResetColor();
                Console.WriteLine($" — {score.Feedback}");

                if (score.MissedKeyPoints.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  Key points you missed:");
                    foreach (var point in score.MissedKeyPoints.Take(3))
                    {
                        Console.WriteLine($"    - {point}");
                    }
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('-', 60));
            Console.ResetColor();
            Console.WriteLine();
        }

        scoring:

        // Score the session
        var report = simulator.ScoreSession(session);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Interview Report ===");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("Average Score: ");
        Console.ForegroundColor = report.AverageScore >= 70 ? ConsoleColor.Green :
            report.AverageScore >= 40 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"{report.AverageScore:F0}/100");
        Console.ResetColor();

        Console.WriteLine($"Answered: {report.AnsweredQuestions}/{report.TotalQuestions}");
        Console.WriteLine();

        Console.ForegroundColor = report.AverageScore >= 70 ? ConsoleColor.Green :
            report.AverageScore >= 40 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.WriteLine($"Verdict: {report.Verdict}");
        Console.ResetColor();
        Console.WriteLine();

        if (report.TopicScores.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Score by Topic:");
            Console.ResetColor();

            foreach (var ts in report.TopicScores)
            {
                Console.ForegroundColor = ts.AverageScore >= 70 ? ConsoleColor.Green :
                    ts.AverageScore >= 40 ? ConsoleColor.Yellow : ConsoleColor.Red;
                Console.Write($"  {ts.AverageScore,3:F0}%");
                Console.ResetColor();
                Console.WriteLine($"  {ts.Topic} ({ts.QuestionCount} questions)");
            }
        }

        if (report.ImprovementAreas.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Areas to Improve:");
            Console.ResetColor();

            foreach (var area in report.ImprovementAreas)
            {
                Console.WriteLine($"  - {area}");
            }
        }

        await Task.CompletedTask;
    }
}
