using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from Himalayas (himalayas.app).
/// Himalayas is a remote-first job board with a public JSON API.
/// All positions are remote by default; geographic restrictions may apply.
/// </summary>
public sealed class HimalayasScraper(HttpClient httpClient, ILogger<HimalayasScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string ApiBase = "https://himalayas.app/jobs/api";
    private const int PageSize = 50;

    public override string PlatformName => "Himalayas";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Patterns that indicate a job is .NET-relevant.
    /// </summary>
    private static readonly string[] NetRelevancePatterns =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "blazor",
        "maui", "wpf", "winforms", "xamarin", "unity3d", "ml.net",
        "signalr", "dapper", "mediatr", "nuget"
    ];

    /// <summary>
    /// Known tech keywords used to extract required skills from description and categories.
    /// </summary>
    private static readonly string[] TechKeywords =
    [
        "C#", ".NET", "ASP.NET", "Entity Framework", "Blazor", "MAUI",
        "Azure", "AWS", "GCP", "Docker", "Kubernetes", "SQL Server",
        "PostgreSQL", "Redis", "RabbitMQ", "Kafka", "gRPC", "REST",
        "GraphQL", "React", "Angular", "Vue", "TypeScript", "JavaScript",
        "Python", "Go", "Rust", "Java", "Terraform", "CI/CD",
        "Microservices", "SignalR", "WPF", "WinForms", "Xamarin",
        "Unity", "ML.NET", "Dapper", "MediatR", "CQRS", "DDD",
        "NoSQL", "MongoDB", "Elasticsearch", "Git", "Linux"
    ];

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            for (int page = 0; page < maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int offset = page * PageSize;

                await Task.Delay(RequestDelay, cancellationToken);

                var response = await httpClient.GetAsync(
                    $"{ApiBase}?limit={PageSize}&offset={offset}", cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Himalayas] API returned {StatusCode} at offset {Offset}",
                        response.StatusCode, offset);
                    break;
                }

                var apiResponse = await response.Content
                    .ReadFromJsonAsync<HimalayasApiResponse>(JsonOptions, cancellationToken);

                if (apiResponse?.Jobs is null or { Count: 0 })
                {
                    logger.LogDebug("[Himalayas] No more jobs at offset {Offset}", offset);
                    break;
                }

                foreach (var job in apiResponse.Jobs)
                {
                    if (IsNetRelated(job))
                    {
                        vacancies.Add(MapToVacancy(job));
                    }
                }

                logger.LogDebug(
                    "[Himalayas] Page {Page}: fetched {Count} jobs, {Relevant} .NET-relevant so far",
                    page + 1, apiResponse.Jobs.Count, vacancies.Count);

                // Stop early if we've fetched all available jobs
                if (offset + apiResponse.Jobs.Count >= (apiResponse.TotalCount ?? int.MaxValue))
                {
                    logger.LogDebug("[Himalayas] Reached end of available jobs ({Total} total)",
                        apiResponse.TotalCount);
                    break;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[Himalayas] API request failed");
        }

        logger.LogInformation("[Himalayas] Scraped {Count} .NET-relevant vacancies", vacancies.Count);
        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // The list API provides all available fields; no additional detail endpoint needed
        return Task.FromResult<JobVacancy?>(null);
    }

    private JobVacancy MapToVacancy(HimalayasJob job)
    {
        var description = job.Description ?? string.Empty;
        var title = job.Title ?? string.Empty;

        // Extract skills from categories and description
        var skills = ExtractSkills(job);

        // Detect seniority from title
        var seniority = DetectSeniority(title);

        // Detect engagement type from description
        var engagementType = DetectEngagementType(description);

        // Build geo restrictions from locationRestrictions field + text detection
        var geoRestrictions = BuildGeoRestrictions(job, description);

        // Parse salary
        var (salaryMin, salaryMax, currency) = ParseSalaryRange(job.Salary);

        // Parse posted date
        var postedDate = DateTimeOffset.TryParse(job.PubDate, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

        return new JobVacancy
        {
            Id = GenerateId(job.Id?.ToString() ?? Guid.NewGuid().ToString()),
            Title = title,
            Company = job.CompanyName ?? string.Empty,
            City = string.Empty,
            Country = DeriveCountryFromRestrictions(job.LocationRestrictions),
            Url = job.ApplicationLink ?? string.Empty,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryCurrency = currency,
            RemotePolicy = RemotePolicy.FullyRemote,
            SeniorityLevel = seniority,
            EngagementType = engagementType,
            GeoRestrictions = geoRestrictions,
            RequiredSkills = skills,
            Description = description,
            PostedDate = postedDate,
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static bool IsNetRelated(HimalayasJob job)
    {
        var title = job.Title?.ToLowerInvariant() ?? "";
        var description = job.Description?.ToLowerInvariant() ?? "";
        var categories = job.Categories ?? [];
        var categoriesText = string.Join(" ", categories).ToLowerInvariant();

        return NetRelevancePatterns.Any(pattern =>
            title.Contains(pattern) ||
            description.Contains(pattern) ||
            categoriesText.Contains(pattern));
    }

    private static List<string> ExtractSkills(HimalayasJob job)
    {
        var skills = new List<string>();
        var searchText = $"{job.Title} {job.Description} {string.Join(" ", job.Categories ?? [])}";

        foreach (var keyword in TechKeywords)
        {
            if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                skills.Add(keyword);
        }

        // Also add raw categories as skills if they look like tech terms
        if (job.Categories is not null)
        {
            foreach (var category in job.Categories)
            {
                var trimmed = category.Trim();
                if (!string.IsNullOrEmpty(trimmed) &&
                    !skills.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    skills.Add(trimmed);
                }
            }
        }

        return skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static SeniorityLevel DetectSeniority(string title)
    {
        var lower = title.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff"))
            return SeniorityLevel.Principal;
        if (lower.Contains("architect"))
            return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("team lead") || lower.Contains("tech lead"))
            return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr "))
            return SeniorityLevel.Senior;
        if (lower.Contains("mid-level") || lower.Contains("mid level") || lower.Contains("middle"))
            return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("jr ") ||
            lower.Contains("entry level") || lower.Contains("entry-level"))
            return SeniorityLevel.Junior;
        if (lower.Contains("intern"))
            return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    /// <summary>
    /// Combines location restrictions from the API with text-based geo restriction detection.
    /// </summary>
    private static List<string> BuildGeoRestrictions(HimalayasJob job, string description)
    {
        var restrictions = new List<string>();

        // Map explicit location restrictions from the API
        if (job.LocationRestrictions is { Count: > 0 })
        {
            foreach (var restriction in job.LocationRestrictions)
            {
                var trimmed = restriction.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    restrictions.Add(trimmed);
            }
        }

        // Also detect from description text
        var textRestrictions = DetectGeoRestrictions(description);
        foreach (var textRestriction in textRestrictions)
        {
            if (!restrictions.Contains(textRestriction, StringComparer.OrdinalIgnoreCase))
                restrictions.Add(textRestriction);
        }

        return restrictions;
    }

    /// <summary>
    /// Derives a country string from location restrictions, if any single country is specified.
    /// </summary>
    private static string DeriveCountryFromRestrictions(List<string>? restrictions)
    {
        if (restrictions is null or { Count: 0 })
            return "Remote";

        if (restrictions.Count == 1)
            return restrictions[0].Trim();

        // Multiple restrictions — return "Remote" with restrictions handled separately
        return "Remote";
    }

    // ── JSON deserialization models for Himalayas API ────────────────────

    private sealed class HimalayasApiResponse
    {
        [JsonPropertyName("jobs")]
        public List<HimalayasJob>? Jobs { get; set; }

        [JsonPropertyName("total_count")]
        public int? TotalCount { get; set; }
    }

    private sealed class HimalayasJob
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("companyName")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("applicationLink")]
        public string? ApplicationLink { get; set; }

        [JsonPropertyName("pubDate")]
        public string? PubDate { get; set; }

        [JsonPropertyName("locationRestrictions")]
        public List<string>? LocationRestrictions { get; set; }

        [JsonPropertyName("timezoneRestrictions")]
        public List<string>? TimezoneRestrictions { get; set; }

        [JsonPropertyName("salary")]
        public string? Salary { get; set; }
    }
}
