using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Arc.dev — a platform for remote developers.
/// Arc.dev focuses on pre-vetted remote developers and high-quality remote positions.
/// All Arc.dev listings are remote positions.
/// </summary>
public sealed class ArcDevScraper(HttpClient httpClient, ILogger<ArcDevScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://arc.dev";
    private const string JobsUrl = "https://arc.dev/remote-jobs/dot-net";

    public override string PlatformName => "ArcDev";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        for (var page = 1; page <= maxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = page == 1 ? JobsUrl : $"{JobsUrl}?page={page}";
            logger.LogInformation("[ArcDev] Scraping page {Page}: {Url}", page, url);

            var document = await FetchPageAsync(url, cancellationToken);
            if (document is null)
            {
                logger.LogWarning("[ArcDev] Failed to fetch page {Page}", page);
                break;
            }

            // Try multiple selector patterns for job cards
            var jobCards = SelectNodes(document, "//div[contains(@class, 'job-card')]")
                ?? SelectNodes(document, "//article[contains(@class, 'job')]")
                ?? SelectNodes(document, "//div[contains(@class, 'position')]")
                ?? SelectNodes(document, "//a[contains(@href, '/remote-jobs/') and contains(@href, 'dot-net')]");

            if (jobCards is null or { Count: 0 })
            {
                logger.LogWarning("[ArcDev] No job cards found on page {Page}. HTML structure may have changed.", page);

                // Log diagnostic info
                var allLinks = SelectNodes(document, "//a[@href]");
                logger.LogDebug("[ArcDev] Total links found: {LinkCount}", allLinks?.Count ?? 0);
                break;
            }

            logger.LogInformation("[ArcDev] Found {Count} job cards on page {Page}", jobCards.Count, page);

            foreach (var card in jobCards)
            {
                var vacancy = ParseJobCard(card);
                if (vacancy is not null)
                {
                    vacancies.Add(vacancy);
                }
            }

            // Check if there's a next page
            var nextPageLink = SelectSingleNode(document, "//a[contains(@class, 'next') or contains(text(), 'Next')]");
            if (nextPageLink is null)
            {
                logger.LogDebug("[ArcDev] No next page link found, stopping at page {Page}", page);
                break;
            }
        }

        logger.LogInformation("[ArcDev] Scraped {Count} total vacancies", vacancies.Count);
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
            // Try to find the job link
            var linkNode = card.SelectSingleNode(".//a[@href]") ?? card;
            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                logger.LogDebug("[ArcDev] Job card has no link, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID from URL
            var idMatch = Regex.Match(fullUrl, @"/remote-jobs/([^/?]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : fullUrl.GetHashCode().ToString();

            // Try multiple patterns for title
            var titleNode = card.SelectSingleNode(".//h2") ??
                           card.SelectSingleNode(".//h3") ??
                           card.SelectSingleNode(".//*[contains(@class, 'title')]") ??
                           linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[ArcDev] Job card has no title, skipping");
                return null;
            }

            // Extract company
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]") ??
                            card.SelectSingleNode(".//span[contains(@class, 'employer')]");
            var company = ExtractText(companyNode);

            // Extract location/remote info
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]") ??
                             card.SelectSingleNode(".//*[contains(@class, 'remote')]");
            var location = ExtractText(locationNode);

            // Extract salary if available
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'compensation')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract tags/skills
            var tagNodes = card.SelectNodes(".//*[contains(@class, 'tag') or contains(@class, 'skill')]");
            var skills = new List<string>();
            if (tagNodes is not null)
            {
                foreach (var tag in tagNodes)
                {
                    var skill = ExtractText(tag);
                    if (!string.IsNullOrWhiteSpace(skill))
                        skills.Add(skill);
                }
            }

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = string.Empty,
                Country = ParseCountry(location),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = RemotePolicy.FullyRemote, // Arc.dev is remote-only
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
            logger.LogDebug(ex, "[ArcDev] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var sourceId = Regex.Match(url, @"/remote-jobs/([^/?]+)").Groups[1].Value;

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1") ??
                          SelectSingleNode(document, "//h2[contains(@class, 'title')]");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//*[contains(@class, 'company-name')]") ??
                            SelectSingleNode(document, "//h2[contains(@class, 'company')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//*[contains(@class, 'description')]") ??
                                SelectSingleNode(document, "//*[contains(@class, 'job-details')]") ??
                                SelectSingleNode(document, "//article");
            var description = ExtractText(descriptionNode);

            // Extract salary
            var salaryNode = SelectSingleNode(document, "//*[contains(@class, 'salary')]") ??
                           SelectSingleNode(document, "//*[contains(@class, 'compensation')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract skills
            var skillNodes = SelectNodes(document, "//*[contains(@class, 'skill') or contains(@class, 'tag')]");
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
            logger.LogError(ex, "[ArcDev] Failed to parse detail page: {Url}", url);
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

        // Return as-is if it looks like a country name
        return location.Trim();
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }
}
