using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Work.ua — Ukraine's largest job search platform.
/// Work.ua has over 92,000 jobs and is the most visited employment website in Ukraine.
/// Supports both Ukrainian and English interfaces.
/// </summary>
public sealed class WorkUaScraper(HttpClient httpClient, ILogger<WorkUaScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://www.work.ua";

    // Search for .NET/C# jobs in IT category
    private static readonly string[] SearchUrls =
    [
        "https://www.work.ua/en/jobs-.net/",
        "https://www.work.ua/en/jobs-c%23/",
        "https://www.work.ua/en/jobs-dotnet/"
    ];

    public override string PlatformName => "WorkUa";

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
                logger.LogInformation("[WorkUa] Scraping {Url} page {Page}", searchUrl, page);

                var document = await FetchPageAsync(url, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[WorkUa] Failed to fetch page {Page} from {Url}", page, searchUrl);
                    break;
                }

                // Try multiple selector patterns
                var jobCards = SelectNodes(document, "//div[contains(@class, 'job-link')]")
                    ?? SelectNodes(document, "//div[contains(@class, 'card') and contains(@class, 'job')]")
                    ?? SelectNodes(document, "//div[@id='pjax-job-list']//div[contains(@class, 'card')]")
                    ?? SelectNodes(document, "//a[contains(@href, '/jobs/')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogWarning("[WorkUa] No job cards found on page {Page}. Trying alternative selectors...", page);

                    // Alternative: look for any job-related divs
                    jobCards = SelectNodes(document, "//div[contains(@class, 'vacancy')]") ??
                              SelectNodes(document, "//article") ??
                              SelectNodes(document, "//li[contains(@class, 'list-unstyled')]//div");

                    if (jobCards is null or { Count: 0 })
                    {
                        logger.LogWarning("[WorkUa] Still no job cards found. Stopping at page {Page}", page);
                        break;
                    }
                }

                logger.LogInformation("[WorkUa] Found {Count} job cards on page {Page}", jobCards.Count, page);

                foreach (var card in jobCards)
                {
                    var vacancy = ParseJobCard(card);
                    if (vacancy is not null && !allVacancies.ContainsKey(vacancy.Id))
                    {
                        allVacancies[vacancy.Id] = vacancy;
                    }
                }

                // Check for next page
                var nextButton = SelectSingleNode(document, "//a[contains(@rel, 'next') or contains(text(), 'Далі') or contains(text(), 'Next')]");
                if (nextButton is null)
                {
                    logger.LogDebug("[WorkUa] No next page button found, stopping at page {Page}", page);
                    break;
                }
            }
        }

        logger.LogInformation("[WorkUa] Scraped {Count} unique vacancies total", allVacancies.Count);
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
            var linkNode = card.SelectSingleNode(".//a[@href and contains(@href, '/jobs/')]") ??
                          card.SelectSingleNode(".//a[@href]") ??
                          card;

            var detailUrl = ExtractAttribute(linkNode, "href");

            if (string.IsNullOrEmpty(detailUrl) || !detailUrl.Contains("/jobs/"))
            {
                return null;
            }

            var fullUrl = detailUrl.StartsWith("http") ? detailUrl : $"{BaseUrl}{detailUrl}";

            // Extract job ID
            var idMatch = Regex.Match(fullUrl, @"/jobs/(\d+)/");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : fullUrl.GetHashCode().ToString();

            // Extract title - multiple patterns
            var titleNode = card.SelectSingleNode(".//h2") ??
                           card.SelectSingleNode(".//h3") ??
                           card.SelectSingleNode(".//*[contains(@class, 'title')]") ??
                           linkNode;

            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[WorkUa] Job card has no title, skipping");
                return null;
            }

            // Extract company
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]") ??
                            card.SelectSingleNode(".//b[contains(@class, 'name')]") ??
                            card.SelectSingleNode(".//span[contains(@class, 'employer')]");
            var company = ExtractText(companyNode);

            // Extract location
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location') or contains(@class, 'city')]") ??
                             card.SelectSingleNode(".//span[contains(text(), 'Київ') or contains(text(), 'Kyiv')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]") ??
                           card.SelectSingleNode(".//*[contains(@class, 'wage')]") ??
                           card.SelectSingleNode(".//b[contains(text(), 'грн') or contains(text(), '$')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract short description if available
            var descNode = card.SelectSingleNode(".//*[contains(@class, 'description') or contains(@class, 'short-text')]");
            var description = ExtractText(descNode);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = ParseCity(location),
                Country = "Ukraine", // Work.ua is Ukraine-focused
                Description = description,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = DetectRemotePolicy(title + " " + description + " " + location),
                SeniorityLevel = DetectSeniority(title),
                EngagementType = DetectEngagementType(title + " " + description),
                GeoRestrictions = DetectGeoRestrictions(location),
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[WorkUa] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            var idMatch = Regex.Match(url, @"/jobs/(\d+)/");
            var sourceId = idMatch.Success ? idMatch.Groups[1].Value : url.GetHashCode().ToString();

            // Extract title
            var titleNode = SelectSingleNode(document, "//h1") ??
                          SelectSingleNode(document, "//h2[@id='job-title']");
            var title = ExtractText(titleNode);

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract company
            var companyNode = SelectSingleNode(document, "//*[@itemprop='hiringOrganization']") ??
                            SelectSingleNode(document, "//*[contains(@class, 'company-name')]") ??
                            SelectSingleNode(document, "//a[contains(@href, '/company/')]");
            var company = ExtractText(companyNode);

            // Extract description
            var descriptionNode = SelectSingleNode(document, "//*[@id='job-description']") ??
                                SelectSingleNode(document, "//*[contains(@class, 'description')]") ??
                                SelectSingleNode(document, "//div[@itemprop='description']");
            var description = ExtractText(descriptionNode);

            // Extract location
            var locationNode = SelectSingleNode(document, "//*[@itemprop='jobLocation']") ??
                             SelectSingleNode(document, "//*[contains(@class, 'location')]");
            var location = ExtractText(locationNode);

            // Extract salary
            var salaryNode = SelectSingleNode(document, "//*[@itemprop='baseSalary']") ??
                           SelectSingleNode(document, "//*[contains(@class, 'salary')]");
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(ExtractText(salaryNode));

            // Extract posted date
            var dateNode = SelectSingleNode(document, "//*[@itemprop='datePosted']") ??
                          SelectSingleNode(document, "//*[contains(@class, 'date')]");
            var dateStr = ExtractAttribute(dateNode, "datetime") ?? ExtractText(dateNode);
            _ = DateTimeOffset.TryParse(dateStr, out var postedDate);

            // Extract skills from description
            var skills = ExtractSkillsFromText(description);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Description = description,
                Url = url,
                City = ParseCity(location),
                Country = "Ukraine",
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
            logger.LogError(ex, "[WorkUa] Failed to parse detail page: {Url}", url);
            return null;
        }
    }

    private static string ParseCity(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        // Extract city from "Kyiv, Ukraine" or "Київ"
        var parts = location.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0].Trim() : location.Trim();
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("керівник")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("старший")) return SeniorityLevel.Senior;
        if (lower.Contains("middle") || lower.Contains("mid") || lower.Contains("середній")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("молодший")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("стажер")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static List<string> ExtractSkillsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lower = text.ToLowerInvariant();

        var knownSkills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "C#",
            [".net"] = ".NET",
            ["dotnet"] = ".NET",
            ["asp.net"] = "ASP.NET",
            ["entity framework"] = "Entity Framework",
            ["ef core"] = "EF Core",
            ["blazor"] = "Blazor",
            ["azure"] = "Azure",
            ["aws"] = "AWS",
            ["docker"] = "Docker",
            ["kubernetes"] = "Kubernetes",
            ["sql"] = "SQL",
            ["postgresql"] = "PostgreSQL",
            ["mongodb"] = "MongoDB",
            ["redis"] = "Redis",
            ["react"] = "React",
            ["angular"] = "Angular",
            ["typescript"] = "TypeScript",
            ["microservices"] = "Microservices",
            ["git"] = "Git"
        };

        foreach (var (keyword, displayName) in knownSkills)
        {
            if (lower.Contains(keyword))
            {
                skills.Add(displayName);
            }
        }

        return skills.ToList();
    }
}
