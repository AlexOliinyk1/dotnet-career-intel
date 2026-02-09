using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using CareerIntel.Persistence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that extracts job vacancies from screenshot images (LinkedIn, job boards) using OCR.
/// Detects remote-from-Ukraine eligibility for each extracted vacancy.
/// Usage: career-intel scan-image [--image path] [--dir path] [--pattern *.png] [--output path]
/// </summary>
public static class ScanImageCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static Command Create()
    {
        var imageOption = new Option<string?>(
            "--image",
            description: "Path to a single image file to scan (PNG, JPG, BMP, TIFF)");

        var dirOption = new Option<string?>(
            "--dir",
            description: "Path to a directory of screenshot images to scan");

        var patternOption = new Option<string>(
            "--pattern",
            getDefaultValue: () => "*.*",
            description: "File pattern when scanning a directory (e.g., '*.png')");

        var outputOption = new Option<string?>(
            "--output",
            description: "Output file path for extracted vacancies JSON");

        var command = new Command("scan-image",
            "Extract job vacancies from screenshot images (LinkedIn, job boards) using OCR")
        {
            imageOption,
            dirOption,
            patternOption,
            outputOption
        };

        command.SetHandler(ExecuteAsync, imageOption, dirOption, patternOption, outputOption);
        return command;
    }

    private static async Task ExecuteAsync(string? image, string? dir, string pattern, string? output)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var scanner = serviceProvider.GetRequiredService<VacancyImageScanner>();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine("          IMAGE VACANCY SCANNER (OCR)                ");
        Console.WriteLine("══════════════════════════════════════════════════════");
        Console.ResetColor();

        var allResults = new List<ImageScanResult>();

        if (!string.IsNullOrEmpty(image))
        {
            if (!File.Exists(image))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  File not found: {image}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"\n  Scanning: {image}");
            var result = scanner.ScanImage(image);
            allResults.Add(result);
            PrintScanResult(result);
        }
        else if (!string.IsNullOrEmpty(dir))
        {
            if (!Directory.Exists(dir))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n  Directory not found: {dir}");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"\n  Scanning directory: {dir} (pattern: {pattern})");
            var summary = scanner.ScanDirectory(dir, pattern);

            foreach (var result in summary.Results)
            {
                allResults.Add(result);
                PrintScanResult(result);
            }

            PrintSummary(summary);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n  Usage: scan-image --image <path>  or  scan-image --dir <path>");
            Console.WriteLine("  Extracts job vacancies from screenshot images using OCR.");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n  Supported formats: PNG, JPG, BMP, TIFF");
            Console.WriteLine("  Requires tessdata/eng.traineddata in the application directory.");
            Console.ResetColor();
            return;
        }

        // Save results to JSON
        var allVacancies = allResults.SelectMany(r => r.Vacancies).ToList();
        if (allVacancies.Count > 0)
        {
            var outputPath = output ?? Path.Combine(Program.DataDirectory,
                $"image-vacancies-{DateTime.Now:yyyy-MM-dd-HHmmss}.json");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Program.DataDirectory);
            var json = JsonSerializer.Serialize(allVacancies, JsonOptions);
            await File.WriteAllTextAsync(outputPath, json);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  Results saved to: {outputPath}");
            Console.ResetColor();

            // Persist to database
            try
            {
                await Program.EnsureDatabaseAsync(serviceProvider);
                var repo = serviceProvider.GetRequiredService<VacancyRepository>();
                await repo.SaveVacanciesAsync(allVacancies);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  Persisted {allVacancies.Count} vacancies to database.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Database save skipped: {ex.Message}");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.ResetColor();
    }

    private static void PrintScanResult(ImageScanResult result)
    {
        var fileName = Path.GetFileName(result.ImagePath);
        Console.Write($"\n  {fileName}: ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"OCR confidence {result.OcrConfidence:F1}%");
        Console.ResetColor();

        if (result.Warnings.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            foreach (var warning in result.Warnings)
                Console.WriteLine($"    ! {warning}");
            Console.ResetColor();
        }

        if (result.Vacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    No vacancies extracted");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"    Extracted {result.Vacancies.Count} vacancy listing(s)");
        Console.ResetColor();

        for (var i = 0; i < result.Vacancies.Count; i++)
        {
            var vacancy = result.Vacancies[i];
            var assessment = i < result.Assessments.Count ? result.Assessments[i] : null;

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"\n    [{i + 1}] {vacancy.Title}");
            Console.ResetColor();

            if (!string.IsNullOrEmpty(vacancy.Company))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($" at {vacancy.Company}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // Location
            var location = !string.IsNullOrEmpty(vacancy.City) && !string.IsNullOrEmpty(vacancy.Country)
                ? $"{vacancy.City}, {vacancy.Country}"
                : !string.IsNullOrEmpty(vacancy.Country) ? vacancy.Country
                : !string.IsNullOrEmpty(vacancy.City) ? vacancy.City
                : "Unknown";
            Console.Write($"        Location: {location}");

            // Remote
            Console.Write(" | Remote: ");
            var remoteColor = vacancy.RemotePolicy switch
            {
                Core.Enums.RemotePolicy.FullyRemote => ConsoleColor.Green,
                Core.Enums.RemotePolicy.RemoteFriendly => ConsoleColor.Yellow,
                Core.Enums.RemotePolicy.Hybrid => ConsoleColor.DarkYellow,
                Core.Enums.RemotePolicy.OnSite => ConsoleColor.Red,
                _ => ConsoleColor.DarkGray
            };
            Console.ForegroundColor = remoteColor;
            Console.Write(vacancy.RemotePolicy);
            Console.ResetColor();

            // Engagement type
            if (vacancy.EngagementType != Core.Enums.EngagementType.Unknown)
            {
                Console.Write($" | Type: {vacancy.EngagementType}");
            }

            // Salary
            if (vacancy.SalaryMin.HasValue || vacancy.SalaryMax.HasValue)
            {
                var min = vacancy.SalaryMin.HasValue ? $"${vacancy.SalaryMin:N0}" : "?";
                var max = vacancy.SalaryMax.HasValue ? $"${vacancy.SalaryMax:N0}" : "?";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($" | Salary: {min}-{max}");
                Console.ResetColor();
            }
            Console.WriteLine();

            // Eligibility
            if (assessment is not null)
            {
                Console.Write("        Eligibility: ");
                if (assessment.IsEligible)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("PASS");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("FAIL");
                    Console.ResetColor();
                    foreach (var rule in assessment.Rules.Where(r => !r.Passed))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"          - {rule.RuleName}: {rule.Reason}");
                        Console.ResetColor();
                    }
                }
            }
        }
    }

    private static void PrintSummary(ImageScanSummary summary)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n  ═══ Summary ═══");
        Console.ResetColor();

        Console.WriteLine($"  Images scanned:  {summary.ImagesScanned}");
        Console.WriteLine($"  Vacancies found: {summary.TotalVacancies}");

        if (summary.TotalVacancies > 0)
        {
            var eligiblePct = (double)summary.Eligible / summary.TotalVacancies * 100;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"  Eligible:        {summary.Eligible}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" ({eligiblePct:F1}%)");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"  Ineligible:      {summary.Ineligible}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($" ({100 - eligiblePct:F1}%)");
            Console.ResetColor();
        }
    }
}
