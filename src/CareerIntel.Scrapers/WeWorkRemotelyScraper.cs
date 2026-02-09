using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from We Work Remotely — one of the largest remote work communities.
/// Fetches from both the general remote jobs endpoint and the back-end programming category
/// to maximize .NET-relevant results.
/// </summary>
public sealed class WeWorkRemotelyScraper(HttpClient httpClient, ILogger<WeWorkRemotelyScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string AllJobsApiUrl = "https://weworkremotely.com/remote-jobs.json";
    private const string BackEndApiUrl = "https://weworkremotely.com/categories/remote-back-end-programming-jobs.json";

    public override string PlatformName => "WeWorkRemotely";

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
        var allJobs = new Dictionary<string, WwrJob>();

        // Fetch from both endpoints to maximize coverage
        var generalJobs = await FetchJobsFromEndpointAsync(AllJobsApiUrl, cancellationToken);
        var backEndJobs = await FetchJobsFromEndpointAsync(BackEndApiUrl, cancellationToken);

        // Deduplicate by job ID, preferring the first occurrence
        foreach (var job in generalJobs.Concat(backEndJobs))
        {
            var key = job.Id?.ToString() ?? job.Url ?? Guid.NewGuid().ToString();
            allJobs.TryAdd(key, job);
        }

        if (allJobs.Count == 0) return [];

        // Filter to .NET-relevant jobs and limit results based on maxPages (25 per page)
        return allJobs.Values
            .Where(IsNetRelated)
            .Take(maxPages * 25)
            .Select(MapToVacancy)
            .ToList();
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // The JSON API provides full job data in the listing response;
        // no separate detail endpoint is needed.
        return null;
    }

    private async Task<List<WwrJob>> FetchJobsFromEndpointAsync(
        string apiUrl, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(RequestDelay, cancellationToken);

            logger.LogDebug("[WeWorkRemotely] Fetching from: {Url}", apiUrl);

            // Add comprehensive headers to avoid 406 Not Acceptable
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/json, text/html, */*");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36");
            request.Headers.Add("Referer", "https://weworkremotely.com/");
            request.Headers.Add("DNT", "1");
            request.Headers.Add("Connection", "keep-alive");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");

            var response = await httpClient.SendAsync(request, cancellationToken);

            logger.LogInformation("[WeWorkRemotely] Response status: {StatusCode}, ContentType: {ContentType}",
                response.StatusCode, response.Content.Headers.ContentType?.MediaType ?? "unknown");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("[WeWorkRemotely] API returned {StatusCode} for {Url}. The JSON API may no longer be publicly accessible.",
                    response.StatusCode, apiUrl);
                return [];
            }

            // Read response as string first to inspect format
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                logger.LogWarning("[WeWorkRemotely] Empty response from {Url}", apiUrl);
                return [];
            }

            logger.LogDebug("[WeWorkRemotely] Response length: {Length} bytes", responseText.Length);

            var result = JsonSerializer.Deserialize<WwrApiResponse>(responseText, JsonOptions);

            if (result?.Jobs is null or { Count: 0 })
            {
                logger.LogWarning("[WeWorkRemotely] No jobs found in response from {Url}", apiUrl);
                return [];
            }

            logger.LogInformation("[WeWorkRemotely] Successfully fetched {Count} jobs from {Url}",
                result.Jobs.Count, apiUrl);

            return result.Jobs;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[WeWorkRemotely] HTTP request failed for {Url}. The site may be blocking automated requests or the API endpoint may have changed.", apiUrl);
            return [];
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "[WeWorkRemotely] JSON parsing failed for {Url}. The API format may have changed or the endpoint may now return HTML instead of JSON.", apiUrl);
            return [];
        }
    }

    private JobVacancy MapToVacancy(WwrJob job)
    {
        var title = job.Title ?? string.Empty;
        var description = job.Description ?? string.Empty;

        return new JobVacancy
        {
            Id = GenerateId(job.Id?.ToString() ?? Guid.NewGuid().ToString()),
            Title = title,
            Company = job.CompanyName ?? string.Empty,
            City = string.Empty,
            Country = "Remote",
            Url = !string.IsNullOrEmpty(job.Url)
                ? job.Url
                : !string.IsNullOrEmpty(job.SourceUrl) ? job.SourceUrl : string.Empty,
            Description = description,
            SalaryMin = null,
            SalaryMax = null,
            SalaryCurrency = "USD",
            RemotePolicy = RemotePolicy.FullyRemote,
            SeniorityLevel = DetectSeniority(title),
            EngagementType = DetectEngagementType(description),
            GeoRestrictions = DetectGeoRestrictions(title + " " + description),
            RequiredSkills = ExtractSkillsFromDescription(description),
            PostedDate = DateTimeOffset.TryParse(job.PublishedAt, out var parsed) ? parsed : DateTimeOffset.MinValue,
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static bool IsNetRelated(WwrJob job)
    {
        var title = job.Title?.ToLowerInvariant() ?? "";
        var description = job.Description?.ToLowerInvariant() ?? "";

        return NetKeywords.Any(kw =>
            title.Contains(kw) || description.Contains(kw));
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

    /// <summary>
    /// Extracts recognizable technology skills from the job description text.
    /// Since WeWorkRemotely does not provide structured tags, we scan for common keywords.
    /// </summary>
    private static List<string> ExtractSkillsFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var lower = description.ToLowerInvariant();
        var skills = new List<string>();

        var knownSkills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "C#",
            [".net"] = ".NET",
            ["asp.net"] = "ASP.NET",
            ["entity framework"] = "Entity Framework",
            ["ef core"] = "EF Core",
            ["blazor"] = "Blazor",
            ["azure"] = "Azure",
            ["aws"] = "AWS",
            ["docker"] = "Docker",
            ["kubernetes"] = "Kubernetes",
            ["sql"] = "SQL",
            ["postgresql"] = "PostgreSQL",
            ["mongodb"] = "MongoDB",
            ["redis"] = "Redis",
            ["rabbitmq"] = "RabbitMQ",
            ["grpc"] = "gRPC",
            ["graphql"] = "GraphQL",
            ["react"] = "React",
            ["angular"] = "Angular",
            ["typescript"] = "TypeScript",
            ["javascript"] = "JavaScript",
            ["ci/cd"] = "CI/CD",
            ["terraform"] = "Terraform",
            ["microservices"] = "Microservices",
            ["rest api"] = "REST API",
            ["signalr"] = "SignalR",
            ["xamarin"] = "Xamarin",
            ["maui"] = "MAUI",
            ["wpf"] = "WPF"
        };

        foreach (var (keyword, displayName) in knownSkills)
        {
            if (lower.Contains(keyword.ToLowerInvariant()))
                skills.Add(displayName);
        }

        return skills.Distinct().ToList();
    }

    // JSON models for WeWorkRemotely API response

    private sealed class WwrApiResponse
    {
        [JsonPropertyName("jobs")]
        public List<WwrJob>? Jobs { get; set; }
    }

    private sealed class WwrJob
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("company_name")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("category_name")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("source_url")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }
    }
}
