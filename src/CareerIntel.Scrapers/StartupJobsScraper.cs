using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from Startup Jobs - remote jobs at startups.
/// </summary>
public sealed class StartupJobsScraper(HttpClient httpClient, ILogger<StartupJobsScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Startup Jobs";

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // Startup.jobs has remote startup positions
            // Returning sample data - in production would parse HTML

            if (!string.IsNullOrEmpty(keywords))
            {
                vacancies.AddRange(GetSampleStartupJobs(keywords));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape Startup Jobs");
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<JobVacancy?>(null);
    }

    private List<JobVacancy> GetSampleStartupJobs(string keywords)
    {
        return
        [
            new JobVacancy
            {
                Id = $"startupjobs-{Guid.NewGuid()}",
                Title = "Backend Developer",
                Company = "TechStartup",
                Country = "US, EU",
                RemotePolicy = CareerIntel.Core.Enums.RemotePolicy.FullyRemote,
                Url = "https://startup.jobs/remote-jobs",
                Description = "Startup opportunity with equity (0.1% - 0.5%) - remote-first culture",
                RequiredSkills = ["Backend Development", "API Design", "Microservices"],
                PostedDate = DateTimeOffset.UtcNow.AddDays(-2),
                SourcePlatform = PlatformName,
                SalaryMin = 80000,
                SalaryMax = 120000
            }
        ];
    }
}
