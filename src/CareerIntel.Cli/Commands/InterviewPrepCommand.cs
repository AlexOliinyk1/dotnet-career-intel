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
/// CLI command that generates a full interview preparation plan for target vacancies.
/// Infers likely interview questions, builds skill-grouped question sets,
/// creates an interview-focused learning plan, estimates pass probability, and outputs a verdict.
///
/// Usage: career-intel interview-prep [--vacancy-id id] [--input path] [--top n] [--json]
/// </summary>
public static class InterviewPrepCommand
{
    public static Command Create()
    {
        var vacancyIdOption = new Option<string?>(
            "--vacancy-id",
            description: "Specific vacancy ID to prepare for");

        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var topOption = new Option<int>(
            "--top",
            getDefaultValue: () => 3,
            description: "Number of top matches to prepare for (when no vacancy-id specified)");

        var jsonOption = new Option<bool>(
            "--json",
            getDefaultValue: () => false,
            description: "Output raw JSON instead of formatted console output");

        var command = new Command("interview-prep",
            "Generate interview prep plans: inferred questions, learning plan, pass probability, verdict")
        {
            vacancyIdOption,
            inputOption,
            topOption,
            jsonOption
        };

        command.SetHandler(ExecuteAsync, vacancyIdOption, inputOption, topOption, jsonOption);
        return command;
    }

    private static async Task ExecuteAsync(string? vacancyId, string? input, int top, bool json)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        var prepEngine = serviceProvider.GetRequiredService<InterviewPrepEngine>();
        var learningPlanner = serviceProvider.GetRequiredService<InterviewLearningPlanner>();

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

        var profile = await LoadJsonAsync<UserProfile>(profilePath) ?? new UserProfile();

        // Load vacancies
        var inputPath = input ?? FindLatestVacanciesFile();
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        var vacancies = await LoadJsonListAsync<JobVacancy>(inputPath);

        // Load supplementary data
        var feedbackPath = Path.Combine(Program.DataDirectory, "interview-feedback.json");
        var interviewHistory = await LoadJsonListAsync<InterviewFeedback>(feedbackPath);

        var companiesPath = Path.Combine(Program.DataDirectory, "companies.json");
        var companyProfiles = await LoadJsonListAsync<CompanyProfile>(companiesPath);

        // Determine target vacancies
        List<JobVacancy> targets;
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

            targets = [vacancy];
        }
        else
        {
            var ranked = matchEngine.RankVacancies(vacancies, 0);
            targets = ranked.Take(top).ToList();
        }

        if (targets.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No vacancies to prepare for.");
            Console.ResetColor();
            return;
        }

        // Generate prep plans
        var allPrepPlans = new List<InterviewPrepPlan>();
        var allLearningPlans = new List<InterviewLearningPlan>();

        foreach (var vacancy in targets)
        {
            var companyProfile = companyProfiles
                .FirstOrDefault(c => c.Name.Equals(vacancy.Company, StringComparison.OrdinalIgnoreCase));

            var prepPlan = prepEngine.GeneratePrepPlan(vacancy, profile, companyProfile, interviewHistory);
            var learningPlan = learningPlanner.CreatePlan(prepPlan, vacancy, profile, interviewHistory);

            allPrepPlans.Add(prepPlan);
            allLearningPlans.Add(learningPlan);
        }

        // JSON output mode
        if (json)
        {
            PrintJson(allPrepPlans, allLearningPlans);
            return;
        }

        // Formatted console output
        PrintHeader();

        for (int i = 0; i < targets.Count; i++)
        {
            PrintPrepPlan(targets[i], allPrepPlans[i], allLearningPlans[i], i + 1);
        }

        // Consolidated learning plan if multiple vacancies
        if (targets.Count > 1)
        {
            var consolidated = learningPlanner.CreateConsolidatedPlan(
                allPrepPlans, targets, profile, interviewHistory);
            PrintConsolidatedPlan(consolidated);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Header
    // ──────────────────────────────────────────────────────────────

    private static void PrintHeader()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║          INTERVIEW PREPARATION ENGINE                    ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Per-vacancy prep plan
    // ──────────────────────────────────────────────────────────────

    private static void PrintPrepPlan(
        JobVacancy vacancy, InterviewPrepPlan prep, InterviewLearningPlan learning, int index)
    {
        // Title bar
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  ━━━ [{index}] {prep.VacancyTitle} at {prep.Company} ━━━");
        Console.ResetColor();
        Console.WriteLine();

        // ── Section 1: Skill Question Sets ──
        PrintSkillQuestionSets(prep);

        // ── Section 2: Behavioral Questions ──
        if (prep.BehavioralQuestions.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  ┌─ BEHAVIORAL QUESTIONS ─────────────────────────────────");
            Console.ResetColor();

            foreach (var q in prep.BehavioralQuestions)
            {
                PrintQuestion(q, "    ");
            }
            Console.WriteLine();
        }

        // ── Section 3: System Design Questions ──
        if (prep.SystemDesignQuestions.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("  ┌─ SYSTEM DESIGN QUESTIONS ──────────────────────────────");
            Console.ResetColor();

            foreach (var q in prep.SystemDesignQuestions)
            {
                PrintQuestion(q, "    ");
            }
            Console.WriteLine();
        }

        // ── Section 4: Red Flags ──
        if (prep.RedFlags.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  ┌─ RED FLAGS ────────────────────────────────────────────");
            Console.ResetColor();

            foreach (var flag in prep.RedFlags)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("    ⊘ ");
                Console.ResetColor();
                Console.WriteLine(flag);
            }
            Console.WriteLine();
        }

        // ── Section 5: Overall Strategy ──
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("  ┌─ STRATEGY ─────────────────────────────────────────────");
        Console.ResetColor();
        PrintWrapped(prep.OverallStrategy, "    ", 72);
        Console.WriteLine();

        // ── Section 6: Interview Learning Plan ──
        PrintLearningPlan(learning);

        // ── Section 7: Pass Probability & Verdict ──
        PrintVerdictBlock(learning);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  ──────────────────────────────────────────────────────────");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Skill question sets
    // ──────────────────────────────────────────────────────────────

    private static void PrintSkillQuestionSets(InterviewPrepPlan prep)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ┌─ SKILL QUESTION SETS ──────────────────────────────────");
        Console.ResetColor();
        Console.WriteLine();

        foreach (var set in prep.SkillSets)
        {
            // Skill header
            var reqLabel = set.IsRequired ? "REQUIRED" : "preferred";
            var reqColor = set.IsRequired ? ConsoleColor.Red : ConsoleColor.DarkGray;

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"    {set.SkillName}");
            Console.ForegroundColor = reqColor;
            Console.Write($" [{reqLabel}]");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" depth:{set.ExpectedDepth}/5 — {set.DepthLabel}");
            Console.ResetColor();
            Console.WriteLine();

            // Questions
            foreach (var q in set.Questions)
            {
                PrintQuestion(q, "      ");
            }

            // Red flags for this skill
            if (set.RedFlags.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("      Red flags:");
                foreach (var flag in set.RedFlags)
                {
                    Console.Write("        - ");
                    Console.ResetColor();
                    Console.WriteLine(flag);
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                Console.ResetColor();
            }

            // Strong answer summary
            if (!string.IsNullOrEmpty(set.StrongAnswerSummary))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("      Strong candidate: ");
                Console.ResetColor();
                Console.WriteLine(set.StrongAnswerSummary);
            }

            Console.WriteLine();
        }
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Single question
    // ──────────────────────────────────────────────────────────────

    private static void PrintQuestion(InferredQuestion q, string indent)
    {
        // Difficulty color
        var diffColor = q.Difficulty switch
        {
            >= 5 => ConsoleColor.Red,
            4 => ConsoleColor.Yellow,
            3 => ConsoleColor.White,
            _ => ConsoleColor.DarkGray
        };

        var diffBar = new string('*', q.Difficulty) + new string('.', 5 - q.Difficulty);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{indent}Q: ");
        Console.ResetColor();
        Console.WriteLine(q.Question);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{indent}   [{q.ArchetypeLabel}] ");
        Console.ForegroundColor = diffColor;
        Console.WriteLine($"Difficulty: {diffBar} ({q.Difficulty}/5)");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write($"{indent}   Why asked: ");
        Console.ResetColor();
        Console.WriteLine(q.WhyAsked);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{indent}   Strong: ");
        Console.ResetColor();
        Console.WriteLine(q.StrongAnswer);

        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"{indent}   Weak:   ");
        Console.ResetColor();
        Console.WriteLine(q.WeakAnswer);

        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Learning plan
    // ──────────────────────────────────────────────────────────────

    private static void PrintLearningPlan(InterviewLearningPlan plan)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  ┌─ INTERVIEW LEARNING PLAN ──────────────────────────────");
        Console.ResetColor();
        Console.WriteLine();

        // Table header
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"    {"#",-3} {"Skill",-22} {"Impact",7} {"Effort",-8} {"Hours",5}  {"Focus"}");
        Console.WriteLine($"    {new string('-', 72)}");
        Console.ResetColor();

        int rank = 1;
        foreach (var item in plan.Items)
        {
            if (item.SkipRecommended)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"    {rank,-3} {item.SkillName,-22} ");
                Console.Write($"{item.InterviewImpactScore,6:F0}% ");
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("SKIP     ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(item.SkipReason);
                Console.ResetColor();
                rank++;
                continue;
            }

            // Impact color
            var impactColor = item.InterviewImpactScore switch
            {
                >= 60 => ConsoleColor.Red,
                >= 30 => ConsoleColor.Yellow,
                _ => ConsoleColor.DarkGray
            };

            // Effort color
            var effortColor = item.Effort switch
            {
                LearningEffort.Fast => ConsoleColor.Green,
                LearningEffort.Medium => ConsoleColor.Yellow,
                LearningEffort.Deep => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            var effortTag = item.Effort switch
            {
                LearningEffort.Fast => "Fast",
                LearningEffort.Medium => "Medium",
                LearningEffort.Deep => "Deep",
                _ => "?"
            };

            Console.Write($"    {rank,-3} ");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{item.SkillName,-22} ");

            Console.ForegroundColor = impactColor;
            Console.Write($"{item.InterviewImpactScore,6:F0}% ");

            Console.ForegroundColor = effortColor;
            Console.Write($"{effortTag,-8} ");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"{item.EstimatedHours + "h",5}  ");

            Console.ResetColor();
            Console.WriteLine(item.FocusArea);

            // Prep actions (indented)
            foreach (var action in item.PrepActions.Take(3))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"          -> {action}");
                Console.ResetColor();
            }

            rank++;
        }

        Console.WriteLine();

        // Summary line
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"    Total: {plan.TotalEstimatedHours}h prep");
        Console.ResetColor();
        Console.Write(" | ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write($"{plan.CriticalGapCount} critical gap(s)");
        Console.ResetColor();
        Console.Write(" | ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{plan.SkippedLowROICount} skipped (low ROI)");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Pass probability & verdict
    // ──────────────────────────────────────────────────────────────

    private static void PrintVerdictBlock(InterviewLearningPlan plan)
    {
        // Pass probability bar
        int barWidth = 30;
        int filled = (int)(plan.EstimatedPassProbability / 100 * barWidth);
        int empty = barWidth - filled;

        var probColor = plan.EstimatedPassProbability switch
        {
            >= 70 => ConsoleColor.Green,
            >= 45 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("    Pass Probability: ");
        Console.ForegroundColor = probColor;
        Console.Write($"[{new string('#', filled)}{new string('-', empty)}] ");
        Console.Write($"{plan.EstimatedPassProbability:F0}%");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine();

        // Verdict
        var verdictColor = plan.Verdict switch
        {
            var v when v.StartsWith("Ready") => ConsoleColor.Green,
            var v when v.StartsWith("Nearly") => ConsoleColor.Green,
            var v when v.StartsWith("Prep") => ConsoleColor.Yellow,
            var v when v.StartsWith("Significant") => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("    VERDICT: ");
        Console.ForegroundColor = verdictColor;
        Console.WriteLine(plan.Verdict);
        Console.ResetColor();
        Console.WriteLine();
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: Consolidated plan (multi-vacancy)
    // ──────────────────────────────────────────────────────────────

    private static void PrintConsolidatedPlan(InterviewLearningPlan consolidated)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║       CONSOLIDATED LEARNING PLAN (ALL VACANCIES)         ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();

        PrintLearningPlan(consolidated);
        PrintVerdictBlock(consolidated);
    }

    // ──────────────────────────────────────────────────────────────
    // OUTPUT: JSON mode
    // ──────────────────────────────────────────────────────────────

    private static void PrintJson(List<InterviewPrepPlan> preps, List<InterviewLearningPlan> learnings)
    {
        var output = new
        {
            PrepPlans = preps,
            LearningPlans = learnings,
            GeneratedDate = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Console.WriteLine(json);
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static void PrintWrapped(string text, string indent, int maxWidth)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var words = text.Split(' ');
        int currentLineLen = 0;
        Console.Write(indent);

        foreach (var word in words)
        {
            if (currentLineLen + word.Length + 1 > maxWidth && currentLineLen > 0)
            {
                Console.WriteLine();
                Console.Write(indent);
                currentLineLen = 0;
            }

            if (currentLineLen > 0)
            {
                Console.Write(' ');
                currentLineLen++;
            }

            Console.Write(word);
            currentLineLen += word.Length;
        }

        Console.WriteLine();
    }

    private static async Task<T?> LoadJsonAsync<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static async Task<List<T>> LoadJsonListAsync<T>(string path)
    {
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static string? FindLatestVacanciesFile()
    {
        if (!Directory.Exists(Program.DataDirectory)) return null;
        return Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
