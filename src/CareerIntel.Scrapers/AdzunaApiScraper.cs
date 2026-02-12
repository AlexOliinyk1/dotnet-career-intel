using System.Text.Json.Serialization;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Adzuna API scraper - Official API for job aggregation
/// API Docs: https://developer.adzuna.com/
/// Free tier: 250 calls/month
/// Aggregates 1000+ job boards worldwide
///
/// Setup:
/// 1. Register at https://developer.adzuna.com/
/// 2. Get APP_ID and APP_KEY
/// 3. Set environment variables: ADZUNA_APP_ID, ADZUNA_APP_KEY
/// </summary>
public class AdzunaApiScraper : BaseScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public override string PlatformName => "Adzuna";

    private readonly string? _appId;
    private readonly string? _appKey;

    public AdzunaApiScraper(HttpClient httpClient, ILogger<AdzunaApiScraper> logger)
        : base(httpClient, logger, null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _appId = Environment.GetEnvironmentVariable("ADZUNA_APP_ID");
        _appKey = Environment.GetEnvironmentVariable("ADZUNA_APP_KEY");
    }

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var jobs = new List<JobVacancy>();

        if (string.IsNullOrEmpty(_appId) || string.IsNullOrEmpty(_appKey))
        {
            _logger.LogWarning("[{Platform}] API credentials not configured. Set ADZUNA_APP_ID and ADZUNA_APP_KEY environment variables.", PlatformName);
            _logger.LogWarning("[{Platform}] Get free API key at: https://developer.adzuna.com/", PlatformName);
            return jobs.AsReadOnly();
        }

        try
        {
            for (int page = 1; page <= maxPages; page++)
            {
                // Adzuna API for US remote jobs
                var url = $"https://api.adzuna.com/v1/api/jobs/us/search/{page}" +
                         $"?app_id={_appId}" +
                         $"&app_key={_appKey}" +
                         $"&what={Uri.EscapeDataString(keywords)}" +
                         $"&where=remote" +
                         $"&results_per_page=50" +
                         $"&sort_by=date";

                var response = await _httpClient.GetStringAsync(url, cancellationToken);
                if (string.IsNullOrEmpty(response))
                {
                    break;
                }

                var apiResponse = System.Text.Json.JsonSerializer.Deserialize<AdzunaApiResponse>(response);
                if (apiResponse?.Results == null || apiResponse.Results.Count == 0)
                {
                    break;
                }

                foreach (var apiJob in apiResponse.Results)
                {
                    var vacancy = new JobVacancy
                    {
                        Id = $"adzuna-{apiJob.Id}",
                        Title = apiJob.Title ?? "Unknown Position",
                        Company = apiJob.Company?.DisplayName ?? "Unknown Company",
                        Description = apiJob.Description ?? "",
                        Country = apiJob.Location?.Area?[2] ?? "USA",
                        City = apiJob.Location?.Area?[0] ?? "Remote",
                        RemotePolicy = apiJob.Location?.Area?[0]?.Contains("remote", StringComparison.OrdinalIgnoreCase) == true
                            ? RemotePolicy.FullyRemote
                            : RemotePolicy.Unknown,
                        Url = apiJob.RedirectUrl ?? "",
                        SourcePlatform = PlatformName,
                        PostedDate = apiJob.Created ?? DateTimeOffset.UtcNow,
                        ScrapedDate = DateTimeOffset.UtcNow
                    };

                    // Parse salary
                    if (apiJob.SalaryMin.HasValue && apiJob.SalaryMin > 0)
                    {
                        vacancy.SalaryMin = apiJob.SalaryMin;
                        vacancy.SalaryMax = apiJob.SalaryMax ?? apiJob.SalaryMin;
                        vacancy.SalaryCurrency = "USD";
                    }

                    // Detect seniority
                    vacancy.SeniorityLevel = DetectSeniorityLevel(vacancy.Title);

                    jobs.Add(vacancy);
                }

                // Check if we have more pages
                if (jobs.Count >= apiResponse.Count)
                {
                    break;
                }

                // Rate limiting - Adzuna allows 250 calls/month
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            return jobs.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Platform}] Failed to scrape Adzuna API", PlatformName);
            return jobs.AsReadOnly();
        }
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        // API already returns full details
        return null;
    }

    private SeniorityLevel DetectSeniorityLevel(string title)
    {
        var lowerTitle = title.ToLowerInvariant();

        if (lowerTitle.Contains("senior") || lowerTitle.Contains("sr.") || lowerTitle.Contains("lead"))
            return SeniorityLevel.Senior;
        if (lowerTitle.Contains("junior") || lowerTitle.Contains("jr.") || lowerTitle.Contains("entry"))
            return SeniorityLevel.Junior;
        if (lowerTitle.Contains("principal") || lowerTitle.Contains("staff"))
            return SeniorityLevel.Principal;
        if (lowerTitle.Contains("architect"))
            return SeniorityLevel.Architect;

        return SeniorityLevel.Middle;
    }
}

// API Response Models
internal class AdzunaApiResponse
{
    [JsonPropertyName("results")]
    public List<AdzunaJob>? Results { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

internal class AdzunaJob
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset? Created { get; set; }

    [JsonPropertyName("location")]
    public AdzunaLocation? Location { get; set; }

    [JsonPropertyName("company")]
    public AdzunaCompany? Company { get; set; }

    [JsonPropertyName("salary_min")]
    public decimal? SalaryMin { get; set; }

    [JsonPropertyName("salary_max")]
    public decimal? SalaryMax { get; set; }

    [JsonPropertyName("redirect_url")]
    public string? RedirectUrl { get; set; }
}

internal class AdzunaLocation
{
    [JsonPropertyName("area")]
    public List<string>? Area { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}

internal class AdzunaCompany
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }
}
