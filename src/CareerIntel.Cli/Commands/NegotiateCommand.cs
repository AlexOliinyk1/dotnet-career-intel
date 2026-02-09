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
/// CLI command that analyzes a salary offer and generates a negotiation strategy.
/// Usage: career-intel negotiate --company "X" --role "Senior .NET" --salary 8000 [--currency USD] [--type B2B]
/// </summary>
public static class NegotiateCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create()
    {
        var companyOption = new Option<string>(
            "--company",
            description: "Company name that made the offer")
        { IsRequired = true };

        var roleOption = new Option<string>(
            "--role",
            description: "Role title for the offer")
        { IsRequired = true };

        var salaryOption = new Option<decimal>(
            "--salary",
            description: "Offered salary amount")
        { IsRequired = true };

        var currencyOption = new Option<string>(
            "--currency",
            getDefaultValue: () => "USD",
            description: "Currency of the salary offer");

        var typeOption = new Option<string>(
            "--type",
            getDefaultValue: () => "Employment",
            description: "Engagement type (Employment, B2B, Contract)");

        var command = new Command("negotiate", "Analyze a salary offer and generate a negotiation strategy")
        {
            companyOption,
            roleOption,
            salaryOption,
            currencyOption,
            typeOption
        };

        command.SetHandler(ExecuteAsync, companyOption, roleOption, salaryOption, currencyOption, typeOption);

        return command;
    }

    private static async Task ExecuteAsync(
        string company, string role, decimal salary, string currency, string type)
    {
        using var serviceProvider = Program.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CareerIntel.Cli.NegotiateCommand");
        var negotiationEngine = serviceProvider.GetRequiredService<NegotiationEngine>();

        // Build NegotiationState from arguments
        var negotiation = new NegotiationState
        {
            Company = company,
            Role = role,
            OfferedSalary = salary,
            OfferedCurrency = currency,
            EngagementType = type,
            Status = "Pending",
            ReceivedDate = DateTimeOffset.UtcNow
        };

        // Load existing negotiations
        var negotiationsPath = Path.Combine(Program.DataDirectory, "negotiations.json");
        var allNegotiations = await LoadNegotiationsAsync(negotiationsPath);

        // Assign incremental ID
        negotiation.Id = allNegotiations.Count > 0 ? allNegotiations.Max(n => n.Id) + 1 : 1;

        // Get active offers for leverage analysis
        var activeOffers = allNegotiations
            .Where(n => n.Status is "Pending" or "Negotiating")
            .ToList();

        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        var profile = new UserProfile();
        if (File.Exists(profilePath))
        {
            var profileJson = await File.ReadAllTextAsync(profilePath);
            profile = JsonSerializer.Deserialize<UserProfile>(profileJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UserProfile();
        }

        // Load market data (vacancies) for salary benchmarking
        var vacanciesPath = FindLatestVacanciesFile();
        var marketData = new List<JobVacancy>();
        if (!string.IsNullOrEmpty(vacanciesPath) && File.Exists(vacanciesPath))
        {
            var vacanciesJson = await File.ReadAllTextAsync(vacanciesPath);
            marketData = JsonSerializer.Deserialize<List<JobVacancy>>(vacanciesJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }

        // Run negotiation analysis
        logger.LogInformation("Analyzing offer from {Company} for {Role}: {Currency} {Salary}",
            company, role, currency, salary);

        var strategy = negotiationEngine.AnalyzeOffer(negotiation, activeOffers, profile, marketData);

        // Save the negotiation with strategy info
        negotiation.CounterOffer = strategy.SuggestedCounter;
        negotiation.Recommendation = strategy.OverallAssessment;
        negotiation.Leverage = strategy.LeveragePoints;
        allNegotiations.Add(negotiation);

        var json = JsonSerializer.Serialize(allNegotiations, JsonOptions);
        await File.WriteAllTextAsync(negotiationsPath, json);

        // Persist to SQLite database
        try
        {
            await Program.EnsureDatabaseAsync(serviceProvider);
            var negotiationRepo = serviceProvider.GetRequiredService<NegotiationRepository>();
            await negotiationRepo.SaveAsync(negotiation);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Persisted negotiation #{negotiation.Id} to database.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist negotiation to database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning: DB persistence failed: {ex.Message}");
            Console.ResetColor();
        }

        // Print results
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("              NEGOTIATION STRATEGY                         ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        // Offer summary
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Company:    {company}");
        Console.WriteLine($"  Role:       {role}");
        Console.WriteLine($"  Offered:    {currency} {salary:N0} ({type})");
        Console.ResetColor();
        Console.WriteLine();

        // Assessment
        var assessmentColor = strategy.OverallAssessment switch
        {
            "Below market" => ConsoleColor.Red,
            "At market" => ConsoleColor.Yellow,
            "Above market" => ConsoleColor.Green,
            _ => ConsoleColor.White
        };

        Console.Write("  Assessment: ");
        Console.ForegroundColor = assessmentColor;
        Console.WriteLine(strategy.OverallAssessment);
        Console.ResetColor();

        // Should negotiate
        Console.Write("  Negotiate:  ");
        Console.ForegroundColor = strategy.ShouldNegotiate ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(strategy.ShouldNegotiate ? "YES - Room for negotiation" : "NO - Offer is competitive");
        Console.ResetColor();

        // Suggested counter
        if (strategy.ShouldNegotiate && strategy.SuggestedCounter > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Suggested Counter: {currency} {strategy.SuggestedCounter:N0}");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Justification: {strategy.CounterJustification}");
            Console.ResetColor();
        }

        // BATNA
        if (strategy.BatnaValue > 0)
        {
            Console.WriteLine();
            Console.Write("  BATNA (Best Alternative): ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{currency} {strategy.BatnaValue:N0}");
            Console.ResetColor();
        }

        // Leverage points
        if (strategy.LeveragePoints.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Leverage Points:");
            Console.ResetColor();
            foreach (var point in strategy.LeveragePoints)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"    + {point}");
                Console.ResetColor();
            }
        }

        // Negotiation script
        if (!string.IsNullOrEmpty(strategy.NegotiationScript))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Negotiation Script:");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.White;

            // Word-wrap the script for readability
            var lines = strategy.NegotiationScript.Split('\n');
            foreach (var line in lines)
            {
                Console.WriteLine($"    {line.Trim()}");
            }

            Console.ResetColor();
        }

        // Risk assessment
        if (!string.IsNullOrEmpty(strategy.RiskAssessment))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  Risk: ");
            Console.ResetColor();
            Console.WriteLine(strategy.RiskAssessment);
        }

        // Active offers summary
        if (activeOffers.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  Other Active Offers:");
            Console.ResetColor();
            foreach (var offer in activeOffers)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    {offer.Company} - {offer.Role}: {offer.OfferedCurrency} {offer.OfferedSalary:N0} ({offer.Status})");
                Console.ResetColor();
            }
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Saved to: {negotiationsPath}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("==========================================================");
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

    private static async Task<List<NegotiationState>> LoadNegotiationsAsync(string path)
    {
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<NegotiationState>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }
}
