using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from NoFluffJobs — a Polish IT job board known for salary transparency.
/// NoFluffJobs mandates salary disclosure and prominently displays B2B as a first-class
/// contract type alongside UoP (employment) and UZ (contract of mandate).
/// Uses HTML scraping via FetchPageAsync with HtmlAgilityPack.
/// </summary>
public sealed class NoFluffJobsScraper(HttpClient httpClient, ILogger<NoFluffJobsScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string BaseUrl = "https://nofluffjobs.com";

    /// <summary>
    /// Search URL patterns for remote .NET and C# jobs on NoFluffJobs.
    /// </summary>
    private static readonly string[] SearchPaths =
    [
        "/pl/praca-zdalna/.net",
        "/pl/praca-zdalna/c-sharp"
    ];

    public override string PlatformName => "NoFluffJobs";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(4);

    /// <summary>
    /// Known .NET-related keywords for relevance filtering.
    /// </summary>
    private static readonly string[] NetKeywords =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "ef core",
        "blazor", "maui", "xamarin", "wpf", "winforms"
    ];

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var allVacancies = new Dictionary<string, JobVacancy>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchPath in SearchPaths)
        {
            for (var page = 1; page <= maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var url = $"{BaseUrl}{searchPath}?page={page}";
                logger.LogDebug("[NoFluffJobs] Fetching page {Page}: {Url}", page, url);

                var document = await FetchPageAsync(url, cancellationToken);

                if (document is null)
                {
                    logger.LogWarning("[NoFluffJobs] Failed to fetch page {Page} for {Path}", page, searchPath);
                    break;
                }

                var jobCards = FindJobCards(document);

                if (jobCards is null || jobCards.Count == 0)
                {
                    logger.LogDebug("[NoFluffJobs] No more job cards found on page {Page} for {Path}", page, searchPath);
                    break;
                }

                foreach (var card in jobCards)
                {
                    var vacancy = ParseJobCard(card);
                    if (vacancy is not null && !allVacancies.ContainsKey(vacancy.Url))
                    {
                        allVacancies[vacancy.Url] = vacancy;
                    }
                }

                logger.LogDebug("[NoFluffJobs] Page {Page} for {Path}: parsed {Count} cards, total unique: {Total}",
                    page, searchPath, jobCards.Count, allVacancies.Count);
            }
        }

        return allVacancies.Values.ToList();
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await FetchPageAsync(url, cancellationToken);
            if (document is null) return null;

            return ParseDetailPage(document, url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[NoFluffJobs] Detail fetch failed for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Finds job card elements in the listing page HTML.
    /// NoFluffJobs uses data-cy attributes and nfj-posting-item elements for job cards.
    /// Multiple selectors are tried defensively for resilience against HTML changes.
    /// </summary>
    private static HtmlNodeCollection? FindJobCards(HtmlDocument document)
    {
        // Try the most specific selector first (data-cy attribute)
        var cards = SelectNodes(document,
            "//*[@data-cy='posting-list-item']");

        if (cards is not null && cards.Count > 0) return cards;

        // Fallback: look for nfj-posting-item custom elements
        cards = SelectNodes(document,
            "//nfj-posting-item");

        if (cards is not null && cards.Count > 0) return cards;

        // Fallback: look for common job listing patterns — anchor tags within list containers
        cards = SelectNodes(document,
            "//a[contains(@class, 'posting-list-item')]");

        if (cards is not null && cards.Count > 0) return cards;

        // Final fallback: any list items that look like job cards
        cards = SelectNodes(document,
            "//div[contains(@class, 'list-container')]//a[contains(@href, '/pl/job/')]");

        return cards;
    }

    private JobVacancy? ParseJobCard(HtmlNode card)
    {
        try
        {
            // Extract job URL — look for the primary link
            var linkNode = card.Name == "a"
                ? card
                : card.SelectSingleNode(".//a[contains(@href, '/pl/job/')]")
                  ?? card.SelectSingleNode(".//a[@href]");

            var relativeUrl = ExtractAttribute(linkNode, "href");
            if (string.IsNullOrEmpty(relativeUrl)) return null;

            var fullUrl = relativeUrl.StartsWith("http")
                ? relativeUrl
                : $"{BaseUrl}{relativeUrl}";

            // Generate a stable source ID from the URL path
            var sourceId = relativeUrl.Trim('/').Replace("/", "-");
            if (string.IsNullOrEmpty(sourceId))
                sourceId = fullUrl.GetHashCode().ToString();

            // Extract title — typically in an h3 or element with posting-title class
            var titleNode = card.SelectSingleNode(".//*[contains(@class, 'posting-title')]")
                            ?? card.SelectSingleNode(".//h3")
                            ?? card.SelectSingleNode(".//h4")
                            ?? card.SelectSingleNode(".//*[contains(@class, 'title')]");
            var title = ExtractText(titleNode);

            // Filter for .NET relevance based on title
            if (!string.IsNullOrEmpty(title) && !IsNetRelatedText(title))
            {
                // Also check if the card itself has .NET indicators
                var cardText = ExtractText(card);
                if (!IsNetRelatedText(cardText)) return null;
            }

            // Extract company name
            var companyNode = card.SelectSingleNode(".//*[contains(@class, 'company')]")
                              ?? card.SelectSingleNode(".//*[contains(@class, 'posting-company')]")
                              ?? card.SelectSingleNode(".//span[contains(@class, 'company-name')]");
            var company = ExtractText(companyNode);

            // Extract salary — NoFluffJobs always shows salary
            var salaryNode = card.SelectSingleNode(".//*[contains(@class, 'salary')]")
                             ?? card.SelectSingleNode(".//*[contains(@class, 'posting-salary')]")
                             ?? card.SelectSingleNode(".//*[contains(@class, 'nfj-posting-item-salary')]");
            var salaryText = ExtractText(salaryNode);
            var (salaryMin, salaryMax, currency) = ParseNoFluffSalary(salaryText);

            // Extract seniority tags
            var seniorityNode = card.SelectSingleNode(".//*[contains(@class, 'seniority')]")
                                ?? card.SelectSingleNode(".//*[contains(@class, 'posting-seniority')]");
            var seniorityText = ExtractText(seniorityNode);
            var seniority = MapSeniority(seniorityText);

            // If seniority not found in dedicated node, try from the title
            if (seniority == SeniorityLevel.Unknown && !string.IsNullOrEmpty(title))
                seniority = MapSeniority(title);

            // Extract location
            var locationNode = card.SelectSingleNode(".//*[contains(@class, 'location')]")
                               ?? card.SelectSingleNode(".//*[contains(@class, 'posting-location')]");
            var locationText = ExtractText(locationNode);

            // Detect engagement type from card text — look for B2B, UoP, UZ markers
            var cardFullText = ExtractText(card);
            var engagementType = DetectNoFluffEngagementType(cardFullText);

            // Detect remote policy
            var remotePolicy = DetectRemotePolicy(locationText, cardFullText);

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = fullUrl,
                City = ParseCity(locationText),
                Country = "PL",
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = remotePolicy,
                SeniorityLevel = seniority,
                EngagementType = engagementType,
                GeoRestrictions = DetectGeoRestrictions(cardFullText),
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[NoFluffJobs] Failed to parse job card");
            return null;
        }
    }

    private JobVacancy? ParseDetailPage(HtmlDocument document, string url)
    {
        try
        {
            // Extract title
            var titleNode = SelectSingleNode(document, "//h1")
                            ?? SelectSingleNode(document, "//*[contains(@class, 'posting-title')]");
            var title = ExtractText(titleNode);

            // Extract company name
            var companyNode = SelectSingleNode(document, "//*[contains(@class, 'company-name')]")
                              ?? SelectSingleNode(document, "//*[@data-cy='company-name']")
                              ?? SelectSingleNode(document, "//*[contains(@class, 'posting-company')]//a");
            var company = ExtractText(companyNode);

            // Extract full description
            var descriptionNode = SelectSingleNode(document, "//*[contains(@class, 'posting-description')]")
                                  ?? SelectSingleNode(document, "//*[@data-cy='JobDescription']")
                                  ?? SelectSingleNode(document, "//section[contains(@class, 'description')]");
            var description = ExtractText(descriptionNode);

            // Extract requirements section
            var requirementsNode = SelectSingleNode(document, "//*[contains(@class, 'posting-requirements')]")
                                   ?? SelectSingleNode(document, "//*[@data-cy='JobRequirements']");
            var requirements = ExtractText(requirementsNode);

            var fullDescription = string.IsNullOrEmpty(requirements)
                ? description
                : $"{description}\n\nRequirements:\n{requirements}";

            // Extract salary from the detail page (more precise breakdown)
            var salaryNode = SelectSingleNode(document, "//*[contains(@class, 'salary')]")
                             ?? SelectSingleNode(document, "//*[@data-cy='TextSalary']")
                             ?? SelectSingleNode(document, "//*[contains(@class, 'posting-salary')]");
            var salaryText = ExtractText(salaryNode);
            var (salaryMin, salaryMax, currency) = ParseNoFluffSalary(salaryText);

            // Extract seniority
            var seniorityNode = SelectSingleNode(document, "//*[contains(@class, 'seniority')]")
                                ?? SelectSingleNode(document, "//*[@data-cy='seniority']");
            var seniorityText = ExtractText(seniorityNode);
            var seniority = MapSeniority(seniorityText);

            if (seniority == SeniorityLevel.Unknown)
                seniority = MapSeniority(title);

            // Extract location
            var locationNode = SelectSingleNode(document, "//*[contains(@class, 'location')]")
                               ?? SelectSingleNode(document, "//*[@data-cy='location']");
            var locationText = ExtractText(locationNode);

            // Extract skills from requirement tags
            var skillNodes = SelectNodes(document, "//*[contains(@class, 'requirement')]//span")
                             ?? SelectNodes(document, "//*[@data-cy='Requirement']");
            var skills = new List<string>();
            if (skillNodes is not null)
            {
                foreach (var skillNode in skillNodes)
                {
                    var skillText = ExtractText(skillNode);
                    if (!string.IsNullOrWhiteSpace(skillText))
                        skills.Add(skillText);
                }
            }

            // If no structured skills found, extract from description text
            if (skills.Count == 0)
                skills = ExtractSkillsFromText(fullDescription);

            // Detect engagement type from full page text
            var pageText = ExtractText(document.DocumentNode);
            var engagementType = DetectNoFluffEngagementType(pageText);

            var remotePolicy = DetectRemotePolicy(locationText, pageText);

            // Generate a stable source ID from the URL
            var pathSegment = new Uri(url).AbsolutePath.Trim('/').Replace("/", "-");
            var sourceId = !string.IsNullOrEmpty(pathSegment)
                ? pathSegment
                : url.GetHashCode().ToString();

            return new JobVacancy
            {
                Id = GenerateId(sourceId),
                Title = title,
                Company = company,
                Url = url,
                City = ParseCity(locationText),
                Country = "PL",
                Description = fullDescription,
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = remotePolicy,
                SeniorityLevel = seniority,
                EngagementType = engagementType,
                GeoRestrictions = DetectGeoRestrictions(pageText),
                RequiredSkills = skills,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[NoFluffJobs] Failed to parse detail page for {Url}", url);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MAPPING HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses NoFluffJobs salary strings which may include PLN, EUR, or USD amounts.
    /// Handles formats like "12 000 - 18 000 PLN", "8 000 - 12 000 PLN netto (+ VAT) / mies.",
    /// or "15 000 - 22 000 PLN brutto / mies."
    /// </summary>
    private static (decimal? Min, decimal? Max, string Currency) ParseNoFluffSalary(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null, "PLN");

        // Detect currency
        var currency = "PLN";
        if (text.Contains("EUR", StringComparison.OrdinalIgnoreCase)) currency = "EUR";
        else if (text.Contains("USD", StringComparison.OrdinalIgnoreCase)) currency = "USD";
        else if (text.Contains("GBP", StringComparison.OrdinalIgnoreCase)) currency = "GBP";
        else if (text.Contains("CHF", StringComparison.OrdinalIgnoreCase)) currency = "CHF";

        // NoFluffJobs uses space as a thousands separator (e.g. "12 000")
        // First normalize the text by removing spaces within numbers
        var normalized = Regex.Replace(text, @"(\d)\s+(\d)", "$1$2");

        // Extract numeric values
        var numbers = Regex.Matches(normalized, @"\d+")
            .Select(m => decimal.TryParse(m.Value, out var v) ? v : 0)
            .Where(v => v > 0)
            .ToList();

        return numbers.Count switch
        {
            0 => (null, null, currency),
            1 => (numbers[0], null, currency),
            _ => (numbers[0], numbers[1], currency)
        };
    }

    /// <summary>
    /// Detects engagement type from NoFluffJobs text.
    /// NoFluffJobs prominently displays B2B, UoP (umowa o prace), and UZ (umowa zlecenie).
    /// </summary>
    private static EngagementType DetectNoFluffEngagementType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return EngagementType.Unknown;

        var lower = text.ToLowerInvariant();

        // B2B is a first-class contract type on NoFluffJobs — check first as it's prominent
        if (lower.Contains("b2b"))
            return EngagementType.ContractB2B;

        // UoP = Umowa o Prace (employment contract, Polish labor law)
        if (lower.Contains("uop") || lower.Contains("uop") ||
            lower.Contains("umowa o prac") || lower.Contains("employment"))
            return EngagementType.Employment;

        // UZ = Umowa Zlecenie (contract of mandate, common in Poland)
        if (lower.Contains("uz ") || lower.Contains("umowa zleceni") ||
            lower.Contains("zlecenie") || lower.Contains("contract of mandate"))
            return EngagementType.Freelance;

        // Fallback to base class detection
        return DetectEngagementType(text);
    }

    private static SeniorityLevel MapSeniority(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("expert") || lower.Contains("senior")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("regular")) return SeniorityLevel.Middle;
        if (lower.Contains("junior")) return SeniorityLevel.Junior;
        if (lower.Contains("intern") || lower.Contains("trainee") || lower.Contains("stażysta")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static RemotePolicy DetectRemotePolicy(string? locationText, string? fullText)
    {
        var combined = $"{locationText} {fullText}".ToLowerInvariant();

        if (combined.Contains("fully remote") || combined.Contains("100% remote") ||
            combined.Contains("praca zdalna") || combined.Contains("full remote"))
            return RemotePolicy.FullyRemote;

        if (combined.Contains("hybrid") || combined.Contains("hybrydowa"))
            return RemotePolicy.Hybrid;

        if (combined.Contains("remote"))
            return RemotePolicy.RemoteFriendly;

        if (combined.Contains("on-site") || combined.Contains("onsite") ||
            combined.Contains("stacjonarna") || combined.Contains("office"))
            return RemotePolicy.OnSite;

        // Since we're searching the remote jobs section, default to FullyRemote
        return RemotePolicy.FullyRemote;
    }

    private static bool IsNetRelatedText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var lower = text.ToLowerInvariant();
        return NetKeywords.Any(kw => lower.Contains(kw));
    }

    private static string ParseCity(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        // NoFluffJobs locations can include "Remote" or city names
        var lower = location.ToLowerInvariant();
        if (lower.Contains("remote") || lower.Contains("zdalnie") || lower.Contains("praca zdalna"))
            return string.Empty;

        var parts = location.Split([',', '+', ';'], StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0].Trim() : string.Empty;
    }

    private static List<string> ExtractSkillsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var lower = text.ToLowerInvariant();
        var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var knownSkills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "C#",
            [".net"] = ".NET",
            ["asp.net"] = "ASP.NET",
            ["entity framework"] = "Entity Framework",
            ["ef core"] = "EF Core",
            ["blazor"] = "Blazor",
            ["maui"] = "MAUI",
            ["xamarin"] = "Xamarin",
            ["wpf"] = "WPF",
            ["azure"] = "Azure",
            ["aws"] = "AWS",
            ["docker"] = "Docker",
            ["kubernetes"] = "Kubernetes",
            ["sql server"] = "SQL Server",
            ["postgresql"] = "PostgreSQL",
            ["mongodb"] = "MongoDB",
            ["redis"] = "Redis",
            ["rabbitmq"] = "RabbitMQ",
            ["kafka"] = "Kafka",
            ["react"] = "React",
            ["angular"] = "Angular",
            ["typescript"] = "TypeScript",
            ["javascript"] = "JavaScript",
            ["git"] = "Git",
            ["ci/cd"] = "CI/CD",
            ["microservices"] = "Microservices",
            ["rest api"] = "REST API",
            ["grpc"] = "gRPC",
            ["signalr"] = "SignalR",
            ["dapper"] = "Dapper",
            ["linq"] = "LINQ",
            ["xunit"] = "xUnit",
            ["nunit"] = "NUnit"
        };

        foreach (var (keyword, displayName) in knownSkills)
        {
            if (lower.Contains(keyword))
                skills.Add(displayName);
        }

        return skills.OrderBy(s => s).ToList();
    }
}
