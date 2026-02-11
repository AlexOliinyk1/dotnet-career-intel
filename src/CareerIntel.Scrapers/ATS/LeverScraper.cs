using System.Text.Json;
using System.Text.Json.Serialization;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers.ATS;

/// <summary>
/// Scrapes job listings from Lever ATS (used by many companies).
/// Lever provides a public JSON API for job postings.
/// </summary>
public sealed class LeverScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LeverScraper> _logger;

    public LeverScraper(HttpClient httpClient, ILogger<LeverScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Scrapes jobs from a company's Lever board.
    /// Example: https://api.lever.co/v0/postings/company-name
    /// </summary>
    public async Task<List<JobVacancy>> ScrapeCompanyJobsAsync(
        string companyName,
        string leverId,
        CancellationToken cancellationToken = default)
    {
        var jobs = new List<JobVacancy>();

        try
        {
            // Lever API endpoint
            var url = $"https://api.lever.co/v0/postings/{leverId}";

            _logger.LogInformation("Scraping Lever jobs for {Company} at {Url}", companyName, url);

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to scrape {Company}: {StatusCode}", companyName, response.StatusCode);
                return jobs;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var leverJobs = JsonSerializer.Deserialize<List<LeverJob>>(json);

            if (leverJobs == null)
            {
                _logger.LogWarning("No jobs found for {Company}", companyName);
                return jobs;
            }

            foreach (var job in leverJobs)
            {
                jobs.Add(new JobVacancy
                {
                    Id = $"lever-{leverId}-{job.Id}",
                    Title = job.Text ?? "Unknown Position",
                    Company = companyName,
                    Country = job.Categories?.Location ?? "Remote",
                    RemotePolicy = DetectRemotePolicy(job.Categories?.Location, job.Categories?.Commitment),
                    Url = job.HostedUrl ?? $"https://jobs.lever.co/{leverId}",
                    Description = job.Description ?? job.DescriptionPlain ?? job.Text ?? "",
                    RequiredSkills = ExtractSkills(job.DescriptionPlain ?? job.Description),
                    PostedDate = job.CreatedAt,
                    SourcePlatform = "Lever",
                    Departments = job.Categories?.Team != null ? [job.Categories.Team] : []
                });
            }

            _logger.LogInformation("Found {Count} jobs for {Company}", jobs.Count, companyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Lever jobs for {Company}", companyName);
        }

        return jobs;
    }

    private static CareerIntel.Core.Enums.RemotePolicy DetectRemotePolicy(string? location, string? commitment)
    {
        var combined = $"{location} {commitment}".ToLowerInvariant();

        if (combined.Contains("remote") || combined.Contains("anywhere") || combined.Contains("worldwide"))
            return CareerIntel.Core.Enums.RemotePolicy.FullyRemote;

        if (combined.Contains("hybrid"))
            return CareerIntel.Core.Enums.RemotePolicy.Hybrid;

        return CareerIntel.Core.Enums.RemotePolicy.Unknown;
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
            "Docker", "Kubernetes", "PostgreSQL", "MongoDB", "Redis", "API",
            "Microservices", "REST", "GraphQL"
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

// Lever API response models
internal class LeverJob
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("hostedUrl")]
    public string? HostedUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("descriptionPlain")]
    public string? DescriptionPlain { get; set; }

    [JsonPropertyName("categories")]
    public LeverCategories? Categories { get; set; }
}

internal class LeverCategories
{
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("commitment")]
    public string? Commitment { get; set; }
}
