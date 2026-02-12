using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes .NET job listings from GermanTechJobs.de — a specialized job board for
/// technology positions in Germany. Salaries are in EUR. The German tech market
/// is one of the largest in Europe with strong demand for .NET developers,
/// particularly in fintech, automotive, and enterprise software sectors.
/// </summary>
public sealed class GermanTechJobsScraper(HttpClient httpClient, ILogger<GermanTechJobsScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://germantechjobs.de";

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

    public override string PlatformName => "GermanTechJobs";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

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
                    ? $"{BaseUrl}/jobs/dotnet"
                    : $"{BaseUrl}/jobs/dotnet?page={page}";

                logger.LogInformation("[GermanTechJobs] Scraping page {Page}: {Url}", page, url);

                var document = await FetchPageAsync(url, cancellationToken);
                if (document is null)
                {
                    logger.LogWarning("[GermanTechJobs] Failed to fetch page {Page}", page);
                    break;
                }

                var jobCards = SelectNodes(document,
                    "//div[contains(@class, 'job-card')] | //div[contains(@class, 'job-item')] | //article[contains(@class, 'job')]")
                    ?? SelectNodes(document,
                    "//div[contains(@class, 'listing')]//a[contains(@href, '/jobs/')] | //div[contains(@class, 'result')]")
                    ?? SelectNodes(document,
                    "//li[contains(@class, 'job')] | //tr[contains(@class, 'job')]")
                    ?? SelectNodes(document,
                    "//a[contains(@href, '/jobs/') and not(contains(@href, '/jobs/dotnet'))]");

                if (jobCards is null or { Count: 0 })
                {
                    logger.LogDebug("[GermanTechJobs] No job cards found on page {Page}. Structure may have changed.", page);
                    break;
                }

                logger.LogInformation("[GermanTechJobs] Page {Page}: found {Count} potential listings", page, jobCards.Count);

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
            logger.LogError(ex, "[GermanTechJobs] Scraping failed");
        }

        logger.LogInformation("[GermanTechJobs] Scraped {Count} .NET vacancies", vacancies.Count);
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
                : card.SelectSingleNode(".//a[contains(@href, '/jobs/')]")
                ?? card.SelectSingleNode(".//a[@href]");

            var href = ExtractAttribute(linkNode, "href");
            if (string.IsNullOrEmpty(href)) return null;

            var fullUrl = href.StartsWith("http") ? href : $"{BaseUrl}{href}";

            // Skip category pages
            if (fullUrl.EndsWith("/jobs/dotnet") || fullUrl.EndsWith("/jobs"))
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
                ".//*[contains(@class, 'location')] | .//*[contains(@class, 'city')]");
            var location = ExtractText(locationNode);

            var descriptionNode = card.SelectSingleNode(
                ".//*[contains(@class, 'description')] | .//*[contains(@class, 'excerpt')] | .//p");
            var description = ExtractText(descriptionNode);

            var salaryNode = card.SelectSingleNode(
                ".//*[contains(@class, 'salary')] | .//*[contains(@class, 'compensation')] | .//*[contains(@class, 'pay')]");
            var salaryText = ExtractText(salaryNode);
            var (salaryMin, salaryMax, _) = ParseSalaryRange(salaryText);

            var skillNodes = card.SelectNodes(
                ".//*[contains(@class, 'skill')] | .//*[contains(@class, 'tag')] | .//*[contains(@class, 'tech')] | .//*[contains(@class, 'badge')]");
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

            var combinedText = $"{title} {description} {location}";

            if (skills.Count == 0)
                skills = ExtractSkillsFromText(combinedText);

            var detectedRemote = DetectRemotePolicy(combinedText);
            var detectedEngagement = DetectEngagementType(combinedText);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? string.Empty : company,
                City = ExtractGermanCity(location),
                Country = "Germany",
                Description = description,
                Url = fullUrl,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = "EUR",
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

            var companyNode = SelectSingleNode(document,
                "//*[contains(@class, 'company')] | //*[contains(@class, 'employer')]");
            var company = ExtractText(companyNode);

            var locationNode = SelectSingleNode(document,
                "//*[contains(@class, 'location')] | //*[contains(@class, 'city')]");
            var location = ExtractText(locationNode);

            var salaryNode = SelectSingleNode(document,
                "//*[contains(@class, 'salary')] | //*[contains(@class, 'compensation')]");
            var salaryText = ExtractText(salaryNode);
            var (salaryMin, salaryMax, _) = ParseSalaryRange(salaryText);

            var skillNodes = SelectNodes(document,
                "//*[contains(@class, 'skill')]//span | //*[contains(@class, 'tag')]//a | //*[contains(@class, 'tech')]//li | //*[contains(@class, 'badge')]");
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

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = string.IsNullOrWhiteSpace(company) ? string.Empty : company,
                City = ExtractGermanCity(location),
                Country = "Germany",
                Description = description,
                Url = url,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = "EUR",
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
        if (lower.Contains("lead") || lower.Contains("tech lead") || lower.Contains("teamlead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle") || lower.Contains("regular")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee") || lower.Contains("werkstudent") || lower.Contains("praktikant")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static string ExtractSourceId(string url)
    {
        var match = Regex.Match(url, @"/jobs/([^/?#]+)$");
        return match.Success
            ? match.Groups[1].Value
            : url.GetHashCode().ToString("x8");
    }

    private static string ExtractGermanCity(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        var lower = location.ToLowerInvariant();

        if (lower.Contains("berlin")) return "Berlin";
        if (lower.Contains("munich") || lower.Contains("münchen")) return "Munich";
        if (lower.Contains("hamburg")) return "Hamburg";
        if (lower.Contains("frankfurt")) return "Frankfurt";
        if (lower.Contains("cologne") || lower.Contains("köln")) return "Cologne";
        if (lower.Contains("düsseldorf") || lower.Contains("dusseldorf")) return "Düsseldorf";
        if (lower.Contains("stuttgart")) return "Stuttgart";
        if (lower.Contains("leipzig")) return "Leipzig";
        if (lower.Contains("dresden")) return "Dresden";
        if (lower.Contains("hannover") || lower.Contains("hanover")) return "Hannover";
        if (lower.Contains("nuremberg") || lower.Contains("nürnberg")) return "Nuremberg";
        if (lower.Contains("remote")) return "Remote";

        return location.Trim();
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
