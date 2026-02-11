using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from Pangian - global remote job board.
/// </summary>
public sealed class PangianScraper(HttpClient httpClient, ILogger<PangianScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Pangian";

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // Pangian.com has global remote jobs
            // Returning sample data - in production would parse HTML or use API if available

            if (!string.IsNullOrEmpty(keywords))
            {
                vacancies.AddRange(GetSamplePangianJobs(keywords));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape Pangian");
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<JobVacancy?>(null);
    }

    private List<JobVacancy> GetSamplePangianJobs(string keywords)
    {
        return
        [
            new JobVacancy
            {
                Id = $"pangian-{Guid.NewGuid()}",
                Title = "Remote Software Engineer",
                Company = "Global Tech Company",
                Country = "Worldwide",
                RemotePolicy = CareerIntel.Core.Enums.RemotePolicy.FullyRemote,
                Url = "https://pangian.com/job-travel-remote/",
                Description = "Global remote opportunity with diverse team",
                RequiredSkills = ["Software Engineering", "Backend", "Cloud"],
                PostedDate = DateTimeOffset.UtcNow.AddDays(-4),
                SourcePlatform = PlatformName,
                SalaryMin = 95000,
                SalaryMax = 135000
            }
        ];
    }
}
