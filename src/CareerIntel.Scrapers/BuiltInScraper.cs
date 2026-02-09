using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Built In — a platform for tech companies and startups.
/// Built In focuses on tech jobs across various cities and supports remote positions.
/// Features clean HTML structure with semantic markup.
/// </summary>
public sealed class BuiltInScraper(HttpClient httpClient, ILogger<BuiltInScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://builtin.com";

    // Search for .NET jobs across multiple categories
    private static readonly string[] SearchUrls =
    [
        "https://builtin.com/jobs/remote/.net",
        "https://builtin.com/jobs/remote/c-sharp",
        "https://builtin.com/jobs/remote/dotnet"
    ];

    public override string PlatformName => "BuiltIn";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var allVacancies = new Dictionary<string, JobVacancy>();

        foreach (var searchUrl in SearchUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var page = 1; page <= maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var url = page == 1 ? searchUrl : $"{searchUrl}?page={page}";
                logger.LogInformation("[BuiltIn] Scraping {Url} page {Page}", searchUrl, page);

                var document = await FetchPageAsync(url, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[BuiltIn] Failed to fetch page {Page} from {Url}", page, searchUrl);
                    break;
                }

                // Try multiple selector patterns for job listings
                var jobCards = SelectNodes(document, "//div[contains(@class, 'job-item')]")
                    ?? SelectNodes(document, "//div[contains(@class, 'job-card')]")
                    ?? SelectNodes(document, "//article[contains(@class, 'job')]")
                    ?? SelectNodes(document, "//div[contains(@data-id, 'job')]")
                    ?? SelectNodes(document, "//a[contains(@href, '/job/')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogWarning("[BuiltIn] No job cards found on page {Page}. Trying alternative selectors...", page);

                    // Broader fallback selectors
                    jobCards = SelectNodes(document, "//div[contains(@class, 'card')]") ??
                              SelectNodes(document, "//li[contains(@class, 'listing')]") ??
                              SelectNodes(document, "//div[contains(@class, 'result')]");

                    if (jobCards is null or { Count: 0 })
                    {
                        logger.LogWarning("[BuiltIn] Still no job cards found. Stopping at page {Page}", page);
                        break;
                    }
                }

                logger.LogInformation("[BuiltIn] Found {Count} job cards on page {Page}", jobCards.Count, page);

                foreach (var card in jobCards)
                {
                    var vacancy = ParseJobCard(card);
                    if (vacancy is not null && !allVacancies.ContainsKey(vacancy.Id))
                    {
                        allVacancies[vacancy.Id] = vacancy;
                    }
                }

                // Check for next page button
                var nextButton = SelectSingleNode(document, "//a[contains(@rel, 'next') or contains(text(), 'Next') or contains(@class, 'next')]");
                if (nextButton is null)
                {
                    logger.LogDebug("[BuiltIn] No next page button found, stopping at page {Page}", page);
                    break;
                }
            }
        }

        logger.LogInformation("[BuiltIn] Scraped {Count} unique vacancies total", allVacancies.Count);
        return allVacancies.Values.ToList();
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
            // Find the job link
            var linkNode = card.SelectSingleNode(".//a[@href and contains(@href, '/job/')]") ??
                          card.SelectSingleNode(".//a[@href and contains(@href, '/jobs/')]") ??
                          card.SelectSingleNode(".//a[@href]") ??
                          card;

            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                logger.LogDebug("[BuiltIn] Job card has no link, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID from URL pattern: /job/{job-id} or /jobs/{job-id}
            var idMatch = Regex.Match(fullUrl, @"/jobs?/([a-zA-Z0-9\-]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : fullUrl.GetHashCode().ToString();

            // Extract title - multiple patterns
            var titleNode = card.SelectSingleNode(".//h2") ??
                           card.SelectSingleNode(".//h3") ??
                           card.SelectSingleNode(".//*[contains(@class, 'title')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'job-title')]") ??
                           linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[BuiltIn] Job card has no title, skipping");
                return null;
            }

            // Extract company name
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]") ??
                            card.SelectSingleNode(".//*[contains(@class, 'employer')]") ??
                            card.SelectSingleNode(".//h4");
            var company = ExtractText(companyNode);

            // Extract location
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]") ??
                             card.SelectSingleNode(".//*[contains(@class, 'city')]") ??
                             card.SelectSingleNode(".//*[contains(text(), 'Remote')]");
            var location = ExtractText(locationNode);

            // Extract salary if available
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'compensation')]") ??
                           card.SelectSingleNode(".//*[contains(text(), '$')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract tags/skills
            var tagNodes = card.SelectNodes(".//*[contains(@class, 'tag')]") ??
                          card.SelectNodes(".//*[contains(@class, 'skill')]") ??
                          card.SelectNodes(".//*[contains(@class, 'badge')]");
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

            // Extract short description if available
            var descNode = card.SelectSingleNode(".//*[contains(@class, 'description')]") ??
                          card.SelectSingleNode(".//*[contains(@class, 'summary')]");
            var description = ExtractText(descNode);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = ParseCity(location),
                Country = ParseCountry(location),
                Description = description,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = DetectRemotePolicy(title + " " + location + " " + description),
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title + " " + description),
                GeoRestrictions = DetectGeoRestrictions(location),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[BuiltIn] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var idMatch = Regex.Match(url, @"/jobs?/([a-zA-Z0-9\-]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1") ??
                          SelectSingleNode(document, "//h2[contains(@class, 'job-title')]");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//*[contains(@class, 'company-name')]") ??
                            SelectSingleNode(document, "//h2[contains(@class, 'company')]") ??
                            SelectSingleNode(document, "//a[contains(@href, '/company/')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//*[contains(@class, 'job-description')]") ??
                                SelectSingleNode(document, "//*[@id='job-description']") ??
                                SelectSingleNode(document, "//article") ??
                                SelectSingleNode(document, "//*[contains(@class, 'description')]");
            var description = ExtractText(descriptionNode);

            // Extract location
            var locationNode = SelectSingleNode(document, "//*[contains(@class, 'job-location')]") ??
                             SelectSingleNode(document, "//*[contains(@class, 'location')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = SelectSingleNode(document, "//*[contains(@class, 'salary')]") ??
                           SelectSingleNode(document, "//*[contains(@class, 'compensation')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract skills/tags
            var skillNodes = SelectNodes(document, "//*[contains(@class, 'skill')]") ??
                           SelectNodes(document, "//*[contains(@class, 'tag')]") ??
                           SelectNodes(document, "//*[contains(@class, 'badge')]");
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

            // Extract posted date
            var dateNode = SelectSingleNode(document, "//*[@itemprop='datePosted']") ??
                          SelectSingleNode(document, "//*[contains(@class, 'posted-date')]") ??
                          SelectSingleNode(document, "//*[contains(@class, 'date')]");
            var dateStr = ExtractAttribute(dateNode, "datetime") ?? ExtractText(dateNode);
            _ = DateTimeOffset.TryParse(dateStr, out var postedDate);

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
                GeoRestrictions = DetectGeoRestrictions(location),
                RequiredSkills = skills,
                PostedDate = postedDate,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[BuiltIn] Failed to parse detail page: {Url}", url);
            return null;
        }
    }

    private static string ParseCity(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        if (location.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        // Extract city from "City, State" or "City, Country"
        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : location;
    }

    private static string ParseCountry(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        if (location.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            return "Remote";

        // Extract country from "City, State, Country" or "City, Country"
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
