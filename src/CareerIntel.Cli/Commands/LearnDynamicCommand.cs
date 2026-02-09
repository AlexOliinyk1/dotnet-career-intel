using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using CareerIntel.Scrapers;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for the dynamic learning system — scrapes, classifies, tracks progress, and adapts.
/// Usage: career-intel learn-dynamic [--scrape] [--study] [--quiz topicId] [--progress] [--insights]
/// </summary>
public static class LearnDynamicCommand
{
    public static Command Create()
    {
        var scrapeOption = new Option<bool>(
            "--scrape",
            getDefaultValue: () => false,
            description: "Scrape forums (DOU, Reddit) for new interview questions and ingest into knowledge base");

        var studyOption = new Option<bool>(
            "--study",
            getDefaultValue: () => false,
            description: "Generate an adaptive study session based on your progress and market demand");

        var quizOption = new Option<string?>(
            "--quiz",
            description: "Start a self-assessment quiz for a topic (e.g., 'dotnet-internals')");

        var progressOption = new Option<bool>(
            "--progress",
            getDefaultValue: () => false,
            description: "Show learning progress dashboard across all topics");

        var insightsOption = new Option<bool>(
            "--insights",
            getDefaultValue: () => false,
            description: "Show dynamic insights — trending topics, emerging skills, knowledge base stats");

        var markOption = new Option<string?>(
            "--mark-studied",
            description: "Mark a topic as studied and set confidence (e.g., 'databases:4' for confidence 4/5)");

        var command = new Command("learn-dynamic", "Dynamic learning system — scrape, study, quiz, track progress")
        {
            scrapeOption,
            studyOption,
            quizOption,
            progressOption,
            insightsOption,
            markOption
        };

        command.SetHandler(ExecuteAsync, scrapeOption, studyOption, quizOption, progressOption, insightsOption, markOption);

        return command;
    }

    private static async Task ExecuteAsync(
        bool scrape, bool study, string? quiz, bool progress, bool insights, string? markStudied)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.LearnDynamicCommand");
        var pipeline = serviceProvider.GetRequiredService<DynamicContentPipeline>();
        var progressTracker = serviceProvider.GetRequiredService<LearningProgressTracker>();
        var knowledgeBase = serviceProvider.GetRequiredService<KnowledgeBaseManager>();

        // Load profile if exists
        UserProfile? profile = null;
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (File.Exists(profilePath))
        {
            var profileJson = await File.ReadAllTextAsync(profilePath);
            profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        // Mark topic as studied
        if (!string.IsNullOrEmpty(markStudied))
        {
            HandleMarkStudied(markStudied, progressTracker);
            return;
        }

        // Show progress dashboard
        if (progress)
        {
            ShowProgress(progressTracker);
            return;
        }

        // Show dynamic insights
        if (insights)
        {
            var dynamicInsights = pipeline.AnalyzeExisting(Program.DataDirectory);
            ShowInsights(dynamicInsights);
            return;
        }

        // Quiz mode
        if (!string.IsNullOrEmpty(quiz))
        {
            RunQuiz(quiz, progressTracker, knowledgeBase);
            return;
        }

        // Scrape + ingest new questions
        if (scrape)
        {
            await RunScrapeAndIngest(serviceProvider, pipeline, profile);
            return;
        }

        // Study session (default if no flags)
        if (study || (!scrape && !progress && !insights && string.IsNullOrEmpty(quiz)))
        {
            ShowStudySession(pipeline, profile);
        }
    }

    private static async Task RunScrapeAndIngest(
        ServiceProvider sp, DynamicContentPipeline pipeline, UserProfile? profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  Scraping forums for interview questions...");
        Console.ResetColor();

        var allQuestions = new List<ScrapedInterviewQuestion>();

        // Scrape DOU Forum
        try
        {
            var douScraper = sp.GetRequiredService<DouForumScraper>();
            Console.Write("  DOU.ua forum... ");
            var douQuestions = await douScraper.ScrapeInterviewQuestionsAsync();
            allQuestions.AddRange(douQuestions);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{douQuestions.Count} questions found");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"skipped ({ex.Message})");
            Console.ResetColor();
        }

        // Scrape Reddit
        try
        {
            var redditScraper = sp.GetRequiredService<RedditScraper>();
            Console.Write("  Reddit r/dotnet, r/csharp... ");
            var redditQuestions = await redditScraper.ScrapeInterviewQuestionsAsync();
            allQuestions.AddRange(redditQuestions);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{redditQuestions.Count} questions found");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"skipped ({ex.Message})");
            Console.ResetColor();
        }

        if (allQuestions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  No questions scraped. Check network connection.");
            Console.ResetColor();
            return;
        }

        // Load vacancies if available
        List<JobVacancy>? vacancies = null;
        var vacancyPath = FindLatestVacanciesFile();
        if (vacancyPath is not null)
        {
            var json = await File.ReadAllTextAsync(vacancyPath);
            vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        // Run pipeline
        var result = await pipeline.RunAsync(new PipelineInput(
            DataDirectory: Program.DataDirectory,
            ScrapedQuestions: allQuestions,
            Vacancies: vacancies,
            Profile: profile));

        // Display results
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("          DYNAMIC CONTENT PIPELINE RESULTS            ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        Console.WriteLine($"\n  Questions processed: {result.Ingestion.TotalProcessed}");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  New questions added: {result.Ingestion.NewQuestionsAdded}");
        Console.ResetColor();
        Console.WriteLine($"  Duplicates skipped: {result.Ingestion.DuplicatesSkipped}");
        Console.WriteLine($"  Unclassified skipped: {result.Ingestion.UnclassifiedSkipped}");

        if (result.Ingestion.TopicsEnriched.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  Topics enriched: {string.Join(", ", result.Ingestion.TopicsEnriched)}");
            Console.ResetColor();
        }

        if (result.TrendingTopics.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Trending Topics (most new questions):");
            Console.ResetColor();

            foreach (var topic in result.TrendingTopics.Take(5))
            {
                Console.Write($"    {topic.TopicName,-30} ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"+{topic.NewQuestionsLast30Days} new ");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"({topic.TotalQuestions} total)");
                Console.ResetColor();
            }
        }

        // Knowledge base stats
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Knowledge Base: {result.Stats.TotalQuestions} questions " +
            $"({result.Stats.StaticQuestions} static + {result.Stats.DynamicQuestions} scraped)");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void ShowStudySession(DynamicContentPipeline pipeline, UserProfile? profile)
    {
        var plan = pipeline.GetAdaptiveStudyPlan(Program.DataDirectory, profile);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("           ADAPTIVE STUDY SESSION                     ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        // Daily goal
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  Today's Goal: {plan.DailyGoal}");
        Console.ResetColor();
        Console.WriteLine($"  Estimated days to interview-ready: {plan.EstimatedDaysToReady}");

        // Readiness
        var readColor = plan.Readiness.OverallScore switch
        {
            >= 80 => ConsoleColor.Green,
            >= 60 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.Write("  Readiness: ");
        Console.ForegroundColor = readColor;
        Console.WriteLine($"{plan.Readiness.OverallScore:F0}/100 — {plan.Readiness.Verdict}");
        Console.ResetColor();

        // Priorities
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  Study Priorities:");
        Console.ResetColor();

        foreach (var priority in plan.Priorities.Take(5))
        {
            var prioColor = priority.PriorityScore switch
            {
                >= 70 => ConsoleColor.Red,
                >= 40 => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };

            Console.ForegroundColor = prioColor;
            Console.Write($"    [{priority.PriorityScore:F0}] ");
            Console.ResetColor();
            Console.Write($"{priority.TopicName,-25} ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{priority.QuestionsRemaining} remaining, ~{priority.EstimatedHours}h — {priority.Reason}");
            Console.ResetColor();
        }

        // Study questions
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Study Session: {plan.NextSession.SessionName} (~{plan.NextSession.EstimatedMinutes}min)");
        Console.ResetColor();

        for (var i = 0; i < plan.NextSession.Questions.Count; i++)
        {
            var q = plan.NextSession.Questions[i];
            var studied = q.IsPreviouslyStudied ? " (review)" : " (new)";
            var diffColor = q.Difficulty switch
            {
                "Senior" => ConsoleColor.Red,
                "Mid" => ConsoleColor.Yellow,
                _ => ConsoleColor.Green
            };

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"\n  Q{i + 1}. ");
            Console.ResetColor();
            Console.Write(q.Question.Length > 100 ? q.Question[..97] + "..." : q.Question);
            Console.ForegroundColor = diffColor;
            Console.Write($" [{q.Difficulty}]");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(studied);
            Console.ResetColor();

            // Show answer (collapsed)
            if (q.ExpectedAnswer.Length > 0)
            {
                var shortAnswer = q.ExpectedAnswer.Length > 200
                    ? q.ExpectedAnswer[..197] + "..."
                    : q.ExpectedAnswer;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     A: {shortAnswer}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  Use --mark-studied \"<topicId>:<confidence 1-5>\" after studying!");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void ShowProgress(LearningProgressTracker tracker)
    {
        var learningProgress = tracker.LoadProgress(Program.DataDirectory);
        var readiness = tracker.AssessReadiness(learningProgress);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("            LEARNING PROGRESS DASHBOARD               ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        // Streaks
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"  Current streak: {learningProgress.CurrentStreak} days");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($" (best: {learningProgress.LongestStreak})");
        Console.ResetColor();

        Console.WriteLine($"  Total study time: {learningProgress.TotalStudyMinutes}min");
        Console.WriteLine($"  Questions studied: {learningProgress.TotalQuestionsStudied}");
        Console.WriteLine($"  Quizzes taken: {learningProgress.TotalQuizzesTaken}");

        // Overall readiness
        var readColor = readiness.OverallScore switch
        {
            >= 80 => ConsoleColor.Green,
            >= 60 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.Write("\n  Overall Readiness: ");
        Console.ForegroundColor = readColor;
        Console.WriteLine($"{readiness.OverallScore:F0}/100 — {readiness.Verdict}");
        Console.ResetColor();

        // Per-topic progress
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  Topic Progress:");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"Topic",-25} {"Score",6} {"Status",-12} {"Studied",8} {"Quiz",6} {"Conf",5}");
        Console.WriteLine($"  {new string('─', 65)}");
        Console.ResetColor();

        foreach (var topic in readiness.ByTopic.OrderByDescending(t => t.Score))
        {
            var statusColor = topic.Status switch
            {
                "Strong" => ConsoleColor.Green,
                "Good" => ConsoleColor.Yellow,
                "Developing" => ConsoleColor.DarkYellow,
                _ => ConsoleColor.Red
            };

            Console.Write($"  {topic.TopicName,-25} ");
            Console.ForegroundColor = statusColor;
            Console.Write($"{topic.Score,5:F0}% ");
            Console.Write($"{topic.Status,-12} ");
            Console.ResetColor();

            if (learningProgress.TopicProgress.TryGetValue(topic.TopicId, out var tp))
            {
                Console.Write($"{tp.QuestionsStudied,7} ");
                Console.Write($"{tp.QuizAccuracy,5:F0}% ");
                Console.Write($"{tp.SelfConfidence,4}/5");
            }
            Console.WriteLine();
        }

        if (readiness.CriticalGaps.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  Critical Gaps: {string.Join(", ", readiness.CriticalGaps)}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void ShowInsights(DynamicInsights dynamicInsights)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("            DYNAMIC LEARNING INSIGHTS                 ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        var stats = dynamicInsights.Stats;
        Console.WriteLine($"\n  Knowledge Base: {stats.TotalQuestions} questions across {stats.TotalTopics} topics");
        Console.WriteLine($"    Static (curated): {stats.StaticQuestions}");
        Console.WriteLine($"    Dynamic (scraped): {stats.DynamicQuestions}");

        if (stats.Sources.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    Sources: {string.Join(", ", stats.Sources)}");
            Console.ResetColor();
        }

        // Topic breakdown
        if (stats.ByTopic.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Questions by Topic:");
            Console.ResetColor();

            foreach (var t in stats.ByTopic.OrderByDescending(x => x.Count))
            {
                var bar = new string('█', Math.Min(t.Count / 2, 30));
                Console.Write($"    {t.TopicName,-25} {t.Count,4} ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(bar);
                Console.ResetColor();
            }
        }

        // Trending
        if (dynamicInsights.TrendingTopics.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Trending Topics (new questions this month):");
            Console.ResetColor();

            foreach (var topic in dynamicInsights.TrendingTopics.Take(5))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"    +{topic.NewQuestionsLast30Days} ");
                Console.ResetColor();
                Console.Write($"{topic.TopicName,-25} ");

                if (topic.GrowthRate > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write($"(+{topic.GrowthRate:F0}% growth)");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
        }

        // Emerging skills
        if (dynamicInsights.EmergingSkills.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Emerging Skills (new in scraped content):");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    {string.Join(", ", dynamicInsights.EmergingSkills.Take(10))}");
            Console.ResetColor();
        }

        // Companies asking interview questions
        if (dynamicInsights.TopCompaniesAsking.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Companies Mentioned in Interview Questions:");
            Console.ResetColor();
            Console.WriteLine($"    {string.Join(", ", dynamicInsights.TopCompaniesAsking.Take(10))}");
        }

        // Readiness
        if (dynamicInsights.Readiness is not null)
        {
            var readColor = dynamicInsights.Readiness.OverallScore >= 60 ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.Write("\n  Your Readiness: ");
            Console.ForegroundColor = readColor;
            Console.WriteLine($"{dynamicInsights.Readiness.OverallScore:F0}/100 — {dynamicInsights.Readiness.Verdict}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void RunQuiz(string topicId, LearningProgressTracker tracker, KnowledgeBaseManager kb)
    {
        var questions = kb.GetQuestionsForTopic(topicId, Program.DataDirectory);
        if (questions.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"No questions found for topic '{topicId}'.");
            Console.ResetColor();

            // Show available topics
            var allTopics = InterviewTopicBank.GetAllTopics();
            Console.WriteLine("\nAvailable topics:");
            foreach (var t in allTopics)
                Console.WriteLine($"  {t.Id,-25} {t.Name}");
            return;
        }

        // Pick 5 random questions for a quick quiz
        var rng = Random.Shared;
        var quizQuestions = questions
            .OrderBy(_ => rng.Next())
            .Take(5)
            .ToList();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n  Quick Quiz: {topicId} ({quizQuestions.Count} questions)");
        Console.WriteLine($"  {new string('─', 50)}");
        Console.ResetColor();
        Console.WriteLine("  Rate yourself 1-5 on each question (or press Enter to skip)\n");

        var scores = new List<int>();

        for (var i = 0; i < quizQuestions.Count; i++)
        {
            var q = quizQuestions[i];
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Q{i + 1}. {q.Question}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            var answerPreview = q.Answer.Length > 300 ? q.Answer[..297] + "..." : q.Answer;
            Console.WriteLine($"\n  Expected: {answerPreview}");
            Console.ResetColor();

            Console.Write("\n  Your confidence (1-5): ");
            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out var score) && score is >= 1 and <= 5)
            {
                scores.Add(score);
            }
            Console.WriteLine();
        }

        if (scores.Count > 0)
        {
            var avgScore = scores.Average();
            var accuracy = (avgScore / 5.0) * 100;
            var correct = scores.Count(s => s >= 4);

            // Record quiz result
            var learningProgress = tracker.LoadProgress(Program.DataDirectory);
            tracker.RecordQuizResult(learningProgress, topicId, correct, scores.Count);
            tracker.RecordConfidence(learningProgress, topicId, (int)Math.Round(avgScore));
            tracker.SaveProgress(learningProgress, Program.DataDirectory);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  Quiz Results: {correct}/{scores.Count} confident (avg: {avgScore:F1}/5)");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Progress saved!");
            Console.ResetColor();
        }
    }

    private static void HandleMarkStudied(string markStudied, LearningProgressTracker tracker)
    {
        // Parse format: "topicId:confidence" e.g. "databases:4"
        var parts = markStudied.Split(':');
        var topicId = parts[0].Trim();
        var confidence = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : 3;

        var learningProgress = tracker.LoadProgress(Program.DataDirectory);
        tracker.RecordConfidence(learningProgress, topicId, Math.Clamp(confidence, 1, 5));
        tracker.SaveProgress(learningProgress, Program.DataDirectory);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Marked '{topicId}' as studied with confidence {confidence}/5");
        Console.ResetColor();
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
