using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes .NET job listings from Relocate.me — a job board specializing in
/// international tech positions offering relocation assistance. Targets developers
/// looking to relocate within Europe and beyond. Many listings include visa sponsorship
/// and relocation packages.
/// </summary>
public sealed class RelocateMeScraper(HttpClient httpClient, ILogger<RelocateMeScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://relocate.me";

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

    public override string PlatformName => "Relocate.me";

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
                    ? $"{BaseUrl}/search?q={Uri.EscapeDataString(keywords)}"
                    : $"{BaseUrl}/search?q={Uri.EscapeDataString(keywords)}&page={page}";

                logger.LogInformation("[Relocate.me] Scraping page {Page}: {Url}", page, url);

                var document = await FetchPageAsync(url, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[Relocate.me] Failed to fetch page {Page}", page);
                    break;
                }

                var jobCards = SelectNodes(document,
                    "//div[contains(@class, 'job-card')] | //div[contains(@class, 'job-item')] | //article[contains(@class, 'job')]")
                    ?? SelectNodes(document,
                    "//div[contains(@class, 'vacancy')] | //div[contains(@class, 'listing')]")
                    ?? SelectNodes(document,
                    "//li[contains(@class, 'job')] | //div[contains(@class, 'result')]")
                    ?? SelectNodes(document,
                    "//a[contains(@href, '/job/') or contains(@href, '/jobs/')]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogDebug("[Relocate.me] No job cards found on page {Page}. Structure may have changed.", page);
                    break;
                }

                logger.LogInformation("[Relocate.me] Page {Page}: found {Count} potential listings", page, jobCards.Count);

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
            logger.LogError(ex, "[Relocate.me] Scraping failed");
        }

        logger.LogInformation("[Relocate.me] Scraped {Count} .NET vacancies", vacancies.Count);
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
                : card.SelectSingleNode(".//a[contains(@href, '/job/') or contains(@href, '/jobs/')]")
                ?? card.SelectSingleNode(".//a[@href]");

            var href = ExtractAttribute(linkNode, "href");
            if (string.IsNullOrEmpty(href)) return null;

            var fullUrl = href.StartsWith("http") ? href : $"{BaseUrl}{href}";

            // Skip search/filter pages
            if (fullUrl.Contains("/search?") || fullUrl.EndsWith("/search"))
                return null;

            var sourceId = ExtractSourceId(fullUrl);

            var titleNode = card.SelectSingleNode(".//h2 | .//h3 | .//span[contains(@class, 'title')] | .//a[contains(@class, 'title')]");
            var title = ExtractText(titleNode);
            if (string.IsNullOrWhiteSpace(title))
                title = ExtractText(linkNode);
            if (string.IsNullOrWhiteSpace(title)) return null;

            var companyNode = card.SelectSingleNode(
                ".//*[contains(@class, 'company')] | .//*[contains(@class, 'employer')]");
            var company = ExtractText(companyNode);

            var locationNode = card.SelectSingleNode(
                ".//*[contains(@class, 'location')] | .//*[contains(@class, 'city')] | .//*[contains(@class, 'country')]");
            var location = ExtractText(locationNode);

            var descriptionNode = card.SelectSingleNode(
                ".//*[contains(@class, 'description')] | .//*[contains(@class, 'excerpt')] | .//p");
            var description = ExtractText(descriptionNode);

            var salaryNode = card.SelectSingleNode(
                ".//*[contains(@class, 'salary')] | .//*[contains(@class, 'compensation')] | .//*[contains(@class, 'pay')]");
            var salaryText = ExtractText(salaryNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(salaryText);

            // Check for relocation-specific elements
            var relocationNode = card.SelectSingleNode(
                ".//*[contains(@class, 'relocation')] | .//*[contains(@class, 'visa')]");
            var relocationInfo = ExtractText(relocationNode);

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

            var combinedText = $"{title} {description} {location} {relocationInfo}";

            if (skills.Count == 0)
                skills = ExtractSkillsFromText(combinedText);

            var detectedRemote = DetectRemotePolicy(combinedText);
            var detectedEngagement = DetectEngagementType(combinedText);
            var (country, city) = ParseLocation(location);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? string.Empty : company,
                City = city,
                Country = country,
                Description = description,
                Url = fullUrl,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
                RemotePolicy = detectedRemote != RemotePolicy.Unknown ? detectedRemote : RemotePolicy.Unknown,
                EngagementType = detectedEngagement != EngagementType.Unknown ? detectedEngagement : EngagementType.Employment,
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

            // Look for relocation/visa details (unique to this platform)
            var relocationNode = SelectSingleNode(document,
                "//*[contains(@class, 'relocation')] | //*[contains(@class, 'visa')] | //*[contains(@class, 'benefits')]");
            var relocationInfo = ExtractText(relocationNode);

            if (!string.IsNullOrWhiteSpace(relocationInfo) && !description.Contains(relocationInfo))
            {
                description = $"{description}\n\nRelocation Benefits:\n{relocationInfo}";
            }

            var companyNode = SelectSingleNode(document,
                "//*[contains(@class, 'company')] | //*[contains(@class, 'employer')]");
            var company = ExtractText(companyNode);

            var locationNode = SelectSingleNode(document,
                "//*[contains(@class, 'location')] | //*[contains(@class, 'city')]");
            var location = ExtractText(locationNode);

            var salaryNode = SelectSingleNode(document,
                "//*[contains(@class, 'salary')] | //*[contains(@class, 'compensation')]");
            var salaryText = ExtractText(salaryNode);
            var (salaryMin, salaryMax, currency) = ParseSalaryRange(salaryText);

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

            var combinedText = $"{title} {description} {location}";
            var (country, city) = ParseLocation(location);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? string.Empty : company,
                City = city,
                Country = country,
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
                RemotePolicy = DetectRemotePolicy(combinedText),
                EngagementType = DetectEngagementType(combinedText),
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
        if (string.IsNullOrEmpty(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static string ExtractSourceId(string url)
    {
        var match = Regex.Match(url, @"/(?:job|jobs)/([^/?#]+)$");
        return match.Success
            ? match.Groups[1].Value
            : url.GetHashCode().ToString("x8");
    }

    private static (string Country, string City) ParseLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return (string.Empty, string.Empty);

        var lower = location.ToLowerInvariant();

        // Common EU relocation destinations
        if (lower.Contains("germany") || lower.Contains("berlin") || lower.Contains("munich"))
            return ("Germany", ExtractCityName(lower, "berlin", "munich", "hamburg", "frankfurt", "stuttgart"));
        if (lower.Contains("netherlands") || lower.Contains("amsterdam") || lower.Contains("rotterdam"))
            return ("Netherlands", ExtractCityName(lower, "amsterdam", "rotterdam", "eindhoven", "the hague"));
        if (lower.Contains("portugal") || lower.Contains("lisbon") || lower.Contains("porto"))
            return ("Portugal", ExtractCityName(lower, "lisbon", "porto"));
        if (lower.Contains("spain") || lower.Contains("madrid") || lower.Contains("barcelona"))
            return ("Spain", ExtractCityName(lower, "madrid", "barcelona", "valencia"));
        if (lower.Contains("uk") || lower.Contains("london") || lower.Contains("united kingdom"))
            return ("UK", ExtractCityName(lower, "london", "manchester", "edinburgh", "bristol"));
        if (lower.Contains("ireland") || lower.Contains("dublin"))
            return ("Ireland", ExtractCityName(lower, "dublin", "cork"));
        if (lower.Contains("austria") || lower.Contains("vienna"))
            return ("Austria", ExtractCityName(lower, "vienna", "graz", "salzburg"));
        if (lower.Contains("switzerland") || lower.Contains("zurich"))
            return ("Switzerland", ExtractCityName(lower, "zurich", "geneva", "bern", "basel"));
        if (lower.Contains("czech") || lower.Contains("prague"))
            return ("Czech Republic", ExtractCityName(lower, "prague", "brno"));
        if (lower.Contains("poland") || lower.Contains("warsaw"))
            return ("Poland", ExtractCityName(lower, "warsaw", "krakow", "wroclaw", "gdansk"));
        if (lower.Contains("remote"))
            return ("Remote", string.Empty);

        return (location.Trim(), string.Empty);
    }

    private static string ExtractCityName(string lowerLocation, params string[] cities)
    {
        foreach (var city in cities)
        {
            if (lowerLocation.Contains(city))
                return char.ToUpper(city[0]) + city[1..];
        }
        return string.Empty;
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
