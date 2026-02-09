using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes public job listings from LinkedIn's guest-accessible search pages.
/// LinkedIn aggressively rate-limits and may return auth walls — this scraper
/// handles those scenarios gracefully.
/// </summary>
public sealed class LinkedInScraper(HttpClient httpClient, ILogger<LinkedInScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://www.linkedin.com";

    public override string PlatformName => "LinkedIn";

    // LinkedIn is aggressive about rate limiting — be respectful.
    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(5);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 3,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        for (var page = 0; page < maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = BuildSearchUrl(keywords, page * 25);
            var document = await FetchPageAsync(url, cancellationToken);

            if (document is null) break;

            // Detect auth wall
            if (IsAuthWall(document))
            {
                logger.LogWarning("[LinkedIn] Auth wall detected — cannot scrape further without login");
                break;
            }

            // TODO: Verify selectors against live LinkedIn guest job search page.
            var jobCards = SelectNodes(document, "//div[contains(@class, 'base-card')]");
            if (jobCards is null or { Count: 0 }) break;

            foreach (var card in jobCards)
            {
                var vacancy = ParseJobCard(card);
                if (vacancy is not null)
                    vacancies.Add(vacancy);
            }
        }

        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        var document = await FetchPageAsync(url, cancellationToken);
        if (document is null || IsAuthWall(document)) return null;
        return ParseDetailPage(document, url);
    }

    private static string BuildSearchUrl(string keywords, int start)
    {
        var encoded = HttpUtility.UrlEncode(keywords);
        // f_TPR=r86400 = last 24 hours, f_WT=2 = remote
        return $"{BaseUrl}/jobs/search/?keywords={encoded}&f_WT=2&f_TPR=r604800&start={start}";
    }

    private JobVacancy? ParseJobCard(HtmlNode card)
    {
        try
        {
            // TODO: Update selectors — LinkedIn changes HTML structure frequently.
            var titleNode = card.SelectSingleNode(".//h3[contains(@class, 'base-search-card__title')]");
            var companyNode = card.SelectSingleNode(".//h4[contains(@class, 'base-search-card__subtitle')]");
            var locationNode = card.SelectSingleNode(".//span[contains(@class, 'job-search-card__location')]");
            var linkNode = card.SelectSingleNode(".//a[contains(@class, 'base-card__full-link')]");
            var dateNode = card.SelectSingleNode(".//time");

            var detailUrl = ExtractAttribute(linkNode, "href");
            if (string.IsNullOrEmpty(detailUrl)) return null;

            var jobIdMatch = Regex.Match(detailUrl, @"(\d{8,})");
            var sourceId = jobIdMatch.Success ? jobIdMatch.Groups[1].Value : detailUrl.GetHashCode().ToString();

            var title = ExtractText(titleNode);
            var location = ExtractText(locationNode);
            var dateStr = ExtractAttribute(dateNode, "datetime");
            _ = DateTimeOffset.TryParse(dateStr, out var postedDate);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = ExtractText(companyNode),
                Url = CleanUrl(detailUrl),
                City = ParseCity(location),
                Country = ParseCountry(location),
                RemotePolicy = DetectRemotePolicy(location + " " + title),
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title),
                GeoRestrictions = DetectGeoRestrictions(title + " " + location),
                PostedDate = postedDate,
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
            var titleNode = SelectSingleNode(document,
                "//h1[contains(@class, 'top-card-layout__title')]");
            var companyNode = SelectSingleNode(document,
                "//a[contains(@class, 'topcard__org-name-link')]");
            var descNode = SelectSingleNode(document,
                "//div[contains(@class, 'show-more-less-html__markup')]");
            var criteriaNodes = SelectNodes(document,
                "//li[contains(@class, 'description__job-criteria-item')]");

            var description = ExtractText(descNode);
            var title = ExtractText(titleNode);

            var jobIdMatch = Regex.Match(url, @"(\d{8,})");
            var sourceId = jobIdMatch.Success ? jobIdMatch.Groups[1].Value : url.GetHashCode().ToString();

            var vacancy = new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = ExtractText(companyNode),
                Description = description,
                Url = url,
                SeniorityLevel = DetectSeniority(title + " " + description),
                RemotePolicy = DetectRemotePolicy(description),
                EngagementType = DetectEngagementType(description),
                GeoRestrictions = DetectGeoRestrictions(description),
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };

            // Parse job criteria (seniority, employment type, etc.)
            if (criteriaNodes is not null)
            {
                foreach (var criteria in criteriaNodes)
                {
                    var header = ExtractText(criteria.SelectSingleNode(".//h3"));
                    var value = ExtractText(criteria.SelectSingleNode(".//span"));

                    if (header.Contains("Seniority", StringComparison.OrdinalIgnoreCase))
                        vacancy.SeniorityLevel = MapLinkedInSeniority(value);
                }
            }

            return vacancy;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAuthWall(HtmlDocument document)
    {
        var loginForm = document.DocumentNode.SelectSingleNode("//form[contains(@action, 'login')]");
        var authHeader = document.DocumentNode.SelectSingleNode("//*[contains(@class, 'authwall')]");
        return loginForm is not null || authHeader is not null;
    }

    private static string CleanUrl(string url)
    {
        // Remove tracking parameters
        var idx = url.IndexOf('?');
        return idx > 0 ? url[..idx] : url;
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Unknown;
        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("manager")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static SeniorityLevel MapLinkedInSeniority(string value)
    {
        var lower = value.ToLowerInvariant().Trim();
        return lower switch
        {
            "director" or "executive" => SeniorityLevel.Principal,
            "associate" => SeniorityLevel.Middle,
            "mid-senior level" => SeniorityLevel.Senior,
            "entry level" => SeniorityLevel.Junior,
            "internship" => SeniorityLevel.Intern,
            _ => SeniorityLevel.Unknown
        };
    }

    private static RemotePolicy DetectRemotePolicy(string text)
    {
        if (string.IsNullOrEmpty(text)) return RemotePolicy.Unknown;
        var lower = text.ToLowerInvariant();

        if (lower.Contains("remote")) return RemotePolicy.FullyRemote;
        if (lower.Contains("hybrid")) return RemotePolicy.Hybrid;
        if (lower.Contains("on-site") || lower.Contains("onsite")) return RemotePolicy.OnSite;

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
