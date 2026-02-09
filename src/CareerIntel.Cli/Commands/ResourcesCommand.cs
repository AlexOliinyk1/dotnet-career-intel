using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that recommends learning resources, certifications, and practice platforms.
/// Usage: career-intel resources [--topic id] [--gaps] [--certs] [--practice] [--external]
/// </summary>
public static class ResourcesCommand
{
    public static Command Create()
    {
        var topicOption = new Option<string?>(
            "--topic",
            description: "Get learning path for a specific topic ID (e.g., 'dotnet-internals', 'databases')");

        var gapsOption = new Option<bool>(
            "--gaps",
            getDefaultValue: () => false,
            description: "Recommend resources based on your skill gaps from latest match results");

        var certsOption = new Option<bool>(
            "--certs",
            getDefaultValue: () => false,
            description: "Show certification recommendations");

        var practiceOption = new Option<bool>(
            "--practice",
            getDefaultValue: () => false,
            description: "Show practice platform recommendations (HackerRank, LeetCode, etc.)");

        var externalOption = new Option<bool>(
            "--external",
            getDefaultValue: () => false,
            description: "Fetch live external resources (MS Learn modules, GitHub trending, .NET blogs, Stack Overflow)");

        var command = new Command("resources", "Get personalized learning resource recommendations")
        {
            topicOption,
            gapsOption,
            certsOption,
            practiceOption,
            externalOption
        };

        command.SetHandler(ExecuteAsync, topicOption, gapsOption, certsOption, practiceOption, externalOption);

        return command;
    }

    private static async Task ExecuteAsync(string? topic, bool gaps, bool certs, bool practice, bool external)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.ResourcesCommand");
        var resourceEngine = serviceProvider.GetRequiredService<ResourceRecommendationEngine>();

        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        UserProfile? profile = null;
        if (File.Exists(profilePath))
        {
            var profileJson = await File.ReadAllTextAsync(profilePath);
            profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        // Topic learning path
        if (!string.IsNullOrEmpty(topic))
        {
            var path = resourceEngine.GetLearningPath(topic);
            PrintLearningPath(path);
            return;
        }

        // Certification recommendations
        if (certs && profile is not null)
        {
            var certRecommendations = resourceEngine.RecommendCertifications(profile);
            PrintCertifications(certRecommendations);
            return;
        }

        // Practice platforms
        if (practice)
        {
            var skills = profile?.Skills?.Select(s => s.SkillName).ToList()
                ?? ["C#", ".NET", "SQL", "Azure"];
            var practiceResources = resourceEngine.RecommendPractice(skills);
            PrintPractice(practiceResources);
            return;
        }

        // Live external resources
        if (external)
        {
            var aggregator = serviceProvider.GetRequiredService<ExternalContentAggregator>();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  Fetching live external resources...");
            Console.ResetColor();

            try
            {
                var content = await aggregator.AggregateAsync();
                PrintExternalContent(content);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n  Failed to fetch external resources: {ex.Message}");
                Console.ResetColor();
            }
            return;
        }

        // Gap-based recommendations (default if nothing else specified)
        if (gaps || (!certs && !practice && string.IsNullOrEmpty(topic)))
        {
            var gapSkills = await LoadGapSkillsAsync();
            if (gapSkills.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No skill gaps detected. Run 'match' first to identify gaps,");
                Console.WriteLine("or use --topic <id> for a specific topic learning path.");
                Console.ResetColor();

                // Show available topics
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\nAvailable topic IDs:");
                Console.ResetColor();
                var allTopics = InterviewTopicBank.GetAllTopics();
                foreach (var t in allTopics)
                {
                    Console.WriteLine($"  {t.Id,-25} {t.Name}");
                }
                return;
            }

            var level = profile?.Experiences?.Count switch
            {
                >= 5 => "Senior",
                >= 2 => "Mid",
                _ => "Junior"
            };

            var plan = resourceEngine.RecommendForGaps(gapSkills, level);
            PrintResourcePlan(plan);
        }
    }

    private static void PrintLearningPath(TopicLearningPath path)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.WriteLine($"  Learning Path: {path.TopicName}");
        Console.WriteLine($"══════════════════════════════════════════════════════");
        Console.ResetColor();

        foreach (var phase in path.Phases)
        {
            var phaseColor = phase.Name switch
            {
                "Fundamentals" => ConsoleColor.Green,
                "Intermediate" => ConsoleColor.Yellow,
                "Advanced" => ConsoleColor.DarkYellow,
                _ => ConsoleColor.Red
            };

            Console.ForegroundColor = phaseColor;
            Console.WriteLine($"\n  ── {phase.Name} ({phase.EstimatedHours}h) ──");
            Console.ResetColor();

            if (phase.Prerequisites.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"     Prerequisites: {string.Join(", ", phase.Prerequisites)}");
                Console.ResetColor();
            }

            foreach (var link in phase.Resources)
            {
                var typeIcon = link.Type switch
                {
                    "Documentation" => "[DOC]",
                    "Course" => "[CRS]",
                    "Video" => "[VID]",
                    "Book" => "[BK] ",
                    "Practice" => "[PRC]",
                    "Article" => "[ART]",
                    "Tool" => "[TL] ",
                    _ => "[---]"
                };

                var freeTag = link.IsFree ? " FREE" : "";
                var langTag = link.Language == "uk" ? " (UA)" : "";

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"     {typeIcon} ");
                Console.ResetColor();
                Console.Write(link.Title);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(freeTag);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(langTag);
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"\n           {link.Url}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintResourcePlan(ResourcePlan plan)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.WriteLine($"          PERSONALIZED LEARNING PLAN                  ");
        Console.WriteLine($"══════════════════════════════════════════════════════");
        Console.ResetColor();

        Console.WriteLine($"\n  Total estimated hours: {plan.TotalEstimatedHours}h");
        Console.Write("  Priority: ");
        var prioColor = plan.Priority switch
        {
            "Critical" => ConsoleColor.Red,
            "High" => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };
        Console.ForegroundColor = prioColor;
        Console.WriteLine(plan.Priority);
        Console.ResetColor();

        if (plan.QuickWins.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  Quick Wins (< 8h): {string.Join(", ", plan.QuickWins)}");
            Console.ResetColor();
        }

        foreach (var rec in plan.Recommendations)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  {rec.Skill} ({rec.TopicArea}) — ~{rec.EstimatedHours}h [{rec.Priority}]");
            Console.ResetColor();

            foreach (var link in rec.Links.Take(4))
            {
                var freeTag = link.IsFree ? " FREE" : "";
                Console.Write($"    [{link.Type}] {link.Title}");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(freeTag);
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"\n         {link.Url}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintCertifications(List<CertificationRecommendation> certs)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.WriteLine($"          CERTIFICATION RECOMMENDATIONS               ");
        Console.WriteLine($"══════════════════════════════════════════════════════");
        Console.ResetColor();

        foreach (var cert in certs)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"\n  {cert.Name}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" ({cert.Provider})");
            Console.ResetColor();

            Console.Write("  Level: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(cert.Level);
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Salary impact: +{cert.SalaryImpactPercent:F0}%");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  {cert.Url}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Skills: {string.Join(", ", cert.RelevantSkills)}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintPractice(List<PracticeResource> resources)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.WriteLine($"          PRACTICE PLATFORMS                           ");
        Console.WriteLine($"══════════════════════════════════════════════════════");
        Console.ResetColor();

        foreach (var res in resources)
        {
            var freeTag = res.IsFree ? "FREE" : "PAID";
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"\n  {res.Platform}");
            Console.ForegroundColor = res.IsFree ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine($" [{freeTag}]");
            Console.ResetColor();

            Console.WriteLine($"  {res.Description}");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  {res.Url}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Skills: {string.Join(", ", res.Skills)}");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintExternalContent(ExternalContentResult content)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n══════════════════════════════════════════════════════");
        Console.WriteLine($"          EXTERNAL LEARNING RESOURCES (Live)          ");
        Console.WriteLine($"══════════════════════════════════════════════════════");
        Console.ResetColor();

        // Microsoft Learn Modules
        if (content.MsLearnModules.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  ── Microsoft Learn Modules ──");
            Console.ResetColor();

            foreach (var module in content.MsLearnModules.Take(8))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  [MOD] {module.Title}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" ({module.DurationMinutes}min)");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" FREE");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"        {module.Url}");
                Console.ResetColor();
            }
        }

        // GitHub Trending
        if (content.GitHubTrending.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  ── GitHub Trending (.NET) ──");
            Console.ResetColor();

            foreach (var repo in content.GitHubTrending.Take(8))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  [GH]  {repo.Name}");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ★ {repo.Stars:N0}");
                Console.ResetColor();

                if (!string.IsNullOrEmpty(repo.Description))
                {
                    var desc = repo.Description.Length > 80
                        ? repo.Description[..77] + "..."
                        : repo.Description;
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"        {desc}");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"        {repo.Url}");
                Console.ResetColor();
            }
        }

        // Blog Posts
        if (content.BlogPosts.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  ── Recent .NET Blog Posts ──");
            Console.ResetColor();

            foreach (var post in content.BlogPosts.Take(8))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  [ART] {post.Title}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" ({post.Source}, {post.Published:MMM d})");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"        {post.Url}");
                Console.ResetColor();
            }
        }

        // Stack Overflow
        if (content.StackOverflowTrending.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n  ── Stack Overflow Hot Questions ──");
            Console.ResetColor();

            foreach (var q in content.StackOverflowTrending.Take(8))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"  [SO]  {q.Title}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" (Score: {q.Score}, {q.AnswerCount} answers)");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine($"        {q.Url}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  Fetched at: {content.FetchedAt:yyyy-MM-dd HH:mm UTC}");
        Console.WriteLine($"══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static async Task<List<string>> LoadGapSkillsAsync()
    {
        // Try to load from latest match results
        if (!Directory.Exists(Program.DataDirectory))
            return [];

        var matchFiles = Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (matchFiles is null)
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(matchFiles);
            var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            // Extract most commonly missing skills
            return vacancies
                .Where(v => v.MatchScore?.MissingSkills.Count > 0)
                .SelectMany(v => v.MatchScore!.MissingSkills)
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(15)
                .Select(g => g.Key)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
