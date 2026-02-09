using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

public sealed class AutoApplyEngine(ILogger<AutoApplyEngine> logger)
{
    private readonly HashSet<string> _excludedCompanies = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds companies to the exclusion list so they are skipped during application preparation.
    /// </summary>
    public void ExcludeCompanies(IEnumerable<string> companyNames)
    {
        foreach (var name in companyNames)
            _excludedCompanies.Add(name);
    }

    /// <summary>
    /// Identifies candidate vacancies for auto-apply by filtering above a match score threshold.
    /// Creates JobApplication records with Status = Pending, sorted by match score descending.
    /// </summary>
    public List<JobApplication> IdentifyCandidates(
        IReadOnlyList<JobVacancy> vacancies,
        UserProfile profile,
        double minMatchScore = 60)
    {
        logger.LogInformation(
            "Identifying candidates from {Count} vacancies with min score {MinScore}",
            vacancies.Count, minMatchScore);

        var candidates = new List<JobApplication>();

        foreach (var vacancy in vacancies)
        {
            var score = vacancy.MatchScore?.OverallScore ?? 0;
            if (score < minMatchScore)
                continue;

            var applyMethod = DetermineApplyMethod(vacancy);

            var application = new JobApplication
            {
                VacancyId = vacancy.Id,
                Company = vacancy.Company,
                VacancyTitle = vacancy.Title,
                VacancyUrl = vacancy.Url,
                MatchScore = score,
                ApplyMethod = applyMethod,
                ApplyUrl = vacancy.Url,
                Status = ApplicationStatus.Pending,
                CreatedDate = DateTimeOffset.UtcNow
            };

            candidates.Add(application);
        }

        candidates.Sort((a, b) => b.MatchScore.CompareTo(a.MatchScore));

        logger.LogInformation(
            "Identified {Count} candidates above {MinScore} threshold",
            candidates.Count, minMatchScore);

        return candidates;
    }

    /// <summary>
    /// Prepares applications by marking resume versions and cover letter paths.
    /// Updates status to ResumeReady. Skips already-applied or excluded companies.
    /// </summary>
    public List<JobApplication> PrepareApplications(
        List<JobApplication> candidates,
        UserProfile profile,
        IReadOnlyList<JobVacancy> vacancies)
    {
        logger.LogInformation("Preparing {Count} candidate applications", candidates.Count);

        var vacancyLookup = vacancies.ToDictionary(v => v.Id, v => v);
        var prepared = new List<JobApplication>();

        foreach (var candidate in candidates)
        {
            // Skip already applied
            if (candidate.Status is ApplicationStatus.Applied
                or ApplicationStatus.Screening
                or ApplicationStatus.Interview
                or ApplicationStatus.Offer)
            {
                logger.LogDebug("Skipping {Company}/{Title} — already in pipeline",
                    candidate.Company, candidate.VacancyTitle);
                continue;
            }

            // Skip excluded companies
            if (_excludedCompanies.Contains(candidate.Company))
            {
                logger.LogDebug("Skipping {Company} — company is excluded", candidate.Company);
                continue;
            }

            // Determine resume version based on match characteristics
            var vacancy = vacancyLookup.GetValueOrDefault(candidate.VacancyId);
            candidate.ResumeVersion = DetermineResumeVersion(vacancy, profile);
            candidate.CoverLetterPath = GenerateCoverLetterPath(candidate);
            candidate.Status = ApplicationStatus.ResumeReady;

            prepared.Add(candidate);
        }

        logger.LogInformation("Prepared {Count} applications for review", prepared.Count);
        return prepared;
    }

    /// <summary>
    /// Groups ready applications into a batch for human review before submission.
    /// </summary>
    public ApplyBatch GenerateApplyBatch(
        List<JobApplication> readyApplications,
        int batchSize = 10)
    {
        var batchItems = readyApplications
            .Where(a => a.Status == ApplicationStatus.ResumeReady)
            .Take(batchSize)
            .ToList();

        var estimatedMinutesPerApplication = 5;

        var batch = new ApplyBatch
        {
            Applications = batchItems,
            TotalCount = batchItems.Count,
            EstimatedMinutes = batchItems.Count * estimatedMinutesPerApplication,
            ApplyUrls = batchItems.Select(a => a.ApplyUrl).ToList(),
            CoverLetterPaths = batchItems.Select(a => a.CoverLetterPath).ToList(),
            CreatedDate = DateTimeOffset.UtcNow
        };

        logger.LogInformation(
            "Generated batch of {Count} applications, estimated {Minutes} minutes",
            batch.TotalCount, batch.EstimatedMinutes);

        return batch;
    }

    /// <summary>
    /// Records the outcome of an application, updating status and timestamps.
    /// </summary>
    public void RecordOutcome(
        JobApplication application,
        ApplicationStatus newStatus,
        string? notes = null)
    {
        var previousStatus = application.Status;
        application.Status = newStatus;

        if (newStatus == ApplicationStatus.Applied && !application.AppliedDate.HasValue)
            application.AppliedDate = DateTimeOffset.UtcNow;

        if (newStatus is ApplicationStatus.Screening
            or ApplicationStatus.Interview
            or ApplicationStatus.Offer
            or ApplicationStatus.Rejected
            or ApplicationStatus.Ghosted)
        {
            application.ResponseDate ??= DateTimeOffset.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(notes))
            application.ResponseNotes = notes;

        logger.LogInformation(
            "Application {Company}/{Title} status changed: {Previous} -> {New}",
            application.Company, application.VacancyTitle, previousStatus, newStatus);
    }

    /// <summary>
    /// Generates a dashboard summarizing all application activity.
    /// </summary>
    public ApplyDashboard GetDashboard(IReadOnlyList<JobApplication> allApplications)
    {
        var byStatus = allApplications
            .GroupBy(a => a.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        var applied = allApplications
            .Where(a => a.Status != ApplicationStatus.Pending
                     && a.Status != ApplicationStatus.ResumeReady
                     && a.Status != ApplicationStatus.Expired)
            .ToList();

        var responded = applied
            .Where(a => a.Status is ApplicationStatus.Screening
                or ApplicationStatus.Interview
                or ApplicationStatus.Offer
                or ApplicationStatus.Rejected)
            .ToList();

        var responseRate = applied.Count > 0
            ? (double)responded.Count / applied.Count * 100
            : 0;

        var avgDaysToResponse = responded
            .Where(a => a.AppliedDate.HasValue && a.ResponseDate.HasValue)
            .Select(a => (a.ResponseDate!.Value - a.AppliedDate!.Value).TotalDays)
            .DefaultIfEmpty(0)
            .Average();

        var topCompanies = allApplications
            .GroupBy(a => a.Company, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        // Weekly velocity: applications submitted per week over the last 4 weeks
        var fourWeeksAgo = DateTimeOffset.UtcNow.AddDays(-28);
        var weeklyVelocity = applied
            .Where(a => a.AppliedDate.HasValue && a.AppliedDate.Value >= fourWeeksAgo)
            .GroupBy(a => GetWeekNumber(a.AppliedDate!.Value))
            .OrderBy(g => g.Key)
            .Select(g => new WeeklyCount { Week = g.Key, Count = g.Count() })
            .ToList();

        return new ApplyDashboard
        {
            StatusBreakdown = byStatus,
            TotalApplications = allApplications.Count,
            TotalApplied = applied.Count,
            TotalResponded = responded.Count,
            ResponseRate = Math.Round(responseRate, 1),
            AverageDaysToResponse = Math.Round(avgDaysToResponse, 1),
            TopCompanies = topCompanies,
            WeeklyVelocity = weeklyVelocity,
            GeneratedDate = DateTimeOffset.UtcNow
        };
    }

    private static string DetermineApplyMethod(JobVacancy vacancy)
    {
        var url = (vacancy.Url ?? string.Empty).ToLowerInvariant();

        if (url.Contains("apply") || url.Contains("careers") || url.Contains("jobs"))
            return "platform";

        if (url.Contains("djinni") || url.Contains("dou.ua"))
            return "platform";

        return "manual";
    }

    private static string DetermineResumeVersion(JobVacancy? vacancy, UserProfile profile)
    {
        if (vacancy is null)
            return "default";

        // Choose resume version based on dominant tech in the vacancy
        var skills = vacancy.RequiredSkills;

        if (skills.Any(s => s.Contains("dotnet", StringComparison.OrdinalIgnoreCase)
                         || s.Contains(".net", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("csharp", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("C#", StringComparison.OrdinalIgnoreCase)))
            return "dotnet-focused";

        if (skills.Any(s => s.Contains("azure", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("cloud", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("devops", StringComparison.OrdinalIgnoreCase)))
            return "cloud-devops";

        if (skills.Any(s => s.Contains("react", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("angular", StringComparison.OrdinalIgnoreCase)
                         || s.Contains("frontend", StringComparison.OrdinalIgnoreCase)))
            return "fullstack";

        return "default";
    }

    private static string GenerateCoverLetterPath(JobApplication application)
    {
        var sanitizedCompany = application.Company
            .Replace(" ", "-")
            .Replace("/", "-")
            .Replace("\\", "-")
            .ToLowerInvariant();

        var sanitizedTitle = application.VacancyTitle
            .Replace(" ", "-")
            .Replace("/", "-")
            .Replace("\\", "-")
            .ToLowerInvariant();

        return $"cover-letters/{sanitizedCompany}_{sanitizedTitle}_{application.VacancyId}.md";
    }

    private static int GetWeekNumber(DateTimeOffset date)
    {
        var startOfYear = new DateTimeOffset(date.Year, 1, 1, 0, 0, 0, date.Offset);
        var dayOfYear = (date - startOfYear).Days;
        return dayOfYear / 7 + 1;
    }

    /// <summary>
    /// Represents a batch of applications ready for human review before submission.
    /// </summary>
    public sealed class ApplyBatch
    {
        public List<JobApplication> Applications { get; set; } = [];
        public int TotalCount { get; set; }
        public int EstimatedMinutes { get; set; }
        public List<string> ApplyUrls { get; set; } = [];
        public List<string> CoverLetterPaths { get; set; } = [];
        public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Dashboard summarizing all application pipeline activity.
    /// </summary>
    public sealed class ApplyDashboard
    {
        public Dictionary<ApplicationStatus, int> StatusBreakdown { get; set; } = [];
        public int TotalApplications { get; set; }
        public int TotalApplied { get; set; }
        public int TotalResponded { get; set; }
        public double ResponseRate { get; set; }
        public double AverageDaysToResponse { get; set; }
        public Dictionary<string, int> TopCompanies { get; set; } = [];
        public List<WeeklyCount> WeeklyVelocity { get; set; } = [];
        public DateTimeOffset GeneratedDate { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class WeeklyCount
    {
        public int Week { get; set; }
        public int Count { get; set; }
    }
}
