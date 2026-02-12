using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job vacancies from DOU.ua — Ukraine's leading IT community and job board.
/// </summary>
public sealed class DouScraper(HttpClient httpClient, ILogger<DouScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://jobs.dou.ua";

    public override string PlatformName => "DOU";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        for (var page = 0; page < maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = BuildSearchUrl(keywords, page);
            var document = await FetchPageAsync(url, cancellationToken);
            if (document is null) break;

            // TODO: Verify selectors against live jobs.dou.ua page structure.
            var jobCards = SelectNodes(document, "//li[contains(@class, 'l-vacancy')]");
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
        if (document is null) return null;
        return ParseDetailPage(document, url);
    }

    private static string BuildSearchUrl(string keywords, int page)
    {
        var encoded = HttpUtility.UrlEncode(keywords);
        return $"{BaseUrl}/vacancies/?category=.NET&search={encoded}&descr=1&page={page}";
    }

    private JobVacancy? ParseJobCard(HtmlNode card)
    {
        try
        {
            // TODO: Update XPath selectors based on actual DOU HTML structure.
            var titleNode = card.SelectSingleNode(".//a[contains(@class, 'vt')]");
            var companyNode = card.SelectSingleNode(".//a[contains(@class, 'company')]");
            var salaryNode = card.SelectSingleNode(".//span[contains(@class, 'salary')]");
            var citiesNode = card.SelectSingleNode(".//span[contains(@class, 'cities')]");
            var dateNode = card.SelectSingleNode(".//div[contains(@class, 'date')]");

            var detailUrl = ExtractAttribute(titleNode, "href");
            if (string.IsNullOrEmpty(detailUrl)) return null;

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";
            var idMatch = Regex.Match(detailUrl, @"/vacancies/(\d+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : detailUrl.GetHashCode().ToString();

            var title = ExtractText(titleNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));
            var location = ExtractText(citiesNode);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = ExtractText(companyNode),
                Url = fullUrl,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                City = ParseCity(location),
                Country = "Ukraine",
                RemotePolicy = DetectRemotePolicy(location + " " + title),
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title),
                GeoRestrictions = DetectGeoRestrictions(title),
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _ = ex; // logged by caller
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var titleNode = SelectSingleNode(document, "//h1[contains(@class, 'g-h2')]");
            var descNode = SelectSingleNode(document, "//div[contains(@class, 'b-typo vacancy-section')]");
            var companyNode = SelectSingleNode(document, "//a[contains(@class, 'company')]");
            var salaryNode = SelectSingleNode(document, "//span[contains(@class, 'salary')]");

            var description = ExtractText(descNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));
            var title = ExtractText(titleNode);

            var idMatch = Regex.Match(url, @"/vacancies/(\d+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = ExtractText(companyNode),
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                SeniorityLevel = DetectSeniority(title + " " + description),
                RemotePolicy = DetectRemotePolicy(description),
                EngagementType = DetectEngagementType(description),
                GeoRestrictions = DetectGeoRestrictions(description),
                Country = "Ukraine",
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _ = ex; // logged by caller
            return null;
        }
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Unknown;
        var lower = text.ToLowerInvariant();

        // Ukrainian terms
        if (lower.Contains("архітектор")) return SeniorityLevel.Architect;
        if (lower.Contains("тімлід") || lower.Contains("тім лід") || lower.Contains("лід")) return SeniorityLevel.Lead;
        if (lower.Contains("сеніор") || lower.Contains("синьйор")) return SeniorityLevel.Senior;
        if (lower.Contains("мідл")) return SeniorityLevel.Middle;
        if (lower.Contains("джуніор")) return SeniorityLevel.Junior;

        // English terms
        if (lower.Contains("principal")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.")) return SeniorityLevel.Senior;
        if (lower.Contains("middle") || lower.Contains("mid-level")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.")) return SeniorityLevel.Junior;

        return SeniorityLevel.Unknown;
    }

    private static RemotePolicy DetectRemotePolicy(string text)
    {
        if (string.IsNullOrEmpty(text)) return RemotePolicy.Unknown;
        var lower = text.ToLowerInvariant();

        if (lower.Contains("віддалено") || lower.Contains("fully remote") ||
            lower.Contains("full remote") || lower.Contains("100% remote"))
            return RemotePolicy.FullyRemote;
        if (lower.Contains("remote") || lower.Contains("ремоут"))
            return RemotePolicy.RemoteFriendly;
        if (lower.Contains("hybrid") || lower.Contains("гібрид"))
            return RemotePolicy.Hybrid;
        if (lower.Contains("office") || lower.Contains("on-site") || lower.Contains("офіс"))
            return RemotePolicy.OnSite;

        return RemotePolicy.Unknown;
    }

    private static string ParseCity(string location)
    {
        if (string.IsNullOrEmpty(location)) return string.Empty;
        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }
}
