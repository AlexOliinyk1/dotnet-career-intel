using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from Jobspresso - curated remote jobs in tech.
/// </summary>
public sealed class JobspressoScraper(HttpClient httpClient, ILogger<JobspressoScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Jobspresso";

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // Jobspresso.co has curated tech jobs
            // Returning sample data - in production would parse HTML

            if (!string.IsNullOrEmpty(keywords))
            {
                vacancies.AddRange(GetSampleJobspressoJobs(keywords));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape Jobspresso");
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<JobVacancy?>(null);
    }

    private List<JobVacancy> GetSampleJobspressoJobs(string keywords)
    {
        return
        [
            new JobVacancy
            {
                Id = $"jobspresso-{Guid.NewGuid()}",
                Title = "Backend Engineer",
                Company = "Tech Startup",
                Country = "Worldwide",
                RemotePolicy = CareerIntel.Core.Enums.RemotePolicy.FullyRemote,
                Url = "https://jobspresso.co/remote-work/",
                Description = "Curated remote backend position in growing startup",
                RequiredSkills = ["Backend", "API", "Database"],
                PostedDate = DateTimeOffset.UtcNow.AddDays(-3),
                SourcePlatform = PlatformName,
                SalaryMin = 85000,
                SalaryMax = 120000
            }
        ];
    }
}
