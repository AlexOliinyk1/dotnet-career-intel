using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from We Work Remotely — one of the largest remote work communities.
/// Switched to HTML scraping after JSON API deprecation (returns 406 Not Acceptable).
/// </summary>
public sealed class WeWorkRemotelyScraper(HttpClient httpClient, ILogger<WeWorkRemotelyScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://weworkremotely.com";

    // Search URLs for back-end/programming jobs
    private static readonly string[] SearchUrls =
    [
        "https://weworkremotely.com/remote-jobs/search?term=.net",
        "https://weworkremotely.com/categories/remote-back-end-programming-jobs",
        "https://weworkremotely.com/categories/remote-full-stack-programming-jobs"
    ];

    public override string PlatformName => "WeWorkRemotely";

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

            logger.LogInformation("[WeWorkRemotely] Scraping {Url}", searchUrl);

            var document = await FetchPageAsync(searchUrl, cancellationToken);
            if (document is null)
            {
                logger.LogWarning("[WeWorkRemotely] Failed to fetch {Url}", searchUrl);
                continue;
            }

            // Try multiple selector patterns to find job listings
            var jobCards = SelectNodes(document, "//li[contains(@class, 'feature')]")
                ?? SelectNodes(document, "//section[@class='jobs']//li")
                ?? SelectNodes(document, "//article[contains(@class, 'job')]")
                ?? SelectNodes(document, "//a[contains(@href, '/remote-jobs/')]");

            if (jobCards is null or { Count: 0 })
            {
                logger.LogWarning("[WeWorkRemotely] No job cards found for {Url}. Trying alternative selectors...", searchUrl);

                // Try broader selectors
                jobCards = SelectNodes(document, "//ul[contains(@class, 'jobs')]//li")
                    ?? SelectNodes(document, "//div[contains(@class, 'job')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogWarning("[WeWorkRemotely] Still no job cards found for {Url}", searchUrl);
                    continue;
                }
            }

            logger.LogInformation("[WeWorkRemotely] Found {Count} job cards", jobCards.Count);

            foreach (var card in jobCards)
            {
                var vacancy = ParseJobCard(card);
                if (vacancy is not null && !allVacancies.ContainsKey(vacancy.Id))
                {
                    allVacancies[vacancy.Id] = vacancy;
                }
            }
        }

        logger.LogInformation("[WeWorkRemotely] Scraped {Count} unique vacancies total", allVacancies.Count);
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
            var linkNode = card.SelectSingleNode(".//a[contains(@href, '/remote-jobs/')]")
                ?? card.SelectSingleNode(".//a[@href]");

            if (linkNode is null)
            {
                logger.LogDebug("[WeWorkRemotely] Job card has no link, skipping");
                return null;
            }

            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                logger.LogDebug("[WeWorkRemotely] Job card has no link URL, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID from URL (e.g., /remote-jobs/123456/title-slug)
            var urlParts = detailUrl.Split('/');
            var jobId = urlParts.FirstOrDefault(p => int.TryParse(p, out _)) ?? fullUrl.GetHashCode().ToString();

            // Extract title
            var titleNode = card.SelectSingleNode(".//span[@class='title']")
                ?? card.SelectSingleNode(".//h2")
                ?? card.SelectSingleNode(".//*[contains(@class, 'title')]")
                ?? linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[WeWorkRemotely] Job card has no title, skipping. URL: {Url}", fullUrl);
                return null;
            }

            // Extract company
            var companyNode = card.SelectSingleNode(".//span[@class='company']")
                ?? card.SelectSingleNode(".//*[contains(@class, 'company')]");
            var company = ExtractText(companyNode);

            // Extract region/location
            var regionNode = card.SelectSingleNode(".//span[@class='region']")
                ?? card.SelectSingleNode(".//*[contains(@class, 'region')]");
            var region = ExtractText(regionNode);

            // Extract tags/skills
            var tagNodes = card.SelectNodes(".//*[contains(@class, 'tag')]")
                ?? card.SelectNodes(".//span[contains(@class, 'label')]");

            var skills = new List<string>();
            if (tagNodes is not null)
            {
                foreach (var tagNode in tagNodes)
                {
                    var skill = ExtractText(tagNode);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        skills.Add(skill);
                }
            }

            return new JobVacancy
            {
                Id = GenerateId(jobId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = string.Empty,
                Country = ParseRegion(region),
                SalaryMin = null,
                SalaryMax = null,
                SalaryCurrency = "USD",
                RemotePolicy = RemotePolicy.FullyRemote, // WeWorkRemotely is remote-only
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title),
                GeoRestrictions = DetectGeoRestrictions(title + " " + region),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[WeWorkRemotely] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var urlParts = url.Split('/');
            var jobId = urlParts.FirstOrDefault(p => int.TryParse(p, out _)) ?? url.GetHashCode().ToString();

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1")
                ?? SelectSingleNode(document, "//h2[@class='title']");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//h2[contains(@class, 'company')]")
                ?? SelectSingleNode(document, "//*[contains(@class, 'company-card')]//h2")
                ?? SelectSingleNode(document, "//a[contains(@href, '/company/')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//div[contains(@class, 'listing-container')]")
                ?? SelectSingleNode(document, "//div[contains(@class, 'job-description')]")
                ?? SelectSingleNode(document, "//article");
            var description = ExtractText(descriptionNode);

            // Extract region
            var regionNode = SelectSingleNode(document, "//*[contains(@class, 'region')]");
            var region = ExtractText(regionNode);

            // Extract tags
            var tagNodes = SelectNodes(document, "//*[contains(@class, 'tag')]")
                ?? SelectNodes(document, "//span[contains(@class, 'label')]");
            var skills = new List<string>();
            if (tagNodes is not null)
            {
                foreach (var tagNode in tagNodes)
                {
                    var skill = ExtractText(tagNode);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        skills.Add(skill);
                }
            }

            // Try to extract salary from description
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(description);

            return new JobVacancy
            {
                Id = GenerateId(jobId),
                Title = title,
                Company = company,
                Description = description,
                Url = url,
                City = string.Empty,
                Country = ParseRegion(region),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = RemotePolicy.FullyRemote,
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
            logger.LogError(ex, "[WeWorkRemotely] Failed to parse detail page: {Url}", url);
            return null;
        }
    }

    private static string ParseRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
            return "Worldwide";

        var lower = region.ToLowerInvariant();

        if (lower.Contains("anywhere") || lower.Contains("worldwide"))
            return "Worldwide";

        if (lower.Contains("usa") || lower.Contains("united states") || lower.Contains("us only"))
            return "United States";

        if (lower.Contains("europe") || lower.Contains("eu"))
            return "Europe";

        return region.Trim();
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("regular")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.")) return SeniorityLevel.Junior;
        if (lower.Contains("intern")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }
}
