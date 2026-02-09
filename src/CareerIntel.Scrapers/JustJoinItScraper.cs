using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from JustJoin.it — Europe's leading IT job board.
/// JustJoin.it exposes a JSON API, making parsing more reliable than HTML scraping.
/// </summary>
public sealed class JustJoinItScraper(HttpClient httpClient, ILogger<JustJoinItScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    // JustJoin.it API endpoints to try (they change frequently)
    private static readonly string[] ApiEndpoints =
    [
        "https://justjoin.it/api/offers",
        "https://api.justjoin.it/v2/offers",
        "https://api.justjoin.it/offers",
        "https://api.justjoin.it/v2/user-panel/offers"
    ];
    private const string WebUrl = "https://justjoin.it/offers";

    public override string PlatformName => "JustJoinIt";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        // Try multiple API endpoints until one works
        foreach (var apiEndpoint in ApiEndpoints)
        {
            try
            {
                await Task.Delay(RequestDelay, cancellationToken);

                logger.LogDebug("[JustJoinIt] Trying API endpoint: {Endpoint}", apiEndpoint);

                var response = await httpClient.GetAsync(apiEndpoint, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogDebug("[JustJoinIt] Endpoint {Endpoint} returned {StatusCode}, trying next...",
                        apiEndpoint, response.StatusCode);
                    continue;
                }

                var offers = await response.Content
                    .ReadFromJsonAsync<List<JustJoinOffer>>(JsonOptions, cancellationToken);

                if (offers is null or { Count: 0 })
                {
                    logger.LogDebug("[JustJoinIt] Endpoint {Endpoint} returned no offers, trying next...", apiEndpoint);
                    continue;
                }

                logger.LogInformation("[JustJoinIt] Successfully fetched {Count} offers from {Endpoint}",
                    offers.Count, apiEndpoint);

                // Filter to .NET-relevant offers and limit to maxPages * 25
                var netOffers = offers
                    .Where(o => IsNetRelated(o))
                    .Take(maxPages * 25)
                    .Select(MapToVacancy)
                    .ToList();

                logger.LogInformation("[JustJoinIt] Filtered to {Count} .NET-relevant offers", netOffers.Count);
                return netOffers;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "[JustJoinIt] JSON parsing failed for {Endpoint}, trying next...", apiEndpoint);
                continue;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[JustJoinIt] Request failed for {Endpoint}, trying next...", apiEndpoint);
                continue;
            }
        }

        logger.LogError("[JustJoinIt] All API endpoints failed");
        return [];
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract offer slug from URL
            var slug = url.Split('/').LastOrDefault();
            if (string.IsNullOrEmpty(slug)) return null;

            await Task.Delay(RequestDelay, cancellationToken);

            // Try multiple API endpoints for detail page
            foreach (var apiEndpoint in ApiEndpoints)
            {
                try
                {
                    var offer = await httpClient.GetFromJsonAsync<JustJoinOfferDetail>(
                        $"{apiEndpoint}/{slug}", JsonOptions, cancellationToken);

                    if (offer is not null)
                    {
                        var vacancy = MapToVacancy(offer);
                        vacancy.Description = offer.Body ?? string.Empty;
                        return vacancy;
                    }
                }
                catch
                {
                    // Try next endpoint
                    continue;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[JustJoinIt] Detail fetch failed for {Url}", url);
            return null;
        }
    }

    private JobVacancy MapToVacancy(JustJoinOffer offer)
    {
        var (salaryMin, salaryMax, currency) = ExtractSalary(offer);

        return new JobVacancy
        {
            Id = GenerateId(offer.Id ?? offer.Slug ?? Guid.NewGuid().ToString()),
            Title = offer.Title ?? string.Empty,
            Company = offer.CompanyName ?? string.Empty,
            City = offer.City ?? string.Empty,
            Country = offer.CountryCode ?? "PL",
            Url = $"{WebUrl}/{offer.Slug}",
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryCurrency = currency,
            RemotePolicy = MapWorkplaceType(offer.WorkplaceType),
            SeniorityLevel = MapExperienceLevel(offer.ExperienceLevel),
            EngagementType = MapEngagementType(offer),
            GeoRestrictions = DetectGeoRestrictions(offer.Title),
            RequiredSkills = offer.Skills?
                .Select(s => s.Name ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? [],
            PostedDate = offer.PublishedAt ?? DateTimeOffset.MinValue,
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static (decimal? Min, decimal? Max, string Currency) ExtractSalary(JustJoinOffer offer)
    {
        if (offer.Salary is not null)
            return (offer.Salary.From, offer.Salary.To, offer.Salary.Currency?.ToUpperInvariant() ?? "PLN");

        if (offer.EmploymentTypes is { Count: > 0 })
        {
            var first = offer.EmploymentTypes.FirstOrDefault(e => e.Salary is not null);
            if (first?.Salary is not null)
                return (first.Salary.From, first.Salary.To, first.Salary.Currency?.ToUpperInvariant() ?? "PLN");
        }

        return (null, null, "PLN");
    }

    private static bool IsNetRelated(JustJoinOffer offer)
    {
        var title = offer.Title?.ToLowerInvariant() ?? "";
        var skills = offer.Skills?.Select(s => s.Name?.ToLowerInvariant() ?? "").ToList() ?? [];
        var marker = offer.MarkerIcon?.ToLowerInvariant() ?? "";

        return title.Contains(".net") || title.Contains("c#") || title.Contains("dotnet") ||
               skills.Any(s => s.Contains(".net") || s.Contains("c#") || s.Contains("dotnet")) ||
               marker.Contains("net");
    }

    private static RemotePolicy MapWorkplaceType(string? type) => type?.ToLowerInvariant() switch
    {
        "remote" => RemotePolicy.FullyRemote,
        "partly_remote" or "hybrid" => RemotePolicy.Hybrid,
        "office" => RemotePolicy.OnSite,
        _ => RemotePolicy.Unknown
    };

    private static Core.Enums.EngagementType MapEngagementType(JustJoinOffer offer)
    {
        // Check structured employment types first
        if (offer.EmploymentTypes is { Count: > 0 })
        {
            var types = offer.EmploymentTypes.Select(e => e.Type?.ToLowerInvariant() ?? "").ToList();

            if (types.Any(t => t.Contains("b2b")))
                return Core.Enums.EngagementType.ContractB2B;
            if (types.Any(t => t.Contains("contract")))
                return Core.Enums.EngagementType.ContractB2B;
            if (types.Any(t => t.Contains("freelance")))
                return Core.Enums.EngagementType.Freelance;
            if (types.All(t => t.Contains("permanent") || t.Contains("employment")))
                return Core.Enums.EngagementType.Employment;
        }

        // Fallback to text detection
        return DetectEngagementType(offer.Title);
    }

    private static SeniorityLevel MapExperienceLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "junior" => SeniorityLevel.Junior,
        "mid" => SeniorityLevel.Middle,
        "senior" => SeniorityLevel.Senior,
        "lead" => SeniorityLevel.Lead,
        "expert" or "architect" => SeniorityLevel.Architect,
        _ => SeniorityLevel.Unknown
    };

    // JSON models for JustJoin.it API response

    private class JustJoinOffer
    {
        public string? Id { get; set; }
        public string? Slug { get; set; }
        public string? Title { get; set; }
        [JsonPropertyName("company_name")]
        public string? CompanyName { get; set; }
        public string? City { get; set; }
        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }
        [JsonPropertyName("workplace_type")]
        public string? WorkplaceType { get; set; }
        [JsonPropertyName("experience_level")]
        public string? ExperienceLevel { get; set; }
        [JsonPropertyName("marker_icon")]
        public string? MarkerIcon { get; set; }
        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }
        public List<JustJoinSkill>? Skills { get; set; }
        public JustJoinSalary? Salary { get; set; }
        [JsonPropertyName("employment_types")]
        public List<JustJoinEmploymentType>? EmploymentTypes { get; set; }
    }

    private sealed class JustJoinOfferDetail : JustJoinOffer
    {
        public string? Body { get; set; }
    }

    private sealed class JustJoinSkill
    {
        public string? Name { get; set; }
        public int Level { get; set; }
    }

    private sealed class JustJoinSalary
    {
        public decimal? From { get; set; }
        public decimal? To { get; set; }
        public string? Currency { get; set; }
    }

    private sealed class JustJoinEmploymentType
    {
        public string? Type { get; set; }
        public JustJoinSalary? Salary { get; set; }
    }
}
