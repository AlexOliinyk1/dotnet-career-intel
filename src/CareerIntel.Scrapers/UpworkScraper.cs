using System.ServiceModel.Syndication;
using System.Xml;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Upwork RSS feeds.
/// Upwork is a leading freelance marketplace with high-value B2B contracts ($50-250/hr).
/// Uses public RSS feeds - no authentication required.
/// </summary>
public sealed class UpworkScraper(HttpClient httpClient, ILogger<UpworkScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    // Upwork RSS feed URLs for .NET/C# jobs
    private static readonly string[] RssFeedUrls =
    [
        "https://www.upwork.com/ab/feed/jobs/rss?q=.net&sort=recency",
        "https://www.upwork.com/ab/feed/jobs/rss?q=c%23&sort=recency",
        "https://www.upwork.com/ab/feed/jobs/rss?q=asp.net&sort=recency"
    ];

    public override string PlatformName => "Upwork";

    protected override TimeSpan RequestDelay => TimeSpan.FromSeconds(2);

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var allJobs = new Dictionary<string, JobVacancy>();

        foreach (var feedUrl in RssFeedUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                logger.LogInformation("[Upwork] Fetching RSS feed: {Url}", feedUrl);

                await Task.Delay(RequestDelay, cancellationToken);

                var response = await httpClient.GetAsync(feedUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Upwork] HTTP {StatusCode} for {Url}", response.StatusCode, feedUrl);
                    continue;
                }

                var xmlContent = await response.Content.ReadAsStreamAsync(cancellationToken);

                using var xmlReader = XmlReader.Create(xmlContent);
                var feed = SyndicationFeed.Load(xmlReader);

                logger.LogInformation("[Upwork] Found {Count} items in feed", feed.Items.Count());

                foreach (var item in feed.Items)
                {
                    var vacancy = ParseFeedItem(item);
                    if (vacancy is not null && !allJobs.ContainsKey(vacancy.Id))
                    {
                        allJobs[vacancy.Id] = vacancy;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Upwork] Failed to fetch/parse RSS feed: {Url}", feedUrl);
            }
        }

        logger.LogInformation("[Upwork] Scraped {Count} unique jobs total", allJobs.Count);
        return allJobs.Values.ToList();
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // RSS feed already provides full job data
        return Task.FromResult<JobVacancy?>(null);
    }

    private JobVacancy? ParseFeedItem(SyndicationItem item)
    {
        try
        {
            var title = item.Title?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                logger.LogDebug("[Upwork] Item has no title, skipping");
                return null;
            }

            var link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(link))
            {
                logger.LogDebug("[Upwork] Item has no link, skipping");
                return null;
            }

            // Extract job ID from URL (e.g., https://www.upwork.com/jobs/~012345678901234567)
            var jobId = ExtractJobId(link);

            var description = item.Summary?.Text ?? string.Empty;

            // Extract budget/hourly rate from description
            var (salaryMin, salaryMax, currency) = ExtractBudget(description);

            // Extract skills and other metadata from description
            var skills = ExtractSkills(description);
            var engagementType = DetectEngagementFromDescription(description);
            var seniority = DetectSeniority(title + " " + description);

            return new JobVacancy
            {
                Id = GenerateId(jobId),
                Title = title,
                Company = "Upwork Client", // Upwork doesn't expose client names in RSS
                Description = description,
                Url = link,
                City = string.Empty,
                Country = DetectCountryFromDescription(description),
                SalaryMin = salaryMin,
                SalaryMax = salaryMax,
                SalaryCurrency = currency,
                RemotePolicy = RemotePolicy.FullyRemote, // Upwork is remote-first
                SeniorityLevel = seniority,
                EngagementType = engagementType,
                GeoRestrictions = DetectGeoRestrictions(description),
                RequiredSkills = skills,
                PostedDate = item.PublishDate,
                SourcePlatform = PlatformName.ToLowerInvariant(),
                ScrapedDate = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Upwork] Failed to parse feed item");
            return null;
        }
    }

    private static string ExtractJobId(string url)
    {
        // Extract ID from URL like: https://www.upwork.com/jobs/~012345678901234567
        var match = System.Text.RegularExpressions.Regex.Match(url, @"~([a-f0-9]+)");
        if (match.Success)
            return match.Groups[1].Value;

        return url.GetHashCode().ToString();
    }

    private static (decimal? Min, decimal? Max, string Currency) ExtractBudget(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (null, null, "USD");

        var lower = description.ToLowerInvariant();

        // Hourly rate patterns
        var hourlyMatch = System.Text.RegularExpressions.Regex.Match(
            description,
            @"\$(\d+(?:\.\d{2})?)-\$?(\d+(?:\.\d{2})?)\s*/?\s*hr",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (hourlyMatch.Success)
        {
            var min = decimal.Parse(hourlyMatch.Groups[1].Value);
            var max = decimal.Parse(hourlyMatch.Groups[2].Value);
            return (min, max, "USD");
        }

        // Single hourly rate
        var singleHourlyMatch = System.Text.RegularExpressions.Regex.Match(
            description,
            @"\$(\d+(?:\.\d{2})?)\s*/?\s*hr",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (singleHourlyMatch.Success)
        {
            var rate = decimal.Parse(singleHourlyMatch.Groups[1].Value);
            return (rate, rate, "USD");
        }

        // Fixed price budget
        var budgetMatch = System.Text.RegularExpressions.Regex.Match(
            description,
            @"[Bb]udget.*?\$(\d+(?:,\d{3})*)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (budgetMatch.Success)
        {
            var budget = decimal.Parse(budgetMatch.Groups[1].Value.Replace(",", ""));
            return (budget, budget, "USD");
        }

        return (null, null, "USD");
    }

    private static List<string> ExtractSkills(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return [];

        var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lower = description.ToLowerInvariant();

        // Common .NET skills
        var skillKeywords = new[]
        {
            "C#", ".NET", "ASP.NET", "Blazor", "MAUI", "WPF", "Entity Framework",
            "Azure", "AWS", "Docker", "Kubernetes", "SQL Server", "PostgreSQL",
            "MongoDB", "Redis", "Microservices", "REST API", "GraphQL", "gRPC",
            "React", "Angular", "TypeScript", "JavaScript", "Git", "CI/CD"
        };

        foreach (var keyword in skillKeywords)
        {
            if (lower.Contains(keyword.ToLowerInvariant()))
                skills.Add(keyword);
        }

        return skills.OrderBy(s => s).ToList();
    }

    private static EngagementType DetectEngagementFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return EngagementType.Unknown;

        var lower = description.ToLowerInvariant();

        // Upwork is primarily freelance/contract work
        if (lower.Contains("hourly") || lower.Contains("contract") || lower.Contains("freelance"))
            return EngagementType.ContractB2B;

        if (lower.Contains("fixed") || lower.Contains("project-based"))
            return EngagementType.Freelance;

        return EngagementType.ContractB2B; // Default for Upwork
    }

    private static string DetectCountryFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Remote";

        var lower = description.ToLowerInvariant();

        if (lower.Contains("worldwide") || lower.Contains("any location") || lower.Contains("anywhere"))
            return "Worldwide";

        // Check for specific regions
        var regions = new Dictionary<string, string>
        {
            ["united states"] = "United States",
            ["us only"] = "United States",
            ["europe"] = "Europe",
            ["eu only"] = "Europe",
            ["uk only"] = "United Kingdom",
            ["canada"] = "Canada",
            ["australia"] = "Australia"
        };

        foreach (var (keyword, country) in regions)
        {
            if (lower.Contains(keyword))
                return country;
        }

        return "Remote";
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SeniorityLevel.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff")) return SeniorityLevel.Principal;
        if (lower.Contains("architect")) return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("tech lead")) return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr ")) return SeniorityLevel.Senior;
        if (lower.Contains("mid") || lower.Contains("middle")) return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("entry")) return SeniorityLevel.Junior;
        if (lower.Contains("intern")) return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }
}
