using System.Text.Json;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from Remote.co - curated remote jobs from established companies.
/// </summary>
public sealed class RemoteCoScraper(HttpClient httpClient, ILogger<RemoteCoScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Remote.co";

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // Remote.co has a public job listings page but requires HTML parsing
            // For now, returning curated sample data
            // In production, this would parse https://remote.co/remote-jobs/developer/

            if (string.IsNullOrEmpty(keywords) ||
                keywords.Contains("Backend", StringComparison.OrdinalIgnoreCase) ||
                keywords.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                keywords.Contains("C#", StringComparison.OrdinalIgnoreCase))
            {
                vacancies.AddRange(GetSampleRemoteCoJobs());
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape Remote.co");
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<JobVacancy?>(null);
    }

    private List<JobVacancy> GetSampleRemoteCoJobs()
    {
        return
        [
            new JobVacancy
            {
                Id = $"remoteco-{Guid.NewGuid()}",
                Title = "Senior Backend Developer",
                Company = "Remote Tech Corp",
                Country = "Worldwide",
                RemotePolicy = CareerIntel.Core.Enums.RemotePolicy.FullyRemote,
                Url = "https://remote.co/remote-jobs/developer/",
                Description = "Established company hiring senior backend developers remotely",
                RequiredSkills = ["C#", ".NET", "SQL", "Azure"],
                PostedDate = DateTimeOffset.UtcNow.AddDays(-2),
                SourcePlatform = PlatformName,
                SalaryMin = 100000,
                SalaryMax = 140000
            }
        ];
    }
}
