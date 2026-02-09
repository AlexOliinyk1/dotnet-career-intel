using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using CareerIntel.Resume;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that generates a tailored resume and cover letter for a specific vacancy.
/// Usage: career-intel resume --vacancy-id id [--input path] [--output path]
/// </summary>
public static class ResumeCommand
{
    public static Command Create()
    {
        var vacancyIdOption = new Option<string>(
            "--vacancy-id",
            description: "ID of the vacancy to tailor the resume for")
        { IsRequired = true };

        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var outputOption = new Option<string?>(
            "--output",
            description: "Output directory path. Defaults to data/output/");

        var command = new Command("resume", "Generate a tailored resume and cover letter for a specific vacancy")
        {
            vacancyIdOption,
            inputOption,
            outputOption
        };

        command.SetHandler(ExecuteAsync, vacancyIdOption, inputOption, outputOption);

        return command;
    }

    private static async Task ExecuteAsync(string vacancyId, string? input, string? output)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CareerIntel.Cli.ResumeCommand");
        var matchEngine = serviceProvider.GetRequiredService<IMatchEngine>();
        var resumeBuilder = serviceProvider.GetRequiredService<ResumeBuilder>();
        var atsTailorer = serviceProvider.GetRequiredService<AtsTailorer>();
        var coverLetterGen = serviceProvider.GetRequiredService<CoverLetterGenerator>();

        // Resolve input file
        var inputPath = input ?? FindLatestVacanciesFile();

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        // Check profile exists
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Profile not found at {profilePath}");
            Console.ResetColor();
            return;
        }

        // Load profile
        await matchEngine.ReloadProfileAsync();

        // Load vacancies and find the target
        var json = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        var vacancy = vacancies.FirstOrDefault(v =>
            v.Id.Equals(vacancyId, StringComparison.OrdinalIgnoreCase));

        if (vacancy is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Vacancy with ID '{vacancyId}' not found in {inputPath}");
            Console.ResetColor();

            // Show available IDs
            Console.WriteLine("\nAvailable vacancy IDs:");
            foreach (var v in vacancies.Take(20))
            {
                Console.WriteLine($"  {v.Id} - {v.Title} at {v.Company}");
            }
            if (vacancies.Count > 20)
            {
                Console.WriteLine($"  ... and {vacancies.Count - 20} more");
            }
            return;
        }

        Console.WriteLine($"Generating tailored documents for: {vacancy.Title} at {vacancy.Company}");

        // Compute match score
        var matchScore = matchEngine.ComputeMatch(vacancy);
        vacancy.MatchScore = matchScore;

        // Load the user profile for the resume builder
        var profileJson = await File.ReadAllTextAsync(profilePath);
        var profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new UserProfile();

        // Generate resume
        Console.WriteLine("\nBuilding tailored resume...");
        var resumeMarkdown = resumeBuilder.Build(profile, vacancy);

        // ATS tailoring
        Console.WriteLine("Applying ATS optimization...");
        var atsResult = atsTailorer.Tailor(resumeMarkdown, vacancy);

        // Generate cover letter
        Console.WriteLine("Generating cover letter...");
        var coverLetter = coverLetterGen.Generate(profile, vacancy);

        // Determine output directory
        var outputDir = output ?? Path.Combine(Program.DataDirectory, "output");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var companySafe = SanitizeFileName(vacancy.Company);
        var dateSuffix = DateTime.Now.ToString("yyyy-MM-dd");

        var resumePath = Path.Combine(outputDir, $"resume-{companySafe}-{dateSuffix}.md");
        var coverLetterPath = Path.Combine(outputDir, $"cover-letter-{companySafe}-{dateSuffix}.md");

        await File.WriteAllTextAsync(resumePath, atsResult.TailoredResume);
        await File.WriteAllTextAsync(coverLetterPath, coverLetter);

        // Print results
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Documents Generated ===");
        Console.ResetColor();
        Console.WriteLine($"  Resume:       {resumePath}");
        Console.WriteLine($"  Cover Letter: {coverLetterPath}");

        // Print changelog/customizations
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Customization Log ===");
        Console.ResetColor();

        Console.WriteLine($"  Match Score: {matchScore.OverallScore:F0}/100 ({matchScore.ActionLabel})");
        Console.WriteLine($"  Matching Skills: {string.Join(", ", matchScore.MatchingSkills)}");
        Console.WriteLine($"  Missing Skills: {string.Join(", ", matchScore.MissingSkills)}");

        // ATS report
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== ATS Optimization Report ===");
        Console.ResetColor();
        Console.WriteLine($"  Keywords Injected: {atsResult.InjectedKeywords.Count}");
        if (atsResult.InjectedKeywords.Count > 0)
        {
            Console.WriteLine($"    {string.Join(", ", atsResult.InjectedKeywords)}");
        }
        Console.WriteLine($"  Keywords Already Present: {atsResult.ExistingKeywords.Count}");
        Console.WriteLine($"  Keyword Density: {atsResult.KeywordDensityPercent:F1}%");
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Select(c => invalidChars.Contains(c) ? '-' : c)
            .ToArray());

        return sanitized.Trim('-').ToLowerInvariant();
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
