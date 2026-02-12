using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Hired.com - tech-focused job marketplace where
/// companies apply to candidates. Known for high-salary positions ($140K-$200K+).
/// </summary>
public sealed class HiredScraper(HttpClient httpClient, ILogger<HiredScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Hired";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(3);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            for (var page = 1; page <= maxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var url = $"https://hired.com/jobs?q={Uri.EscapeDataString(keywords)}&page={page}";

                var doc = await FetchPageAsync(url, cancellationToken);
                if (doc is null) break;

                var jobCards = SelectNodes(doc, "//div[contains(@class,'job-card')] | //div[contains(@class,'JobCard')] | //article[contains(@class,'job')]");

                if (jobCards is null || jobCards.Count == 0)
                {
                    logger.LogDebug("No more results on page {Page}", page);
                    break;
                }

                foreach (var card in jobCards)
                {
                    try
                    {
                        var titleNode = card.SelectSingleNode(".//h2//a | .//a[contains(@class,'job-title')] | .//h3//a");
                        var title = ExtractText(titleNode);
                        var jobUrl = ExtractAttribute(titleNode, "href");

                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var company = ExtractText(
                            card.SelectSingleNode(".//span[contains(@class,'company')] | .//a[contains(@class,'company')] | .//div[contains(@class,'company')]"));

                        var location = ExtractText(
                            card.SelectSingleNode(".//span[contains(@class,'location')] | .//div[contains(@class,'location')]"));

                        var salaryText = ExtractText(
                            card.SelectSingleNode(".//span[contains(@class,'salary')] | .//div[contains(@class,'salary')] | .//span[contains(@class,'compensation')]"));

                        var (salMin, salMax, currency) = ParseSalaryRange(salaryText);

                        var tags = card.SelectNodes(".//span[contains(@class,'tag')] | .//span[contains(@class,'skill')] | .//li[contains(@class,'tag')]");
                        var skills = tags?.Select(ExtractText).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [];

                        var description = ExtractText(
                            card.SelectSingleNode(".//p[contains(@class,'description')] | .//div[contains(@class,'snippet')]"));

                        var fullText = $"{title} {description} {location} {string.Join(" ", skills)}";

                        if (skills.Count == 0)
                            skills = ExtractSkillsFromText(fullText);

                        vacancies.Add(new JobVacancy
                        {
                            Id = GenerateId(jobUrl.GetHashCode().ToString("x")),
                            Title = title,
                            Company = company,
                            Country = DetectCountry(location),
                            Url = jobUrl.StartsWith("http") ? jobUrl : $"https://hired.com{jobUrl}",
                            Description = description,
                            RemotePolicy = DetectRemotePolicy(fullText),
                            EngagementType = DetectEngagementType(fullText),
                            SalaryMin = salMin,
                            SalaryMax = salMax,
                            SalaryCurrency = currency,
                            RequiredSkills = skills,
                            PostedDate = DateTimeOffset.UtcNow,
                            SourcePlatform = PlatformName,
                            GeoRestrictions = DetectGeoRestrictions(fullText)
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to parse a Hired listing");
                    }
                }

                logger.LogInformation("[Hired] Page {Page}: found {Count} jobs", page, jobCards.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape Hired");
        }

        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        var doc = await FetchPageAsync(url, cancellationToken);
        if (doc is null) return null;

        var title = ExtractText(SelectSingleNode(doc, "//h1"));
        var company = ExtractText(SelectSingleNode(doc, "//a[contains(@class,'company')] | //span[contains(@class,'company')]"));
        var description = ExtractText(SelectSingleNode(doc, "//div[contains(@class,'description')] | //div[contains(@class,'job-details')]"));
        var salaryText = ExtractText(SelectSingleNode(doc, "//div[contains(@class,'salary')] | //span[contains(@class,'compensation')]"));
        var location = ExtractText(SelectSingleNode(doc, "//span[contains(@class,'location')]"));

        var (salMin, salMax, currency) = ParseSalaryRange(salaryText);
        var fullText = $"{title} {description} {location}";

        return new JobVacancy
        {
            Id = GenerateId(url.GetHashCode().ToString("x")),
            Title = title,
            Company = company,
            Country = DetectCountry(location),
            Url = url,
            Description = description,
            RemotePolicy = DetectRemotePolicy(fullText),
            EngagementType = DetectEngagementType(fullText),
            SalaryMin = salMin,
            SalaryMax = salMax,
            SalaryCurrency = currency,
            RequiredSkills = ExtractSkillsFromText(fullText),
            PostedDate = DateTimeOffset.UtcNow,
            SourcePlatform = PlatformName,
            GeoRestrictions = DetectGeoRestrictions(fullText)
        };
    }

    private static string DetectCountry(string location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "US";

        var lower = location.ToLowerInvariant();
        if (lower.Contains("remote")) return "Remote";
        if (lower.Contains("uk") || lower.Contains("london")) return "UK";
        if (lower.Contains("canada") || lower.Contains("toronto") || lower.Contains("vancouver")) return "CA";
        if (lower.Contains("germany") || lower.Contains("berlin") || lower.Contains("munich")) return "DE";
        return "US";
    }

    private static List<string> ExtractSkillsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var lower = text.ToLowerInvariant();
        var skills = new List<string>();
        var knownSkills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c#"] = "C#", [".net"] = ".NET", ["asp.net"] = "ASP.NET",
            ["azure"] = "Azure", ["aws"] = "AWS", ["docker"] = "Docker",
            ["kubernetes"] = "Kubernetes", ["sql server"] = "SQL Server",
            ["postgresql"] = "PostgreSQL", ["redis"] = "Redis",
            ["react"] = "React", ["angular"] = "Angular", ["typescript"] = "TypeScript",
            ["microservices"] = "Microservices", ["rest api"] = "REST API",
            ["graphql"] = "GraphQL", ["rabbitmq"] = "RabbitMQ", ["kafka"] = "Kafka",
            ["terraform"] = "Terraform", ["ci/cd"] = "CI/CD", ["git"] = "Git",
            ["python"] = "Python", ["java"] = "Java", ["node.js"] = "Node.js",
            ["blazor"] = "Blazor", ["entity framework"] = "Entity Framework",
            ["mongodb"] = "MongoDB", ["elasticsearch"] = "Elasticsearch"
        };

        foreach (var (key, display) in knownSkills)
        {
            if (lower.Contains(key))
                skills.Add(display);
        }

        return skills;
    }
}
