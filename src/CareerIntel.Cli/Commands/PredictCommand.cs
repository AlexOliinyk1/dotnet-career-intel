using System.CommandLine;
using System.Text.Json;
using CareerIntel.Core.Models;
using CareerIntel.Matching;

namespace CareerIntel.Cli.Commands;

/// <summary>
/// CLI command for predicting application success rates using historical data patterns.
/// Analyzes past application outcomes to forecast response likelihood for new opportunities.
/// Usage: career-intel predict [--vacancy-id id] [--company company-name]
/// </summary>
public static class PredictCommand
{
    public static Command Create()
    {
        var vacancyIdOption = new Option<string?>(
            "--vacancy-id",
            description: "Predict success rate for a specific vacancy by ID");

        var companyOption = new Option<string?>(
            "--company",
            description: "Predict success rate for a specific company");

        var command = new Command("predict", "Predict application response rates based on historical data")
        {
            vacancyIdOption,
            companyOption
        };

        command.SetHandler(ExecuteAsync, vacancyIdOption, companyOption);

        return command;
    }

    private static async Task ExecuteAsync(string? vacancyId, string? company)
    {
        // Load historical applications
        var applicationsPath = Path.Combine(Program.DataDirectory, "applications.json");
        if (!File.Exists(applicationsPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No historical application data found.");
            Console.WriteLine("Apply to jobs using 'career-intel apply' to build prediction models.");
            Console.ResetColor();
            return;
        }

        var applicationsJson = await File.ReadAllTextAsync(applicationsPath);
        var applications = JsonSerializer.Deserialize<List<JobApplication>>(applicationsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];

        if (applications.Count < 5)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Only {applications.Count} applications in history. Need at least 5 for meaningful predictions.");
            Console.ResetColor();
            return;
        }

        // Load profile
        var profilePath = Path.Combine(Program.DataDirectory, "my-profile.json");
        if (!File.Exists(profilePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Profile not found. Please create your profile first.");
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

        if (!string.IsNullOrEmpty(vacancyId))
        {
            await PredictForVacancy(vacancyId, applications, profile);
        }
        else if (!string.IsNullOrEmpty(company))
        {
            await PredictForCompany(company, applications, profile);
        }
        else
        {
            await ShowOverallPredictions(applications, profile);
        }
    }

    private static async Task PredictForVacancy(string vacancyId, List<JobApplication> applications, UserProfile profile)
    {
        // Load latest vacancies
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

        var vacancy = vacancies.FirstOrDefault(v => v.Id == vacancyId);
        if (vacancy == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Vacancy '{vacancyId}' not found.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ Response Rate Prediction: {vacancy.Title} at {vacancy.Company} ═══\n");
        Console.ResetColor();

        var prediction = BuildPrediction(vacancy, applications, profile);

        PrintPrediction(prediction, vacancy);
    }

    private static async Task PredictForCompany(string company, List<JobApplication> applications, UserProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══ Response Rate Prediction: {company} ═══\n");
        Console.ResetColor();

        var companyApplications = applications
            .Where(a => a.Company.Equals(company, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (companyApplications.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No historical applications to {company} found.");
            Console.WriteLine("\nBased on general market patterns:");
            Console.ResetColor();

            // Use general predictions
            var generalPrediction = BuildGeneralPrediction(applications, profile);
            PrintGeneralPrediction(generalPrediction);
            return;
        }

        // Company-specific statistics
        var responseRate = companyApplications.Count(a => a.Status >= ApplicationStatus.Viewed) * 100.0 / companyApplications.Count;
        var interviewRate = companyApplications.Count(a => a.Status >= ApplicationStatus.Interview) * 100.0 / companyApplications.Count;
        var offerRate = companyApplications.Count(a => a.Status == ApplicationStatus.Offer) * 100.0 / companyApplications.Count;

        Console.WriteLine($"Historical data from {companyApplications.Count} applications:\n");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Response Rate:  {responseRate:F1}%");
        Console.WriteLine($"  Interview Rate: {interviewRate:F1}%");
        Console.WriteLine($"  Offer Rate:     {offerRate:F1}%");
        Console.ResetColor();

        Console.WriteLine("\nFactors influencing your success:");

        var avgDaysToResponse = companyApplications
            .Where(a => a.Status >= ApplicationStatus.Viewed && a.ResponseDate.HasValue && a.AppliedDate.HasValue)
            .Select(a => (a.ResponseDate!.Value - a.AppliedDate!.Value).TotalDays)
            .DefaultIfEmpty(0)
            .Average();

        if (avgDaysToResponse > 0)
        {
            Console.WriteLine($"  • Average time to response: {avgDaysToResponse:F1} days");
        }

        var bestMatchScore = companyApplications
            .Where(a => a.Status >= ApplicationStatus.Interview)
            .Select(a => a.MatchScore)
            .DefaultIfEmpty(0)
            .Average();

        if (bestMatchScore > 0)
        {
            Console.WriteLine($"  • Interviews came from applications with match score ≥ {bestMatchScore:F0}%");
        }

        Console.WriteLine($"\n{GetCompanyRecommendation(responseRate, interviewRate)}");
    }

    private static async Task ShowOverallPredictions(List<JobApplication> applications, UserProfile profile)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n═══ Response Rate Predictions (Based on Your History) ═══\n");
        Console.ResetColor();

        var model = BuildPredictionModel(applications);

        PrintPredictionModel(model);

        Console.WriteLine("\n\nKey Success Factors:\n");

        // Analyze what worked
        var successfulApps = applications.Where(a => a.Status >= ApplicationStatus.Interview).ToList();
        var failedApps = applications.Where(a => a.Status < ApplicationStatus.Screening).ToList();

        if (successfulApps.Any() && failedApps.Any())
        {
            var avgSuccessScore = successfulApps.Average(a => a.MatchScore);
            var avgFailScore = failedApps.Average(a => a.MatchScore);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ Match Score Sweet Spot: ≥ {avgSuccessScore:F0}% (avg for interviews: {avgSuccessScore:F0}% vs rejected: {avgFailScore:F0}%)");
            Console.ResetColor();

            // Application timing
            var successfulDaysOfWeek = successfulApps
                .Where(a => a.AppliedDate.HasValue)
                .GroupBy(a => a.AppliedDate!.Value.DayOfWeek)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefault();

            if (successfulDaysOfWeek != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  ✓ Best Application Day: {successfulDaysOfWeek.Day} ({successfulDaysOfWeek.Count} successful apps)");
                Console.ResetColor();
            }
        }

        Console.WriteLine("\n\nRecommendations:\n");

        var overallResponseRate = applications.Count(a => a.Status >= ApplicationStatus.Viewed) * 100.0 / applications.Count;

        if (overallResponseRate < 20)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  • Your response rate is low. Focus on:");
            Console.WriteLine("    - Tailoring resume to each job (use 'career-intel resume')");
            Console.WriteLine("    - Applying to roles with match score > 70%");
            Console.WriteLine("    - Improving ATS keyword optimization");
            Console.ResetColor();
        }
        else if (overallResponseRate < 40)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  • Your response rate is average. To improve:");
            Console.WriteLine("    - Target companies where you have referrals");
            Console.WriteLine("    - Apply within first 48 hours of posting");
            Console.WriteLine("    - Customize cover letters for top targets");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  • Excellent response rate! Keep doing what you're doing.");
            Console.WriteLine("  • Consider being more selective to save time");
            Console.ResetColor();
        }
    }

    private static ResponsePrediction BuildPrediction(JobVacancy vacancy, List<JobApplication> applications, UserProfile profile)
    {
        var model = BuildPredictionModel(applications);

        // Calculate match score for this vacancy
        var matchScore = CalculateMatchScore(vacancy, profile);

        // Find similar applications
        var similarApps = applications
            .Where(a => Math.Abs(a.MatchScore - matchScore) < 15)
            .ToList();

        double baseResponseRate = model.OverallResponseRate;
        double adjustedRate = baseResponseRate;

        // Adjust based on match score
        if (matchScore >= 80)
            adjustedRate *= 1.5;
        else if (matchScore >= 60)
            adjustedRate *= 1.2;
        else if (matchScore < 40)
            adjustedRate *= 0.6;

        // Adjust based on seniority match (using profile experience)
        var yearsOfExperience = CalculateYearsOfExperience(profile);
        var requiredSeniorityRank = GetSeniorityRank(vacancy.SeniorityLevel);
        var profileSeniorityRank = yearsOfExperience < 2 ? 1 : yearsOfExperience < 5 ? 2 : yearsOfExperience < 8 ? 3 : 4;

        if (Math.Abs(requiredSeniorityRank - profileSeniorityRank) <= 1)
            adjustedRate *= 1.2;
        else if (Math.Abs(requiredSeniorityRank - profileSeniorityRank) > 2)
            adjustedRate *= 0.7;

        // Adjust based on remote policy
        if (vacancy.RemotePolicy == Core.Enums.RemotePolicy.FullyRemote && profile.Preferences.RemoteOnly)
            adjustedRate *= 1.1;

        // Adjust based on company size pattern
        var companyApps = applications.Where(a => a.Company.Equals(vacancy.Company, StringComparison.OrdinalIgnoreCase)).ToList();
        if (companyApps.Any())
        {
            var companyRate = companyApps.Count(a => a.Status >= ApplicationStatus.Viewed) * 100.0 / companyApps.Count;
            adjustedRate = (adjustedRate + companyRate) / 2; // Blend with company-specific rate
        }

        adjustedRate = Math.Min(adjustedRate, 95); // Cap at 95%

        return new ResponsePrediction
        {
            ResponseRate = adjustedRate,
            InterviewRate = adjustedRate * 0.4, // Rough estimate
            OfferRate = adjustedRate * 0.15, // Rough estimate
            MatchScore = matchScore,
            ConfidenceLevel = similarApps.Count >= 3 ? "High" : similarApps.Count >= 1 ? "Medium" : "Low",
            SimilarApplications = similarApps.Count,
            KeyFactors = DetermineKeyFactors(vacancy, profile, matchScore)
        };
    }

    private static PredictionModel BuildPredictionModel(List<JobApplication> applications)
    {
        var totalApps = applications.Count;
        var responded = applications.Count(a => a.Status >= ApplicationStatus.Viewed);
        var interviewed = applications.Count(a => a.Status >= ApplicationStatus.Interview);
        var offered = applications.Count(a => a.Status == ApplicationStatus.Offer);

        return new PredictionModel
        {
            TotalApplications = totalApps,
            OverallResponseRate = responded * 100.0 / totalApps,
            OverallInterviewRate = interviewed * 100.0 / totalApps,
            OverallOfferRate = offered * 100.0 / totalApps,
            AverageMatchScore = applications.Average(a => a.MatchScore),
            BestPerformingMatchRange = DetermineBestMatchRange(applications)
        };
    }

    private static string DetermineBestMatchRange(List<JobApplication> applications)
    {
        var ranges = new[]
        {
            (Range: "90-100%", Min: 90, Max: 100),
            (Range: "80-89%", Min: 80, Max: 89),
            (Range: "70-79%", Min: 70, Max: 79),
            (Range: "60-69%", Min: 60, Max: 69),
            (Range: "<60%", Min: 0, Max: 59)
        };

        var bestRange = ranges
            .Select(r => new
            {
                r.Range,
                Apps = applications.Where(a => a.MatchScore >= r.Min && a.MatchScore <= r.Max).ToList()
            })
            .Where(x => x.Apps.Any())
            .Select(x => new
            {
                x.Range,
                SuccessRate = x.Apps.Count(a => a.Status >= ApplicationStatus.Interview) * 100.0 / x.Apps.Count
            })
            .OrderByDescending(x => x.SuccessRate)
            .FirstOrDefault();

        return bestRange?.Range ?? "N/A";
    }

    private static int CalculateMatchScore(JobVacancy vacancy, UserProfile profile)
    {
        var userSkills = profile.Skills.Select(s => s.SkillName.ToLowerInvariant()).ToHashSet();
        var requiredSkills = vacancy.RequiredSkills.Select(s => s.ToLowerInvariant()).ToList();
        var preferredSkills = vacancy.PreferredSkills.Select(s => s.ToLowerInvariant()).ToList();

        var matchedRequired = requiredSkills.Count(s => userSkills.Contains(s));
        var matchedPreferred = preferredSkills.Count(s => userSkills.Contains(s));

        var requiredScore = requiredSkills.Any() ? matchedRequired * 60.0 / requiredSkills.Count : 60;
        var preferredScore = preferredSkills.Any() ? matchedPreferred * 30.0 / preferredSkills.Count : 30;

        return (int)(requiredScore + preferredScore + 10); // 10 base points
    }

    private static double CalculateYearsOfExperience(UserProfile profile)
    {
        if (!profile.Experiences.Any())
            return 0;

        var totalYears = 0.0;
        foreach (var exp in profile.Experiences)
        {
            if (exp.StartDate.HasValue && exp.EndDate.HasValue)
            {
                totalYears += (exp.EndDate.Value - exp.StartDate.Value).TotalDays / 365.25;
            }
            else if (exp.StartDate.HasValue)
            {
                // Assume current position
                totalYears += (DateTimeOffset.UtcNow - exp.StartDate.Value).TotalDays / 365.25;
            }
            else
            {
                // Fallback: estimate 2 years per role
                totalYears += 2;
            }
        }

        return totalYears;
    }

    private static int GetSeniorityRank(Core.Enums.SeniorityLevel seniority)
    {
        return seniority switch
        {
            Core.Enums.SeniorityLevel.Intern => 0,
            Core.Enums.SeniorityLevel.Junior => 1,
            Core.Enums.SeniorityLevel.Middle => 2,
            Core.Enums.SeniorityLevel.Senior => 3,
            Core.Enums.SeniorityLevel.Lead => 4,
            Core.Enums.SeniorityLevel.Architect => 5,
            Core.Enums.SeniorityLevel.Principal => 6,
            _ => 2
        };
    }

    private static List<string> DetermineKeyFactors(JobVacancy vacancy, UserProfile profile, int matchScore)
    {
        var factors = new List<string>();

        if (matchScore >= 80)
            factors.Add("✓ Strong skill match");
        else if (matchScore < 50)
            factors.Add("✗ Weak skill match - consider upskilling");

        var yearsOfExperience = CalculateYearsOfExperience(profile);
        var requiredSeniorityRank = GetSeniorityRank(vacancy.SeniorityLevel);
        var profileSeniorityRank = yearsOfExperience < 2 ? 1 : yearsOfExperience < 5 ? 2 : yearsOfExperience < 8 ? 3 : 4;

        if (Math.Abs(requiredSeniorityRank - profileSeniorityRank) <= 1)
            factors.Add("✓ Seniority level matches your experience");
        else if (requiredSeniorityRank > profileSeniorityRank)
            factors.Add("⚠ Reach position - may be competitive");
        else
            factors.Add("⚠ Below your level - may be seen as overqualified");

        if (vacancy.RemotePolicy == Core.Enums.RemotePolicy.FullyRemote && profile.Preferences.RemoteOnly)
            factors.Add("✓ Remote work aligns with preference");

        if (vacancy.SalaryMax.HasValue && vacancy.SalaryMax.Value > 100000)
            factors.Add("✓ High compensation tier");

        return factors;
    }

    private static GeneralPrediction BuildGeneralPrediction(List<JobApplication> applications, UserProfile profile)
    {
        var model = BuildPredictionModel(applications);

        return new GeneralPrediction
        {
            ResponseRate = model.OverallResponseRate,
            InterviewRate = model.OverallInterviewRate,
            OfferRate = model.OverallOfferRate
        };
    }

    private static void PrintPrediction(ResponsePrediction prediction, JobVacancy vacancy)
    {
        Console.WriteLine($"Match Score: {prediction.MatchScore}%");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Predicted Success Rates:");
        Console.ResetColor();

        PrintRateWithBar("Response Rate", prediction.ResponseRate);
        PrintRateWithBar("Interview Rate", prediction.InterviewRate);
        PrintRateWithBar("Offer Rate", prediction.OfferRate);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Confidence: {prediction.ConfidenceLevel}");
        Console.ResetColor();
        Console.WriteLine($"Based on {prediction.SimilarApplications} similar applications");

        if (prediction.KeyFactors.Any())
        {
            Console.WriteLine("\nKey Factors:\n");
            foreach (var factor in prediction.KeyFactors)
            {
                var color = factor.StartsWith("✓") ? ConsoleColor.Green :
                           factor.StartsWith("✗") ? ConsoleColor.Red :
                           ConsoleColor.Yellow;
                Console.ForegroundColor = color;
                Console.WriteLine($"  {factor}");
                Console.ResetColor();
            }
        }

        Console.WriteLine("\nRecommendation:");
        Console.ForegroundColor = prediction.ResponseRate >= 50 ? ConsoleColor.Green :
                                   prediction.ResponseRate >= 30 ? ConsoleColor.Yellow :
                                   ConsoleColor.Red;

        if (prediction.ResponseRate >= 50)
            Console.WriteLine("  Strong candidate - apply with confidence!");
        else if (prediction.ResponseRate >= 30)
            Console.WriteLine("  Moderate chance - worth applying if interested");
        else
            Console.WriteLine("  Low probability - consider upskilling or targeting better matches");

        Console.ResetColor();
    }

    private static void PrintPredictionModel(PredictionModel model)
    {
        Console.WriteLine($"Historical Performance ({model.TotalApplications} applications):\n");

        PrintRateWithBar("Response Rate", model.OverallResponseRate);
        PrintRateWithBar("Interview Rate", model.OverallInterviewRate);
        PrintRateWithBar("Offer Rate", model.OverallOfferRate);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"Average Match Score: {model.AverageMatchScore:F1}%");
        Console.WriteLine($"Best Performing Range: {model.BestPerformingMatchRange}");
        Console.ResetColor();
    }

    private static void PrintGeneralPrediction(GeneralPrediction prediction)
    {
        PrintRateWithBar("Expected Response Rate", prediction.ResponseRate);
        PrintRateWithBar("Expected Interview Rate", prediction.InterviewRate);
        PrintRateWithBar("Expected Offer Rate", prediction.OfferRate);
    }

    private static void PrintRateWithBar(string label, double rate)
    {
        Console.Write($"  {label,-20} ");

        var color = rate >= 50 ? ConsoleColor.Green :
                   rate >= 30 ? ConsoleColor.Yellow :
                   ConsoleColor.Red;

        Console.ForegroundColor = color;
        Console.Write($"{rate:F1}%");
        Console.ResetColor();

        Console.Write(" ");
        var barLength = (int)(rate / 5);
        Console.ForegroundColor = color;
        Console.WriteLine(new string('█', barLength));
        Console.ResetColor();
    }

    private static string GetCompanyRecommendation(double responseRate, double interviewRate)
    {
        if (responseRate >= 60 && interviewRate >= 30)
            return "✓ This company responds well to your profile. Strong target!";
        else if (responseRate >= 40)
            return "⚠ Moderate success rate. Worth applying to select roles.";
        else if (responseRate > 0)
            return "✗ Low historical success. Consider if this is the right fit.";
        else
            return "No response history yet. Proceed with standard strategy.";
    }

    private sealed class ResponsePrediction
    {
        public double ResponseRate { get; init; }
        public double InterviewRate { get; init; }
        public double OfferRate { get; init; }
        public int MatchScore { get; init; }
        public string ConfidenceLevel { get; init; } = string.Empty;
        public int SimilarApplications { get; init; }
        public List<string> KeyFactors { get; init; } = [];
    }

    private sealed class PredictionModel
    {
        public int TotalApplications { get; init; }
        public double OverallResponseRate { get; init; }
        public double OverallInterviewRate { get; init; }
        public double OverallOfferRate { get; init; }
        public double AverageMatchScore { get; init; }
        public string BestPerformingMatchRange { get; init; } = string.Empty;
    }

    private sealed class GeneralPrediction
    {
        public double ResponseRate { get; init; }
        public double InterviewRate { get; init; }
        public double OfferRate { get; init; }
    }
}
