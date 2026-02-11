using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers.ATS;

/// <summary>
/// Scrapes job listings from Workable ATS.
/// Workable provides a public JSON API for job boards.
/// </summary>
public sealed class WorkableScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public WorkableScraper(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<JobVacancy>> ScrapeCompanyJobsAsync(
        string companyName,
        string workableId,
        CancellationToken cancellationToken = default)
    {
        var jobs = new List<JobVacancy>();

        try
        {
            // Workable public API endpoint
            var url = $"https://apply.workable.com/api/v1/widget/accounts/{workableId}";

            _logger.LogInformation("Scraping Workable jobs for {Company} at {Url}", companyName, url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; CareerIntel/1.0)");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to scrape {Company}: {StatusCode}", companyName, response.StatusCode);
                return jobs;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var workableResponse = JsonSerializer.Deserialize<WorkableResponse>(json);

            if (workableResponse?.Jobs == null || workableResponse.Jobs.Count == 0)
            {
                _logger.LogWarning("No jobs found for {Company}", companyName);
                return jobs;
            }

            foreach (var job in workableResponse.Jobs)
            {
                jobs.Add(new JobVacancy
                {
                    Id = $"workable-{workableId}-{job.Shortcode ?? job.Id}",
                    Title = job.Title ?? "Unknown Position",
                    Company = companyName,
                    Country = job.Location?.CountryCode ?? job.Location?.Country ?? "Remote",
                    City = job.Location?.City ?? string.Empty,
                    RemotePolicy = DetectRemotePolicy(job),
                    Url = job.Url ?? $"https://apply.workable.com/{workableId}/j/{job.Shortcode}/",
                    Description = job.Description ?? job.Title ?? "",
                    RequiredSkills = ExtractSkills(job.Description ?? job.Requirements),
                    PostedDate = job.PublishedOn,
                    SourcePlatform = "Workable"
                });
            }

            _logger.LogInformation("Found {Count} jobs for {Company}", jobs.Count, companyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Workable jobs for {Company}", companyName);
        }

        return jobs;
    }

    private static Core.Enums.RemotePolicy DetectRemotePolicy(WorkableJob job)
    {
        if (job.Telecommute == true)
            return Core.Enums.RemotePolicy.FullyRemote;

        var location = job.Location?.City ?? job.Location?.Country ?? "";
        var lower = location.ToLowerInvariant();

        if (lower.Contains("remote") || lower.Contains("anywhere"))
            return Core.Enums.RemotePolicy.FullyRemote;
        if (lower.Contains("hybrid"))
            return Core.Enums.RemotePolicy.Hybrid;

        return Core.Enums.RemotePolicy.OnSite;
    }

    private static List<string> ExtractSkills(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        var skills = new List<string>();
        var lower = content.ToLowerInvariant();

        var commonSkills = new[]
        {
            "C#", ".NET", "ASP.NET", "Azure", "AWS", "SQL", "React", "Angular",
            "Node.js", "TypeScript", "JavaScript", "Python", "Go", "Rust",
            "Docker", "Kubernetes", "PostgreSQL", "MongoDB", "Redis",
            "Kafka", "RabbitMQ", "gRPC", "GraphQL", "Terraform"
        };

        foreach (var skill in commonSkills)
        {
            if (lower.Contains(skill.ToLowerInvariant()))
                skills.Add(skill);
        }

        return skills.Distinct().ToList();
    }
}

// Workable API response models
internal class WorkableResponse
{
    [JsonPropertyName("jobs")]
    public List<WorkableJob>? Jobs { get; set; }
}

internal class WorkableJob
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("shortcode")]
    public string? Shortcode { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("requirements")]
    public string? Requirements { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("location")]
    public WorkableLocation? Location { get; set; }

    [JsonPropertyName("telecommute")]
    public bool? Telecommute { get; set; }

    [JsonPropertyName("published_on")]
    public DateTimeOffset PublishedOn { get; set; }
}

internal class WorkableLocation
{
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}
