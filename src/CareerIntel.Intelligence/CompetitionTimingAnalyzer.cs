using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Detects market congestion and optimal application timing.
/// Recommends: "Wait 2 weeks - this role just got 200 applicants" or "Apply NOW - low competition window"
/// </summary>
public sealed class CompetitionTimingAnalyzer
{
    // High-competition periods (month numbers)
    private static readonly int[] HighCompetitionMonths = [1, 2, 9]; // Jan, Feb (New Year), Sep (back-to-school)

    /// <summary>
    /// Analyzes competition level for a vacancy and recommends timing.
    /// </summary>
    public TimingRecommendation AnalyzeCompetition(JobVacancy vacancy, List<JobVacancy> recentVacancies)
    {
        var recommendation = new TimingRecommendation
        {
            VacancyId = vacancy.Id,
            VacancyTitle = vacancy.Title,
            Company = vacancy.Company
        };

        // Signal 1: Vacancy age (fresh postings get flooded)
        var vacancyAge = (DateTimeOffset.UtcNow - vacancy.PostedDate).TotalDays;
        if (vacancyAge <= 1)
        {
            recommendation.Signals.Add("⚠ Just posted (<24h) - likely flooded with applicants");
            recommendation.CompetitionLevel = CompetitionLevel.High;
            recommendation.RecommendedDelay = 3; // Wait 3 days for initial rush to pass
        }
        else if (vacancyAge >= 14 && vacancyAge <= 30)
        {
            recommendation.Signals.Add("✓ 2-4 weeks old - initial rush has passed");
            recommendation.CompetitionLevel = CompetitionLevel.Medium;
        }
        else if (vacancyAge > 30)
        {
            recommendation.Signals.Add("✓ 30+ days old - low competition, likely struggling to fill");
            recommendation.CompetitionLevel = CompetitionLevel.Low;
        }

        // Signal 2: Seasonal patterns
        var currentMonth = DateTimeOffset.UtcNow.Month;
        if (HighCompetitionMonths.Contains(currentMonth))
        {
            recommendation.Signals.Add($"⚠ High-competition month ({GetMonthName(currentMonth)}) - more applicants than usual");
            recommendation.CompetitionLevel = recommendation.CompetitionLevel == CompetitionLevel.High
                ? CompetitionLevel.VeryHigh
                : CompetitionLevel.High;
        }

        // Signal 3: Market saturation (similar roles posted recently)
        var similarRoles = recentVacancies
            .Where(v => v.Id != vacancy.Id)
            .Where(v => SimilarRole(v, vacancy))
            .Where(v => (DateTimeOffset.UtcNow - v.PostedDate).TotalDays <= 30)
            .ToList();

        if (similarRoles.Count >= 10)
        {
            recommendation.Signals.Add($"⚠ Market saturation: {similarRoles.Count} similar roles posted recently");
            recommendation.CompetitionLevel = CompetitionLevel.High;
        }
        else if (similarRoles.Count <= 3)
        {
            recommendation.Signals.Add($"✓ Low saturation: Only {similarRoles.Count} similar roles - less competition");
            if (recommendation.CompetitionLevel > CompetitionLevel.Low)
                recommendation.CompetitionLevel = CompetitionLevel.Medium;
        }

        // Signal 4: Day of week (weekends see fewer applications)
        var dayOfWeek = DateTimeOffset.UtcNow.DayOfWeek;
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
        {
            recommendation.Signals.Add("✓ Weekend - fewer applications, higher visibility");
            if (recommendation.CompetitionLevel == CompetitionLevel.Medium)
                recommendation.CompetitionLevel = CompetitionLevel.Low;
        }
        else if (dayOfWeek == DayOfWeek.Monday)
        {
            recommendation.Signals.Add("⚠ Monday - high application volume (weekend job seekers)");
        }

        // Signal 5: Expiration urgency (overrides competition concerns)
        if (vacancy.ExpiresAt.HasValue)
        {
            var daysUntilExpiry = (vacancy.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalDays;
            if (daysUntilExpiry <= 3)
            {
                recommendation.Signals.Add($"🚨 URGENT: Expires in {daysUntilExpiry:F0} days - apply NOW regardless of competition");
                recommendation.CompetitionLevel = CompetitionLevel.Low; // Force apply
                recommendation.RecommendedDelay = 0;
            }
        }

        // Generate final recommendation
        recommendation.Recommendation = GenerateRecommendation(recommendation);

        return recommendation;
    }

    /// <summary>
    /// Batch analysis: find vacancies with optimal timing windows.
    /// </summary>
    public List<TimingOpportunity> FindOptimalTimingWindows(List<JobVacancy> vacancies)
    {
        var opportunities = new List<TimingOpportunity>();

        foreach (var vacancy in vacancies)
        {
            var analysis = AnalyzeCompetition(vacancy, vacancies);

            if (analysis.CompetitionLevel <= CompetitionLevel.Medium &&
                analysis.RecommendedDelay == 0)
            {
                opportunities.Add(new TimingOpportunity
                {
                    Vacancy = vacancy,
                    CompetitionLevel = analysis.CompetitionLevel,
                    Reason = string.Join(" | ", analysis.Signals),
                    Score = CalculateTimingScore(analysis)
                });
            }
        }

        return opportunities.OrderByDescending(o => o.Score).ToList();
    }

    private static bool SimilarRole(JobVacancy v1, JobVacancy v2)
    {
        // Simple similarity check - in production, use more sophisticated matching
        var title1 = v1.Title.ToLowerInvariant();
        var title2 = v2.Title.ToLowerInvariant();

        // Check for common keywords
        var keywords1 = title1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keywords2 = title2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var commonKeywords = keywords1.Intersect(keywords2).Count();
        return commonKeywords >= 2; // At least 2 common words
    }

    private static string GenerateRecommendation(TimingRecommendation timing)
    {
        return timing.CompetitionLevel switch
        {
            CompetitionLevel.VeryHigh when timing.RecommendedDelay > 0 =>
                $"⚠ WAIT {timing.RecommendedDelay} days - very high competition right now",

            CompetitionLevel.High when timing.RecommendedDelay > 0 =>
                $"⚠ Consider waiting {timing.RecommendedDelay} days for competition to decrease",

            CompetitionLevel.Medium =>
                "✓ APPLY NOW - moderate competition, good timing",

            CompetitionLevel.Low =>
                "✓ APPLY NOW - low competition window, excellent timing!",

            _ => "APPLY NOW"
        };
    }

    private static int CalculateTimingScore(TimingRecommendation timing)
    {
        var score = 50; // Base score

        score += timing.CompetitionLevel switch
        {
            CompetitionLevel.Low => 30,
            CompetitionLevel.Medium => 15,
            CompetitionLevel.High => -10,
            CompetitionLevel.VeryHigh => -25,
            _ => 0
        };

        score -= timing.RecommendedDelay * 5; // Penalty for delay

        return Math.Clamp(score, 0, 100);
    }

    private static string GetMonthName(int month)
    {
        return month switch
        {
            1 => "January",
            2 => "February",
            3 => "March",
            4 => "April",
            5 => "May",
            6 => "June",
            7 => "July",
            8 => "August",
            9 => "September",
            10 => "October",
            11 => "November",
            12 => "December",
            _ => "Unknown"
        };
    }
}

/// <summary>
/// Timing recommendation for a specific vacancy.
/// </summary>
public sealed class TimingRecommendation
{
    public string VacancyId { get; set; } = string.Empty;
    public string VacancyTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public CompetitionLevel CompetitionLevel { get; set; }
    public List<string> Signals { get; set; } = [];
    public int RecommendedDelay { get; set; } // Days to wait (0 = apply now)
    public string Recommendation { get; set; } = string.Empty;
}

/// <summary>
/// Vacancy with optimal timing window.
/// </summary>
public sealed class TimingOpportunity
{
    public JobVacancy Vacancy { get; set; } = null!;
    public CompetitionLevel CompetitionLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Score { get; set; } // 0-100
}

/// <summary>
/// Competition level for a vacancy.
/// </summary>
public enum CompetitionLevel
{
    Low,       // Best time to apply
    Medium,    // Good time to apply
    High,      // Consider waiting
    VeryHigh   // Highly competitive, wait if possible
}
