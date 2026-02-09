using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Jobicy — a remote-first job board with a public JSON API.
/// Fetches both .NET developer and C# tagged listings, merges, and deduplicates results.
/// All Jobicy listings are remote positions.
/// </summary>
public sealed class JobicyScraper(HttpClient httpClient, ILogger<JobicyScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseApiUrl = "https://jobicy.com/api/v2/remote-jobs";

    private static readonly string[] SearchTags = ["net-developer", "csharp"];

    public override string PlatformName => "Jobicy";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Known .NET-related keywords used for relevance filtering and skill extraction.
    /// </summary>
    private static readonly string[] NetKeywords =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "ef core",
        "blazor", "maui", "xamarin", "wpf", "winforms", "azure",
        "sql server", "linq", "nuget", "roslyn", "signalr"
    ];

    /// <summary>
    /// Broader skill keywords to extract from job descriptions.
    /// </summary>
    private static readonly Dictionary<string, string> SkillKeywordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c#"] = "C#",
        [".net"] = ".NET",
        ["dotnet"] = ".NET",
        ["asp.net"] = "ASP.NET",
        ["entity framework"] = "Entity Framework",
        ["ef core"] = "EF Core",
        ["blazor"] = "Blazor",
        ["maui"] = "MAUI",
        ["xamarin"] = "Xamarin",
        ["wpf"] = "WPF",
        ["azure"] = "Azure",
        ["aws"] = "AWS",
        ["docker"] = "Docker",
        ["kubernetes"] = "Kubernetes",
        ["sql server"] = "SQL Server",
        ["postgresql"] = "PostgreSQL",
        ["mongodb"] = "MongoDB",
        ["redis"] = "Redis",
        ["rabbitmq"] = "RabbitMQ",
        ["kafka"] = "Kafka",
        ["graphql"] = "GraphQL",
        ["rest api"] = "REST API",
        ["grpc"] = "gRPC",
        ["signalr"] = "SignalR",
        ["microservices"] = "Microservices",
        ["ci/cd"] = "CI/CD",
        ["git"] = "Git",
        ["terraform"] = "Terraform",
        ["react"] = "React",
        ["angular"] = "Angular",
        ["typescript"] = "TypeScript",
        ["javascript"] = "JavaScript",
        ["linq"] = "LINQ",
        ["xunit"] = "xUnit",
        ["nunit"] = "NUnit",
        ["roslyn"] = "Roslyn",
        ["nuget"] = "NuGet",
        ["dapper"] = "Dapper",
        ["mediator"] = "MediatR",
        ["cqrs"] = "CQRS",
        ["domain driven"] = "DDD",
        ["clean architecture"] = "Clean Architecture"
    };

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var allJobs = new Dictionary<int, JobicyJob>();
        var maxResults = maxPages * 50;

        foreach (var tag in SearchTags)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await Task.Delay(RequestDelay, cancellationToken);

                var url = $"{BaseApiUrl}?count=50&tag={Uri.EscapeDataString(tag)}";
                logger.LogDebug("[Jobicy] Fetching tag: {Tag} from {Url}", tag, url);

                var response = await httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Jobicy] API returned {StatusCode} for tag {Tag}",
                        response.StatusCode, tag);
                    continue;
                }

                var apiResponse = await response.Content
                    .ReadFromJsonAsync<JobicyApiResponse>(JsonOptions, cancellationToken);

                if (apiResponse?.Jobs is null or { Count: 0 })
                {
                    logger.LogDebug("[Jobicy] No jobs returned for tag {Tag}", tag);
                    continue;
                }

                logger.LogInformation("[Jobicy] API returned {Count} jobs for tag {Tag}", apiResponse.Jobs.Count, tag);

                // Merge and deduplicate by job ID
                foreach (var job in apiResponse.Jobs)
                {
                    if (job.Id > 0 && !allJobs.ContainsKey(job.Id))
                    {
                        allJobs[job.Id] = job;
                        logger.LogDebug("[Jobicy] Added job {Id}: {Title} at {Company}",
                            job.Id, job.JobTitle ?? "N/A", job.CompanyName ?? "N/A");
                    }
                }

                logger.LogInformation("[Jobicy] Fetched {Count} jobs for tag {Tag}, total unique: {Total}",
                    apiResponse.Jobs.Count, tag, allJobs.Count);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "[Jobicy] API request failed for tag {Tag}", tag);
            }
        }

        if (allJobs.Count == 0)
        {
            logger.LogWarning("[Jobicy] No jobs found after fetching all tags");
            return [];
        }

        logger.LogInformation("[Jobicy] Total unique jobs before filtering: {Count}", allJobs.Count);

        // Filter for .NET relevance and limit to maxResults
        var netRelatedJobs = allJobs.Values.Where(IsNetRelated).ToList();
        logger.LogInformation("[Jobicy] Jobs after .NET relevance filtering: {Count}", netRelatedJobs.Count);

        if (netRelatedJobs.Count == 0)
        {
            logger.LogWarning("[Jobicy] No jobs passed .NET relevance filter. Returning all jobs from tag search.");
            // Since we're already searching for .NET-specific tags, trust the API filtering
            return allJobs.Values
                .Take(maxResults)
                .Select(MapToVacancy)
                .ToList();
        }

        return netRelatedJobs
            .Take(maxResults)
            .Select(MapToVacancy)
            .ToList();
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // Jobicy API provides full job data in the listing endpoint — no detail fetch needed.
        return Task.FromResult<JobVacancy?>(null);
    }

    private JobVacancy MapToVacancy(JobicyJob job)
    {
        var description = job.JobDescription ?? job.JobExcerpt ?? string.Empty;
        var (salaryMin, salaryMax, currency) = ParseSalaryRange(description);

        return new JobVacancy
        {
            Id = GenerateId(job.Id.ToString()),
            Title = job.JobTitle ?? string.Empty,
            Company = job.CompanyName ?? string.Empty,
            Url = job.Url ?? string.Empty,
            City = string.Empty,
            Country = MapGeoToCountry(job.JobGeo),
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryCurrency = currency,
            RemotePolicy = RemotePolicy.FullyRemote,
            SeniorityLevel = MapJobLevel(job.JobLevel),
            EngagementType = MapJobType(job.JobType),
            GeoRestrictions = MapGeoRestrictions(job.JobGeo),
            RequiredSkills = ExtractSkills(description),
            Description = description,
            PostedDate = ParsePublishedDate(job.PubDate),
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static bool IsNetRelated(JobicyJob job)
    {
        var title = job.JobTitle?.ToLowerInvariant() ?? "";
        var description = job.JobDescription?.ToLowerInvariant() ?? "";
        var excerpt = job.JobExcerpt?.ToLowerInvariant() ?? "";
        var industry = job.JobIndustry?.ToLowerInvariant() ?? "";

        var searchable = $"{title} {description} {excerpt} {industry}";

        return NetKeywords.Any(kw => searchable.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static EngagementType MapJobType(List<string>? jobTypes)
    {
        if (jobTypes is null or { Count: 0 })
            return EngagementType.Unknown;

        var types = jobTypes.Select(t => t.ToLowerInvariant()).ToList();

        if (types.Any(t => t.Contains("contract") || t.Contains("freelance")))
            return EngagementType.ContractB2B;

        if (types.Any(t => t.Contains("full_time") || t.Contains("full-time") || t.Contains("permanent")))
            return EngagementType.Employment;

        if (types.Any(t => t.Contains("part_time") || t.Contains("part-time")))
            return EngagementType.Employment;

        return EngagementType.Unknown;
    }

    private static SeniorityLevel MapJobLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return SeniorityLevel.Unknown;

        var lower = level.ToLowerInvariant();

        if (lower.Contains("principal")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("head")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle") || lower.Contains("regular")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static string MapGeoToCountry(string? jobGeo)
    {
        if (string.IsNullOrWhiteSpace(jobGeo)) return string.Empty;

        var lower = jobGeo.ToLowerInvariant();

        if (lower.Contains("anywhere") || lower.Contains("worldwide"))
            return "Worldwide";

        // Return the raw geo string as a country approximation
        return jobGeo.Trim();
    }

    private static List<string> MapGeoRestrictions(string? jobGeo)
    {
        if (string.IsNullOrWhiteSpace(jobGeo)) return [];

        var lower = jobGeo.ToLowerInvariant();

        // "Anywhere in the World" or "Worldwide" means no restrictions
        if (lower.Contains("anywhere") || lower.Contains("worldwide"))
            return [];

        var restrictions = new List<string>();

        if (lower.Contains("europe") || lower.Contains("eu ") || lower.Contains("eu,") || lower == "eu")
            restrictions.Add("EU-only");
        if (lower.Contains("united states") || lower.Contains("usa") || lower.Contains("us only") || lower == "us")
            restrictions.Add("US-only");
        if (lower.Contains("united kingdom") || lower.Contains("uk"))
            restrictions.Add("UK-only");
        if (lower.Contains("canada"))
            restrictions.Add("Canada-only");
        if (lower.Contains("australia"))
            restrictions.Add("AU-only");
        if (lower.Contains("latin america") || lower.Contains("latam"))
            restrictions.Add("LATAM-only");

        // If no specific restriction was detected but it's not "anywhere",
        // treat the raw geo as a restriction
        if (restrictions.Count == 0)
            restrictions.Add($"{jobGeo.Trim()}-only");

        return restrictions;
    }

    private static List<string> ExtractSkills(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return [];

        var lower = description.ToLowerInvariant();
        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (keyword, displayName) in SkillKeywordMap)
        {
            if (lower.Contains(keyword))
            {
                detected.Add(displayName);
            }
        }

        return detected.OrderBy(s => s).ToList();
    }

    private static DateTimeOffset ParsePublishedDate(string? pubDate)
    {
        if (string.IsNullOrWhiteSpace(pubDate)) return DateTimeOffset.MinValue;

        return DateTimeOffset.TryParse(pubDate, out var date)
            ? date
            : DateTimeOffset.MinValue;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  JSON DESERIALIZATION MODELS
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class JobicyApiResponse
    {
        [JsonPropertyName("jobs")]
        public List<JobicyJob>? Jobs { get; set; }
    }

    private sealed class JobicyJob
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("jobTitle")]
        public string? JobTitle { get; set; }

        [JsonPropertyName("companyName")]
        public string? CompanyName { get; set; }

        [JsonPropertyName("companyLogo")]
        public string? CompanyLogo { get; set; }

        [JsonPropertyName("jobIndustry")]
        public string? JobIndustry { get; set; }

        [JsonPropertyName("jobType")]
        public List<string>? JobType { get; set; }

        [JsonPropertyName("jobGeo")]
        public string? JobGeo { get; set; }

        [JsonPropertyName("jobLevel")]
        public string? JobLevel { get; set; }

        [JsonPropertyName("jobExcerpt")]
        public string? JobExcerpt { get; set; }

        [JsonPropertyName("jobDescription")]
        public string? JobDescription { get; set; }

        [JsonPropertyName("pubDate")]
        public string? PubDate { get; set; }
    }
}
