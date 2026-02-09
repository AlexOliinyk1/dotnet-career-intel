using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from RemoteOK — a remote-first job board.
/// RemoteOK exposes a public JSON API at /api, making parsing straightforward.
/// The first element in the response array is metadata and must be skipped.
/// </summary>
public sealed class RemoteOkScraper(HttpClient httpClient, ILogger<RemoteOkScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string ApiUrl = "https://remoteok.com/api";

    public override string PlatformName => "RemoteOK";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] NetKeywords =
    [
        ".net", "c#", "dotnet", "asp.net", "entity framework", "blazor",
        "maui", "xamarin", "wpf", "winforms", "ef core", "efcore"
    ];

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(RequestDelay, cancellationToken);

            // RemoteOK requires proper headers to return JSON
            var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.Add("Accept", "application/json, text/plain, */*");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[RemoteOK] API returned {StatusCode}", response.StatusCode);
                return [];
            }

            // The API response format might have changed - let's read as text first
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                logger.LogWarning("[RemoteOK] Empty response from API");
                return [];
            }

            // Try to parse as array (original format)
            JsonElement[]? rawArray = null;
            try
            {
                rawArray = JsonSerializer.Deserialize<JsonElement[]>(responseText, JsonOptions);
            }
            catch (JsonException)
            {
                logger.LogWarning("[RemoteOK] Response is not a JSON array - API format may have changed");
                return [];
            }

            if (rawArray is null or { Length: 0 })
            {
                logger.LogWarning("[RemoteOK] Empty array response");
                return [];
            }

            logger.LogDebug("[RemoteOK] Received {Count} elements in response array", rawArray.Length);

            // Skip the first element (metadata), then deserialize each remaining element as a job
            var jobs = new List<RemoteOkJob>();
            for (int i = 1; i < rawArray.Length; i++)
            {
                try
                {
                    var job = rawArray[i].Deserialize<RemoteOkJob>(JsonOptions);
                    if (job is not null)
                        jobs.Add(job);
                }
                catch (JsonException ex)
                {
                    logger.LogDebug(ex, "[RemoteOK] Failed to deserialize job at index {Index}", i);
                }
            }

            if (jobs.Count == 0)
            {
                logger.LogWarning("[RemoteOK] No jobs deserialized from API response");
                return [];
            }

            logger.LogDebug("[RemoteOK] Fetched {TotalJobs} total jobs", jobs.Count);

            // Filter to .NET-relevant jobs and limit results based on maxPages (25 per page)
            var filtered = jobs
                .Where(IsNetRelated)
                .Take(maxPages * 25)
                .Select(MapToVacancy)
                .ToList();

            logger.LogInformation("[RemoteOK] Found {Count} .NET-relevant vacancies out of {Total} total",
                filtered.Count, jobs.Count);

            return filtered;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[RemoteOK] API request failed");
            return [];
        }
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // RemoteOK API returns full job data in the listing response;
        // there is no separate detail endpoint.
        return null;
    }

    private JobVacancy MapToVacancy(RemoteOkJob job)
    {
        var description = job.Description ?? string.Empty;
        var position = job.Position ?? string.Empty;

        return new JobVacancy
        {
            Id = GenerateId(job.Id?.ToString() ?? job.Slug ?? Guid.NewGuid().ToString()),
            Title = position,
            Company = job.Company ?? string.Empty,
            City = string.Empty,
            Country = ExtractCountry(job.Location),
            Url = !string.IsNullOrEmpty(job.Url) ? job.Url : $"https://remoteok.com/remote-jobs/{job.Slug}",
            Description = description,
            SalaryMin = job.SalaryMin,
            SalaryMax = job.SalaryMax,
            SalaryCurrency = "USD",
            RemotePolicy = MapRemotePolicy(job.Location),
            SeniorityLevel = DetectSeniority(position),
            EngagementType = DetectEngagementType(description),
            GeoRestrictions = DetectGeoRestrictions(position + " " + description),
            RequiredSkills = job.Tags?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList() ?? [],
            PostedDate = DateTimeOffset.TryParse(job.Date, out var parsed) ? parsed : DateTimeOffset.MinValue,
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static bool IsNetRelated(RemoteOkJob job)
    {
        var position = job.Position?.ToLowerInvariant() ?? "";
        var description = job.Description?.ToLowerInvariant() ?? "";
        var tags = job.Tags?.Select(t => t.ToLowerInvariant()).ToList() ?? [];

        return NetKeywords.Any(kw =>
            position.Contains(kw) ||
            description.Contains(kw) ||
            tags.Any(t => t.Contains(kw)));
    }

    private static RemotePolicy MapRemotePolicy(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return RemotePolicy.FullyRemote;

        var lower = location.ToLowerInvariant();

        if (lower.Contains("worldwide") || lower.Contains("remote") ||
            lower.Contains("anywhere") || lower.Contains("global"))
            return RemotePolicy.FullyRemote;

        if (lower.Contains("hybrid"))
            return RemotePolicy.Hybrid;

        // RemoteOK is a remote-first board, so default to FullyRemote
        return RemotePolicy.FullyRemote;
    }

    private static string ExtractCountry(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return string.Empty;

        var lower = location.ToLowerInvariant();

        if (lower.Contains("worldwide") || lower.Contains("anywhere") || lower.Contains("global"))
            return "Worldwide";

        // Return the raw location as a best-effort country/region indicator
        return location.Trim();
    }

    private static SeniorityLevel DetectSeniority(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return SeniorityLevel.Unknown;

        var lower = title.ToLowerInvariant();

        if (lower.Contains("principal"))
            return SeniorityLevel.Principal;
        if (lower.Contains("architect"))
            return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("team lead") || lower.Contains("tech lead"))
            return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr "))
            return SeniorityLevel.Senior;
        if (lower.Contains("middle") || lower.Contains("mid-level") || lower.Contains("mid level"))
            return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("jr "))
            return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee"))
            return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    // JSON models for RemoteOK API response

    private sealed class RemoteOkJob
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("company")]
        public string? Company { get; set; }

        [JsonPropertyName("position")]
        public string? Position { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("salary_min")]
        public decimal? SalaryMin { get; set; }

        [JsonPropertyName("salary_max")]
        public decimal? SalaryMax { get; set; }
    }
}
