using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for Just-in-Time Micro-Learning: 30-60 minute focused tasks.
/// Replaces 8-36 week roadmaps with surgical, interview-driven learning.
/// Usage: career-intel micro-learn [--interview-date date] [--skill skill] [--hours hours]
/// </summary>
public static class MicroLearnCommand
{
    public static Command Create()
    {
        var interviewDateOption = new Option<string?>(
            "--interview-date",
            description: "Upcoming interview date (YYYY-MM-DD) to prioritize learning");

        var skillOption = new Option<string?>(
            "--skill",
            description: "Specific skill to generate micro-tasks for");

        var hoursOption = new Option<int>(
            name: "--hours",
            getDefaultValue: () => 2,
            description: "Available learning hours today");

        var command = new Command("micro-learn", "Get Just-in-Time 30-60 min learning tasks")
        {
            interviewDateOption,
            skillOption,
            hoursOption
        };

        command.SetHandler(ExecuteAsync, interviewDateOption, skillOption, hoursOption);

        return command;
    }

    private static async Task ExecuteAsync(string? interviewDate, string? skill, int hours)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ JUST-IN-TIME MICRO-LEARNING ═══\n");
        Console.ResetColor();

        // Load question confidence data
        var questionDataPath = Path.Combine(Program.DataDirectory, "question-confidence.json");
        var questions = new List<InterviewQuestionConfidence>();

        if (File.Exists(questionDataPath))
        {
            var json = await File.ReadAllTextAsync(questionDataPath);
            questions = JsonSerializer.Deserialize<List<InterviewQuestionConfidence>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }

        // Parse interview date if provided
        DateTimeOffset? interviewDateTime = null;
        if (!string.IsNullOrEmpty(interviewDate) && DateTimeOffset.TryParse(interviewDate, out var parsedDate))
        {
            interviewDateTime = parsedDate;
        }

        // Generate micro-tasks
        List<MicroLearningTask> tasks;

        if (!string.IsNullOrEmpty(skill))
        {
            tasks = GenerateSkillSpecificTasks(skill, hours);
        }
        else if (interviewDateTime.HasValue)
        {
            tasks = GenerateInterviewDrivenTasks(interviewDateTime.Value, questions, hours);
        }
        else
        {
            tasks = GenerateGeneralTasks(questions, hours);
        }

        // Display tasks
        PrintMicroTasks(tasks, hours, interviewDateTime);
    }

    private static List<MicroLearningTask> GenerateInterviewDrivenTasks(
        DateTimeOffset interviewDate,
        List<InterviewQuestionConfidence> questions,
        int availableHours)
    {
        var tasks = new List<MicroLearningTask>();
        var daysUntilInterview = (interviewDate - DateTimeOffset.UtcNow).TotalDays;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Interview in {daysUntilInterview:F0} days - generating urgent prep tasks\n");
        Console.ResetColor();

        // Prioritize weakest questions
        var weakQuestions = questions
            .Where(q => q.ConfidenceLevel < 75)
            .OrderBy(q => q.ConfidenceLevel)
            .ThenByDescending(q => q.TimesAsked)
            .ToList();

        var hoursUsed = 0;
        foreach (var question in weakQuestions)
        {
            if (hoursUsed >= availableHours)
                break;

            var taskHours = Math.Min(1, availableHours - hoursUsed); // 30-60 min tasks

            tasks.Add(new MicroLearningTask
            {
                Title = $"Practice: {question.Question}",
                Duration = taskHours,
                Priority = question.ConfidenceLevel < 50 ? 1 : 2,
                Category = question.Category,
                Steps = GenerateQuestionPracticeSteps(question),
                ExpectedOutcome = $"Confidence {question.ConfidenceLevel}% → {Math.Min(100, question.ConfidenceLevel + 20)}%",
                Resources = GetQuestionResources(question)
            });

            hoursUsed += taskHours;
        }

        return tasks;
    }

    private static List<MicroLearningTask> GenerateSkillSpecificTasks(string skill, int availableHours)
    {
        var tasks = new List<MicroLearningTask>();

        // Generate 30-60 min micro-tasks for this skill
        var microTasks = GetSkillMicroTasks(skill);

        var hoursUsed = 0;
        foreach (var task in microTasks)
        {
            if (hoursUsed >= availableHours)
                break;

            tasks.Add(task);
            hoursUsed += task.Duration;
        }

        return tasks;
    }

    private static List<MicroLearningTask> GenerateGeneralTasks(
        List<InterviewQuestionConfidence> questions,
        int availableHours)
    {
        var tasks = new List<MicroLearningTask>();

        if (questions.Count == 0)
        {
            // No question data - suggest foundational tasks
            tasks.Add(new MicroLearningTask
            {
                Title = "Initialize interview prep tracker",
                Duration = 1,
                Priority = 1,
                Category = "Setup",
                Steps =
                [
                    "Run: career-intel questions --role \"Senior .NET Developer\"",
                    "Review generated question list",
                    "Identify your top 3 weak areas",
                    "Schedule practice sessions"
                ],
                ExpectedOutcome = "Question confidence tracker initialized with baseline",
                Resources = []
            });

            return tasks;
        }

        // Generate tasks for weakest areas
        var weakQuestions = questions
            .Where(q => q.ConfidenceLevel < 75)
            .OrderBy(q => q.ConfidenceLevel)
            .Take(5)
            .ToList();

        var hoursUsed = 0;
        foreach (var question in weakQuestions)
        {
            if (hoursUsed >= availableHours)
                break;

            tasks.Add(new MicroLearningTask
            {
                Title = $"Master: {question.Question}",
                Duration = 1,
                Priority = question.ConfidenceLevel < 50 ? 1 : 2,
                Category = question.Category,
                Steps = GenerateQuestionPracticeSteps(question),
                ExpectedOutcome = $"Confidence {question.ConfidenceLevel}% → {Math.Min(100, question.ConfidenceLevel + 20)}%",
                Resources = GetQuestionResources(question)
            });

            hoursUsed++;
        }

        return tasks;
    }

    private static List<string> GenerateQuestionPracticeSteps(InterviewQuestionConfidence question)
    {
        var steps = new List<string>();

        if (question.Category.Contains("Technical"))
        {
            steps.Add($"📖 Read: 15 min crash course on {question.Question.Split(' ')[0]}");
            steps.Add("💻 Code: 20 min hands-on implementation");
            steps.Add("🎤 Practice: 10 min explain out loud");
            steps.Add("✓ Test: Answer without notes");
        }
        else if (question.Category.Contains("System Design"))
        {
            steps.Add("📖 Study: 20 min system design patterns");
            steps.Add("✏️ Diagram: 15 min draw architecture");
            steps.Add("🎤 Present: 10 min explain design choices");
            steps.Add("🔍 Review: Compare with reference solutions");
        }
        else if (question.Category.Contains("Behavioral"))
        {
            steps.Add("📝 Write: Draft STAR answer (10 min)");
            steps.Add("🎤 Practice: Record yourself answering (5 min)");
            steps.Add("👂 Review: Listen and refine (10 min)");
            steps.Add("✓ Memorize: Key points (5 min)");
        }
        else
        {
            steps.Add($"Research: {question.Question} (15 min)");
            steps.Add("Practice: Answer out loud (10 min)");
            steps.Add("Refine: Improve weak points (5 min)");
        }

        return steps;
    }

    private static List<string> GetQuestionResources(InterviewQuestionConfidence question)
    {
        // Provide curated 5-10 min resources (not 8-week courses)
        if (question.Question.Contains("SOLID"))
        {
            return
            [
                "https://www.youtube.com/watch?v=pTB30aXS77U (10 min video)",
                "Uncle Bob's SOLID Principles summary (5 min read)"
            ];
        }

        if (question.Question.Contains("async"))
        {
            return
            [
                "https://docs.microsoft.com/dotnet/csharp/async (15 min read)",
                "Stephen Cleary's async/await primer (10 min)"
            ];
        }

        if (question.Question.Contains("System Design"))
        {
            return
            [
                "https://github.com/donnemartin/system-design-primer",
                "Gaurav Sen System Design videos (10-15 min each)"
            ];
        }

        return ["Search: \"{question}\" interview question"];
    }

    private static List<MicroLearningTask> GetSkillMicroTasks(string skill)
    {
        var normalized = skill.ToLowerInvariant();

        return normalized switch
        {
            "kubernetes" or "k8s" =>
            [
                new MicroLearningTask
                {
                    Title = "Deploy your first pod",
                    Duration = 1,
                    Priority = 1,
                    Category = "Kubernetes",
                    Steps =
                    [
                        "Install minikube (if not installed)",
                        "kubectl run nginx --image=nginx",
                        "kubectl get pods",
                        "kubectl logs <pod-name>",
                        "kubectl delete pod <pod-name>"
                    ],
                    ExpectedOutcome = "Understand pod lifecycle",
                    Resources = ["https://kubernetes.io/docs/tutorials/kubernetes-basics/"]
                },
                new MicroLearningTask
                {
                    Title = "Create a Deployment",
                    Duration = 1,
                    Priority = 2,
                    Category = "Kubernetes",
                    Steps =
                    [
                        "Write deployment.yaml for nginx with 3 replicas",
                        "kubectl apply -f deployment.yaml",
                        "kubectl get deployments",
                        "kubectl scale deployment nginx --replicas=5",
                        "Observe rolling update behavior"
                    ],
                    ExpectedOutcome = "Master Deployment concept",
                    Resources = ["kubectl cheat sheet"]
                }
            ],

            "docker" =>
            [
                new MicroLearningTask
                {
                    Title = "Containerize a .NET app",
                    Duration = 1,
                    Priority = 1,
                    Category = "Docker",
                    Steps =
                    [
                        "Create Dockerfile for simple .NET console app",
                        "docker build -t my-app .",
                        "docker run my-app",
                        "docker ps, docker logs",
                        "Push to Docker Hub"
                    ],
                    ExpectedOutcome = "Understand Docker basics",
                    Resources = ["Docker .NET quickstart"]
                }
            ],

            _ =>
            [
                new MicroLearningTask
                {
                    Title = $"{skill} fundamentals",
                    Duration = 1,
                    Priority = 1,
                    Category = skill,
                    Steps =
                    [
                        $"Find 15 min tutorial on {skill}",
                        "Follow along with code examples",
                        "Build something small",
                        "Explain it out loud"
                    ],
                    ExpectedOutcome = $"Basic {skill} understanding",
                    Resources = [$"Search: '{skill} tutorial for beginners'"]
                }
            ]
        };
    }

    private static void PrintMicroTasks(List<MicroLearningTask> tasks, int availableHours, DateTimeOffset? interviewDate)
    {
        if (tasks.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No tasks generated. Initialize questions first:");
            Console.WriteLine("  career-intel questions --role \"Senior .NET Developer\"");
            Console.ResetColor();
            return;
        }

        var totalHours = tasks.Sum(t => t.Duration);

        Console.WriteLine($"Available time: {availableHours}h");
        Console.WriteLine($"Generated {tasks.Count} micro-tasks ({totalHours}h total)\n");

        if (interviewDate.HasValue)
        {
            var daysUntil = (interviewDate.Value - DateTimeOffset.UtcNow).TotalDays;
            Console.ForegroundColor = daysUntil <= 3 ? ConsoleColor.Red : ConsoleColor.Yellow;
            Console.WriteLine($"⏰ Interview in {daysUntil:F0} days - FOCUS MODE\n");
            Console.ResetColor();
        }

        var rank = 1;
        foreach (var task in tasks)
        {
            var priorityColor = task.Priority == 1 ? ConsoleColor.Red : ConsoleColor.Yellow;

            Console.ForegroundColor = priorityColor;
            Console.WriteLine($"▌Task {rank}: {task.Title} ({task.Duration}h)");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Category: {task.Category}");
            Console.WriteLine($"  Expected: {task.ExpectedOutcome}");
            Console.ResetColor();

            Console.WriteLine("\n  Steps:");
            foreach (var step in task.Steps)
            {
                Console.WriteLine($"    • {step}");
            }

            if (task.Resources.Count > 0)
            {
                Console.WriteLine("\n  Resources:");
                foreach (var resource in task.Resources)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"    → {resource}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
            rank++;
        }

        // Summary
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("💡 Quick Tips:");
        Console.ResetColor();
        Console.WriteLine("  • Focus on ONE task at a time");
        Console.WriteLine("  • Use a timer (Pomodoro: 25 min work + 5 min break)");
        Console.WriteLine("  • Update confidence after each task:");
        Console.WriteLine("    career-intel questions --update q1:75");
        Console.WriteLine("  • Don't aim for perfection - aim for 'ready enough'");
    }

    private sealed class MicroLearningTask
    {
        public string Title { get; init; } = string.Empty;
        public int Duration { get; init; } // Hours (usually 1 = 30-60 min)
        public int Priority { get; init; } // 1 = critical, 2 = important, 3 = nice-to-have
        public string Category { get; init; } = string.Empty;
        public List<string> Steps { get; init; } = [];
        public string ExpectedOutcome { get; init; } = string.Empty;
        public List<string> Resources { get; init; } = [];
    }
}
