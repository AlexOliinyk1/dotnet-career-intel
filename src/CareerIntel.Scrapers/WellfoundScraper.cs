using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Wellfound (formerly AngelList Talent) — a platform for startup jobs.
/// Wellfound connects job seekers with high-growth startups and tech companies.
/// Focuses on remote and hybrid positions in the tech industry.
/// </summary>
public sealed class WellfoundScraper(HttpClient httpClient, ILogger<WellfoundScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://wellfound.com";
    private const string JobsUrl = "https://wellfound.com/role/r/.net-developer";

    public override string PlatformName => "Wellfound";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        logger.LogInformation("[Wellfound] Starting scrape for .NET developer roles");

        var document = await FetchPageAsync(JobsUrl, cancellationToken);
        if (document is null)
        {
            logger.LogWarning("[Wellfound] Failed to fetch jobs page");
            return vacancies;
        }

        // Try multiple selector patterns for job listings
        var jobCards = SelectNodes(document, "//div[contains(@class, 'job') and contains(@class, 'card')]")
            ?? SelectNodes(document, "//div[contains(@data-test, 'JobSearchResult')]")
            ?? SelectNodes(document, "//a[contains(@href, '/company/') and contains(@href, '/jobs/')]")
            ?? SelectNodes(document, "//div[contains(@class, 'startup-result')]");

        if (jobCards is null or { Count: 0 })
        {
            logger.LogWarning("[Wellfound] No job cards found. Trying alternative selectors...");

            // Try broader selectors
            jobCards = SelectNodes(document, "//article") ??
                      SelectNodes(document, "//div[contains(@class, 'listing')]") ??
                      SelectNodes(document, "//li[contains(@class, 'job')]");

            if (jobCards is null or { Count: 0 })
            {
                logger.LogWarning("[Wellfound] Could not find job listings. HTML structure may have changed.");

                // Log diagnostic info
                var allLinks = SelectNodes(document, "//a[@href]");
                logger.LogDebug("[Wellfound] Total links found: {LinkCount}", allLinks?.Count ?? 0);
                return vacancies;
            }
        }

        logger.LogInformation("[Wellfound] Found {Count} job cards", jobCards.Count);

        foreach (var card in jobCards)
        {
            var vacancy = ParseJobCard(card);
            if (vacancy is not null)
            {
                vacancies.Add(vacancy);
            }
        }

        logger.LogInformation("[Wellfound] Scraped {Count} total vacancies", vacancies.Count);
        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        var document = await FetchPageAsync(url, cancellationToken);
        if (document is null) return null;

        return ParseDetailPage(document, url);
    }

    private JobVacancy? ParseJobCard(HtmlNode card)
    {
        try
        {
            // Find job link - Wellfound uses specific URL patterns
            var linkNode = card.SelectSingleNode(".//a[contains(@href, '/company/') and contains(@href, '/jobs/')]") ??
                          card.SelectSingleNode(".//a[contains(@href, '/jobs/')]") ??
                          card.SelectSingleNode(".//a[@href]") ??
                          card;

            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                logger.LogDebug("[Wellfound] Job card has no link, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID from URL pattern: /company/{company}/jobs/{job-id}
            var idMatch = Regex.Match(fullUrl, @"/jobs/(\d+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : fullUrl.GetHashCode().ToString();

            // Extract title
            var titleNode = card.SelectSingleNode(".//h2") ??
                           card.SelectSingleNode(".//h3") ??
                           card.SelectSingleNode(".//*[contains(@class, 'title')]") ??
                           linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[Wellfound] Job card has no title, skipping");
                return null;
            }

            // Extract company name
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]") ??
                            card.SelectSingleNode(".//*[contains(@class, 'startup')]") ??
                            card.SelectSingleNode(".//h4");
            var company = ExtractText(companyNode);

            // Extract location/remote info
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]") ??
                             card.SelectSingleNode(".//*[contains(@class, 'remote')]") ??
                             card.SelectSingleNode(".//*[contains(text(), 'Remote')]");
            var location = ExtractText(locationNode);

            // Extract salary/compensation
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'compensation')]") ??
                           card.SelectSingleNode(".//*[contains(text(), '$')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract tags/skills if available
            var tagNodes = card.SelectNodes(".//*[contains(@class, 'tag')]");
            var skills = new List<string>();
            if (tagNodes is not null)
            {
                foreach (var tag in tagNodes)
                {
                    var skill = ExtractText(tag);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        skills.Add(skill);
                }
            }

            // Extract job type (remote/hybrid/onsite)
            var remotePolicy = DetectRemotePolicy(title + " " + location);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = ParseCity(location),
                Country = ParseCountry(location),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = remotePolicy,
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title),
                GeoRestrictions = DetectGeoRestrictions(title + " " + location),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Wellfound] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var idMatch = Regex.Match(url, @"/jobs/(\d+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1") ??
                          SelectSingleNode(document, "//h2[contains(@class, 'title')]");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//*[contains(@class, 'company-name')]") ??
                            SelectSingleNode(document, "//h2[contains(@class, 'company')]") ??
                            SelectSingleNode(document, "//a[contains(@href, '/company/')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//*[contains(@class, 'description')]") ??
                                SelectSingleNode(document, "//*[@id='job-description']") ??
                                SelectSingleNode(document, "//article");
            var description = ExtractText(descriptionNode);

            // Extract location
            var locationNode = SelectSingleNode(document, "//*[contains(@class, 'location')]") ??
                             SelectSingleNode(document, "//*[contains(text(), 'Remote')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = SelectSingleNode(document, "//*[contains(@class, 'salary')]") ??
                           SelectSingleNode(document, "//*[contains(@class, 'compensation')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract skills/tags
            var skillNodes = SelectNodes(document, "//*[contains(@class, 'tag')]");
            var skills = new List<string>();
            if (skillNodes is not null)
            {
                foreach (var skillNode in skillNodes)
                {
                    var skill = ExtractText(skillNode);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        skills.Add(skill);
                }
            }

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Description = description,
                Url = url,
                City = ParseCity(location),
                Country = ParseCountry(location),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = DetectRemotePolicy(title + " " + description + " " + location),
                SeniorityLevel = DetectSeniority(title + " " + description),
                EngagementType = DetectEngagementType(description),
                GeoRestrictions = DetectGeoRestrictions(description),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Wellfound] Failed to parse detail page: {Url}", url);
            return null;
        }
    }

    private static string ParseCity(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        if (location.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : location;
    }

    private static string ParseCountry(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        if (location.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            return "Remote";

        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[^1] : location;
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("engineering manager")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }
}
