using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for making authoritative GO/NO-GO decisions on job applications.
/// Uses ApplicationDecisionEngine to provide actionable verdicts: APPLY NOW, LEARN THEN APPLY, or SKIP.
/// This is the "brain" that prevents wasted effort and over-preparation paralysis.
/// Usage: career-intel decide [--vacancy-id id] [--top N] [--filter verdict]
/// </summary>
public static class DecideCommand
{
    public static Command Create()
    {
        var vacancyIdOption = new Option<string?>(
            "--vacancy-id",
            description: "Specific vacancy ID to analyze");

        var topOption = new Option<int>(
            name: "--top",
            getDefaultValue: () => 10,
            description: "Number of top vacancies to analyze");

        var filterOption = new Option<string?>(
            "--filter",
            description: "Filter by verdict: apply-now, learn-first, skip");

        var showReasoningOption = new Option<bool>(
            name: "--reasoning",
            getDefaultValue: () => true,
            description: "Show detailed reasoning");

        var command = new Command("decide", "Get GO/NO-GO decisions for job applications")
        {
            vacancyIdOption,
            topOption,
            filterOption,
            showReasoningOption
        };

        command.SetHandler(ExecuteAsync, vacancyIdOption, topOption, filterOption, showReasoningOption);

        return command;
    }

    private static async Task ExecuteAsync(string? vacancyId, int top, string? filter, bool showReasoning)
    {
        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Profile not found. Create your profile first with 'career-intel profile create'");
            Console.ResetColor();
            return;
        }

        var profileJson = await File.ReadAllTextAsync(profilePath);
        var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (profile == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Could not load profile.");
            Console.ResetColor();
            return;
        }

        // Load vacancies
        var latestFile = Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (latestFile == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No vacancies data found. Run 'career-intel scan' first.");
            Console.ResetColor();
            return;
        }

        var json = await File.ReadAllTextAsync(latestFile);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        if (vacancies.Count == 0)
        {
            Console.WriteLine("No vacancies to analyze.");
            return;
        }

        // Initialize decision engine
        var scorer = new ScoringEngine();
        var decisionEngine = new ApplicationDecisionEngine(null!, scorer); // ProfileMatcher not needed, we pass profile directly

        // Analyze specific vacancy or top N
        if (!string.IsNullOrEmpty(vacancyId))
        {
            var vacancy = vacancies.FirstOrDefault(v => v.Id == vacancyId);
            if (vacancy == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Vacancy '{vacancyId}' not found.");
                Console.ResetColor();
                return;
            }

            var decision = decisionEngine.Decide(vacancy, profile);
            PrintDetailedDecision(vacancy, decision, showReasoning);
        }
        else
        {
            // Analyze all vacancies and show top N
            var decisions = vacancies
                .Select(v => (Vacancy: v, Decision: decisionEngine.Decide(v, profile)))
                .ToList();

            // Apply filter if specified
            if (!string.IsNullOrEmpty(filter))
            {
                var filterVerdict = filter.ToLowerInvariant() switch
                {
                    "apply-now" or "apply" or "now" => ApplicationVerdict.ApplyNow,
                    "learn-first" or "learn" => ApplicationVerdict.LearnThenApply,
                    "skip" => ApplicationVerdict.Skip,
                    _ => (ApplicationVerdict?)null
                };

                if (filterVerdict.HasValue)
                {
                    decisions = decisions.Where(d => d.Decision.Verdict == filterVerdict.Value).ToList();
                }
            }

            // Sort: APPLY_NOW first, then LEARN_THEN_APPLY, then SKIP
            // Within each category, sort by match score
            decisions = decisions
                .OrderBy(d => d.Decision.Verdict)
                .ThenByDescending(d => d.Decision.MatchScore)
                .Take(top)
                .ToList();

            PrintDecisionSummary(decisions, showReasoning);
        }
    }

    private static void PrintDetailedDecision(JobVacancy vacancy, ApplicationDecision decision, bool showReasoning)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ DECISION: {vacancy.Title} at {vacancy.Company} ═══\n");
        Console.ResetColor();

        // Verdict
        PrintVerdict(decision.Verdict);
        Console.WriteLine();

        // Scores
        Console.WriteLine("Scores:");
        Console.WriteLine($"  Match:     {decision.MatchScore}%");
        Console.WriteLine($"  Readiness: {decision.ReadinessScore}%");
        Console.WriteLine($"  Confidence: {decision.Confidence}%");
        Console.WriteLine();

        // Apply by date
        if (decision.ApplyByDate.HasValue)
        {
            var daysUntil = (decision.ApplyByDate.Value - DateTimeOffset.UtcNow).Days;
            Console.ForegroundColor = decision.Verdict == ApplicationVerdict.ApplyNow ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($"Apply by: {decision.ApplyByDate.Value:MMM dd, yyyy} ({daysUntil} days)");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Skill gaps
        if (decision.SkillGaps.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Skill Gaps ({decision.EstimatedLearningHours}h total learning time):");
            Console.ResetColor();

            foreach (var gap in decision.SkillGaps.Take(10))
            {
                var icon = gap.IsCritical ? "✗" : "○";
                var color = gap.IsCritical ? ConsoleColor.Red : ConsoleColor.DarkYellow;
                Console.ForegroundColor = color;
                Console.Write($"  {icon} {gap.SkillName,-25}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" Level {gap.CurrentLevel}→{gap.TargetLevel} (~{gap.HoursToLearn}h)");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        // Quick wins
        if (decision.QuickWins.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Quick Wins (≤4h learning):");
            Console.ResetColor();
            foreach (var skill in decision.QuickWins)
            {
                Console.WriteLine($"  • {skill}");
            }
            Console.WriteLine();
        }

        // Reasoning
        if (showReasoning)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Reasoning:");
            Console.WriteLine(decision.Reasoning);
            Console.ResetColor();
            Console.WriteLine();
        }

        // Action items
        PrintActionItems(decision, vacancy);
    }

    private static void PrintDecisionSummary(List<(JobVacancy Vacancy, ApplicationDecision Decision)> decisions, bool showReasoning)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ APPLICATION DECISIONS ({decisions.Count} positions analyzed) ═══\n");
        Console.ResetColor();

        // Group by verdict
        var byVerdict = decisions.GroupBy(d => d.Decision.Verdict).ToList();

        foreach (var group in byVerdict.OrderBy(g => g.Key))
        {
            var verdict = group.Key;
            var count = group.Count();

            Console.ForegroundColor = GetVerdictColor(verdict);
            Console.WriteLine($"▌{verdict.ToString().ToUpperInvariant()}: {count} position(s)");
            Console.ResetColor();
            Console.WriteLine();

            var rank = 1;
            foreach (var item in group.Take(20))
            {
                var vacancy = item.Vacancy;
                var decision = item.Decision;

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  {rank}. {vacancy.Title}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" at {vacancy.Company}");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"     Match: {decision.MatchScore}% | Readiness: {decision.ReadinessScore}%");

                if (decision.EstimatedLearningHours > 0)
                    Console.Write($" | Learning: {decision.EstimatedLearningHours}h");

                if (decision.ApplyByDate.HasValue)
                {
                    var days = (decision.ApplyByDate.Value - DateTimeOffset.UtcNow).Days;
                    Console.Write($" | Apply by: {decision.ApplyByDate.Value:MMM dd} ({days}d)");
                }

                Console.WriteLine();
                Console.ResetColor();

                if (showReasoning)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    var firstLine = decision.Reasoning.Split('\n').FirstOrDefault() ?? "";
                    Console.WriteLine($"     {firstLine}");
                    Console.ResetColor();
                }

                Console.WriteLine();
                rank++;
            }
        }

        // Summary stats
        PrintSummaryStats(decisions);
    }

    private static void PrintVerdict(ApplicationVerdict verdict)
    {
        var (icon, text, color) = verdict switch
        {
            ApplicationVerdict.ApplyNow => ("✓", "APPLY NOW", ConsoleColor.Green),
            ApplicationVerdict.LearnThenApply => ("⚠", "LEARN THEN APPLY", ConsoleColor.Yellow),
            ApplicationVerdict.Skip => ("✗", "SKIP", ConsoleColor.Red),
            _ => ("?", "UNKNOWN", ConsoleColor.Gray)
        };

        Console.ForegroundColor = color;
        Console.WriteLine($"{icon} VERDICT: {text}");
        Console.ResetColor();
    }

    private static ConsoleColor GetVerdictColor(ApplicationVerdict verdict)
    {
        return verdict switch
        {
            ApplicationVerdict.ApplyNow => ConsoleColor.Green,
            ApplicationVerdict.LearnThenApply => ConsoleColor.Yellow,
            ApplicationVerdict.Skip => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }

    private static void PrintActionItems(ApplicationDecision decision, JobVacancy vacancy)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Next Steps:");
        Console.ResetColor();

        switch (decision.Verdict)
        {
            case ApplicationVerdict.ApplyNow:
                Console.WriteLine($"  1. Tailor resume: career-intel resume --vacancy-id {vacancy.Id}");
                Console.WriteLine($"  2. Prepare for interview: career-intel interview-prep --company \"{vacancy.Company}\"");
                Console.WriteLine($"  3. Apply: career-intel apply --vacancy-id {vacancy.Id}");
                break;

            case ApplicationVerdict.LearnThenApply:
                Console.WriteLine($"  1. Start learning: career-intel learn --skills \"{string.Join(",", decision.CriticalMissingSkills.Take(3))}\"");
                Console.WriteLine($"  2. Track progress: career-intel assess");
                Console.WriteLine($"  3. Re-evaluate in {decision.EstimatedLearningHours / 2} days");
                if (decision.QuickWins.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  💡 Quick win: Learn '{decision.QuickWins.First()}' first (≤4h)");
                    Console.ResetColor();
                }
                break;

            case ApplicationVerdict.Skip:
                Console.WriteLine($"  1. Skip this position");
                Console.WriteLine($"  2. Focus on better matches: career-intel decide --filter apply-now");
                break;
        }
    }

    private static void PrintSummaryStats(List<(JobVacancy Vacancy, ApplicationDecision Decision)> decisions)
    {
        var applyNow = decisions.Count(d => d.Decision.Verdict == ApplicationVerdict.ApplyNow);
        var learnFirst = decisions.Count(d => d.Decision.Verdict == ApplicationVerdict.LearnThenApply);
        var skip = decisions.Count(d => d.Decision.Verdict == ApplicationVerdict.Skip);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("─────────────────────────────────────────────────");
        Console.ResetColor();

        Console.WriteLine($"\nSummary:");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {applyNow} ready to apply NOW");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {learnFirst} need learning first");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {skip} should skip");
        Console.ResetColor();

        if (applyNow > 0)
        {
            Console.WriteLine("\nRecommendation:");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  Start with {Math.Min(applyNow, 3)} top APPLY NOW positions");
            Console.WriteLine($"  Use: career-intel decide --filter apply-now");
            Console.ResetColor();
        }
    }
}
