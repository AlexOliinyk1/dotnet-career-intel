using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes freelance .NET job listings from Gun.io — a premium freelance marketplace
/// that connects vetted developers with high-quality contract opportunities.
/// Gun.io is freelance-only, so all jobs default to EngagementType.Freelance.
/// </summary>
public sealed class GunIoScraper(HttpClient httpClient, ILogger<GunIoScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://gun.io";

    private static readonly string[] DotNetKeywords =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "ef core",
        "blazor", "maui", "xamarin", "wpf", "winforms", "nuget",
        "signalr", "minimal api", "web api", "grpc"
    ];

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

    public override string PlatformName => "Gun.io";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(4);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 3,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            for (var page = 1; page <= maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var url = page == 1
                    ? $"{BaseUrl}/find/work?q={Uri.EscapeDataString(keywords)}"
                    : $"{BaseUrl}/find/work?q={Uri.EscapeDataString(keywords)}&page={page}";

                logger.LogInformation("[Gun.io] Scraping page {Page}: {Url}", page, url);

                var document = await FetchPageAsync(url, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[Gun.io] Failed to fetch page {Page}", page);
                    break;
                }

                var jobCards = SelectNodes(document,
                    "//div[contains(@class, 'job-listing')] | //div[contains(@class, 'opportunity')] | //article[contains(@class, 'job')]")
                    ?? SelectNodes(document,
                    "//div[contains(@class, 'card')]//a[contains(@href, '/find/work/')] | //div[contains(@class, 'listing')]")
                    ?? SelectNodes(document,
                    "//a[contains(@href, '/find/work/')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogDebug("[Gun.io] No job cards found on page {Page}. Structure may have changed.", page);
                    break;
                }

                logger.LogInformation("[Gun.io] Page {Page}: found {Count} potential listings", page, jobCards.Count);

                foreach (var card in jobCards)
                {
                    var vacancy = ParseJobCard(card);
                    if (vacancy is null) continue;
                    if (!seenUrls.Add(vacancy.Url)) continue;
                    if (IsNetRelated(vacancy.Title, vacancy.Description, vacancy.RequiredSkills))
                    {
                        vacancies.Add(vacancy);
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Gun.io] Scraping failed");
        }

        logger.LogInformation("[Gun.io] Scraped {Count} .NET vacancies", vacancies.Count);
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
            var linkNode = card.Name == "a" ? card
                : card.SelectSingleNode(".//a[contains(@href, '/find/work/')]")
                ?? card.SelectSingleNode(".//a[@href]");

            var href = ExtractAttribute(linkNode, "href");
            if (string.IsNullOrEmpty(href)) return null;

            var fullUrl = href.StartsWith("http") ? href : $"{BaseUrl}{href}";
            var sourceId = ExtractSourceId(fullUrl);

            var titleNode = card.SelectSingleNode(".//h2 | .//h3 | .//span[contains(@class, 'title')]");
            var title = ExtractText(titleNode);
            if (string.IsNullOrWhiteSpace(title))
                title = ExtractText(linkNode);
            if (string.IsNullOrWhiteSpace(title)) return null;

            var companyNode = card.SelectSingleNode(
                ".//*[contains(@class, 'company')] | .//*[contains(@class, 'client')]");
            var company = ExtractText(companyNode);

            var descriptionNode = card.SelectSingleNode(
                ".//*[contains(@class, 'description')] | .//*[contains(@class, 'excerpt')] | .//p");
            var description = ExtractText(descriptionNode);

            var rateNode = card.SelectSingleNode(
                ".//*[contains(@class, 'rate')] | .//*[contains(@class, 'salary')] | .//*[contains(@class, 'budget')]");
            var rateText = ExtractText(rateNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(rateText);
            var isHourly = !string.IsNullOrWhiteSpace(rateText) &&
                (rateText.Contains("/hr", StringComparison.OrdinalIgnoreCase) ||
                 rateText.Contains("per hour", StringComparison.OrdinalIgnoreCase) ||
                 rateText.Contains("hourly", StringComparison.OrdinalIgnoreCase));

            var skillNodes = card.SelectNodes(
                ".//*[contains(@class, 'skill')] | .//*[contains(@class, 'tag')] | .//*[contains(@class, 'tech')]");
            var skills = new List<string>();
            if (skillNodes is not null)
            {
                foreach (var node in skillNodes)
                {
                    var skill = ExtractText(node);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        skills.Add(skill);
                }
            }

            var combinedText = $"{title} {description} {rateText}";

            if (skills.Count == 0)
                skills = ExtractSkillsFromText(combinedText);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? "Gun.io Client" : company,
                Description = description,
                Url = fullUrl,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                IsHourlyRate = isHourly,
                RemotePolicy = RemotePolicy.FullyRemote,
                EngagementType = EngagementType.Freelance,
                SeniorityLevel = DetectSeniority(title),
                GeoRestrictions = DetectGeoRestrictions(combinedText),
                RequiredSkills = skills,
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
            var sourceId = ExtractSourceId(url);

            var titleNode = SelectSingleNode(document, "//h1")
                ?? SelectSingleNode(document, "//h2[contains(@class, 'title')]")
                ?? SelectSingleNode(document, "//*[contains(@class, 'job-title')]");
            var title = ExtractText(titleNode);
            if (string.IsNullOrWhiteSpace(title)) return null;

            var descriptionNode = SelectSingleNode(document,
                "//div[contains(@class, 'description')] | //div[contains(@class, 'job-details')] | //div[contains(@class, 'content')]")
                ?? SelectSingleNode(document, "//article")
                ?? SelectSingleNode(document, "//main");
            var description = ExtractText(descriptionNode);

            var companyNode = SelectSingleNode(document,
                "//*[contains(@class, 'company')] | //*[contains(@class, 'client')]");
            var company = ExtractText(companyNode);

            var rateNode = SelectSingleNode(document,
                "//*[contains(@class, 'rate')] | //*[contains(@class, 'salary')] | //*[contains(@class, 'budget')]");
            var rateText = ExtractText(rateNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(rateText);
            var isHourly = !string.IsNullOrWhiteSpace(rateText) &&
                (rateText.Contains("/hr", StringComparison.OrdinalIgnoreCase) ||
                 rateText.Contains("per hour", StringComparison.OrdinalIgnoreCase) ||
                 rateText.Contains("hourly", StringComparison.OrdinalIgnoreCase));

            var skillNodes = SelectNodes(document,
                "//*[contains(@class, 'skill')]//span | //*[contains(@class, 'tag')]//a | //*[contains(@class, 'tech')]//li");
            var skills = new List<string>();
            if (skillNodes is not null)
            {
                foreach (var node in skillNodes)
                {
                    var skill = ExtractText(node);
                    if (!string.IsNullOrWhiteSpace(skill) && skill.Length < 50)
                        skills.Add(skill);
                }
            }

            if (skills.Count == 0)
                skills = ExtractSkillsFromText(description);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? "Gun.io Client" : company,
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                IsHourlyRate = isHourly,
                RemotePolicy = RemotePolicy.FullyRemote,
                EngagementType = EngagementType.Freelance,
                SeniorityLevel = DetectSeniority(title + " " + description),
                GeoRestrictions = DetectGeoRestrictions(description),
                RequiredSkills = skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
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
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Senior;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;

        // Gun.io is a premium freelance platform — default to Senior
        return SeniorityLevel.Senior;
    }

    private static string ExtractSourceId(string url)
    {
        var match = Regex.Match(url, @"/find/work/([^/?#]+)$");
        return match.Success
            ? match.Groups[1].Value
            : url.GetHashCode().ToString("x8");
    }

    private static bool IsNetRelated(string title, string description, List<string> skills)
    {
        var combinedText = $"{title} {description}".ToLowerInvariant();
        var skillsLower = skills.Select(s => s.ToLowerInvariant()).ToList();

        return DotNetKeywords.Any(kw =>
            combinedText.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
            skillsLower.Any(s => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> ExtractSkillsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        return SkillKeywords
            .Where(skill => text.Contains(skill, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
