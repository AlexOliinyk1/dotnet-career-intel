using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for side-by-side comparison of multiple job offers.
/// Calculates total compensation including base, bonus, equity, benefits, and cost of living adjustments.
/// Usage: career-intel compare-offers [--offers-file path]
/// </summary>
public static class CompareOffersCommand
{
    public static Command Create()
    {
        var offersFileOption = new Option<string?>(
            "--offers-file",
            description: "Path to offers.json file. Defaults to data/offers.json");

        var command = new Command("compare-offers", "Compare multiple job offers side-by-side with total comp analysis")
        {
            offersFileOption
        };

        command.SetHandler(ExecuteAsync, offersFileOption);

        return command;
    }

    private static async Task ExecuteAsync(string? offersFilePath)
    {
        var filePath = offersFilePath ?? Path.Combine(Program.DataDirectory, "offers.json");

        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Offers file not found: {filePath}");
            Console.WriteLine("\nCreating example offers.json template...");
            Console.ResetColor();

            await CreateExampleOffersFileAsync(filePath);
            return;
        }

        var json = await File.ReadAllTextAsync(filePath);
        var offersData = JsonSerializer.Deserialize<OffersData>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (offersData?.Offers == null || offersData.Offers.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No offers found in the file.");
            Console.ResetColor();
            return;
        }

        PrintOfferComparison(offersData.Offers);
    }

    private static void PrintOfferComparison(List<JobOffer> offers)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n══════════════════════════════════════════════════════════");
        Console.WriteLine("              JOB OFFER COMPARISON TOOL                   ");
        Console.WriteLine("══════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();

        // Print summary table
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{"Offer",-25} {"Base",-15} {"Total Comp",-15} {"Score",-10}");
        Console.WriteLine(new string('─', 70));
        Console.ResetColor();

        foreach (var offer in offers.OrderByDescending(o => o.TotalCompensation))
        {
            var totalComp = offer.TotalCompensation;
            var color = totalComp >= 150000 ? ConsoleColor.Green :
                       totalComp >= 100000 ? ConsoleColor.Yellow : ConsoleColor.DarkYellow;

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{Truncate(offer.Company, 24),-25}");

            Console.ForegroundColor = color;
            Console.Write($"${offer.BaseSalary:N0}".PadLeft(15));
            Console.Write($"${totalComp:N0}".PadLeft(15));

            var score = CalculateOfferScore(offer);
            Console.ForegroundColor = score >= 80 ? ConsoleColor.Green :
                                     score >= 60 ? ConsoleColor.Yellow : ConsoleColor.Red;
            Console.WriteLine($"{score}/100".PadLeft(10));
            Console.ResetColor();
        }

        Console.WriteLine();

        // Detailed comparison
        foreach (var offer in offers.OrderByDescending(o => o.TotalCompensation))
        {
            PrintDetailedOffer(offer);
        }

        // Recommendation
        PrintRecommendation(offers);
    }

    private static void PrintDetailedOffer(JobOffer offer)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ {offer.Company} - {offer.Title} ═══");
        Console.ResetColor();

        // Compensation breakdown
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nCompensation Breakdown:");
        Console.ResetColor();

        Console.WriteLine($"  Base Salary:          ${offer.BaseSalary:N0} / year");

        if (offer.SigningBonus > 0)
            Console.WriteLine($"  Signing Bonus:        ${offer.SigningBonus:N0}");

        if (offer.AnnualBonus > 0)
            Console.WriteLine($"  Annual Bonus:         ${offer.AnnualBonus:N0} ({offer.AnnualBonus / offer.BaseSalary * 100:F0}% of base)");

        if (offer.EquityValue > 0)
        {
            var annualEquity = offer.EquityValue / offer.EquityVestingYears;
            Console.WriteLine($"  Equity (RSUs/Options): ${offer.EquityValue:N0} over {offer.EquityVestingYears} years (${annualEquity:N0}/year)");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Total Year 1 Comp:    ${offer.TotalCompensation:N0}");
        Console.ResetColor();

        // Benefits
        if (!string.IsNullOrEmpty(offer.Benefits))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\nBenefits:");
            Console.ResetColor();
            Console.WriteLine($"  {offer.Benefits}");
        }

        // Work arrangement
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nWork Arrangement:");
        Console.ResetColor();
        Console.WriteLine($"  Remote Policy:        {offer.RemotePolicy}");
        Console.WriteLine($"  Location:             {offer.Location}");

        if (offer.CostOfLivingMultiplier != 1.0m)
        {
            var adjustedSalary = offer.BaseSalary / offer.CostOfLivingMultiplier;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  COL Adjusted Salary:  ${adjustedSalary:N0} (multiplier: {offer.CostOfLivingMultiplier:F2}x)");
            Console.ResetColor();
        }

        // Pros/Cons
        if (offer.Pros.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nPros:");
            foreach (var pro in offer.Pros)
            {
                Console.WriteLine($"  + {pro}");
            }
            Console.ResetColor();
        }

        if (offer.Cons.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nCons:");
            foreach (var con in offer.Cons)
            {
                Console.WriteLine($"  - {con}");
            }
            Console.ResetColor();
        }

        // Score
        var score = CalculateOfferScore(offer);
        var scoreColor = score >= 80 ? ConsoleColor.Green :
                        score >= 60 ? ConsoleColor.Yellow : ConsoleColor.Red;

        Console.ForegroundColor = scoreColor;
        Console.WriteLine($"\nOverall Score: {score}/100");
        Console.ResetColor();
    }

    private static void PrintRecommendation(List<JobOffer> offers)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ Recommendation ═══");
        Console.ResetColor();

        var bestByComp = offers.OrderByDescending(o => o.TotalCompensation).First();
        var bestByScore = offers.OrderByDescending(o => CalculateOfferScore(o)).First();
        var bestByCOL = offers.OrderByDescending(o => o.BaseSalary / o.CostOfLivingMultiplier).First();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nHighest Total Comp:      {bestByComp.Company} (${bestByComp.TotalCompensation:N0})");
        Console.WriteLine($"Best Overall Score:      {bestByScore.Company} ({CalculateOfferScore(bestByScore)}/100)");
        Console.WriteLine($"Best COL-Adjusted:       {bestByCOL.Company} (${bestByCOL.BaseSalary / bestByCOL.CostOfLivingMultiplier:N0})");
        Console.ResetColor();

        if (bestByScore == bestByComp)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n→ Recommendation: {bestByScore.Company} leads in both compensation and overall score.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n→ Trade-off decision:");
            Console.WriteLine($"   {bestByComp.Company} offers higher comp but {bestByScore.Company} has better overall fit.");
            Console.WriteLine($"   Consider: Do you prioritize money or work-life balance/growth?");
            Console.ResetColor();
        }
    }

    private static int CalculateOfferScore(JobOffer offer)
    {
        var score = 0;

        // Compensation (40 points max)
        var compScore = Math.Min(40, (int)(offer.TotalCompensation / 5000));
        score += compScore;

        // Remote policy (20 points max)
        score += offer.RemotePolicy switch
        {
            "Fully Remote" => 20,
            "Hybrid" => 15,
            "On-site with flexibility" => 10,
            _ => 0
        };

        // Benefits (10 points)
        if (!string.IsNullOrEmpty(offer.Benefits))
        {
            score += offer.Benefits.Length > 100 ? 10 : 5;
        }

        // Growth potential (15 points)
        score += offer.GrowthPotential switch
        {
            "Excellent" => 15,
            "Good" => 10,
            "Average" => 5,
            _ => 0
        };

        // Work-life balance (15 points)
        score += offer.WorkLifeBalance switch
        {
            "Excellent" => 15,
            "Good" => 10,
            "Average" => 5,
            _ => 0
        };

        return Math.Clamp(score, 0, 100);
    }

    private static async Task CreateExampleOffersFileAsync(string filePath)
    {
        var exampleOffers = new OffersData
        {
            Offers =
            [
                new JobOffer
                {
                    Company = "Google",
                    Title = "Senior Software Engineer",
                    BaseSalary = 180000,
                    SigningBonus = 50000,
                    AnnualBonus = 36000,
                    EquityValue = 400000,
                    EquityVestingYears = 4,
                    Location = "Mountain View, CA",
                    RemotePolicy = "Hybrid",
                    CostOfLivingMultiplier = 1.8m,
                    Benefits = "Excellent health insurance, unlimited PTO, 401k matching, free meals, gym",
                    GrowthPotential = "Excellent",
                    WorkLifeBalance = "Good",
                    Pros = ["Top-tier compensation", "Great learning opportunities", "Prestige"],
                    Cons = ["High COL", "Competitive environment", "Bureaucracy"]
                },
                new JobOffer
                {
                    Company = "Startup XYZ",
                    Title = "Lead Backend Engineer",
                    BaseSalary = 140000,
                    SigningBonus = 10000,
                    AnnualBonus = 14000,
                    EquityValue = 200000,
                    EquityVestingYears = 4,
                    Location = "Remote",
                    RemotePolicy = "Fully Remote",
                    CostOfLivingMultiplier = 1.0m,
                    Benefits = "Health insurance, flexible PTO, home office stipend",
                    GrowthPotential = "Excellent",
                    WorkLifeBalance = "Average",
                    Pros = ["High equity upside", "Fully remote", "High impact", "Flexibility"],
                    Cons = ["Startup risk", "Long hours", "Less structure"]
                },
                new JobOffer
                {
                    Company = "European SaaS Co",
                    Title = "Senior .NET Developer",
                    BaseSalary = 95000,
                    SigningBonus = 0,
                    AnnualBonus = 9500,
                    EquityValue = 0,
                    EquityVestingYears = 0,
                    Location = "Berlin, Germany",
                    RemotePolicy = "Hybrid",
                    CostOfLivingMultiplier = 1.2m,
                    Benefits = "Excellent health, 30 days vacation, pension, relocation support",
                    GrowthPotential = "Good",
                    WorkLifeBalance = "Excellent",
                    Pros = ["Work-life balance", "Vacation time", "Stable", "Europe lifestyle"],
                    Cons = ["Lower compensation", "Slower pace", "Relocation required"]
                }
            ]
        };

        var json = JsonSerializer.Serialize(exampleOffers, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, json);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Created example file: {filePath}");
        Console.WriteLine("\nEdit this file with your actual offers, then run:");
        Console.WriteLine("  career-intel compare-offers");
        Console.ResetColor();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 2)] + "..";

    private sealed class OffersData
    {
        public List<JobOffer> Offers { get; set; } = [];
    }

    private sealed class JobOffer
    {
        public string Company { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal BaseSalary { get; set; }
        public decimal SigningBonus { get; set; }
        public decimal AnnualBonus { get; set; }
        public decimal EquityValue { get; set; }
        public int EquityVestingYears { get; set; }
        public string Location { get; set; } = string.Empty;
        public string RemotePolicy { get; set; } = "On-site";
        public decimal CostOfLivingMultiplier { get; set; } = 1.0m;
        public string Benefits { get; set; } = string.Empty;
        public string GrowthPotential { get; set; } = "Average";
        public string WorkLifeBalance { get; set; } = "Average";
        public List<string> Pros { get; set; } = [];
        public List<string> Cons { get; set; } = [];

        public decimal TotalCompensation =>
            BaseSalary +
            SigningBonus +
            AnnualBonus +
            (EquityVestingYears > 0 ? EquityValue / EquityVestingYears : 0);
    }
}
