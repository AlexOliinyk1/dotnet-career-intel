using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// Unified dashboard command that shows the overall system state at a glance.
/// Usage: career-intel dashboard [--verbose]
/// </summary>
public static class DashboardCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Command Create()
    {
        var verboseOption = new Option<bool>(
            ["--verbose", "-v"],
            getDefaultValue: () => false,
            description: "Show extended details for each section");

        var command = new Command("dashboard", "Show a unified overview of system state, profile, vacancies, matches, and learning progress")
        {
            verboseOption
        };

        command.SetHandler(ExecuteAsync, verboseOption);

        return command;
    }

    private static async Task ExecuteAsync(bool verbose)
    {
        var dataDir = Program.DataDirectory;

        PrintHeader();

        await PrintProfileSection(dataDir, verbose);
        await PrintVacancyDatabaseSection(dataDir, verbose);
        await PrintMatchOverviewSection(dataDir, verbose);
        PrintLearningProgressSection(dataDir, verbose);
        PrintSystemCapabilitiesSection(dataDir);
        await PrintSuggestedActionsSection(dataDir);

        PrintFooter();
    }

    // ─────────────────────────────────────────────────────────────
    //  Header / Footer
    // ─────────────────────────────────────────────────────────────

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.WriteLine("          CAREER INTEL DASHBOARD");
        Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintFooter()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintSectionHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  \u2500\u2500 {title} \u2500\u2500");
        Console.ResetColor();
    }

    // ─────────────────────────────────────────────────────────────
    //  1. Profile Status
    // ─────────────────────────────────────────────────────────────

    private static async Task PrintProfileSection(string dataDir, bool verbose)
    {
        PrintSectionHeader("Profile");

        var profilePath = Path.Combine(dataDir, "my-profile.json");

        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No profile found. Run 'profile create' to get started.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(profilePath);
            var profile = JsonSerializer.Deserialize<UserProfile>(json, JsonOptions);

            if (profile is null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Profile file exists but could not be parsed.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

            var name = !string.IsNullOrWhiteSpace(profile.Personal.Name)
                ? profile.Personal.Name
                : "(not set)";

            var title = !string.IsNullOrWhiteSpace(profile.Personal.Title)
                ? profile.Personal.Title
                : "(not set)";

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Name: {name}");
            Console.WriteLine($"  Title: {title}");

            var skillCount = profile.Skills.Count;
            var experienceCount = profile.Experiences.Count;
            var completeness = CalculateCompleteness(profile);

            Console.Write($"  Skills: {skillCount} | Experience: {experienceCount} role{(experienceCount != 1 ? "s" : "")} | Completeness: ");

            var completenessColor = completeness switch
            {
                >= 80 => ConsoleColor.Green,
                >= 50 => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            Console.ForegroundColor = completenessColor;
            Console.WriteLine($"{completeness}%");

            // Progress bar
            var filledBlocks = (int)Math.Round(completeness / 5.0);
            filledBlocks = Math.Clamp(filledBlocks, 0, 20);
            var emptyBlocks = 20 - filledBlocks;

            Console.ForegroundColor = completenessColor;
            Console.Write("  ");
            Console.Write(new string('\u2588', filledBlocks));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string('\u2591', emptyBlocks));
            Console.ForegroundColor = completenessColor;
            Console.WriteLine($" {completeness}%");
            Console.ResetColor();

            if (verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                if (!string.IsNullOrWhiteSpace(profile.Personal.Email))
                    Console.WriteLine($"  Email: {profile.Personal.Email}");
                if (!string.IsNullOrWhiteSpace(profile.Personal.LinkedInUrl))
                    Console.WriteLine($"  LinkedIn: {profile.Personal.LinkedInUrl}");
                if (!string.IsNullOrWhiteSpace(profile.Personal.Summary))
                    Console.WriteLine($"  Summary: {Truncate(profile.Personal.Summary, 80)}");
                if (profile.Preferences.TargetSalaryUsd > 0)
                    Console.WriteLine($"  Target salary: ${profile.Preferences.TargetSalaryUsd:N0} USD");
                Console.ResetColor();
            }
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Error reading profile file.");
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    private static int CalculateCompleteness(UserProfile profile)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(profile.Personal.Name))
            score += 10;
        if (!string.IsNullOrWhiteSpace(profile.Personal.Title))
            score += 10;
        if (profile.Skills.Count >= 5)
            score += 20;
        if (profile.Experiences.Count > 0)
            score += 20;
        if (profile.Preferences.TargetSalaryUsd > 0 || profile.Preferences.MinSalaryUsd > 0)
            score += 10;
        if (!string.IsNullOrWhiteSpace(profile.Personal.Summary))
            score += 10;
        if (!string.IsNullOrWhiteSpace(profile.Personal.LinkedInUrl))
            score += 10;
        if (!string.IsNullOrWhiteSpace(profile.Personal.Email))
            score += 10;

        return Math.Min(score, 100);
    }

    // ─────────────────────────────────────────────────────────────
    //  2. Vacancy Database Stats
    // ─────────────────────────────────────────────────────────────

    private static async Task PrintVacancyDatabaseSection(string dataDir, bool verbose)
    {
        PrintSectionHeader("Vacancy Database");

        if (!Directory.Exists(dataDir))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Data directory not found. No vacancy data available.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        var vacancyFiles = Directory.GetFiles(dataDir, "vacancies-*.json")
            .OrderByDescending(f => f)
            .ToArray();

        var imageFiles = Directory.GetFiles(dataDir, "image-vacancies-*.json")
            .OrderByDescending(f => f)
            .ToArray();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  Scan files:      {vacancyFiles.Length}");

        if (vacancyFiles.Length > 0)
        {
            var latestFile = vacancyFiles[0];
            var latestDate = File.GetLastWriteTime(latestFile);

            try
            {
                var json = await File.ReadAllTextAsync(latestFile);
                var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, JsonOptions) ?? [];

                Console.WriteLine($"  Total vacancies: {vacancies.Count} (latest: {latestDate:yyyy-MM-dd})");

                if (verbose && vacancies.Count > 0)
                {
                    var platforms = vacancies
                        .GroupBy(v => v.SourcePlatform)
                        .OrderByDescending(g => g.Count())
                        .Select(g => $"{g.Key}: {g.Count()}");

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"  By platform:     {string.Join(", ", platforms)}");
                    Console.ResetColor();
                }
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Total vacancies: (error reading latest file)");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  No vacancy scan files found.");
            Console.ResetColor();
        }

        if (imageFiles.Length > 0)
        {
            var totalImageVacancies = 0;

            try
            {
                foreach (var imageFile in imageFiles)
                {
                    var imgJson = await File.ReadAllTextAsync(imageFile);
                    var imgVacancies = JsonSerializer.Deserialize<List<JobVacancy>>(imgJson, JsonOptions) ?? [];
                    totalImageVacancies += imgVacancies.Count;
                }
            }
            catch
            {
                // Ignore errors counting image vacancies
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Image scans:     {imageFiles.Length} ({totalImageVacancies} vacancies extracted)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Image scans:     0");
        }

        Console.ResetColor();
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────
    //  3. Match Overview
    // ─────────────────────────────────────────────────────────────

    private static async Task PrintMatchOverviewSection(string dataDir, bool verbose)
    {
        PrintSectionHeader("Match Summary");

        var latestFile = FindLatestVacanciesFile(dataDir);

        if (latestFile is null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  No vacancy data available for match summary.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(latestFile);
            var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, JsonOptions) ?? [];

            var matched = vacancies.Where(v => v.MatchScore is not null).ToList();

            if (matched.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  No matches computed yet. Run 'match' to analyze vacancies.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

            var totalVacancies = vacancies.Count;
            var applyCount = matched.Count(v => v.MatchScore?.RecommendedAction == RecommendedAction.Apply);
            var prepareCount = matched.Count(v => v.MatchScore?.RecommendedAction == RecommendedAction.PrepareAndApply);
            var skillUpCount = matched.Count(v => v.MatchScore?.RecommendedAction == RecommendedAction.SkillUpFirst);
            var skipCount = matched.Count(v => v.MatchScore?.RecommendedAction == RecommendedAction.Skip);
            var avgScore = matched.Average(v => v.MatchScore!.OverallScore);

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Matched: {matched.Count}/{totalVacancies}");

            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"Apply Now: {applyCount}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" | ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"Prepare: {prepareCount}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" | ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write($"SkillUp: {skillUpCount}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" | ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"Skip: {skipCount}");
            Console.ResetColor();
            Console.WriteLine();

            // Top missing skills
            var missingSkills = matched
                .Where(v => v.MatchScore?.MissingSkills is not null)
                .SelectMany(v => v.MatchScore!.MissingSkills)
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            var missingDisplay = missingSkills.Count > 0
                ? string.Join(", ", missingSkills)
                : "none";

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  Avg score: {avgScore:F1}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" | ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"Top missing: {missingDisplay}");
            Console.ResetColor();
            Console.WriteLine();

            if (verbose && matched.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                var topMatches = matched
                    .OrderByDescending(v => v.MatchScore!.OverallScore)
                    .Take(3);

                Console.WriteLine("  Top 3 matches:");
                foreach (var v in topMatches)
                {
                    Console.WriteLine($"    [{v.MatchScore!.OverallScore:F0}] {v.Title} at {v.Company}");
                }
                Console.ResetColor();
            }
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Error reading vacancy/match data.");
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────
    //  4. Learning Progress
    // ─────────────────────────────────────────────────────────────

    private static void PrintLearningProgressSection(string dataDir, bool verbose)
    {
        PrintSectionHeader("Learning");

        var progressPath = Path.Combine(dataDir, "learning-progress.json");

        if (!File.Exists(progressPath))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  No learning progress found. Run 'interview-prep' to start studying.");
            Console.ResetColor();
            Console.WriteLine();
            return;
        }

        try
        {
            var json = File.ReadAllText(progressPath);
            var progress = JsonSerializer.Deserialize<LearningProgressData>(json, JsonOptions);

            if (progress is null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Learning progress file could not be parsed.");
                Console.ResetColor();
                Console.WriteLine();
                return;
            }

            var topicsStudied = progress.TopicProgress?.Count(kv => kv.Value.QuestionsStudied > 0) ?? 0;
            var totalTopics = progress.TopicProgress?.Count ?? 0;
            var totalQuestions = progress.TotalQuestionsStudied;

            // Compute a rough readiness from topic data
            var readiness = CalculateLearningReadiness(progress);

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"  Topics studied: {topicsStudied}/{totalTopics}");
            Console.Write($" | Questions: {totalQuestions}");
            Console.Write(" | Readiness: ");

            var readinessColor = readiness switch
            {
                >= 70 => ConsoleColor.Green,
                >= 40 => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            Console.ForegroundColor = readinessColor;
            Console.Write($"{readiness}%");
            Console.ResetColor();
            Console.WriteLine();

            if (verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Quizzes taken: {progress.TotalQuizzesTaken}");
                Console.WriteLine($"  Current streak: {progress.CurrentStreak} day{(progress.CurrentStreak != 1 ? "s" : "")} | Longest: {progress.LongestStreak} day{(progress.LongestStreak != 1 ? "s" : "")}");

                if (progress.LastStudyDate != default)
                {
                    Console.WriteLine($"  Last study session: {progress.LastStudyDate:yyyy-MM-dd}");
                }

                Console.ResetColor();
            }
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Error reading learning progress.");
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    private static int CalculateLearningReadiness(LearningProgressData progress)
    {
        if (progress.TopicProgress is null || progress.TopicProgress.Count == 0)
            return 0;

        double totalScore = 0;
        var topicCount = 0;

        foreach (var (_, tp) in progress.TopicProgress)
        {
            var coverageScore = tp.TotalQuestionsAvailable > 0
                ? (double)tp.QuestionsStudied / tp.TotalQuestionsAvailable * 100.0
                : 0;

            var quizScore = tp.QuizAttempts > 0 ? tp.QuizAccuracy : 0;
            var confidenceScore = tp.SelfConfidence / 5.0 * 100.0;

            var score = coverageScore * 0.4 + quizScore * 0.3 + confidenceScore * 0.3;
            totalScore += Math.Min(score, 100);
            topicCount++;
        }

        return topicCount > 0 ? (int)Math.Round(totalScore / topicCount) : 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  5. System Capabilities
    // ─────────────────────────────────────────────────────────────

    private static void PrintSystemCapabilitiesSection(string dataDir)
    {
        PrintSectionHeader("System");

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("  Scrapers: ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("11");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(" | Engines: ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("25+");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(" | Commands: ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("23");
        Console.ResetColor();
        Console.WriteLine();

        // OCR capability
        var tessdataPath = Path.Combine(dataDir, "tessdata", "eng.traineddata");
        var ocrReady = File.Exists(tessdataPath);

        Console.Write("  OCR: ");
        if (ocrReady)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\u2713 Ready");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("\u2717 Not configured");
        }

        Console.ResetColor();

        // Notifications
        var notifConfigPath = Path.Combine(dataDir, "notification-config.json");
        var notifConfigured = File.Exists(notifConfigPath);

        Console.Write(" | Notifications: ");
        if (notifConfigured)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\u2713 Configured");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("\u2717 Not configured");
        }

        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────
    //  6. Quick Actions
    // ─────────────────────────────────────────────────────────────

    private static async Task PrintSuggestedActionsSection(string dataDir)
    {
        PrintSectionHeader("Suggested Actions");

        var actions = new List<string>();

        // Check profile
        var profilePath = Path.Combine(dataDir, "my-profile.json");
        var hasProfile = false;

        if (!File.Exists(profilePath))
        {
            actions.Add("career-intel profile create");
        }
        else
        {
            try
            {
                var profileJson = await File.ReadAllTextAsync(profilePath);
                var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, JsonOptions);
                hasProfile = profile is not null && !string.IsNullOrWhiteSpace(profile.Personal.Name);
            }
            catch
            {
                hasProfile = false;
            }
        }

        // Check vacancies
        var latestFile = FindLatestVacanciesFile(dataDir);
        var hasVacancies = false;
        var hasMatches = false;
        string? latestVacancyFileName = null;

        if (latestFile is null)
        {
            if (hasProfile)
                actions.Add("career-intel scan --all");
        }
        else
        {
            latestVacancyFileName = Path.GetFileName(latestFile);

            try
            {
                var vacJson = await File.ReadAllTextAsync(latestFile);
                var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(vacJson, JsonOptions) ?? [];
                hasVacancies = vacancies.Count > 0;
                hasMatches = vacancies.Any(v => v.MatchScore is not null);

                if (hasVacancies && !hasMatches)
                {
                    actions.Add($"career-intel match --input {latestVacancyFileName}");
                }

                if (hasMatches)
                {
                    actions.Add($"career-intel assess --input {latestVacancyFileName}");

                    var topApply = vacancies
                        .Where(v => v.MatchScore?.RecommendedAction == RecommendedAction.Apply)
                        .OrderByDescending(v => v.MatchScore!.OverallScore)
                        .FirstOrDefault();

                    if (topApply is not null)
                    {
                        actions.Add($"career-intel resume --vacancy-id {topApply.Id}");
                    }
                }
            }
            catch
            {
                // If we can't read the file, suggest a rescan
                actions.Add("career-intel scan --all");
            }
        }

        // Always suggest learning actions if there is some profile data
        if (hasProfile)
        {
            actions.Add("career-intel interview-prep --topic dotnet-internals");
            actions.Add("career-intel resources --external");
        }

        if (actions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  No actions suggested at this time.");
            Console.ResetColor();
        }
        else
        {
            foreach (var action in actions)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("  \u2192 ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(action);
            }
            Console.ResetColor();
        }

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static string? FindLatestVacanciesFile(string dataDir)
    {
        if (!Directory.Exists(dataDir))
            return null;

        return Directory.GetFiles(dataDir, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Collapse newlines to spaces for single-line display
        var singleLine = text.Replace('\n', ' ').Replace('\r', ' ');

        return singleLine.Length <= maxLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, maxLength - 3), "...");
    }

    /// <summary>
    /// Lightweight DTO for deserializing learning-progress.json without depending
    /// on the Intelligence assembly. Mirrors the shape written by LearningProgressTracker.
    /// </summary>
    private sealed class LearningProgressData
    {
        public Dictionary<string, LearningTopicProgressData>? TopicProgress { get; set; }
        public int TotalQuestionsStudied { get; set; }
        public int TotalQuizzesTaken { get; set; }
        public DateTimeOffset LastStudyDate { get; set; }
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }
    }

    private sealed class LearningTopicProgressData
    {
        public string TopicId { get; set; } = string.Empty;
        public string TopicName { get; set; } = string.Empty;
        public int QuestionsStudied { get; set; }
        public int TotalQuestionsAvailable { get; set; }
        public int SelfConfidence { get; set; }
        public double QuizAccuracy { get; set; }
        public int QuizAttempts { get; set; }
    }
}
