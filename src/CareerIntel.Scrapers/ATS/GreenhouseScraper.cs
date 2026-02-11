using System.Text.Json;
using System.Text.Json.Serialization;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers.ATS;

/// <summary>
/// Scrapes job listings from Greenhouse ATS (used by many companies).
/// Greenhouse provides a public JSON API for job listings.
/// </summary>
public sealed class GreenhouseScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GreenhouseScraper> _logger;

    public GreenhouseScraper(HttpClient httpClient, ILogger<GreenhouseScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Scrapes jobs from a company's Greenhouse board.
    /// Example: https://boards-api.greenhouse.io/v1/boards/company-name/jobs
    /// </summary>
    public async Task<List<JobVacancy>> ScrapeCompanyJobsAsync(
        string companyName,
        string greenhouseId,
        CancellationToken cancellationToken = default)
    {
        var jobs = new List<JobVacancy>();

        try
        {
            // Greenhouse API endpoint
            var url = $"https://boards-api.greenhouse.io/v1/boards/{greenhouseId}/jobs";

            _logger.LogInformation("Scraping Greenhouse jobs for {Company} at {Url}", companyName, url);

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to scrape {Company}: {StatusCode}", companyName, response.StatusCode);
                return jobs;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var greenhouseResponse = JsonSerializer.Deserialize<GreenhouseResponse>(json);

            if (greenhouseResponse?.Jobs == null)
            {
                _logger.LogWarning("No jobs found for {Company}", companyName);
                return jobs;
            }

            foreach (var job in greenhouseResponse.Jobs)
            {
                jobs.Add(new JobVacancy
                {
                    Id = $"greenhouse-{greenhouseId}-{job.Id}",
                    Title = job.Title ?? "Unknown Position",
                    Company = companyName,
                    Country = job.Location?.Name ?? "Remote",
                    RemotePolicy = DetectRemotePolicy(job.Location?.Name),
                    Url = job.AbsoluteUrl ?? $"https://boards.greenhouse.io/{greenhouseId}",
                    Description = job.Content ?? job.Title ?? "",
                    RequiredSkills = ExtractSkills(job.Content),
                    PostedDate = job.UpdatedAt,
                    SourcePlatform = "Greenhouse"
                });
            }

            _logger.LogInformation("Found {Count} jobs for {Company}", jobs.Count, companyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Greenhouse jobs for {Company}", companyName);
        }

        return jobs;
    }

    private static CareerIntel.Core.Enums.RemotePolicy DetectRemotePolicy(string? location)
    {
        if (string.IsNullOrEmpty(location))
            return CareerIntel.Core.Enums.RemotePolicy.Unknown;

        var lower = location.ToLowerInvariant();

        if (lower.Contains("remote") || lower.Contains("anywhere") || lower.Contains("worldwide"))
            return CareerIntel.Core.Enums.RemotePolicy.FullyRemote;

        if (lower.Contains("hybrid"))
            return CareerIntel.Core.Enums.RemotePolicy.Hybrid;

        return CareerIntel.Core.Enums.RemotePolicy.OnSite;
    }

    private static List<string> ExtractSkills(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        var skills = new List<string>();
        var lower = content.ToLowerInvariant();

        // Common tech skills
        var commonSkills = new[]
        {
            "C#", ".NET", "ASP.NET", "Azure", "AWS", "SQL", "React", "Angular",
            "Node.js", "TypeScript", "JavaScript", "Python", "Go", "Rust",
            "Docker", "Kubernetes", "PostgreSQL", "MongoDB", "Redis"
        };

        foreach (var skill in commonSkills)
        {
            if (lower.Contains(skill.ToLowerInvariant()))
            {
                skills.Add(skill);
            }
        }

        return skills.Distinct().ToList();
    }
}

// Greenhouse API response models
internal class GreenhouseResponse
{
    [JsonPropertyName("jobs")]
    public List<GreenhouseJob>? Jobs { get; set; }
}

internal class GreenhouseJob
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("location")]
    public GreenhouseLocation? Location { get; set; }

    [JsonPropertyName("absolute_url")]
    public string? AbsoluteUrl { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("departments")]
    public List<GreenhouseDepartment>? Departments { get; set; }
}

internal class GreenhouseLocation
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class GreenhouseDepartment
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
