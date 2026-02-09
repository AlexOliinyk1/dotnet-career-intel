using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Evaluates how competitive the user would be as an applicant for each vacancy.
/// Goes beyond simple skill matching to estimate competitive positioning and response probability
/// across 7 weighted dimensions: skill depth, experience relevance, seniority fit, salary positioning,
/// application freshness, market competition, and platform response rate.
/// </summary>
public sealed class ApplicantCompetitivenessEngine(ILogger<ApplicantCompetitivenessEngine> logger)
{
    private const double SkillDepthWeight = 0.25;
    private const double ExperienceRelevanceWeight = 0.20;
    private const double SeniorityFitWeight = 0.15;
    private const double SalaryPositioningWeight = 0.10;
    private const double FreshnessWeight = 0.10;
    private const double MarketCompetitionWeight = 0.10;
    private const double PlatformResponseWeight = 0.10;

    private const double MaxResponseProbability = 95.0;

    private static readonly HashSet<string> CommonFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "React", "Angular", "Vue", "Node.js", "Express", "Django", "Flask", "Spring", "Rails",
        ".NET", "ASP.NET", "Laravel", "Next.js", "Nuxt.js", "Svelte", "jQuery", "Bootstrap",
        "Tailwind", "Docker", "Kubernetes", "AWS", "Azure", "GCP", "PostgreSQL", "MySQL",
        "MongoDB", "Redis", "GraphQL", "REST", "TypeScript", "JavaScript", "Python", "Java",
        "C#", "Go", "Rust", "PHP", "Ruby", "Swift", "Kotlin"
    };

    private static readonly HashSet<string> MajorCities = new(StringComparer.OrdinalIgnoreCase)
    {
        "London", "New York", "San Francisco", "Berlin", "Amsterdam", "Paris", "Dublin",
        "Toronto", "Singapore", "Sydney", "Tokyo", "Zurich", "Munich", "Stockholm",
        "Barcelona", "Warsaw", "Kyiv", "Krakow", "Prague", "Vienna", "Copenhagen",
        "Los Angeles", "Seattle", "Austin", "Chicago", "Boston"
    };

    private static readonly Dictionary<string, double> PlatformResponseRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["djinni"] = 80,
        ["dou"] = 75,
        ["justjoinit"] = 65,
        ["nofluffjobs"] = 70,
        ["remoteok"] = 45,
        ["weworkremotely"] = 45,
        ["hackernews"] = 55,
        ["himalayas"] = 50,
        ["jobicy"] = 50,
        ["toptal"] = 60,
        ["linkedin"] = 35,
        ["image-scan"] = 50
    };

    /// <summary>
    /// Assess competitiveness for a single vacancy against the user's profile.
    /// </summary>
    public CompetitivenessAssessment Assess(JobVacancy vacancy, UserProfile profile)
    {
        logger.LogInformation("Assessing competitiveness for vacancy '{Title}' at {Company} [{Platform}]",
            vacancy.Title, vacancy.Company, vacancy.SourcePlatform);

        var strengths = new List<string>();
        var weaknesses = new List<string>();
        var tips = new List<string>();

        double skillDepth = ComputeSkillDepthScore(vacancy, profile, strengths, weaknesses, tips);
        double experienceRelevance = ComputeExperienceRelevanceScore(vacancy, profile, strengths, weaknesses, tips);
        double seniorityFit = ComputeSeniorityFitScore(vacancy, profile, strengths, weaknesses, tips);
        double salaryPositioning = ComputeSalaryPositioningScore(vacancy, profile, strengths, weaknesses, tips);
        double freshness = ComputeFreshnessScore(vacancy, tips);
        double marketCompetition = ComputeMarketCompetitionScore(vacancy, strengths, weaknesses);
        double platformResponse = ComputePlatformResponseScore(vacancy);

        double competitivenessScore =
            skillDepth * SkillDepthWeight +
            experienceRelevance * ExperienceRelevanceWeight +
            seniorityFit * SeniorityFitWeight +
            salaryPositioning * SalaryPositioningWeight +
            freshness * FreshnessWeight +
            marketCompetition * MarketCompetitionWeight +
            platformResponse * PlatformResponseWeight;

        competitivenessScore = Math.Clamp(competitivenessScore, 0, 100);

        var (tier, percentile) = ClassifyTier(competitivenessScore);
        double responseProbability = ComputeResponseProbability(competitivenessScore, platformResponse, freshness);

        var breakdown = new CompetitivenessBreakdown(
            SkillDepthScore: skillDepth,
            ExperienceRelevanceScore: experienceRelevance,
            SeniorityFitScore: seniorityFit,
            SalaryPositioningScore: salaryPositioning,
            FreshnessScore: freshness,
            MarketCompetitionScore: marketCompetition,
            PlatformResponseScore: platformResponse);

        logger.LogInformation(
            "Competitiveness for '{Title}': {Score:F1}/100 ({Tier}), response probability {Response:F1}%",
            vacancy.Title, competitivenessScore, tier, responseProbability);

        return new CompetitivenessAssessment(
            Vacancy: vacancy,
            CompetitivenessScore: Math.Round(competitivenessScore, 1),
            Tier: tier,
            EstimatedPercentile: percentile,
            ResponseProbability: Math.Round(responseProbability, 1),
            Breakdown: breakdown,
            StrengthFactors: strengths,
            WeaknessFactors: weaknesses,
            Tips: tips);
    }

    /// <summary>
    /// Assess competitiveness for all vacancies, rank them, and produce a summary report.
    /// </summary>
    public CompetitivenessReport AssessAll(IReadOnlyList<JobVacancy> vacancies, UserProfile profile)
    {
        logger.LogInformation("Assessing competitiveness across {Count} vacancies", vacancies.Count);

        var assessments = vacancies
            .Select(v => Assess(v, profile))
            .OrderByDescending(a => a.CompetitivenessScore)
            .ToList();

        int topCandidateCount = assessments.Count(a => a.CompetitivenessScore >= 80);
        int strongContenderCount = assessments.Count(a => a.CompetitivenessScore >= 65 && a.CompetitivenessScore < 80);
        int competitiveCount = assessments.Count(a => a.CompetitivenessScore >= 50 && a.CompetitivenessScore < 65);
        int averageCount = assessments.Count(a => a.CompetitivenessScore >= 35 && a.CompetitivenessScore < 50);
        int longShotCount = assessments.Count(a => a.CompetitivenessScore < 35);

        double averageScore = assessments.Count > 0
            ? assessments.Average(a => a.CompetitivenessScore)
            : 0;

        var topContributingSkills = assessments
            .Where(a => a.CompetitivenessScore >= 65)
            .SelectMany(a => a.StrengthFactors)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        var mostNeededSkills = assessments
            .SelectMany(a => a.WeaknessFactors)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        string verdict = GenerateOverallVerdict(
            assessments.Count, topCandidateCount, strongContenderCount, competitiveCount, averageScore);

        logger.LogInformation(
            "Competitiveness report: {Total} vacancies, avg {Avg:F1}, top candidates {Top}, strong {Strong}, competitive {Comp}",
            vacancies.Count, averageScore, topCandidateCount, strongContenderCount, competitiveCount);

        return new CompetitivenessReport(
            Assessments: assessments,
            TotalVacancies: vacancies.Count,
            AverageScore: Math.Round(averageScore, 1),
            TopCandidateCount: topCandidateCount,
            StrongContenderCount: strongContenderCount,
            CompetitiveCount: competitiveCount,
            AverageCount: averageCount,
            LongShotCount: longShotCount,
            TopContributingSkills: topContributingSkills,
            MostNeededSkills: mostNeededSkills,
            OverallVerdict: verdict);
    }

    /// <summary>
    /// Measures HOW WELL the user knows matched skills, not just overlap.
    /// Expert (5) + 5+ years = 100, Advanced (4) + 3+ years = 85, Intermediate (3) + 2+ years = 65,
    /// Beginner (1-2) or less than 1 year = 30, Missing = 0.
    /// </summary>
    private static double ComputeSkillDepthScore(
        JobVacancy vacancy,
        UserProfile profile,
        List<string> strengths,
        List<string> weaknesses,
        List<string> tips)
    {
        var requiredSkills = vacancy.RequiredSkills;
        var preferredSkills = vacancy.PreferredSkills;

        if (requiredSkills.Count == 0 && preferredSkills.Count == 0)
            return 60.0;

        double requiredScore = 0;

        if (requiredSkills.Count > 0)
        {
            double totalRequiredPoints = 0;

            foreach (var skill in requiredSkills)
            {
                var userSkill = profile.Skills
                    .FirstOrDefault(s => s.SkillName.Equals(skill, StringComparison.OrdinalIgnoreCase));

                if (userSkill is null)
                {
                    weaknesses.Add($"Missing required skill: {skill}");

                    var relatedSkill = FindRelatedSkillInProfile(skill, profile);
                    if (relatedSkill is not null)
                    {
                        tips.Add($"Consider highlighting your {relatedSkill.SkillName} experience to partially cover the {skill} gap");
                    }

                    continue;
                }

                double points = ComputeSkillPoints(userSkill);
                totalRequiredPoints += points;

                if (points >= 85)
                {
                    strengths.Add($"Expert-level {userSkill.SkillName} ({userSkill.YearsOfExperience:F0}+ years)");
                }

                if (userSkill.YearsOfExperience >= 5 && points >= 100)
                {
                    tips.Add($"Your {userSkill.YearsOfExperience:F0} years of {userSkill.SkillName} experience makes you a standout for this role");
                }
            }

            requiredScore = totalRequiredPoints / requiredSkills.Count;
        }
        else
        {
            requiredScore = 60.0;
        }

        double preferredScore = 0;

        if (preferredSkills.Count > 0)
        {
            double totalPreferredPoints = 0;

            foreach (var skill in preferredSkills)
            {
                var userSkill = profile.Skills
                    .FirstOrDefault(s => s.SkillName.Equals(skill, StringComparison.OrdinalIgnoreCase));

                if (userSkill is not null)
                {
                    totalPreferredPoints += ComputeSkillPoints(userSkill);
                }
            }

            preferredScore = totalPreferredPoints / preferredSkills.Count;
        }

        double finalScore = requiredSkills.Count > 0
            ? requiredScore * 0.7 + preferredScore * 0.3
            : preferredScore;

        return Math.Clamp(finalScore, 0, 100);
    }

    private static double ComputeSkillPoints(SkillProfile skill)
    {
        if (skill.ProficiencyLevel >= 5 && skill.YearsOfExperience >= 5.0)
            return 100;

        if (skill.ProficiencyLevel >= 4 && skill.YearsOfExperience >= 3.0)
            return 85;

        if (skill.ProficiencyLevel >= 3 && skill.YearsOfExperience >= 2.0)
            return 65;

        if (skill.ProficiencyLevel <= 2 || skill.YearsOfExperience < 1.0)
            return 30;

        // Intermediate cases: interpolate based on proficiency and years
        return 50;
    }

    /// <summary>
    /// Measures how well past roles and tech stacks align with this vacancy.
    /// Score = (techStackOverlap / totalRequired * 60) + (roleRelevance * 40).
    /// Minimum 20 if user has any experiences.
    /// </summary>
    private static double ComputeExperienceRelevanceScore(
        JobVacancy vacancy,
        UserProfile profile,
        List<string> strengths,
        List<string> weaknesses,
        List<string> tips)
    {
        if (profile.Experiences.Count == 0)
        {
            weaknesses.Add("No work experience entries in profile");
            tips.Add("Add your work experience to your profile to improve matching accuracy");
            return 0;
        }

        int totalRequired = vacancy.RequiredSkills.Count;

        // Count unique required skills that appear in any experience tech stack
        int techStackOverlap = 0;
        if (totalRequired > 0)
        {
            techStackOverlap = vacancy.RequiredSkills
                .Count(rs => profile.Experiences
                    .Any(exp => exp.TechStack.Contains(rs, StringComparer.OrdinalIgnoreCase)));
        }

        // Extract keywords from vacancy title for role matching
        var titleKeywords = ExtractTitleKeywords(vacancy.Title);

        int roleMatches = 0;
        foreach (var experience in profile.Experiences)
        {
            bool matched = titleKeywords.Any(keyword =>
                experience.Role.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (matched)
                roleMatches++;
        }

        double techStackComponent = totalRequired > 0
            ? (double)techStackOverlap / totalRequired * 60.0
            : 30.0;

        double roleRelevanceRatio = profile.Experiences.Count > 0
            ? Math.Min(1.0, (double)roleMatches / profile.Experiences.Count)
            : 0;

        double roleComponent = roleRelevanceRatio * 40.0;

        double score = techStackComponent + roleComponent;
        score = Math.Max(20.0, score);

        if (techStackOverlap > 0 && totalRequired > 0)
        {
            strengths.Add($"Past tech stacks cover {techStackOverlap}/{totalRequired} required skills");
        }

        if (roleMatches > 0)
        {
            strengths.Add($"{roleMatches} past role(s) align with this vacancy's title");
        }
        else if (titleKeywords.Count > 0)
        {
            tips.Add("Tailor your resume to emphasize relevant responsibilities that align with this role");
        }

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Directional seniority penalty: overqualified is different from underqualified.
    /// Uses total years of experience to estimate actual level.
    /// </summary>
    private static double ComputeSeniorityFitScore(
        JobVacancy vacancy,
        UserProfile profile,
        List<string> strengths,
        List<string> weaknesses,
        List<string> tips)
    {
        if (vacancy.SeniorityLevel == SeniorityLevel.Unknown)
            return 50.0;

        var estimatedLevel = EstimateUserSeniorityLevel(profile);

        if (estimatedLevel == SeniorityLevel.Unknown)
            return 50.0;

        int vacancyLevel = (int)vacancy.SeniorityLevel;
        int userLevel = (int)estimatedLevel;
        int gap = userLevel - vacancyLevel;

        double score = gap switch
        {
            0 => 100,
            1 => 90,
            >= 2 => 60,
            -1 => 70,
            _ => 30 // -2 or lower
        };

        if (gap == 0)
        {
            strengths.Add($"Seniority level ({estimatedLevel}) matches vacancy requirement exactly");
        }
        else if (gap > 0)
        {
            weaknesses.Add($"Potentially overqualified: you're estimated as {estimatedLevel}, vacancy targets {vacancy.SeniorityLevel}");
            tips.Add("Emphasize your interest in the role's specific challenges if you're overqualified");
        }
        else
        {
            weaknesses.Add($"Seniority stretch: you're estimated as {estimatedLevel}, vacancy targets {vacancy.SeniorityLevel}");

            if (gap == -1)
            {
                tips.Add("This is a stretch role — highlight leadership and complex project experience to strengthen your application");
            }
            else
            {
                tips.Add("This role targets a significantly higher seniority level; consider whether you meet the expectations");
            }
        }

        return score;
    }

    /// <summary>
    /// Where user's salary expectations fall within the posted range.
    /// </summary>
    private static double ComputeSalaryPositioningScore(
        JobVacancy vacancy,
        UserProfile profile,
        List<string> strengths,
        List<string> weaknesses,
        List<string> tips)
    {
        if (vacancy.SalaryMin is null && vacancy.SalaryMax is null)
            return 50.0;

        decimal userTarget = profile.Preferences.TargetSalaryUsd;
        decimal userMin = profile.Preferences.MinSalaryUsd;

        decimal salaryMin = vacancy.SalaryMin ?? 0;
        decimal salaryMax = vacancy.SalaryMax ?? salaryMin;

        if (salaryMax <= 0)
            return 50.0;

        // User target within range
        if (userTarget >= salaryMin && userTarget <= salaryMax)
        {
            strengths.Add("Salary expectations align well with the posted range");
            return 100.0;
        }

        // User min is below range minimum (budget-friendly)
        if (userMin > 0 && userMin < salaryMin)
        {
            strengths.Add("Your minimum salary is below the posted range — budget-friendly candidate");
            return 90.0;
        }

        // User target above max
        if (userTarget > salaryMax)
        {
            decimal overagePercent = salaryMax > 0
                ? (userTarget - salaryMax) / salaryMax * 100
                : 0;

            if (overagePercent <= 20)
            {
                tips.Add($"Your target salary is {overagePercent:F0}% above the max — negotiable, but mention flexibility");
                return 70.0;
            }
            else
            {
                weaknesses.Add($"Salary mismatch: your target is {overagePercent:F0}% above the posted max");
                tips.Add("Consider whether the role's non-monetary benefits make up for the salary gap");
                return 30.0;
            }
        }

        // User target below range minimum (undervaluing self)
        if (userTarget < salaryMin && userTarget > 0)
        {
            tips.Add("Your target salary is below the posted minimum — you may be undervaluing yourself");
            return 90.0;
        }

        return 50.0;
    }

    /// <summary>
    /// Freshness of the posting: newer postings have less competition and are more likely to respond.
    /// </summary>
    private static double ComputeFreshnessScore(JobVacancy vacancy, List<string> tips)
    {
        if (vacancy.PostedDate == default)
            return 50.0;

        double daysOld = (DateTimeOffset.UtcNow - vacancy.PostedDate).TotalDays;

        if (daysOld <= 3)
        {
            tips.Add($"Apply within 48h — this was posted {daysOld:F0} day(s) ago");
            return 100.0;
        }

        if (daysOld <= 7)
        {
            tips.Add("This posting is less than a week old — apply soon for best results");
            return 85.0;
        }

        if (daysOld <= 14)
        {
            return 65.0;
        }

        if (daysOld <= 30)
        {
            tips.Add("This posting is over 2 weeks old — the role may already have strong candidates in the pipeline");
            return 40.0;
        }

        tips.Add("This posting is over 30 days old — verify the role is still open before applying");
        return 15.0;
    }

    /// <summary>
    /// Estimates supply/demand for this vacancy. Higher score = less competition = better for applicant.
    /// Combines multiple heuristics by averaging applicable signals.
    /// </summary>
    private static double ComputeMarketCompetitionScore(
        JobVacancy vacancy,
        List<string> strengths,
        List<string> weaknesses)
    {
        var signals = new List<double>();

        // Niche skills: fewer than 3 common frameworks in required skills
        int commonFrameworkCount = vacancy.RequiredSkills
            .Count(s => CommonFrameworks.Contains(s));

        if (commonFrameworkCount < 3 && vacancy.RequiredSkills.Count > 0)
        {
            signals.Add(85.0);
            strengths.Add("Niche skill requirements reduce competition");
        }

        // Demanding: more than 8 required skills
        if (vacancy.RequiredSkills.Count > 8)
        {
            signals.Add(75.0);
            strengths.Add("High number of required skills narrows the applicant pool");
        }

        // Remote policy impact
        if (vacancy.RemotePolicy == RemotePolicy.FullyRemote)
        {
            signals.Add(40.0);
            weaknesses.Add("Fully remote roles attract global competition");
        }
        else if (vacancy.RemotePolicy is RemotePolicy.OnSite or RemotePolicy.Hybrid)
        {
            bool isMajorCity = !string.IsNullOrWhiteSpace(vacancy.City) && MajorCities.Contains(vacancy.City);
            if (!isMajorCity)
            {
                signals.Add(80.0);
                strengths.Add($"Location ({vacancy.City}) has less competition than major tech hubs");
            }
        }

        // Engagement type impact
        if (vacancy.EngagementType is EngagementType.ContractB2B or EngagementType.Freelance)
        {
            signals.Add(70.0);
        }

        // Salary attractiveness
        if (vacancy.SalaryMax.HasValue && vacancy.SalaryMax.Value > 80_000)
        {
            signals.Add(60.0);
        }

        if (signals.Count == 0)
            return 55.0;

        return signals.Average();
    }

    /// <summary>
    /// Estimated response likelihood by source platform.
    /// </summary>
    private static double ComputePlatformResponseScore(JobVacancy vacancy)
    {
        string platform = vacancy.SourcePlatform ?? string.Empty;

        return PlatformResponseRates.TryGetValue(platform, out double rate)
            ? rate
            : 50.0;
    }

    /// <summary>
    /// Transforms the competitiveness score into an estimated response probability percentage.
    /// ResponseProbability = score * 0.8 * platformFactor * freshnessFactor, capped at 95%.
    /// </summary>
    private static double ComputeResponseProbability(double competitivenessScore, double platformScore, double freshnessScore)
    {
        double platformFactor = platformScore / 100.0;
        double freshnessFactor = freshnessScore / 100.0;

        double probability = competitivenessScore * 0.8 * platformFactor * freshnessFactor;

        return Math.Min(probability, MaxResponseProbability);
    }

    private static (string Tier, int Percentile) ClassifyTier(double score) =>
        score switch
        {
            >= 80 => ("Top Candidate", 95),
            >= 65 => ("Strong Contender", 85),
            >= 50 => ("Competitive", 65),
            >= 35 => ("Average", 40),
            _ => ("Long Shot", 15)
        };

    /// <summary>
    /// Estimates user's actual seniority level based on total years of professional experience.
    /// </summary>
    private static SeniorityLevel EstimateUserSeniorityLevel(UserProfile profile)
    {
        double totalYears = 0;

        foreach (var exp in profile.Experiences)
        {
            if (exp.StartDate.HasValue)
            {
                var end = exp.EndDate ?? DateTimeOffset.UtcNow;
                totalYears += (end - exp.StartDate.Value).TotalDays / 365.25;
            }
        }

        // Fallback: check max YearsOfExperience across skills
        if (totalYears <= 0)
        {
            totalYears = profile.Skills
                .Select(s => s.YearsOfExperience)
                .DefaultIfEmpty(0)
                .Max();
        }

        return totalYears switch
        {
            < 1 => SeniorityLevel.Intern,
            < 2 => SeniorityLevel.Junior,
            < 4 => SeniorityLevel.Middle,
            < 7 => SeniorityLevel.Senior,
            < 10 => SeniorityLevel.Lead,
            < 14 => SeniorityLevel.Architect,
            _ => SeniorityLevel.Principal
        };
    }

    /// <summary>
    /// Extract meaningful keywords from a vacancy title for role matching.
    /// </summary>
    private static List<string> ExtractTitleKeywords(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return [];

        // Split on common delimiters and filter to meaningful keywords
        var words = title.Split([' ', '/', '-', '(', ')', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var meaningfulKeywords = new List<string>();
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "and", "or", "for", "at", "in", "to", "with", "of", "is", "we", "are", "our"
        };

        foreach (var word in words)
        {
            if (word.Length >= 2 && !stopWords.Contains(word))
            {
                meaningfulKeywords.Add(word);
            }
        }

        return meaningfulKeywords;
    }

    /// <summary>
    /// Attempts to find a related skill in the user's profile that could partially cover a gap.
    /// Uses simple substring matching as a heuristic.
    /// </summary>
    private static SkillProfile? FindRelatedSkillInProfile(string missingSkill, UserProfile profile)
    {
        // Check for skills that share a common root (e.g., "Kubernetes" -> "Docker", "React" -> "React Native")
        return profile.Skills
            .Where(s => s.ProficiencyLevel >= 3)
            .FirstOrDefault(s =>
                s.SkillName.Contains(missingSkill, StringComparison.OrdinalIgnoreCase) ||
                missingSkill.Contains(s.SkillName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GenerateOverallVerdict(
        int totalVacancies,
        int topCandidateCount,
        int strongContenderCount,
        int competitiveCount,
        double averageScore)
    {
        if (totalVacancies == 0)
            return "No vacancies to assess.";

        int strongOrBetter = topCandidateCount + strongContenderCount;

        if (topCandidateCount > 0)
        {
            return $"You're a top candidate for {topCandidateCount}/{totalVacancies} vacancies and a strong contender for {strongContenderCount} more. "
                   + $"Average competitiveness score: {averageScore:F0}/100. Focus your applications on your top-ranked matches for the best response rates.";
        }

        if (strongContenderCount > 0)
        {
            return $"You're a strong contender for {strongContenderCount}/{totalVacancies} vacancies with an average score of {averageScore:F0}/100. "
                   + "Invest time in tailoring your resume for these roles and addressing any skill gaps mentioned in the breakdown.";
        }

        if (competitiveCount > 0)
        {
            return $"You're competitive for {competitiveCount}/{totalVacancies} vacancies but not a standout yet. "
                   + $"Average score: {averageScore:F0}/100. Consider upskilling in your most common missing skills to improve your positioning.";
        }

        return $"Your current profile scores an average of {averageScore:F0}/100 across {totalVacancies} vacancies. "
               + "Significant skill or experience gaps exist. Focus on building relevant experience and closing key skill gaps before mass-applying.";
    }
}

// ── Output Records ──────────────────────────────────────────────────────────────

public sealed record CompetitivenessAssessment(
    JobVacancy Vacancy,
    double CompetitivenessScore,
    string Tier,
    int EstimatedPercentile,
    double ResponseProbability,
    CompetitivenessBreakdown Breakdown,
    List<string> StrengthFactors,
    List<string> WeaknessFactors,
    List<string> Tips);

public sealed record CompetitivenessBreakdown(
    double SkillDepthScore,
    double ExperienceRelevanceScore,
    double SeniorityFitScore,
    double SalaryPositioningScore,
    double FreshnessScore,
    double MarketCompetitionScore,
    double PlatformResponseScore);

public sealed record CompetitivenessReport(
    IReadOnlyList<CompetitivenessAssessment> Assessments,
    int TotalVacancies,
    double AverageScore,
    int TopCandidateCount,
    int StrongContenderCount,
    int CompetitiveCount,
    int AverageCount,
    int LongShotCount,
    List<string> TopContributingSkills,
    List<string> MostNeededSkills,
    string OverallVerdict);
