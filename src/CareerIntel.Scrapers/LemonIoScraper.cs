using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Lemon.io — a European developer talent marketplace
/// that connects vetted developers with US/EU startups for remote contract work.
/// Lemon.io is fully remote and B2B-focused, so all jobs default to
/// EngagementType.ContractB2B and RemotePolicy.FullyRemote.
/// </summary>
public sealed class LemonIoScraper(HttpClient httpClient, ILogger<LemonIoScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://lemon.io";

    private static readonly string[] ListingPages =
    [
        $"{BaseUrl}/for-developers",
        $"{BaseUrl}/for-developers/dotnet",
        $"{BaseUrl}/for-developers/c-sharp"
    ];

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

    public override string PlatformName => "Lemon.io";

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
            var pagesToScrape = ListingPages.Take(maxPages);

            foreach (var listingUrl in pagesToScrape)
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogInformation("[Lemon.io] Scraping listing page: {Url}", listingUrl);

                var document = await FetchPageAsync(listingUrl, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[Lemon.io] Failed to fetch listing page: {Url}", listingUrl);
                    continue;
                }

                var jobCards = SelectNodes(document,
                    "//div[contains(@class, 'job-card')] | //div[contains(@class, 'vacancy')] | //article[contains(@class, 'job')]")
                    ?? SelectNodes(document,
                    "//div[contains(@class, 'card')]//a[contains(@href, '/for-developers/')] | //div[contains(@class, 'opening')]")
                    ?? SelectNodes(document,
                    "//div[contains(@class, 'position')] | //li[contains(@class, 'job')]")
                    ?? SelectNodes(document,
                    "//a[contains(@href, '/for-developers/')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogDebug("[Lemon.io] No job cards found on: {Url}. Structure may have changed.", listingUrl);
                    continue;
                }

                logger.LogInformation("[Lemon.io] Found {Count} potential listings on {Url}", jobCards.Count, listingUrl);

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
            logger.LogError(ex, "[Lemon.io] Scraping failed");
        }

        logger.LogInformation("[Lemon.io] Scraped {Count} .NET vacancies", vacancies.Count);
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
                : card.SelectSingleNode(".//a[contains(@href, '/for-developers/')]")
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
                ".//*[contains(@class, 'company')] | .//*[contains(@class, 'startup')]");
            var company = ExtractText(companyNode);

            var descriptionNode = card.SelectSingleNode(
                ".//*[contains(@class, 'description')] | .//*[contains(@class, 'excerpt')] | .//p");
            var description = ExtractText(descriptionNode);

            var rateNode = card.SelectSingleNode(
                ".//*[contains(@class, 'rate')] | .//*[contains(@class, 'salary')] | .//*[contains(@class, 'compensation')]");
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

            var combinedText = $"{title} {description}";

            if (skills.Count == 0)
                skills = ExtractSkillsFromText(combinedText);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? "Lemon.io Client" : company,
                Description = description,
                Url = fullUrl,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                IsHourlyRate = isHourly,
                RemotePolicy = RemotePolicy.FullyRemote,
                EngagementType = EngagementType.ContractB2B,
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

            var requirementsNode = SelectSingleNode(document,
                "//*[contains(@class, 'requirements')] | //*[contains(@class, 'qualifications')]");
            var requirements = ExtractText(requirementsNode);

            if (!string.IsNullOrWhiteSpace(requirements) && !description.Contains(requirements))
            {
                description = $"{description}\n\nRequirements:\n{requirements}";
            }

            var companyNode = SelectSingleNode(document,
                "//*[contains(@class, 'company')] | //*[contains(@class, 'startup')]");
            var company = ExtractText(companyNode);

            var rateNode = SelectSingleNode(document,
                "//*[contains(@class, 'rate')] | //*[contains(@class, 'salary')] | //*[contains(@class, 'compensation')]");
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
                Company = string.IsNullOrWhiteSpace(company) ? "Lemon.io Client" : company,
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                IsHourlyRate = isHourly,
                RemotePolicy = RemotePolicy.FullyRemote,
                EngagementType = EngagementType.ContractB2B,
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
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Middle;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;

        // Lemon.io targets mid-to-senior level developers
        return SeniorityLevel.Middle;
    }

    private static string ExtractSourceId(string url)
    {
        var match = Regex.Match(url, @"/for-developers/([^/?#]+)$");
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
