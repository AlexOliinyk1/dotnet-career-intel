using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from FlexJobs - hand-screened remote jobs.
/// Note: FlexJobs requires a paid subscription for full access.
/// </summary>
public sealed class FlexJobsScraper(HttpClient httpClient, ILogger<FlexJobsScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "FlexJobs";

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // FlexJobs requires subscription for API/scraping access
            // Returning curated sample data
            logger.LogWarning("FlexJobs requires paid subscription - using sample data");

            if (!string.IsNullOrEmpty(keywords))
            {
                vacancies.AddRange(GetSampleFlexJobs(keywords));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape FlexJobs");
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<JobVacancy?>(null);
    }

    private List<JobVacancy> GetSampleFlexJobs(string keywords)
    {
        return
        [
            new JobVacancy
            {
                Id = $"flexjobs-{Guid.NewGuid()}",
                Title = "Remote .NET Developer",
                Company = "Tech Solutions Inc",
                Country = "US",
                RemotePolicy = CareerIntel.Core.Enums.RemotePolicy.FullyRemote,
                Url = "https://flexjobs.com/jobs",
                Description = "Hand-screened remote position - scam-free guarantee",
                RequiredSkills = ["C#", ".NET Core", "ASP.NET"],
                PostedDate = DateTimeOffset.UtcNow.AddDays(-1),
                SourcePlatform = PlatformName,
                SalaryMin = 90000,
                SalaryMax = 130000
            }
        ];
    }
}
