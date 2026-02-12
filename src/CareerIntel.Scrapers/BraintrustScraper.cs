using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Braintrust — a decentralized talent network that connects
/// freelancers with enterprise clients. Braintrust uses a JSON API for job listings.
/// Default engagement type is ContractB2B as the platform focuses on contractor roles.
/// </summary>
public sealed class BraintrustScraper(HttpClient httpClient, ILogger<BraintrustScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string ApiBaseUrl = "https://app.usebraintrust.com/api/v1/jobs";
    private const string WebBaseUrl = "https://app.usebraintrust.com/jobs";

    public override string PlatformName => "Braintrust";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(4);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] DotNetKeywords =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "ef core",
        "blazor", "maui", "xamarin", "wpf", "winforms", "nuget",
        "signalr", "minimal api", "web api", "grpc"
    ];

    private static readonly string[] SkillKeywords =
    [
        "C#", ".NET", "ASP.NET", "Entity Framework", "EF Core", "Blazor",
        "Azure", "AWS", "Docker", "Kubernetes", "SQL Server", "PostgreSQL",
        "MongoDB", "Redis", "RabbitMQ", "Kafka", "gRPC", "SignalR",
        "React", "Angular", "TypeScript", "JavaScript", "REST", "GraphQL",
        "Microservices", "CI/CD", "Git", "Agile", "Scrum", "MAUI",
        "Xamarin", "WPF", "LINQ", "Dapper", "MediatR", "CQRS",
        "Domain-Driven Design", "Clean Architecture", "xUnit", "NUnit"
    ];

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 3,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            for (var page = 1; page <= maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(RequestDelay, cancellationToken);

                var apiUrl = $"{ApiBaseUrl}?search={Uri.EscapeDataString(keywords)}&page={page}&per_page=25";

                logger.LogInformation("[Braintrust] Fetching API page {Page}: {Url}", page, apiUrl);

                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("Accept", "application/json, text/plain, */*");

                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogError(ex, "[Braintrust] API request failed for page {Page}", page);
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Braintrust] API returned {StatusCode} for page {Page}",
                        response.StatusCode, page);
                    break;
                }

                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    logger.LogWarning("[Braintrust] Empty response on page {Page}", page);
                    break;
                }

                // Try to parse as an object with a results array, or as a direct array
                List<BraintrustJob>? jobs = null;

                try
                {
                    var wrapper = JsonSerializer.Deserialize<BraintrustApiResponse>(responseText, JsonOptions);
                    jobs = wrapper?.Results;
                }
                catch (JsonException)
                {
                    // Fallback: try parsing as a direct array
                    try
                    {
                        jobs = JsonSerializer.Deserialize<List<BraintrustJob>>(responseText, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "[Braintrust] Failed to deserialize page {Page}", page);
                        break;
                    }
                }

                if (jobs is null or { Count: 0 })
                {
                    logger.LogDebug("[Braintrust] No more results on page {Page}", page);
                    break;
                }

                logger.LogInformation("[Braintrust] Page {Page}: received {Count} jobs", page, jobs.Count);

                foreach (var job in jobs)
                {
                    if (!IsNetRelated(job)) continue;
                    vacancies.Add(MapToVacancy(job));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Braintrust] Scraping failed");
        }

        logger.LogInformation("[Braintrust] Found {Count} .NET-relevant vacancies", vacancies.Count);
        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // Attempt to extract job ID from URL and fetch from API
        var jobIdMatch = Regex.Match(url, @"/jobs/(\d+)");
        if (!jobIdMatch.Success) return null;

        var jobId = jobIdMatch.Groups[1].Value;

        try
        {
            await Task.Delay(RequestDelay, cancellationToken);

            var apiUrl = $"{ApiBaseUrl}/{jobId}";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Accept", "application/json");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var job = await response.Content.ReadFromJsonAsync<BraintrustJob>(JsonOptions, cancellationToken);
            if (job is null) return null;

            return MapToVacancy(job);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Braintrust] Failed to fetch detail for {Url}", url);
            return null;
        }
    }

    private JobVacancy MapToVacancy(BraintrustJob job)
    {
        var title = job.Title ?? string.Empty;
        var description = job.Description ?? string.Empty;
        var combinedText = $"{title} {description}";

        var skills = job.Skills?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];
        if (skills.Count == 0)
            skills = ExtractSkillsFromText(combinedText);

        var (salaryMin, salaryMax, currency) = ParseSalaryRange(job.BudgetRange ?? job.Rate);
        var rateText = job.Rate ?? job.BudgetRange ?? string.Empty;
        var isHourly = rateText.Contains("/hr", StringComparison.OrdinalIgnoreCase) ||
            rateText.Contains("per hour", StringComparison.OrdinalIgnoreCase) ||
            rateText.Contains("hourly", StringComparison.OrdinalIgnoreCase);

        var detectedEngagement = DetectEngagementType(combinedText);
        var detectedRemote = DetectRemotePolicy(combinedText);

        return new JobVacancy
        {
            Id = GenerateId(job.Id?.ToString() ?? job.Slug ?? Guid.NewGuid().ToString()),
            Title = title,
            Company = job.Company ?? "Braintrust Client",
            Country = job.Location ?? string.Empty,
            Url = !string.IsNullOrEmpty(job.Slug)
                ? $"{WebBaseUrl}/{job.Slug}"
                : $"{WebBaseUrl}/{job.Id}",
            Description = description,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryCurrency = currency,
            IsHourlyRate = isHourly,
            RemotePolicy = detectedRemote != RemotePolicy.Unknown ? detectedRemote : RemotePolicy.FullyRemote,
            EngagementType = detectedEngagement != EngagementType.Unknown ? detectedEngagement : EngagementType.ContractB2B,
            SeniorityLevel = DetectSeniority(title),
            GeoRestrictions = DetectGeoRestrictions(combinedText),
            RequiredSkills = skills,
            PostedDate = DateTimeOffset.TryParse(job.CreatedAt, out var parsed) ? parsed : DateTimeOffset.MinValue,
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static bool IsNetRelated(BraintrustJob job)
    {
        var title = job.Title?.ToLowerInvariant() ?? "";
        var description = job.Description?.ToLowerInvariant() ?? "";
        var skills = job.Skills?.Select(s => s.ToLowerInvariant()).ToList() ?? [];

        return DotNetKeywords.Any(kw =>
            title.Contains(kw) ||
            description.Contains(kw) ||
            skills.Any(s => s.Contains(kw)));
    }

    private static SeniorityLevel DetectSeniority(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle") || lower.Contains("mid-level")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static List<string> ExtractSkillsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return SkillKeywords
            .Where(skill => text.Contains(skill, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // JSON models for Braintrust API response

    private sealed class BraintrustApiResponse
    {
        [JsonPropertyName("results")]
        public List<BraintrustJob>? Results { get; set; }

        [JsonPropertyName("count")]
        public int? Count { get; set; }

        [JsonPropertyName("next")]
        public string? Next { get; set; }
    }

    private sealed class BraintrustJob
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("company")]
        public string? Company { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("skills")]
        public List<string>? Skills { get; set; }

        [JsonPropertyName("rate")]
        public string? Rate { get; set; }

        [JsonPropertyName("budget_range")]
        public string? BudgetRange { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
    }
}
