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
/// CLI command that generates a portfolio project idea based on skill gaps.
/// Usage: career-intel portfolio [--input path]
/// </summary>
public static class PortfolioCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var command = new Command("portfolio", "Generate a portfolio project idea based on skill gaps from match results")
        {
            inputOption
        };

        command.SetHandler(ExecuteAsync, inputOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.PortfolioCommand");
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        var portfolioGenerator = serviceProvider.GetRequiredService<PortfolioGenerator>();

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

        var profileJson = await File.ReadAllTextAsync(profilePath);
        var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new UserProfile();

        // Load vacancies
        var inputPath = input ?? FindLatestVacanciesFile();
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        var vacanciesJson = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(vacanciesJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        // Compute match scores to identify skill gaps
        var ranked = matchEngine.RankVacancies(vacancies, 0);
        var allMissingSkillNames = ranked
            .Where(v => v.MatchScore is not null)
            .SelectMany(v => v.MatchScore!.MissingSkills)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        if (allMissingSkillNames.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("No skill gaps detected! Your profile covers all required skills.");
            Console.ResetColor();
            return;
        }

        // Convert to SkillGap objects
        var skillGaps = allMissingSkillNames
            .Select(name => new SkillGap
            {
                SkillName = name,
                CurrentLevel = 0,
                RequiredLevel = 3,
                ImpactWeight = 1.0,
                RecommendedAction = "Learn and build projects"
            })
            .ToList();

        logger.LogInformation("Generating portfolio project for {Count} skill gaps", skillGaps.Count);

        // Generate portfolio project
        var project = portfolioGenerator.GenerateProject(skillGaps, profile, vacancies);

        // Print project details
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("              PORTFOLIO PROJECT GENERATOR                  ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // Title
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Project: {project.Title}");
        Console.ResetColor();

        // Complexity
        var complexityColor = project.Complexity switch
        {
            "Complex" => ConsoleColor.Red,
            "Medium" => ConsoleColor.Yellow,
            _ => ConsoleColor.Green
        };
        Console.Write("  Complexity: ");
        Console.ForegroundColor = complexityColor;
        Console.WriteLine(project.Complexity);
        Console.ResetColor();

        // Problem statement
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Problem Statement:");
        Console.ResetColor();
        Console.WriteLine($"    {project.ProblemStatement}");

        // Architecture
        if (!string.IsNullOrEmpty(project.Architecture))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Architecture:");
            Console.ResetColor();
            var archLines = project.Architecture.Split('\n');
            foreach (var line in archLines)
            {
                Console.WriteLine($"    {line.Trim()}");
            }
        }

        // Tech stack
        if (project.TechStack.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Tech Stack:");
            Console.ResetColor();
            foreach (var tech in project.TechStack)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"    - {tech}");
                Console.ResetColor();
            }
        }

        // Target skill gaps
        if (project.TargetSkillGaps.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Closes Skill Gaps:");
            Console.ResetColor();
            foreach (var gap in project.TargetSkillGaps)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    -> {gap}");
                Console.ResetColor();
            }
        }

        // Backlog
        if (project.Backlog.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Backlog:");
            Console.ResetColor();
            for (var i = 0; i < project.Backlog.Count; i++)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"    {i + 1}. {project.Backlog[i]}");
                Console.ResetColor();
            }
        }

        // Interview narrative
        if (!string.IsNullOrEmpty(project.InterviewNarrative))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Interview Narrative:");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    \"{project.InterviewNarrative}\"");
            Console.ResetColor();
        }

        // Save as markdown file
        var portfolioDir = Path.Combine(Program.DataDirectory, "portfolio");
        if (!Directory.Exists(portfolioDir))
        {
            Directory.CreateDirectory(portfolioDir);
        }

        var safeTitle = SanitizeFileName(project.Title);
        var mdPath = Path.Combine(portfolioDir, $"{safeTitle}.md");

        var readmeContent = GenerateReadmeContent(project);
        await File.WriteAllTextAsync(mdPath, readmeContent);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  README saved to: {mdPath}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("==========================================================");
        Console.ResetColor();
    }

    private static string GenerateReadmeContent(PortfolioProject project)
    {
        var content = $"""
            # {project.Title}

            > {project.ProblemStatement}

            ## Architecture

            {project.Architecture}

            ## Tech Stack

            {string.Join("\n", project.TechStack.Select(t => $"- {t}"))}

            ## Target Skill Gaps

            This project is designed to close the following skill gaps:

            {string.Join("\n", project.TargetSkillGaps.Select(g => $"- {g}"))}

            ## Backlog

            {string.Join("\n", project.Backlog.Select((b, i) => $"{i + 1}. {b}"))}

            ## Interview Narrative

            {project.InterviewNarrative}

            ---
            *Generated by CareerIntel on {project.CreatedDate:yyyy-MM-dd}*
            """;

        return content;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Select(c => invalidChars.Contains(c) ? '-' : c)
            .ToArray());

        return sanitized
            .Trim('-')
            .ToLowerInvariant()
            .Replace(' ', '-');
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
