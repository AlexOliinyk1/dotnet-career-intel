using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Matching;

/// <summary>
/// Configurable weighted scoring engine that evaluates how well a job vacancy
/// aligns with a user profile across multiple dimensions.
/// </summary>
public sealed class ScoringEngine
{
    /// <summary>Weight applied to skill match scoring (0-1).</summary>
    public double SkillWeight { get; set; } = 0.40;

    /// <summary>Weight applied to seniority alignment scoring (0-1).</summary>
    public double SeniorityWeight { get; set; } = 0.20;

    /// <summary>Weight applied to salary alignment scoring (0-1).</summary>
    public double SalaryWeight { get; set; } = 0.20;

    /// <summary>Weight applied to remote policy alignment scoring (0-1).</summary>
    public double RemoteWeight { get; set; } = 0.10;

    /// <summary>Weight applied to growth opportunity scoring (0-1).</summary>
    public double GrowthWeight { get; set; } = 0.10;

    /// <summary>
    /// Computes a detailed <see cref="MatchScore"/> for the given vacancy against the user profile.
    /// </summary>
    public MatchScore Score(JobVacancy vacancy, UserProfile profile)
    {
        var userSkillNames = profile.Skills
            .Select(s => s.SkillName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matchingRequired = vacancy.RequiredSkills
            .Where(s => userSkillNames.Contains(s))
            .ToList();

        var missingRequired = vacancy.RequiredSkills
            .Where(s => !userSkillNames.Contains(s))
            .ToList();

        var matchingPreferred = vacancy.PreferredSkills
            .Where(s => userSkillNames.Contains(s))
            .ToList();

        var missingPreferred = vacancy.PreferredSkills
            .Where(s => !userSkillNames.Contains(s))
            .ToList();

        var matchingSkills = matchingRequired
            .Concat(matchingPreferred)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        double skillScore = ComputeSkillMatchScore(
            vacancy.RequiredSkills.Count,
            matchingRequired.Count,
            vacancy.PreferredSkills.Count,
            matchingPreferred.Count);

        double seniorityScore = ComputeSeniorityMatchScore(
            vacancy.SeniorityLevel,
            profile.Preferences.MinSeniority);

        double salaryScore = ComputeSalaryMatchScore(
            vacancy.SalaryMin,
            vacancy.SalaryMax,
            profile.Preferences.MinSalaryUsd,
            profile.Preferences.TargetSalaryUsd);

        double remoteScore = ComputeRemoteMatchScore(
            vacancy.RemotePolicy,
            profile.Preferences.RemoteOnly);

        double growthScore = ComputeGrowthScore(
            missingPreferred.Count,
            vacancy.PreferredSkills.Count);

        double overall =
            skillScore * SkillWeight +
            seniorityScore * SeniorityWeight +
            salaryScore * SalaryWeight +
            remoteScore * RemoteWeight +
            growthScore * GrowthWeight;

        var result = new MatchScore
        {
            OverallScore = Math.Round(overall, 2),
            SkillMatchScore = Math.Round(skillScore, 2),
            SeniorityMatchScore = Math.Round(seniorityScore, 2),
            SalaryMatchScore = Math.Round(salaryScore, 2),
            RemoteMatchScore = Math.Round(remoteScore, 2),
            GrowthScore = Math.Round(growthScore, 2),
            MatchingSkills = matchingSkills,
            MissingSkills = missingRequired,
            BonusSkills = matchingPreferred,
            RecommendedAction = DetermineAction(overall)
        };

        // --- P7: Explainable Scoring Engine v2 ---

        // Confidence calculation based on data completeness
        result.Confidence = ComputeConfidence(vacancy, profile);

        // Explanation generation
        result.Explanation = BuildExplanation(
            vacancy, profile, result,
            matchingRequired, missingRequired,
            matchingPreferred, missingPreferred);

        // Estimated weeks to ready
        result.EstimatedWeeksToReady = result.RecommendedAction switch
        {
            RecommendedAction.Apply => 0,
            RecommendedAction.PrepareAndApply => missingRequired.Count * 2,
            RecommendedAction.SkillUpFirst => missingRequired.Count * 4,
            _ => 99
        };

        return result;
    }

    /// <summary>
    /// Skill match: (matched_required / total_required * 70) + (matched_preferred / total_preferred * 30).
    /// If no skills listed, returns 50 (neutral).
    /// </summary>
    internal static double ComputeSkillMatchScore(
        int totalRequired, int matchedRequired,
        int totalPreferred, int matchedPreferred)
    {
        if (totalRequired == 0 && totalPreferred == 0)
            return 50.0;

        double requiredPortion = totalRequired > 0
            ? (double)matchedRequired / totalRequired * 70.0
            : 70.0;

        double preferredPortion = totalPreferred > 0
            ? (double)matchedPreferred / totalPreferred * 30.0
            : 30.0;

        return requiredPortion + preferredPortion;
    }

    /// <summary>
    /// Seniority match: exact = 100, one level off = 75, two levels off = 40, more = 10.
    /// Uses the user's min seniority as the reference point.
    /// </summary>
    internal static double ComputeSeniorityMatchScore(
        SeniorityLevel vacancyLevel, SeniorityLevel userMinLevel)
    {
        if (vacancyLevel == SeniorityLevel.Unknown || userMinLevel == SeniorityLevel.Unknown)
            return 50.0;

        int gap = Math.Abs((int)vacancyLevel - (int)userMinLevel);

        return gap switch
        {
            0 => 100.0,
            1 => 75.0,
            2 => 40.0,
            _ => 10.0
        };
    }

    /// <summary>
    /// Salary match: >= target = 100, >= min = 70, no data = 50, below min = 20.
    /// Uses the higher of SalaryMin/SalaryMax from the vacancy for comparison.
    /// </summary>
    internal static double ComputeSalaryMatchScore(
        decimal? vacancySalaryMin, decimal? vacancySalaryMax,
        decimal userMinSalary, decimal userTargetSalary)
    {
        decimal? vacancySalary = vacancySalaryMax ?? vacancySalaryMin;

        if (vacancySalary is null)
            return 50.0;

        if (vacancySalary >= userTargetSalary)
            return 100.0;

        if (vacancySalary >= userMinSalary)
            return 70.0;

        return 20.0;
    }

    /// <summary>
    /// Remote match: if user requires remote, FullyRemote = 100, RemoteFriendly = 80,
    /// Hybrid = 40, OnSite = 0. If user does not require remote, all policies score 80+.
    /// </summary>
    internal static double ComputeRemoteMatchScore(
        RemotePolicy vacancyPolicy, bool userRemoteOnly)
    {
        if (!userRemoteOnly)
        {
            return vacancyPolicy switch
            {
                RemotePolicy.FullyRemote => 100.0,
                RemotePolicy.RemoteFriendly => 95.0,
                RemotePolicy.Hybrid => 90.0,
                RemotePolicy.OnSite => 80.0,
                _ => 85.0
            };
        }

        return vacancyPolicy switch
        {
            RemotePolicy.FullyRemote => 100.0,
            RemotePolicy.RemoteFriendly => 80.0,
            RemotePolicy.Hybrid => 40.0,
            RemotePolicy.OnSite => 0.0,
            _ => 50.0
        };
    }

    /// <summary>
    /// Growth score: bonus for skills in the vacancy the user does not have.
    /// More missing preferred skills means a higher growth score (stretch goals).
    /// </summary>
    internal static double ComputeGrowthScore(int missingPreferredCount, int totalPreferred)
    {
        if (totalPreferred == 0)
            return 50.0;

        double ratio = (double)missingPreferredCount / totalPreferred;
        return Math.Min(ratio * 100.0, 100.0);
    }

    /// <summary>
    /// Determines the recommended action based on the overall weighted score.
    /// </summary>
    internal static RecommendedAction DetermineAction(double overallScore) => overallScore switch
    {
        >= 75.0 => RecommendedAction.Apply,
        >= 55.0 => RecommendedAction.PrepareAndApply,
        >= 35.0 => RecommendedAction.SkillUpFirst,
        _ => RecommendedAction.Skip
    };

    /// <summary>
    /// Computes a confidence score (0.1-1.0) based on data completeness of vacancy and profile.
    /// </summary>
    internal static double ComputeConfidence(JobVacancy vacancy, UserProfile profile)
    {
        double confidence = 1.0;

        if (vacancy.RequiredSkills.Count == 0)
            confidence -= 0.3;

        if (vacancy.SalaryMin is null && vacancy.SalaryMax is null)
            confidence -= 0.15;

        if (vacancy.SeniorityLevel == SeniorityLevel.Unknown)
            confidence -= 0.15;

        if (vacancy.RemotePolicy == RemotePolicy.Unknown)
            confidence -= 0.1;

        if (profile.Skills.Count < 3)
            confidence -= 0.2;

        return Math.Clamp(confidence, 0.1, 1.0);
    }

    /// <summary>
    /// Builds human-readable explanations for each scoring dimension.
    /// </summary>
    internal static ScoreExplanation BuildExplanation(
        JobVacancy vacancy,
        UserProfile profile,
        MatchScore result,
        List<string> matchingRequired,
        List<string> missingRequired,
        List<string> matchingPreferred,
        List<string> missingPreferred)
    {
        var explanation = new ScoreExplanation();

        // Skill match explanation
        int totalRequired = vacancy.RequiredSkills.Count;
        if (totalRequired > 0)
        {
            int pct = (int)Math.Round((double)matchingRequired.Count / totalRequired * 100);
            explanation.SkillMatch = missingRequired.Count > 0
                ? $"Matched {matchingRequired.Count}/{totalRequired} required skills ({pct}%). Missing: {string.Join(", ", missingRequired)}"
                : $"Matched all {totalRequired} required skills ({pct}%)";
        }
        else
        {
            explanation.SkillMatch = "No required skills listed in vacancy — skill match is estimated";
        }

        // Seniority fit explanation
        if (vacancy.SeniorityLevel == SeniorityLevel.Unknown)
        {
            explanation.SeniorityFit = $"Vacancy seniority is unknown, you target {profile.Preferences.MinSeniority} — neutral score ({result.SeniorityMatchScore:F0}/100)";
        }
        else
        {
            int gap = Math.Abs((int)vacancy.SeniorityLevel - (int)profile.Preferences.MinSeniority);
            explanation.SeniorityFit = gap == 0
                ? $"Vacancy is {vacancy.SeniorityLevel}, matching your target — perfect fit ({result.SeniorityMatchScore:F0}/100)"
                : $"Vacancy is {vacancy.SeniorityLevel}, you target {profile.Preferences.MinSeniority} — {gap} level gap ({result.SeniorityMatchScore:F0}/100)";
        }

        // Salary alignment explanation
        decimal? vacancySalary = vacancy.SalaryMax ?? vacancy.SalaryMin;
        if (vacancySalary is null)
        {
            explanation.SalaryAlignment = $"No salary info provided — neutral score ({result.SalaryMatchScore:F0}/100)";
        }
        else
        {
            explanation.SalaryAlignment = $"Offered ${vacancySalary:F0} vs your target of ${profile.Preferences.TargetSalaryUsd:F0} ({result.SalaryMatchScore:F0}/100)";
        }

        // Remote fit explanation
        if (vacancy.RemotePolicy == RemotePolicy.Unknown)
        {
            explanation.RemoteFit = $"Remote policy unknown — neutral score ({result.RemoteMatchScore:F0}/100)";
        }
        else
        {
            string policyLabel = vacancy.RemotePolicy switch
            {
                RemotePolicy.FullyRemote => "Fully remote",
                RemotePolicy.RemoteFriendly => "Remote-friendly",
                RemotePolicy.Hybrid => "Hybrid",
                RemotePolicy.OnSite => "On-site",
                _ => vacancy.RemotePolicy.ToString()
            };

            string fitQuality = result.RemoteMatchScore switch
            {
                100.0 => "perfect match",
                >= 80.0 => "good match",
                >= 40.0 => "partial match",
                _ => "poor match"
            };

            explanation.RemoteFit = $"{policyLabel} — {fitQuality} ({result.RemoteMatchScore:F0}/100)";
        }

        // Growth opportunity explanation
        if (vacancy.PreferredSkills.Count == 0)
        {
            explanation.GrowthOpportunity = $"No preferred skills listed — neutral growth score ({result.GrowthScore:F0}/100)";
        }
        else if (missingPreferred.Count > 0)
        {
            explanation.GrowthOpportunity = $"{missingPreferred.Count} preferred skill{(missingPreferred.Count == 1 ? "" : "s")} to learn ({string.Join(", ", missingPreferred)}) — good stretch ({result.GrowthScore:F0}/100)";
        }
        else
        {
            explanation.GrowthOpportunity = $"You already have all {vacancy.PreferredSkills.Count} preferred skills — limited stretch ({result.GrowthScore:F0}/100)";
        }

        // Overall verdict
        explanation.OverallVerdict = result.RecommendedAction switch
        {
            RecommendedAction.Apply => $"Strong match at {result.OverallScore:F0}/100 — apply with confidence",
            RecommendedAction.PrepareAndApply => $"Solid potential at {result.OverallScore:F0}/100 — apply with minor prep on {missingRequired.Count} missing skill{(missingRequired.Count == 1 ? "" : "s")}",
            RecommendedAction.SkillUpFirst => $"Growth opportunity at {result.OverallScore:F0}/100 — invest in {missingRequired.Count} missing skill{(missingRequired.Count == 1 ? "" : "s")} before applying",
            _ => $"Weak match at {result.OverallScore:F0}/100 — significant gaps, consider skipping"
        };

        // Strengths: top 3 matching areas
        var strengths = new List<(string Label, double Score)>
        {
            ("Skill match", result.SkillMatchScore),
            ("Seniority fit", result.SeniorityMatchScore),
            ("Salary alignment", result.SalaryMatchScore),
            ("Remote fit", result.RemoteMatchScore),
            ("Growth opportunity", result.GrowthScore)
        };

        explanation.Strengths = strengths
            .OrderByDescending(s => s.Score)
            .Take(3)
            .Where(s => s.Score >= 50.0)
            .Select(s => $"{s.Label} ({s.Score:F0}/100)")
            .ToList();

        // Risks: list of concerns
        var risks = new List<string>();

        if (missingRequired.Count > 0)
            risks.Add($"Missing {missingRequired.Count} required skill{(missingRequired.Count == 1 ? "" : "s")}: {string.Join(", ", missingRequired)}");

        if (vacancySalary is not null && vacancySalary < profile.Preferences.MinSalaryUsd)
            risks.Add($"Salary ${vacancySalary:F0} is below your minimum of ${profile.Preferences.MinSalaryUsd:F0}");

        if (vacancy.RemotePolicy == RemotePolicy.OnSite && profile.Preferences.RemoteOnly)
            risks.Add("On-site only — conflicts with your remote-only preference");

        if (vacancy.RemotePolicy == RemotePolicy.Hybrid && profile.Preferences.RemoteOnly)
            risks.Add("Hybrid work — may conflict with your remote-only preference");

        if (vacancy.SeniorityLevel != SeniorityLevel.Unknown
            && profile.Preferences.MinSeniority != SeniorityLevel.Unknown
            && (int)vacancy.SeniorityLevel < (int)profile.Preferences.MinSeniority)
            risks.Add($"Vacancy seniority {vacancy.SeniorityLevel} is below your target of {profile.Preferences.MinSeniority}");

        explanation.Risks = risks;

        return explanation;
    }
}
