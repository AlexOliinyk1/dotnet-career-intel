using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Builds and enriches company profiles by consolidating signals from
/// vacancy data, interview feedback, and tech stack mentions.
/// </summary>
public sealed class CompanyIntelligenceEngine(ILogger<CompanyIntelligenceEngine> logger)
{
    /// <summary>
    /// Enriches a company profile from scraped vacancy data for that company.
    /// Extracts tech stack, salary ranges, seniority distribution, and remote policy.
    /// </summary>
    public CompanyProfile EnrichFromVacancies(
        CompanyProfile profile,
        IReadOnlyList<JobVacancy> companyVacancies)
    {
        logger.LogInformation(
            "Enriching {Company} from {Count} vacancies",
            profile.Name, companyVacancies.Count);

        // Extract real tech stack from all required + preferred skills
        var allSkills = companyVacancies
            .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => s.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        // Merge with existing tech stack (preserve existing, add new)
        foreach (var skill in allSkills)
        {
            if (!profile.RealTechStack.Contains(skill, StringComparer.OrdinalIgnoreCase))
                profile.RealTechStack.Add(skill);
        }

        // Detect interview style from vacancy patterns
        if (string.IsNullOrEmpty(profile.InterviewStyle))
        {
            profile.InterviewStyle = InferInterviewStyle(companyVacancies);
        }

        // Extract pros from vacancy data
        var pros = ExtractPros(companyVacancies);
        foreach (var pro in pros)
        {
            if (!profile.Pros.Contains(pro, StringComparer.OrdinalIgnoreCase))
                profile.Pros.Add(pro);
        }

        // Extract red flags
        var flags = DetectRedFlags(companyVacancies);
        foreach (var flag in flags)
        {
            if (!profile.RedFlags.Contains(flag, StringComparer.OrdinalIgnoreCase))
                profile.RedFlags.Add(flag);
        }

        profile.LastUpdated = DateTimeOffset.UtcNow;
        return profile;
    }

    /// <summary>
    /// Generates a consolidated intelligence report for a company,
    /// combining vacancy signals and interview feedback patterns.
    /// </summary>
    public CompanyIntelligenceReport GenerateReport(
        CompanyProfile profile,
        IReadOnlyList<JobVacancy> companyVacancies,
        IReadOnlyList<InterviewFeedback> companyFeedback)
    {
        logger.LogInformation(
            "Generating intelligence report for {Company}: {Vacancies} vacancies, {Feedback} feedback entries",
            profile.Name, companyVacancies.Count, companyFeedback.Count);

        var report = new CompanyIntelligenceReport
        {
            CompanyName = profile.Name,
            GeneratedDate = DateTimeOffset.UtcNow,
            VacancyCount = companyVacancies.Count,
            FeedbackCount = companyFeedback.Count
        };

        // Hiring velocity: vacancies per month
        var monthGroups = companyVacancies
            .Where(v => v.PostedDate != DateTimeOffset.MinValue)
            .GroupBy(v => new { v.PostedDate.Year, v.PostedDate.Month });
        report.HiringVelocity = monthGroups.Any()
            ? Math.Round(monthGroups.Average(g => g.Count()), 1)
            : 0;

        // Salary range from vacancy data
        var salaries = companyVacancies
            .Where(v => v.SalaryMin.HasValue)
            .ToList();
        if (salaries.Count > 0)
        {
            report.SalaryRangeMin = salaries.Min(v => v.SalaryMin!.Value);
            report.SalaryRangeMax = salaries.Max(v => v.SalaryMax ?? v.SalaryMin!.Value);
            report.SalaryCurrency = salaries.First().SalaryCurrency;
        }

        // Seniority distribution
        report.SeniorityDistribution = companyVacancies
            .GroupBy(v => v.SeniorityLevel)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // Remote policy distribution
        report.RemotePolicyDistribution = companyVacancies
            .GroupBy(v => v.RemotePolicy)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        // Top required skills (frequency-ranked)
        report.TopSkills = companyVacancies
            .SelectMany(v => v.RequiredSkills)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => s.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .Select(g => g.Key)
            .ToList();

        // Interview insights from feedback
        if (companyFeedback.Count > 0)
        {
            report.AverageDifficulty = Math.Round(companyFeedback.Average(f => f.DifficultyRating), 1);
            report.PassRate = companyFeedback.Count(f =>
                string.Equals(f.Outcome, "Passed", StringComparison.OrdinalIgnoreCase))
                / (double)companyFeedback.Count * 100;
            report.PassRate = Math.Round(report.PassRate, 1);

            report.CommonWeakAreas = companyFeedback
                .SelectMany(f => f.WeakAreas)
                .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToList();
        }

        // Recommendation
        report.Recommendation = GenerateRecommendation(profile, report);

        return report;
    }

    private static string InferInterviewStyle(IReadOnlyList<JobVacancy> vacancies)
    {
        var titles = string.Join(" ", vacancies.Select(v => v.Title.ToLowerInvariant()));
        var companies = string.Join(" ", vacancies.Select(v => v.Company.ToLowerInvariant()));

        if (titles.Contains("architect") || titles.Contains("principal") || titles.Contains("staff"))
            return "FAANG-like";
        if (companies.Contains("outsourc") || companies.Contains("consult"))
            return "Outsource";
        if (titles.Contains("startup") || companies.Contains("startup"))
            return "Startup";

        return "EU-product";
    }

    private static List<string> ExtractPros(IReadOnlyList<JobVacancy> vacancies)
    {
        var pros = new List<string>();

        var remoteCount = vacancies.Count(v =>
            v.RemotePolicy == Core.Enums.RemotePolicy.FullyRemote ||
            v.RemotePolicy == Core.Enums.RemotePolicy.RemoteFriendly);
        if (remoteCount > vacancies.Count * 0.5)
            pros.Add("Remote-friendly culture");

        var withSalary = vacancies.Where(v => v.SalaryMin.HasValue).ToList();
        if (withSalary.Count > vacancies.Count * 0.5)
            pros.Add("Transparent salary ranges");

        if (vacancies.Any(v => v.PreferredSkills.Count > 3))
            pros.Add("Invests in diverse tech stack");

        return pros;
    }

    private static List<string> DetectRedFlags(IReadOnlyList<JobVacancy> vacancies)
    {
        var flags = new List<string>();

        // Too many required skills may indicate unrealistic expectations
        var avgRequired = vacancies.Average(v => v.RequiredSkills.Count);
        if (avgRequired > 10)
            flags.Add("Unrealistic skill requirements (avg >10 required skills)");

        // Frequent reposting (same title appears multiple times)
        var titleGroups = vacancies
            .GroupBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 3);
        if (titleGroups.Any())
            flags.Add("Frequent reposting — possible retention issues");

        // No salary data across all vacancies
        if (vacancies.All(v => !v.SalaryMin.HasValue))
            flags.Add("No salary transparency across all postings");

        return flags;
    }

    private static string GenerateRecommendation(CompanyProfile profile, CompanyIntelligenceReport report)
    {
        var parts = new List<string>();

        if (report.PassRate > 0 && report.PassRate < 30)
            parts.Add("Low pass rate — prepare extensively before applying");
        else if (report.PassRate >= 70)
            parts.Add("High pass rate — good odds with standard preparation");

        if (report.AverageDifficulty >= 8)
            parts.Add("Very high difficulty bar — expect system design and deep technical rounds");
        else if (report.AverageDifficulty >= 6)
            parts.Add("Moderate difficulty — standard technical interview preparation recommended");

        if (profile.RedFlags.Count > 0)
            parts.Add($"Watch for: {string.Join("; ", profile.RedFlags.Take(2))}");

        if (profile.Pros.Count > 0)
            parts.Add($"Strengths: {string.Join("; ", profile.Pros.Take(2))}");

        return parts.Count > 0
            ? string.Join(". ", parts) + "."
            : "Insufficient data for recommendation — continue collecting signals.";
    }
}

/// <summary>
/// Consolidated intelligence report for a specific company.
/// </summary>
public sealed class CompanyIntelligenceReport
{
    public string CompanyName { get; set; } = string.Empty;
    public DateTimeOffset GeneratedDate { get; set; }
    public int VacancyCount { get; set; }
    public int FeedbackCount { get; set; }

    /// <summary>Average vacancies posted per month.</summary>
    public double HiringVelocity { get; set; }

    public decimal? SalaryRangeMin { get; set; }
    public decimal? SalaryRangeMax { get; set; }
    public string SalaryCurrency { get; set; } = string.Empty;

    public Dictionary<string, int> SeniorityDistribution { get; set; } = [];
    public Dictionary<string, int> RemotePolicyDistribution { get; set; } = [];
    public List<string> TopSkills { get; set; } = [];

    public double AverageDifficulty { get; set; }
    public double PassRate { get; set; }
    public List<string> CommonWeakAreas { get; set; } = [];

    public string Recommendation { get; set; } = string.Empty;
}
