using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from JustRemote.co — a remote job board focusing on tech positions.
/// JustRemote features clean listings with company logos and detailed remote work info.
/// All positions are 100% remote.
/// </summary>
public sealed class JustRemoteScraper(HttpClient httpClient, ILogger<JustRemoteScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://justremote.co";

    // Search for developer/engineering jobs
    private static readonly string[] SearchUrls =
    [
        "https://justremote.co/remote-developer-jobs",
        "https://justremote.co/remote-software-engineer-jobs",
        "https://justremote.co/remote-backend-developer-jobs"
    ];

    public override string PlatformName => "JustRemote";

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

            logger.LogInformation("[JustRemote] Scraping {Url}", searchUrl);

            var document = await FetchPageAsync(searchUrl, cancellationToken);
            if (document is null)
            {
                logger.LogWarning("[JustRemote] Failed to fetch {Url}", searchUrl);
                continue;
            }

            // Try multiple selector patterns
            var jobCards = SelectNodes(document, "//div[contains(@class, 'job-list-item')]")
                ?? SelectNodes(document, "//article[contains(@class, 'job')]")
                ?? SelectNodes(document, "//div[contains(@class, 'job-card')]")
                ?? SelectNodes(document, "//a[contains(@href, '/remote-jobs/')]");

            if (jobCards is null or { Count: 0 })
            {
                logger.LogWarning("[JustRemote] No job cards found. Trying alternative selectors...");

                jobCards = SelectNodes(document, "//div[contains(@class, 'job')]") ??
                          SelectNodes(document, "//li[contains(@class, 'posting')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogWarning("[JustRemote] Still no job cards found for {Url}", searchUrl);
                    continue;
                }
            }

            logger.LogInformation("[JustRemote] Found {Count} job cards", jobCards.Count);

            foreach (var card in jobCards)
            {
                var vacancy = ParseJobCard(card);
                if (vacancy is not null && !allVacancies.ContainsKey(vacancy.Id))
                {
                    allVacancies[vacancy.Id] = vacancy;
                }
            }
        }

        logger.LogInformation("[JustRemote] Scraped {Count} unique vacancies total", allVacancies.Count);
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
            var linkNode = card.SelectSingleNode(".//a[@href and contains(@href, '/remote-jobs/')]") ??
                          card.SelectSingleNode(".//a[@href]") ??
                          card;

            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl))
            {
                logger.LogDebug("[JustRemote] Job card has no link, skipping");
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID
            var idMatch = Regex.Match(fullUrl, @"/remote-jobs/([a-zA-Z0-9\-]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : fullUrl.GetHashCode().ToString();

            // Extract title
            var titleNode = card.SelectSingleNode(".//h2") ??
                           card.SelectSingleNode(".//h3") ??
                           card.SelectSingleNode(".//*[contains(@class, 'title')]") ??
                           linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[JustRemote] Job card has no title, skipping");
                return null;
            }

            // Extract company
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]") ??
                            card.SelectSingleNode(".//span[contains(@class, 'employer')]");
            var company = ExtractText(companyNode);

            // Extract location/region
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]") ??
                             card.SelectSingleNode(".//*[contains(@class, 'region')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]") ??
                           card.SelectSingleNode(".//*[contains(text(), '$')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract tags
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
                RemotePolicy = RemotePolicy.FullyRemote, // JustRemote is remote-only
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
            logger.LogDebug(ex, "[JustRemote] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var idMatch = Regex.Match(url, @"/remote-jobs/([a-zA-Z0-9\-]+)");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//*[contains(@class, 'company')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//*[contains(@class, 'description')]") ??
                                SelectSingleNode(document, "//article");
            var description = ExtractText(descriptionNode);

            // Extract salary
            var salaryNode = SelectSingleNode(document, "//*[contains(@class, 'salary')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract skills
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
                City = string.Empty,
                Country = "Remote",
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
            logger.LogError(ex, "[JustRemote] Failed to parse detail page: {Url}", url);
            return null;
        }
    }

    private static string ParseCountry(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "Remote";

        var lower = location.ToLowerInvariant();

        if (lower.Contains("worldwide") || lower.Contains("anywhere"))
            return "Worldwide";

        return location.Trim();
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.")) return SeniorityLevel.Junior;
        if (lower.Contains("intern")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }
}
