using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Analyzes application outcomes and automatically suggests strategy adjustments.
/// Self-correcting system that learns from results: "20 startup apps, 0 responses → pivot to mid-size companies"
/// </summary>
public sealed class StrategyAdvisor
{
    private const int MinApplicationsForAdvice = 10; // Need data before giving advice

    /// <summary>
    /// Analyze application history and suggest strategic pivots.
    /// </summary>
    public StrategyRecommendation AnalyzeStrategy(
        List<JobApplication> applications,
        List<InterviewFeedback> interviewFeedback)
    {
        var recommendation = new StrategyRecommendation();

        if (applications.Count < MinApplicationsForAdvice)
        {
            recommendation.Advice.Add($"Need more data - apply to {MinApplicationsForAdvice - applications.Count} more positions before strategy analysis");
            return recommendation;
        }

        // Analyze response rate by company size
        var responseBySizePattern = AnalyzeCompanySize(applications);
        if (responseBySizePattern != null)
            recommendation.Pivots.Add(responseBySizePattern);

        // Analyze response rate by remote policy
        var responseByRemotePattern = AnalyzeRemotePolicy(applications);
        if (responseByRemotePattern != null)
            recommendation.Pivots.Add(responseByRemotePattern);

        // Analyze match score threshold
        var matchScorePattern = AnalyzeMatchScoreThreshold(applications);
        if (matchScorePattern != null)
            recommendation.Pivots.Add(matchScorePattern);

        // Analyze day-of-week patterns
        var timingPattern = AnalyzeApplyTiming(applications);
        if (timingPattern != null)
            recommendation.Pivots.Add(timingPattern);

        // Analyze interview failure patterns
        var interviewPattern = AnalyzeInterviewFailures(interviewFeedback);
        if (interviewPattern != null)
            recommendation.Pivots.Add(interviewPattern);

        // Generate overall strategy score
        recommendation.StrategyEffectiveness = CalculateStrategyScore(applications);

        // Generate actionable advice
        recommendation.Advice = GenerateActionableAdvice(recommendation.Pivots, applications);

        return recommendation;
    }

    private static StrategyPivot? AnalyzeCompanySize(List<JobApplication> applications)
    {
        // Categorize by assumed company size (simplified - in reality, integrate with company data)
        var startupKeywords = new[] { "startup", "seed", "series a", "series b" };
        var enterpriseKeywords = new[] { "enterprise", "fortune", "global", "corporation" };

        var startupApps = applications.Where(a =>
            startupKeywords.Any(k => a.Company.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();

        var enterpriseApps = applications.Where(a =>
            enterpriseKeywords.Any(k => a.Company.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();

        var midSizeApps = applications.Except(startupApps).Except(enterpriseApps).ToList();

        var startupResponseRate = CalculateResponseRate(startupApps);
        var enterpriseResponseRate = CalculateResponseRate(enterpriseApps);
        var midSizeResponseRate = CalculateResponseRate(midSizeApps);

        // Find best performer
        var best = new[] {
            ("Startups", startupResponseRate, startupApps.Count),
            ("Mid-size", midSizeResponseRate, midSizeApps.Count),
            ("Enterprise", enterpriseResponseRate, enterpriseApps.Count)
        }.Where(x => x.Item3 >= 5) // At least 5 applications
        .OrderByDescending(x => x.Item2)
        .FirstOrDefault();

        var worst = new[] {
            ("Startups", startupResponseRate, startupApps.Count),
            ("Mid-size", midSizeResponseRate, midSizeApps.Count),
            ("Enterprise", enterpriseResponseRate, enterpriseApps.Count)
        }.Where(x => x.Item3 >= 5)
        .OrderBy(x => x.Item2)
        .FirstOrDefault();

        if (best.Item1 != null && worst.Item1 != null && best.Item2 > worst.Item2 * 1.5)
        {
            return new StrategyPivot
            {
                Type = "Company Size",
                Finding = $"{worst.Item1}: {worst.Item2:P0} response rate vs. {best.Item1}: {best.Item2:P0}",
                Recommendation = $"🎯 PIVOT: Focus on {best.Item1} companies (reduce {worst.Item1} apps by 50%)",
                Impact = "High",
                Confidence = best.Item3 >= 10 ? "High" : "Medium"
            };
        }

        return null;
    }

    private static StrategyPivot? AnalyzeRemotePolicy(List<JobApplication> applications)
    {
        var remoteApps = applications.Where(a => a.Notes.Contains("remote", StringComparison.OrdinalIgnoreCase)).ToList();
        var onsiteApps = applications.Where(a => a.Notes.Contains("onsite", StringComparison.OrdinalIgnoreCase)).ToList();

        if (remoteApps.Count < 5 && onsiteApps.Count < 5)
            return null;

        var remoteRate = CalculateResponseRate(remoteApps);
        var onsiteRate = CalculateResponseRate(onsiteApps);

        if (Math.Abs(remoteRate - onsiteRate) > 0.2) // 20%+ difference
        {
            var better = remoteRate > onsiteRate ? "Remote" : "On-site";
            var worse = remoteRate > onsiteRate ? "On-site" : "Remote";
            var betterRate = Math.Max(remoteRate, onsiteRate);
            var worseRate = Math.Min(remoteRate, onsiteRate);

            return new StrategyPivot
            {
                Type = "Remote Policy",
                Finding = $"{better}: {betterRate:P0} vs. {worse}: {worseRate:P0}",
                Recommendation = $"🎯 PIVOT: Prioritize {better} positions",
                Impact = "Medium",
                Confidence = "Medium"
            };
        }

        return null;
    }

    private static StrategyPivot? AnalyzeMatchScoreThreshold(List<JobApplication> applications)
    {
        var validApps = applications.Where(a => a.MatchScore > 0).ToList();
        if (validApps.Count < MinApplicationsForAdvice)
            return null;

        // Group by match score buckets
        var highMatch = validApps.Where(a => a.MatchScore >= 80).ToList();
        var medMatch = validApps.Where(a => a.MatchScore >= 60 && a.MatchScore < 80).ToList();
        var lowMatch = validApps.Where(a => a.MatchScore < 60).ToList();

        var highRate = CalculateResponseRate(highMatch);
        var medRate = CalculateResponseRate(medMatch);
        var lowRate = CalculateResponseRate(lowMatch);

        // If low-match apps have terrible ROI
        if (lowMatch.Count >= 5 && lowRate < 0.1 && highRate > lowRate * 3)
        {
            return new StrategyPivot
            {
                Type = "Match Score Threshold",
                Finding = $"<60% match: {lowRate:P0} response vs. ≥80% match: {highRate:P0}",
                Recommendation = $"🎯 PIVOT: Stop applying to <60% match positions ({lowMatch.Count} apps wasted)",
                Impact = "High",
                Confidence = "High"
            };
        }

        return null;
    }

    private static StrategyPivot? AnalyzeApplyTiming(List<JobApplication> applications)
    {
        var withDates = applications.Where(a => a.AppliedDate.HasValue).ToList();
        if (withDates.Count < 20)
            return null;

        var byDayOfWeek = withDates
            .GroupBy(a => a.AppliedDate!.Value.DayOfWeek)
            .Select(g => new
            {
                Day = g.Key,
                Count = g.Count(),
                ResponseRate = CalculateResponseRate(g.ToList())
            })
            .OrderByDescending(x => x.ResponseRate)
            .ToList();

        var best = byDayOfWeek.FirstOrDefault();
        var worst = byDayOfWeek.LastOrDefault();

        if (best != null && worst != null && best.ResponseRate > worst.ResponseRate * 1.5 && best.Count >= 5)
        {
            return new StrategyPivot
            {
                Type = "Application Timing",
                Finding = $"{best.Day}: {best.ResponseRate:P0} vs. {worst.Day}: {worst.ResponseRate:P0}",
                Recommendation = $"💡 TIP: Apply on {best.Day}s for better response rates",
                Impact = "Low",
                Confidence = "Medium"
            };
        }

        return null;
    }

    private static StrategyPivot? AnalyzeInterviewFailures(List<InterviewFeedback> feedback)
    {
        if (feedback.Count < 5)
            return null;

        var failures = feedback.Where(f =>
            f.Outcome.Contains("Reject", StringComparison.OrdinalIgnoreCase) ||
            f.Outcome.Contains("Fail", StringComparison.OrdinalIgnoreCase)).ToList();

        if (failures.Count < 3)
            return null;

        // Find most common failure round
        var failuresByRound = failures
            .GroupBy(f => f.Round)
            .Select(g => new { Round = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        if (failuresByRound != null && failuresByRound.Count >= 3)
        {
            return new StrategyPivot
            {
                Type = "Interview Bottleneck",
                Finding = $"{failuresByRound.Count} failures at {failuresByRound.Round} round",
                Recommendation = $"🎯 FOCUS: Master {failuresByRound.Round} interviews before applying more",
                Impact = "Critical",
                Confidence = "High"
            };
        }

        return null;
    }

    private static double CalculateResponseRate(List<JobApplication> apps)
    {
        if (apps.Count == 0)
            return 0;

        var responses = apps.Count(a => a.Status >= ApplicationStatus.Viewed);
        return (double)responses / apps.Count;
    }

    private static int CalculateStrategyScore(List<JobApplication> applications)
    {
        if (applications.Count == 0)
            return 0;

        var responseRate = CalculateResponseRate(applications);
        var interviewRate = applications.Count(a => a.Status >= ApplicationStatus.Interview) / (double)applications.Count;
        var offerRate = applications.Count(a => a.Status == ApplicationStatus.Offer) / (double)applications.Count;

        // Weighted score
        var score = (responseRate * 30) + (interviewRate * 40) + (offerRate * 30);
        return (int)(score * 100);
    }

    private static List<string> GenerateActionableAdvice(List<StrategyPivot> pivots, List<JobApplication> applications)
    {
        var advice = new List<string>();

        if (pivots.Count == 0)
        {
            advice.Add("✓ Current strategy is working - keep doing what you're doing");
            advice.Add($"  Applied to {applications.Count} positions, no major pivots needed");
            return advice;
        }

        // Prioritize critical pivots
        var critical = pivots.Where(p => p.Impact == "Critical").ToList();
        var high = pivots.Where(p => p.Impact == "High").ToList();

        if (critical.Count > 0)
        {
            advice.Add("🚨 CRITICAL ISSUES - Address These First:");
            foreach (var pivot in critical)
            {
                advice.Add($"  • {pivot.Recommendation}");
            }
        }

        if (high.Count > 0)
        {
            advice.Add("\n🎯 HIGH-IMPACT CHANGES:");
            foreach (var pivot in high)
            {
                advice.Add($"  • {pivot.Recommendation}");
            }
        }

        var medium = pivots.Where(p => p.Impact == "Medium").ToList();
        if (medium.Count > 0)
        {
            advice.Add("\n💡 OPTIMIZATION OPPORTUNITIES:");
            foreach (var pivot in medium)
            {
                advice.Add($"  • {pivot.Recommendation}");
            }
        }

        return advice;
    }
}

/// <summary>
/// Strategy recommendation with identified pivots and actionable advice.
/// </summary>
public sealed class StrategyRecommendation
{
    public List<StrategyPivot> Pivots { get; set; } = [];
    public int StrategyEffectiveness { get; set; } // 0-100 score
    public List<string> Advice { get; set; } = [];
}

/// <summary>
/// A specific strategy pivot based on data analysis.
/// </summary>
public sealed class StrategyPivot
{
    public string Type { get; set; } = string.Empty; // Company Size, Remote Policy, Match Threshold, etc.
    public string Finding { get; set; } = string.Empty; // What the data shows
    public string Recommendation { get; set; } = string.Empty; // What to do about it
    public string Impact { get; set; } = string.Empty; // Critical, High, Medium, Low
    public string Confidence { get; set; } = string.Empty; // High, Medium, Low
}
