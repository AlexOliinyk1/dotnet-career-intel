using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes freelance .NET job listings from Toptal — a premium freelance talent network.
/// Toptal is 100% freelance/remote, so all jobs are mapped accordingly.
/// Uses a 5-second request delay since Toptal is aggressive about scraping detection.
/// </summary>
public sealed class ToptalScraper(HttpClient httpClient, ILogger<ToptalScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://www.toptal.com";

    private static readonly string[] ListingPages =
    [
        $"{BaseUrl}/freelance-jobs/developers/dot-net",
        $"{BaseUrl}/freelance-jobs/developers/c-sharp"
    ];

    /// <summary>
    /// Keywords used to determine .NET relevance in job titles and descriptions.
    /// </summary>
    private static readonly string[] DotNetKeywords =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "ef core",
        "blazor", "maui", "xamarin", "wpf", "winforms", "nuget",
        "signalr", "minimal api", "web api", "grpc"
    ];

    /// <summary>
    /// Common skill keywords to scan for in description text.
    /// </summary>
    private static readonly string[] SkillKeywords =
    [
        "C#", ".NET", "ASP.NET", "Entity Framework", "EF Core", "Blazor",
        "Azure", "AWS", "Docker", "Kubernetes", "SQL Server", "PostgreSQL",
        "MongoDB", "Redis", "RabbitMQ", "Kafka", "gRPC", "SignalR",
        "React", "Angular", "TypeScript", "JavaScript", "REST", "GraphQL",
        "Microservices", "CI/CD", "Git", "Agile", "Scrum", "MAUI",
        "Xamarin", "WPF", "LINQ", "Dapper", "MediatR", "CQRS",
        "Domain-Driven Design", "Clean Architecture", "xUnit", "NUnit"
    ];

    public override string PlatformName => "Toptal";

    // Toptal is aggressive about scraping — be very polite.
    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(5);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 2,
        CancellationToken cancellationToken = default)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vacancies = new List<JobVacancy>();

        foreach (var listingUrl in ListingPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogInformation("[Toptal] Scraping listing page: {Url}", listingUrl);

            var document = await FetchPageAsync(listingUrl, cancellationToken);
            if (document is null)
            {
                logger.LogWarning("[Toptal] Failed to fetch listing page: {Url}", listingUrl);
                continue;
            }

            // Try multiple selector strategies
            var jobCards = SelectNodes(document,
                "//div[contains(@class, 'job') or contains(@class, 'freelance')]//a[contains(@href, '/freelance-jobs/')]")
                ?? SelectNodes(document,
                "//a[contains(@href, '/freelance-jobs/') and not(contains(@href, '/developers/'))]")
                ?? SelectNodes(document,
                "//li[contains(@class, 'job')]//a | //div[contains(@class, 'card')]//a[contains(@href, '/freelance')]")
                ?? SelectNodes(document,
                "//article//a[contains(@href, '/freelance-jobs/')]")
                ?? SelectNodes(document,
                "//a[contains(@href, '/freelance-jobs/')]");

            if (jobCards is null or { Count: 0 })
            {
                logger.LogWarning("[Toptal] No job cards found on listing page: {Url}. Page structure may have changed.", listingUrl);
                logger.LogDebug("[Toptal] All links found: {LinkCount}",
                    document.DocumentNode.SelectNodes("//a[@href]")?.Count ?? 0);
                continue;
            }

            logger.LogInformation("[Toptal] Found {Count} potential job links", jobCards.Count);

            foreach (var card in jobCards)
            {
                var vacancy = ParseJobCard(card, document);
                if (vacancy is null) continue;

                // Deduplicate across listing pages (same job may appear on both .NET and C# pages)
                if (!seenUrls.Add(vacancy.Url)) continue;

                if (IsNetRelated(vacancy.Title, vacancy.Description, vacancy.RequiredSkills))
                {
                    vacancies.Add(vacancy);
                }
            }
        }

        logger.LogInformation("[Toptal] Scraped {Count} unique .NET vacancies", vacancies.Count);
        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        var document = await FetchPageAsync(url, cancellationToken);
        if (document is null) return null;

        return ParseDetailPage(document, url);
    }

    private JobVacancy? ParseJobCard(HtmlNode card, HtmlDocument document)
    {
        try
        {
            // Extract the detail URL from the link node
            var href = ExtractAttribute(card, "href");
            if (string.IsNullOrEmpty(href)) return null;

            var fullUrl = href.StartsWith("http") ? href : $"{BaseUrl}{href}";

            // Skip non-job links (navigation, category pages, etc.)
            if (!fullUrl.Contains("/freelance-jobs/") ||
                fullUrl.Contains("/developers/dot-net") ||
                fullUrl.Contains("/developers/c-sharp") ||
                fullUrl.Contains("/developers/"))
                return null;

            var sourceId = ExtractSourceId(fullUrl);

            // Extract title from the link text or nearest heading
            var title = ExtractText(card);
            if (string.IsNullOrWhiteSpace(title))
            {
                var titleNode = card.SelectSingleNode(".//h2 | .//h3 | .//span[contains(@class, 'title')]");
                title = ExtractText(titleNode);
            }

            if (string.IsNullOrWhiteSpace(title)) return null;

            // Try to extract description snippet from a sibling or parent container
            var parentContainer = card.ParentNode;
            var descriptionNode = parentContainer?.SelectSingleNode(
                ".//*[contains(@class, 'description') or contains(@class, 'excerpt') or contains(@class, 'summary')]");
            var descriptionExcerpt = ExtractText(descriptionNode);

            // Extract skill tags if present near the card
            var skillNodes = parentContainer?.SelectNodes(
                ".//*[contains(@class, 'skill') or contains(@class, 'tag') or contains(@class, 'tech')]");
            var skills = new List<string>();
            if (skillNodes is not null)
            {
                foreach (var skillNode in skillNodes)
                {
                    var skill = ExtractText(skillNode);
                    if (!string.IsNullOrWhiteSpace(skill))
                        skills.Add(skill);
                }
            }

            var combinedText = $"{title} {descriptionExcerpt}";

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = "Toptal Client",
                Description = descriptionExcerpt,
                Url = fullUrl,
                RemotePolicy = RemotePolicy.FullyRemote,
                EngagementType = EngagementType.Freelance,
                SeniorityLevel = DetectSeniority(title),
                GeoRestrictions = DetectGeoRestrictions(combinedText),
                RequiredSkills = skills.Count > 0 ? skills : ExtractSkillsFromText(combinedText),
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
            var sourceId = ExtractSourceId(url);

            // Try multiple selectors for the title — Toptal's structure may vary
            var titleNode = SelectSingleNode(document, "//h1")
                ?? SelectSingleNode(document, "//h2[contains(@class, 'title')]")
                ?? SelectSingleNode(document, "//*[contains(@class, 'job-title')]");

            var title = ExtractText(titleNode);
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Extract full description from detail page
            var descriptionNode = SelectSingleNode(document,
                "//div[contains(@class, 'description') or contains(@class, 'job-description') or contains(@class, 'content')]")
                ?? SelectSingleNode(document, "//article")
                ?? SelectSingleNode(document, "//main//div[contains(@class, 'body')]");
            var description = ExtractText(descriptionNode);

            // Extract requirements section if separate from description
            var requirementsNode = SelectSingleNode(document,
                "//*[contains(@class, 'requirements') or contains(@class, 'qualifications')]");
            var requirements = ExtractText(requirementsNode);

            if (!string.IsNullOrWhiteSpace(requirements) && !description.Contains(requirements))
            {
                description = $"{description}\n\nRequirements:\n{requirements}";
            }

            // Extract structured skill tags
            var skillNodes = SelectNodes(document,
                "//*[contains(@class, 'skill') or contains(@class, 'tag') or contains(@class, 'tech')]//span | " +
                "//*[contains(@class, 'skill') or contains(@class, 'tag') or contains(@class, 'tech')]//a | " +
                "//*[contains(@class, 'skill') or contains(@class, 'tag') or contains(@class, 'tech')]//li");

            var structuredSkills = new List<string>();
            if (skillNodes is not null)
            {
                foreach (var skillNode in skillNodes)
                {
                    var skill = ExtractText(skillNode);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        structuredSkills.Add(skill);
                }
            }

            // If no structured skills found, extract from description text
            var skills = structuredSkills.Count > 0
                ? structuredSkills.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : ExtractSkillsFromText(description);

            var (salaryMin, salaryMax, currency) = ParseSalaryRange(description);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = "Toptal Client",
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = RemotePolicy.FullyRemote,
                EngagementType = EngagementType.Freelance,
                SeniorityLevel = DetectSeniority(title + " " + description),
                GeoRestrictions = DetectGeoRestrictions(description),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects seniority level from text. Defaults to Senior if no level is detected,
    /// since Toptal is a premium platform targeting experienced professionals.
    /// </summary>
    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Senior;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;

        // Toptal is a premium platform — default to Senior if seniority is not explicit
        return SeniorityLevel.Senior;
    }

    /// <summary>
    /// Extracts a stable source ID from a Toptal job URL slug.
    /// </summary>
    private static string ExtractSourceId(string url)
    {
        // URL pattern: https://www.toptal.com/freelance-jobs/{slug}
        var match = Regex.Match(url, @"/freelance-jobs/([^/?#]+)$");
        return match.Success
            ? match.Groups[1].Value
            : url.GetHashCode().ToString("x8");
    }

    /// <summary>
    /// Determines whether a job listing is relevant to .NET development based on
    /// its title, description, and skill tags.
    /// </summary>
    private static bool IsNetRelated(string title, string description, List<string> skills)
    {
        var combinedText = $"{title} {description}".ToLowerInvariant();
        var skillsLower = skills.Select(s => s.ToLowerInvariant()).ToList();

        return DotNetKeywords.Any(kw =>
            combinedText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            skillsLower.Any(s => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Scans description text for known technology keywords and returns matching skills.
    /// </summary>
    private static List<string> ExtractSkillsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return SkillKeywords
            .Where(skill => text.Contains(skill, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
