using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command that discovers and displays Ukraine-friendly companies from vacancy data.
/// Usage: career-intel companies [--input path] [--min-vacancies n] [--tech-stack filter]
/// </summary>
public static class CompaniesCommand
{
    public static Command Create()
    {
        var inputOption = new Option<string?>(
            "--input",
            description: "Path to vacancies JSON file. Defaults to latest in data directory.");

        var minVacanciesOption = new Option<int>(
            "--min-vacancies",
            getDefaultValue: () => 2,
            description: "Minimum vacancy count to include a company");

        var techStackOption = new Option<string?>(
            "--tech-stack",
            description: "Filter by tech stack (comma-separated, e.g. \".NET,Azure\")");

        var command = new Command("companies", "Discover Ukraine-friendly companies from vacancy data")
        {
            inputOption,
            minVacanciesOption,
            techStackOption
        };

        command.SetHandler(ExecuteAsync, inputOption, minVacanciesOption, techStackOption);

        return command;
    }

    private static async Task ExecuteAsync(string? input, int minVacancies, string? techStack)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.CompaniesCommand");
        var discoveryEngine = serviceProvider.GetRequiredService<CompanyDiscoveryEngine>();

        // Resolve input file
        var inputPath = input ?? FindLatestVacanciesFile();

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No vacancies file found. Run 'scan' first or specify --input.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Loading vacancies from: {inputPath}");

        // Load vacancies
        var json = await File.ReadAllTextAsync(inputPath);
        var vacancies = JsonSerializer.Deserialize<List<JobVacancy>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        if (vacancies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: No vacancies found in the input file.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Discovering companies from {vacancies.Count} vacancies...\n");

        // Discover companies
        var discovered = discoveryEngine.DiscoverFromVacancies(vacancies);

        // Optionally filter by tech stack
        if (!string.IsNullOrWhiteSpace(techStack))
        {
            var skills = techStack.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            discovered = discoveryEngine.FilterByTechStack(discovered, skills);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Filtered by tech stack: {string.Join(", ", skills)}");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Get top companies above the minimum vacancy threshold
        var filtered = discovered
            .Where(c => c.VacancyCount >= minVacancies)
            .ToList();

        var topCompanies = discoveryEngine.GetTopCompanies(filtered);

        if (topCompanies.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No companies found with at least {minVacancies} vacancies.");
            Console.ResetColor();
            return;
        }

        // Display header
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("          UKRAINE-FRIENDLY COMPANIES DISCOVERY             ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // Table header
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"#",-4} {"Company",-28} {"Score",6} {"Vac",4} {"B2B",4} {"Rmt",4} {"UA",4}  {"Top Tech Stack"}");
        Console.WriteLine($"  {new string('-', 90)}");
        Console.ResetColor();

        for (var i = 0; i < topCompanies.Count; i++)
        {
            var company = topCompanies[i];
            PrintCompanyRow(i + 1, company);
        }

        // Summary
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Total companies discovered: {discovered.Count}");
        Console.WriteLine($"  Companies with >= {minVacancies} vacancies: {filtered.Count}");
        Console.WriteLine($"  Showing top {topCompanies.Count}");
        Console.ResetColor();

        var confirmedCount = topCompanies.Count(c => c.ConfirmedUkraineHiring);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Confirmed Ukraine hiring: {confirmedCount}");
        Console.ResetColor();

        // Save results
        var outputPath = Path.Combine(Program.DataDirectory, "ukraine-friendly-companies.json");
        var outputJson = JsonSerializer.Serialize(topCompanies, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(outputPath, outputJson);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Results saved to: {outputPath}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintCompanyRow(int rank, UkraineFriendlyCompany company)
    {
        var confirmedFlag = company.ConfirmedUkraineHiring ? "Yes" : " - ";
        var topTech = string.Join(", ", company.TechStack.Take(5));

        // Score color
        var score = company.B2BVacancyCount * 3
                  + company.RemoteVacancyCount * 2
                  + company.VacancyCount
                  + (company.ConfirmedUkraineHiring ? 10 : 0);

        var scoreColor = score switch
        {
            >= 20 => ConsoleColor.Green,
            >= 10 => ConsoleColor.Yellow,
            _ => ConsoleColor.White
        };

        Console.Write($"  {rank,-4} ");

        // Company name
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{Truncate(company.Name, 27),-28} ");

        // Score
        Console.ForegroundColor = scoreColor;
        Console.Write($"{score,6} ");
        Console.ResetColor();

        // Vacancy counts
        Console.Write($"{company.VacancyCount,4} ");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"{company.B2BVacancyCount,4} ");
        Console.ResetColor();

        Console.Write($"{company.RemoteVacancyCount,4} ");

        // Confirmed Ukraine flag
        Console.ForegroundColor = company.ConfirmedUkraineHiring ? ConsoleColor.Green : ConsoleColor.DarkGray;
        Console.Write($"{confirmedFlag,4}");
        Console.ResetColor();

        // Top tech stack
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write($"  {Truncate(topTech, 30)}");
        Console.ResetColor();

        Console.WriteLine();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 2), "..");

    private static string? FindLatestVacanciesFile()
    {
        if (!Directory.Exists(Program.DataDirectory))
            return null;

        return Directory.GetFiles(Program.DataDirectory, "vacancies-*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
