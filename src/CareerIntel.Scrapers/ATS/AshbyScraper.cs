using System.Text.Json;
using System.Text.Json.Serialization;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers.ATS;

/// <summary>
/// Scrapes job listings from Ashby ATS.
/// Ashby provides a public JSON API for job boards.
/// </summary>
public sealed class AshbyScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public AshbyScraper(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<JobVacancy>> ScrapeCompanyJobsAsync(
        string companyName,
        string ashbyId,
        CancellationToken cancellationToken = default)
    {
        var jobs = new List<JobVacancy>();

        try
        {
            // Ashby public API endpoint
            var url = $"https://api.ashbyhq.com/posting-api/job-board/{ashbyId}";

            _logger.LogInformation("Scraping Ashby jobs for {Company} at {Url}", companyName, url);

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
            var ashbyResponse = JsonSerializer.Deserialize<AshbyResponse>(json);

            if (ashbyResponse?.Jobs == null || ashbyResponse.Jobs.Count == 0)
            {
                _logger.LogWarning("No jobs found for {Company}", companyName);
                return jobs;
            }

            foreach (var job in ashbyResponse.Jobs)
            {
                jobs.Add(new JobVacancy
                {
                    Id = $"ashby-{ashbyId}-{job.Id}",
                    Title = job.Title ?? "Unknown Position",
                    Company = companyName,
                    Country = job.Location ?? "Remote",
                    City = job.Address?.City ?? string.Empty,
                    RemotePolicy = DetectRemotePolicy(job),
                    Url = job.JobUrl ?? $"https://jobs.ashbyhq.com/{ashbyId}/{job.Id}",
                    Description = job.DescriptionHtml ?? job.DescriptionPlain ?? job.Title ?? "",
                    RequiredSkills = ExtractSkills(job.DescriptionPlain ?? job.DescriptionHtml),
                    PostedDate = job.PublishedAt,
                    SourcePlatform = "Ashby"
                });
            }

            _logger.LogInformation("Found {Count} jobs for {Company}", jobs.Count, companyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping Ashby jobs for {Company}", companyName);
        }

        return jobs;
    }

    private static Core.Enums.RemotePolicy DetectRemotePolicy(AshbyJob job)
    {
        var isRemote = job.IsRemote == true;
        if (isRemote)
            return Core.Enums.RemotePolicy.FullyRemote;

        var location = job.Location ?? "";
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

// Ashby API response models
internal class AshbyResponse
{
    [JsonPropertyName("jobs")]
    public List<AshbyJob>? Jobs { get; set; }
}

internal class AshbyJob
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("address")]
    public AshbyAddress? Address { get; set; }

    [JsonPropertyName("descriptionHtml")]
    public string? DescriptionHtml { get; set; }

    [JsonPropertyName("descriptionPlain")]
    public string? DescriptionPlain { get; set; }

    [JsonPropertyName("jobUrl")]
    public string? JobUrl { get; set; }

    [JsonPropertyName("isRemote")]
    public bool? IsRemote { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }
}

internal class AshbyAddress
{
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}
