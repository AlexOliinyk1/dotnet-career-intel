using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from WorkingNomads.com — a curated remote job board for digital nomads.
/// WorkingNomads focuses exclusively on remote positions across tech, design, and management roles.
/// All positions are 100% remote with location flexibility.
/// </summary>
public sealed class WorkingNomadsScraper(HttpClient httpClient, ILogger<WorkingNomadsScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://www.workingnomads.com";

    // Search URLs for .NET/developer jobs
    private static readonly string[] SearchUrls =
    [
        "https://www.workingnomads.com/jobs?category=development",
        "https://www.workingnomads.com/jobs?category=programming"
    ];

    public override string PlatformName => "WorkingNomads";

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

                var url = page == 1 ? searchUrl : $"{searchUrl}&page={page}";
                logger.LogInformation("[WorkingNomads] Scraping {Url} page {Page}", searchUrl, page);

                var document = await FetchPageAsync(url, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[WorkingNomads] Failed to fetch page {Page} from {Url}", page, searchUrl);
                    break;
                }

                // Try multiple selector patterns for job cards
                // WorkingNomads uses links that wrap job content
                var jobCards = SelectNodes(document, "//a[contains(@href, '/jobs/')]")
                    ?? SelectNodes(document, "//li[contains(@class, 'job')]")
                    ?? SelectNodes(document, "//article[contains(@class, 'job')]")
                    ?? SelectNodes(document, "//div[contains(@class, 'job-card')]")
                    ?? SelectNodes(document, "//div[contains(@class, 'job-list-item')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogWarning("[WorkingNomads] No job cards found on page {Page}. Trying alternative selectors...", page);

                    // Broader fallback selectors - try any link or structure
                    jobCards = SelectNodes(document, "//div[contains(@class, 'posting')]") ??
                              SelectNodes(document, "//tr[contains(@class, 'job')]") ??
                              SelectNodes(document, "//li[contains(@class, 'listing')]") ??
                              SelectNodes(document, "//div[contains(@class, 'job')]") ??
                              SelectNodes(document, "//article");

                    if (jobCards is null or { Count: 0 })
                    {
                        logger.LogWarning("[WorkingNomads] Still no job cards found. Stopping at page {Page}", page);
                        break;
                    }
                }

                logger.LogInformation("[WorkingNomads] Found {Count} job cards on page {Page}", jobCards.Count, page);

                foreach (var card in jobCards)
                {
                    var vacancy = ParseJobCard(card);
                    if (vacancy is not null && !allVacancies.ContainsKey(vacancy.Id))
                    {
                        allVacancies[vacancy.Id] = vacancy;
                    }
                }

                // Check for next page
                var nextButton = SelectSingleNode(document, "//a[contains(@rel, 'next') or contains(@aria-label, 'Next') or contains(@class, 'next')]");
                if (nextButton is null)
                {
                    logger.LogDebug("[WorkingNomads] No next page button found, stopping at page {Page}", page);
                    break;
                }
            }
        }

        logger.LogInformation("[WorkingNomads] Scraped {Count} unique vacancies total", allVacancies.Count);
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
            // Find job link
            var linkNode = card.SelectSingleNode(".//a[@href and contains(@href, '/jobs/')]") ??
                          card.SelectSingleNode(".//a[@href]") ??
                          card;

            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                logger.LogDebug("[WorkingNomads] Job card has no link, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID from URL
            var idMatch = Regex.Match(fullUrl, @"/jobs/([a-zA-Z0-9\-]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : fullUrl.GetHashCode().ToString();

            // Extract title
            var titleNode = card.SelectSingleNode(".//h2") ??
                           card.SelectSingleNode(".//h3") ??
                           card.SelectSingleNode(".//*[contains(@class, 'job-title')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'title')]") ??
                           linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[WorkingNomads] Job card has no title, skipping");
                return null;
            }

            // Extract company
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]") ??
                            card.SelectSingleNode(".//*[contains(@class, 'employer')]") ??
                            card.SelectSingleNode(".//span[contains(@class, 'organization')]");
            var company = ExtractText(companyNode);

            // Extract location
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]") ??
                             card.SelectSingleNode(".//*[contains(@class, 'region')]") ??
                             card.SelectSingleNode(".//*[contains(text(), 'Remote')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'compensation')]") ??
                           card.SelectSingleNode(".//*[contains(text(), '$')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract skills/tags
            var skillNodes = card.SelectNodes(".//*[contains(@class, 'tag')]") ??
                           card.SelectNodes(".//*[contains(@class, 'skill')]") ??
                           card.SelectNodes(".//*[contains(@class, 'badge')]");
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

            // Extract description preview
            var descNode = card.SelectSingleNode(".//*[contains(@class, 'description')]") ??
                          card.SelectSingleNode(".//*[contains(@class, 'summary')]") ??
                          card.SelectSingleNode(".//*[contains(@class, 'excerpt')]");
            var description = ExtractText(descNode);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = string.Empty,
                Country = ParseCountry(location),
                Description = description,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = RemotePolicy.FullyRemote, // WorkingNomads is remote-only
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title + " " + description),
                GeoRestrictions = DetectGeoRestrictions(location + " " + description),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[WorkingNomads] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var idMatch = Regex.Match(url, @"/jobs/([a-zA-Z0-9\-]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1") ??
                          SelectSingleNode(document, "//h2[contains(@class, 'job-title')]");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//*[contains(@class, 'company-name')]") ??
                            SelectSingleNode(document, "//*[contains(@class, 'company')]") ??
                            SelectSingleNode(document, "//a[contains(@href, '/company/')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//*[contains(@class, 'job-description')]") ??
                                SelectSingleNode(document, "//*[contains(@class, 'description')]") ??
                                SelectSingleNode(document, "//article");
            var description = ExtractText(descriptionNode);

            // Extract location
            var locationNode = SelectSingleNode(document, "//*[contains(@class, 'location')]") ??
                             SelectSingleNode(document, "//*[contains(@class, 'region')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = SelectSingleNode(document, "//*[contains(@class, 'salary')]") ??
                           SelectSingleNode(document, "//*[contains(@class, 'compensation')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract skills
            var skillNodes = SelectNodes(document, "//*[contains(@class, 'tag')]") ??
                           SelectNodes(document, "//*[contains(@class, 'skill')]") ??
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
            var dateNode = SelectSingleNode(document, "//*[contains(@class, 'posted')]") ??
                          SelectSingleNode(document, "//*[contains(@class, 'date')]") ??
                          SelectSingleNode(document, "//time");
            var dateStr = ExtractText(dateNode);
            _ = DateTimeOffset.TryParse(dateStr, out var postedDate);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Description = description,
                Url = url,
                City = string.Empty,
                Country = ParseCountry(location),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = RemotePolicy.FullyRemote,
                SeniorityLevel = DetectSeniority(title + " " + description),
                EngagementType = DetectEngagementType(description),
                GeoRestrictions = DetectGeoRestrictions(location + " " + description),
                RequiredSkills = skills,
                PostedDate = postedDate,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[WorkingNomads] Failed to parse detail page: {Url}", url);
            return null;
        }
    }

    private static string ParseCountry(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "Remote";

        var lower = location.ToLowerInvariant();

        if (lower.Contains("worldwide") || lower.Contains("anywhere") || lower.Contains("global"))
            return "Worldwide";

        if (lower.Contains("remote"))
            return "Remote";

        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[^1] : location.Trim();
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
