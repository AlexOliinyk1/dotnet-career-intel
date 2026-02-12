using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from ZipRecruiter - one of the largest US job aggregators.
/// Uses their public search RSS/JSON endpoints for .NET positions.
/// </summary>
public sealed class ZipRecruiterScraper(HttpClient httpClient, ILogger<ZipRecruiterScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "ZipRecruiter";

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

                var url = $"https://www.ziprecruiter.com/jobs-search?search={Uri.EscapeDataString(keywords)}&location=Remote&page={page}";

                var doc = await FetchPageAsync(url, cancellationToken);
                if (doc is null) break;

                var jobCards = SelectNodes(doc, "//article[contains(@class,'job_result')]")
                           ?? SelectNodes(doc, "//div[contains(@class,'job_content')]");

                if (jobCards is null || jobCards.Count == 0)
                {
                    logger.LogDebug("No more results on page {Page}", page);
                    break;
                }

                foreach (var card in jobCards)
                {
                    try
                    {
                        var titleNode = card.SelectSingleNode(".//h2//a | .//a[contains(@class,'job_link')]");
                        var title = ExtractText(titleNode);
                        var jobUrl = ExtractAttribute(titleNode, "href");

                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var company = ExtractText(
                            card.SelectSingleNode(".//a[contains(@class,'company_name')] | .//p[contains(@class,'company')]"));

                        var location = ExtractText(
                            card.SelectSingleNode(".//span[contains(@class,'location')] | .//p[contains(@class,'location')]"));

                        var salaryText = ExtractText(
                            card.SelectSingleNode(".//span[contains(@class,'salary')] | .//p[contains(@class,'salary')]"));

                        var (salMin, salMax, currency) = ParseSalaryRange(salaryText);

                        var description = ExtractText(
                            card.SelectSingleNode(".//p[contains(@class,'snippet')] | .//div[contains(@class,'description')]"));

                        var fullText = $"{title} {description} {location}";

                        vacancies.Add(new JobVacancy
                        {
                            Id = GenerateId(jobUrl.GetHashCode().ToString("x")),
                            Title = title,
                            Company = company,
                            Country = "US",
                            Url = jobUrl.StartsWith("http") ? jobUrl : $"https://www.ziprecruiter.com{jobUrl}",
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
                        });
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to parse a ZipRecruiter listing");
                    }
                }

                logger.LogInformation("[ZipRecruiter] Page {Page}: found {Count} jobs", page, jobCards.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to scrape ZipRecruiter");
        }

        return vacancies;
    }

    public override async Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        var doc = await FetchPageAsync(url, cancellationToken);
        if (doc is null) return null;

        var title = ExtractText(SelectSingleNode(doc, "//h1[contains(@class,'job_title')] | //h1"));
        var company = ExtractText(SelectSingleNode(doc, "//a[contains(@class,'company_name')] | //span[contains(@class,'company')]"));
        var description = ExtractText(SelectSingleNode(doc, "//div[contains(@class,'job_description')] | //div[contains(@class,'jobDescriptionSection')]"));
        var salaryText = ExtractText(SelectSingleNode(doc, "//div[contains(@class,'salary')] | //span[contains(@class,'salary')]"));
        var location = ExtractText(SelectSingleNode(doc, "//span[contains(@class,'location')]"));

        var (salMin, salMax, currency) = ParseSalaryRange(salaryText);
        var fullText = $"{title} {description} {location}";

        return new JobVacancy
        {
            Id = GenerateId(url.GetHashCode().ToString("x")),
            Title = title,
            Company = company,
            Country = "US",
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
