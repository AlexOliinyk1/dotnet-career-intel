using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job vacancies from djinni.co — a popular Ukrainian IT job board.
/// Focuses on .NET positions with configurable seniority and remote filters.
/// </summary>
public sealed class DjinniScraper : BaseScraper
{
    private const string BaseUrl = "https://djinni.co";
    private readonly ILogger<DjinniScraper> _logger;

    public override string PlatformName => "Djinni";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    public DjinniScraper(HttpClient httpClient, ILogger<DjinniScraper> logger, ScrapingCompliance? compliance = null)
        : base(httpClient, logger, compliance)
    {
        _logger = logger;
    }

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        for (var page = 1; page <= maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = BuildSearchUrl(keywords, page);
            var document = await FetchPageAsync(url, cancellationToken);

            if (document is null) break;

            // Try multiple selector patterns - Djinni may have changed their HTML structure
            var jobCards = SelectNodes(document, "//li[contains(@class, 'list-jobs__item')]");

            if (jobCards is null || jobCards.Count == 0)
            {
                // Try alternative selector patterns
                jobCards = SelectNodes(document, "//li[contains(@class, 'job-item')]") ??
                          SelectNodes(document, "//div[contains(@class, 'job-list-item')]") ??
                          SelectNodes(document, "//article[contains(@class, 'job')]") ??
                          SelectNodes(document, "//li[contains(@class, 'list-item')]") ??
                          SelectNodes(document, "//ul[contains(@class, 'jobs')]/li") ??
                          SelectNodes(document, "//div[contains(@class, 'vacancy')]") ??
                          SelectNodes(document, "//div[contains(@class, 'job-card')]");
            }

            if (jobCards is null || jobCards.Count == 0)
            {
                // Log diagnostic info about page structure
                var allLists = SelectNodes(document, "//ul") ?? SelectNodes(document, "//ol");
                var allArticles = SelectNodes(document, "//article");
                var allDivsWithClass = SelectNodes(document, "//div[@class]");

                _logger.LogWarning("[Djinni] No job cards found on page {Page}. Found {Lists} lists, {Articles} articles, {Divs} divs with classes",
                    page, allLists?.Count ?? 0, allArticles?.Count ?? 0, allDivsWithClass?.Count ?? 0);

                // Log some class names to help identify structure
                if (allDivsWithClass is not null && allDivsWithClass.Count > 0)
                {
                    var sampleClasses = allDivsWithClass
                        .Take(10)
                        .Select(d => d.GetAttributeValue("class", ""))
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Take(5);
                    _logger.LogDebug("[Djinni] Sample div classes: {Classes}", string.Join(", ", sampleClasses));
                }

                break;
            }

            _logger.LogDebug("[Djinni] Found {Count} job cards on page {Page}", jobCards.Count, page);

            foreach (var card in jobCards)
            {
                var vacancy = ParseJobCard(card);
                if (vacancy is not null)
                {
                    vacancies.Add(vacancy);
                }
            }
        }

        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var document = await FetchPageAsync(url, cancellationToken);
        if (document is null) return null;

        return ParseDetailPage(document, url);
    }

    private static string BuildSearchUrl(string keywords, int page)
    {
        var encodedKeywords = HttpUtility.UrlEncode(keywords);
        // TODO: Verify query parameter names against live site.
        return $"{BaseUrl}/jobs/?primary_keyword={encodedKeywords}&page={page}";
    }

    private JobVacancy? ParseJobCard(HtmlNode card)
    {
        try
        {
            // Try multiple selector patterns for title link
            var titleNode = card.SelectSingleNode(".//a[contains(@class, 'profile')]") ??
                           card.SelectSingleNode(".//a[contains(@href, '/jobs/')]") ??
                           card.SelectSingleNode(".//h2/a") ??
                           card.SelectSingleNode(".//h3/a") ??
                           card.SelectSingleNode(".//a[contains(@class, 'job-title')]") ??
                           card.SelectSingleNode(".//a[1]"); // Fallback to first link

            var companyNode = card.SelectSingleNode(".//a[contains(@class, 'company')]") ??
                             card.SelectSingleNode(".//*[contains(@class, 'company-name')]");
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'public-salary')]") ??
                            card.SelectSingleNode(".//*[contains(@class, 'salary')]");
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]");
            var detailUrl = ExtractAttribute(titleNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                _logger.LogDebug("[Djinni] Job card has no valid link, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";
            var idMatch = Regex.Match(detailUrl, @"/jobs/(\d+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : detailUrl.GetHashCode().ToString();

            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = ExtractText(titleNode),
                Company = ExtractText(companyNode),
                Url = fullUrl,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                City = ParseCity(ExtractText(locationNode)),
                Country = ParseCountry(ExtractText(locationNode)),
                RemotePolicy = DetectRemotePolicy(ExtractText(locationNode)),
                EngagementType = DetectEngagementType(ExtractText(titleNode)),
                GeoRestrictions = DetectGeoRestrictions(ExtractText(titleNode)),
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            // TODO: Update selectors for the vacancy detail page structure.
            var titleNode = SelectSingleNode(document, "//h1");
            var descriptionNode = SelectSingleNode(document,
                "//div[contains(@class, 'vacancy-description')]");
            var companyNode = SelectSingleNode(document,
                "//a[contains(@class, 'company')]");
            var salaryNode = SelectSingleNode(document,
                "//*[contains(@class, 'public-salary')]");

            var description = ExtractText(descriptionNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            var idMatch = Regex.Match(url, @"/jobs/(\d+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = ExtractText(titleNode),
                Company = ExtractText(companyNode),
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                SeniorityLevel = DetectSeniority(description),
                RemotePolicy = DetectRemotePolicy(description),
                EngagementType = DetectEngagementType(description),
                GeoRestrictions = DetectGeoRestrictions(description),
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();
        if (lower.Contains("principal")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.")) return SeniorityLevel.Senior;
        if (lower.Contains("middle") || lower.Contains("mid-level")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee")) return SeniorityLevel.Intern;
        return SeniorityLevel.Unknown;
    }

    private static RemotePolicy DetectRemotePolicy(string text)
    {
        if (string.IsNullOrEmpty(text)) return RemotePolicy.Unknown;

        var lower = text.ToLowerInvariant();
        if (lower.Contains("fully remote") || lower.Contains("full remote")
            || lower.Contains("100% remote")) return RemotePolicy.FullyRemote;
        if (lower.Contains("remote")) return RemotePolicy.RemoteFriendly;
        if (lower.Contains("hybrid")) return RemotePolicy.Hybrid;
        if (lower.Contains("office") || lower.Contains("on-site")
            || lower.Contains("onsite")) return RemotePolicy.OnSite;
        return RemotePolicy.Unknown;
    }

    private static string ParseCity(string location)
    {
        if (string.IsNullOrEmpty(location)) return string.Empty;
        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static string ParseCountry(string location)
    {
        if (string.IsNullOrEmpty(location)) return string.Empty;
        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[^1] : string.Empty;
    }
}
