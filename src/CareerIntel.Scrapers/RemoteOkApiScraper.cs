using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// RemoteOK API scraper - Uses official public API (no scraping needed!)
/// API: https://remoteok.com/api
/// Free, no authentication required
/// </summary>
public class RemoteOkApiScraper : BaseScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public override string PlatformName => "RemoteOK-API";

    public RemoteOkApiScraper(HttpClient httpClient, ILogger<RemoteOkApiScraper> logger)
        : base(httpClient, logger, null)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var jobs = new List<JobVacancy>();

        try
        {
            // RemoteOK API returns JSON directly
            var apiUrl = "https://remoteok.com/api";

            var response = await _httpClient.GetStringAsync(apiUrl, cancellationToken);
            if (string.IsNullOrEmpty(response))
            {
                return jobs;
            }

            var apiJobs = System.Text.Json.JsonSerializer.Deserialize<List<RemoteOkApiJob>>(response);
            if (apiJobs == null || apiJobs.Count == 0)
            {
                return jobs;
            }

            // First item is metadata, skip it
            foreach (var apiJob in apiJobs.Skip(1))
            {
                // Filter by keywords
                if (!string.IsNullOrEmpty(keywords))
                {
                    var matchesKeyword =
                        (apiJob.Position?.Contains(keywords, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (apiJob.Description?.Contains(keywords, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (apiJob.Tags?.Any(t => t.Contains(keywords, StringComparison.OrdinalIgnoreCase)) ?? false);

                    if (!matchesKeyword)
                        continue;
                }

                var vacancy = new JobVacancy
                {
                    Id = $"remoteok-{apiJob.Id}",
                    Title = apiJob.Position ?? "Unknown Position",
                    Company = apiJob.Company ?? "Unknown Company",
                    Description = apiJob.Description ?? "",
                    Country = "Remote",
                    City = "Worldwide",
                    RemotePolicy = RemotePolicy.FullyRemote,
                    Url = $"https://remoteok.com/remote-jobs/{apiJob.Slug}",
                    SourcePlatform = PlatformName,
                    PostedDate = apiJob.Date != null ? DateTimeOffset.FromUnixTimeSeconds(apiJob.Date.Value) : DateTimeOffset.UtcNow,
                    ScrapedDate = DateTimeOffset.UtcNow
                };

                // Parse salary if available
                if (apiJob.SalaryMin.HasValue && apiJob.SalaryMin > 0)
                {
                    vacancy.SalaryMin = apiJob.SalaryMin;
                    vacancy.SalaryMax = apiJob.SalaryMax ?? apiJob.SalaryMin;
                    vacancy.SalaryCurrency = "USD";
                }

                // Add tags as skills
                if (apiJob.Tags != null && apiJob.Tags.Any())
                {
                    vacancy.RequiredSkills = apiJob.Tags.ToList();
                }

                // Detect seniority from title
                vacancy.SeniorityLevel = DetectSeniorityLevel(vacancy.Title);

                jobs.Add(vacancy);

                if (jobs.Count >= maxPages * 20) // ~20 jobs per "page"
                    break;
            }

            return jobs.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Platform}] Failed to scrape RemoteOK API", PlatformName);
            return jobs.AsReadOnly();
        }
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        // API already returns full details, no need to scrape individual pages
        return null;
    }

    private SeniorityLevel DetectSeniorityLevel(string title)
    {
        var lowerTitle = title.ToLowerInvariant();

        if (lowerTitle.Contains("senior") || lowerTitle.Contains("sr.") || lowerTitle.Contains("lead"))
            return SeniorityLevel.Senior;
        if (lowerTitle.Contains("junior") || lowerTitle.Contains("jr."))
            return SeniorityLevel.Junior;
        if (lowerTitle.Contains("principal") || lowerTitle.Contains("staff"))
            return SeniorityLevel.Principal;
        if (lowerTitle.Contains("architect"))
            return SeniorityLevel.Architect;

        return SeniorityLevel.Middle;
    }
}

// API Response Model
internal class RemoteOkApiJob
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("company_logo")]
    public string? CompanyLogo { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("date")]
    public long? Date { get; set; }

    [JsonPropertyName("salary_min")]
    public decimal? SalaryMin { get; set; }

    [JsonPropertyName("salary_max")]
    public decimal? SalaryMax { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
