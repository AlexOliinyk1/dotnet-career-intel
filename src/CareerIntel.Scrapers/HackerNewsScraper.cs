using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes job listings from Hacker News "Who is Hiring?" monthly threads.
/// Uses the official HN Firebase API to fetch the latest hiring thread and
/// parse individual job comments for .NET-relevant positions.
/// </summary>
public sealed class HackerNewsScraper(HttpClient httpClient, ILogger<HackerNewsScraper> logger, ScrapingCompliance? compliance = null)
    : BaseScraper(httpClient, logger, compliance)
{
    private const string HnApiBase = "https://hacker-news.firebaseio.com/v0";
    private const string HnWebBase = "https://news.ycombinator.com/item?id=";
    private const int CommentsPerPage = 25;

    public override string PlatformName => "HackerNews";

    protected override TimeSpan RequestDelay => TimeSpan.FromMilliseconds(500);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Known tech keywords used to extract required skills from comment text.
    /// </summary>
    private static readonly string[] TechKeywords =
    [
        "C#", ".NET", "ASP.NET", "Entity Framework", "Blazor", "MAUI",
        "Azure", "AWS", "GCP", "Docker", "Kubernetes", "SQL Server",
        "PostgreSQL", "Redis", "RabbitMQ", "Kafka", "gRPC", "REST",
        "GraphQL", "React", "Angular", "Vue", "TypeScript", "JavaScript",
        "Python", "Go", "Rust", "Java", "Terraform", "CI/CD",
        "Microservices", "SignalR", "WPF", "WinForms", "Xamarin",
        "Unity", "ML.NET", "Dapper", "MediatR", "CQRS", "DDD",
        "NoSQL", "MongoDB", "Elasticsearch", "Git", "Linux"
    ];

    /// <summary>
    /// Patterns that indicate a comment is .NET-relevant.
    /// </summary>
    private static readonly string[] NetRelevancePatterns =
    [
        "c#", ".net", "dotnet", "asp.net", "entity framework", "blazor",
        "maui", "wpf", "winforms", "xamarin", "unity3d", "ml.net",
        "signalr", "dapper", "mediatr", "nuget"
    ];

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Get the "whoishiring" user to find submitted story IDs
            await Task.Delay(RequestDelay, cancellationToken);

            var user = await httpClient.GetFromJsonAsync<HnUser>(
                $"{HnApiBase}/user/whoishiring.json", JsonOptions, cancellationToken);

            if (user?.Submitted is null or { Count: 0 })
            {
                logger.LogWarning("[HackerNews] Could not fetch whoishiring user or no submissions found");
                return [];
            }

            // Step 2: Find the most recent "Who is Hiring?" story
            var hiringStoryId = await FindLatestHiringStoryAsync(user.Submitted, cancellationToken);

            if (hiringStoryId is null)
            {
                logger.LogWarning("[HackerNews] No 'Who is hiring?' story found in recent submissions");
                return [];
            }

            // Step 3: Fetch the story to get its comment IDs (kids)
            await Task.Delay(RequestDelay, cancellationToken);

            var story = await httpClient.GetFromJsonAsync<HnItem>(
                $"{HnApiBase}/item/{hiringStoryId}.json", JsonOptions, cancellationToken);

            if (story?.Kids is null or { Count: 0 })
            {
                logger.LogWarning("[HackerNews] Hiring story {Id} has no comments", hiringStoryId);
                return [];
            }

            logger.LogInformation(
                "[HackerNews] Found hiring story {Id}: '{Title}' with {Count} top-level comments",
                hiringStoryId, story.Title, story.Kids.Count);

            // Step 4: Fetch comments up to maxPages * CommentsPerPage
            int maxComments = maxPages * CommentsPerPage;
            var commentIds = story.Kids.Take(maxComments).ToList();
            var vacancies = new List<JobVacancy>();

            foreach (var commentId in commentIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(RequestDelay, cancellationToken);

                try
                {
                    var comment = await httpClient.GetFromJsonAsync<HnItem>(
                        $"{HnApiBase}/item/{commentId}.json", JsonOptions, cancellationToken);

                    if (comment?.Text is null || comment.Deleted == true || comment.Dead == true)
                        continue;

                    // Decode HTML entities and strip HTML tags
                    var cleanText = StripHtml(WebUtility.HtmlDecode(comment.Text));

                    if (!IsNetRelated(cleanText))
                        continue;

                    var vacancy = ParseCommentToVacancy(comment.Id, cleanText, comment.Time);
                    if (vacancy is not null)
                        vacancies.Add(vacancy);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "[HackerNews] Failed to fetch comment {Id}", commentId);
                }
            }

            logger.LogInformation("[HackerNews] Scraped {Count} .NET-relevant vacancies", vacancies.Count);
            return vacancies;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[HackerNews] API request failed");
            return [];
        }
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(
        string url, CancellationToken cancellationToken = default)
    {
        // HN comments are self-contained; no detail page to fetch beyond the comment itself
        return Task.FromResult<JobVacancy?>(null);
    }

    /// <summary>
    /// Searches through the most recent submitted story IDs to find the latest
    /// "Who is hiring?" thread.
    /// </summary>
    private async Task<long?> FindLatestHiringStoryAsync(
        List<long> submittedIds, CancellationToken cancellationToken)
    {
        // Check the most recent submissions (the first few are typically the latest monthly threads)
        var candidateIds = submittedIds.Take(10);

        foreach (var storyId in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(RequestDelay, cancellationToken);

            try
            {
                var item = await httpClient.GetFromJsonAsync<HnItem>(
                    $"{HnApiBase}/item/{storyId}.json", JsonOptions, cancellationToken);

                if (item?.Title is not null &&
                    item.Title.Contains("Who is hiring?", StringComparison.OrdinalIgnoreCase))
                {
                    return storyId;
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[HackerNews] Failed to fetch story {Id}", storyId);
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a single HN comment into a JobVacancy. Returns null if the comment
    /// does not contain enough structured information.
    /// </summary>
    private JobVacancy? ParseCommentToVacancy(long commentId, string cleanText, long? unixTime)
    {
        if (string.IsNullOrWhiteSpace(cleanText))
            return null;

        // HN hiring comments typically follow the format:
        // Company | Location | Remote | Salary | Tech Stack
        // followed by a longer description
        var lines = cleanText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return null;

        var headerLine = lines[0];
        var headerParts = headerLine.Split('|', StringSplitOptions.TrimEntries);

        var company = headerParts.Length > 0
            ? headerParts[0].Trim()
            : "Unknown";

        // Truncate excessively long company names (sometimes the entire first line is a sentence)
        if (company.Length > 80)
            company = company[..80].Trim();

        var location = headerParts.Length > 1
            ? headerParts[1].Trim()
            : string.Empty;

        // Build description from remaining lines
        var description = lines.Length > 1
            ? string.Join("\n", lines.Skip(1)).Trim()
            : cleanText;

        // Detect remote policy from the full text
        var remotePolicy = DetectRemotePolicy(cleanText);

        // Detect salary from full text
        var (salaryMin, salaryMax, currency) = ParseSalaryRange(cleanText);

        // Extract skills
        var skills = ExtractSkills(cleanText);

        // Detect seniority from full text
        var seniority = DetectSeniority(cleanText);

        // Build a title — HN comments don't have formal titles,
        // so we construct one from the header
        var title = headerParts.Length > 0
            ? headerLine.Length > 120 ? headerLine[..120].Trim() : headerLine
            : "HN Hiring Post";

        var postedDate = unixTime.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(unixTime.Value)
            : DateTimeOffset.MinValue;

        return new JobVacancy
        {
            Id = GenerateId(commentId.ToString()),
            Title = title,
            Company = company,
            City = ExtractCity(location),
            Country = ExtractCountry(location),
            Url = $"{HnWebBase}{commentId}",
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SalaryCurrency = currency,
            RemotePolicy = remotePolicy,
            SeniorityLevel = seniority,
            EngagementType = DetectEngagementType(cleanText),
            GeoRestrictions = DetectGeoRestrictions(cleanText),
            RequiredSkills = skills,
            Description = description,
            PostedDate = postedDate,
            SourcePlatform = PlatformName.ToLowerInvariant(),
            ScrapedDate = DateTimeOffset.UtcNow
        };
    }

    private static bool IsNetRelated(string text)
    {
        var lower = text.ToLowerInvariant();
        return NetRelevancePatterns.Any(pattern => lower.Contains(pattern));
    }

    private static RemotePolicy DetectRemotePolicy(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("fully remote") || lower.Contains("100% remote") ||
            lower.Contains("remote only") || lower.Contains("| remote |") ||
            lower.Contains("| remote\n") || Regex.IsMatch(lower, @"\bremote\b"))
            return RemotePolicy.FullyRemote;

        if (lower.Contains("hybrid") || lower.Contains("partially remote") ||
            lower.Contains("remote/onsite") || lower.Contains("onsite/remote"))
            return RemotePolicy.Hybrid;

        if (lower.Contains("onsite only") || lower.Contains("on-site only") ||
            lower.Contains("in-office only") || lower.Contains("no remote"))
            return RemotePolicy.OnSite;

        return RemotePolicy.Unknown;
    }

    private static SeniorityLevel DetectSeniority(string text)
    {
        var lower = text.ToLowerInvariant();

        if (lower.Contains("principal") || lower.Contains("staff engineer"))
            return SeniorityLevel.Principal;
        if (lower.Contains("architect"))
            return SeniorityLevel.Architect;
        if (lower.Contains("lead") || lower.Contains("team lead") || lower.Contains("tech lead"))
            return SeniorityLevel.Lead;
        if (lower.Contains("senior") || lower.Contains("sr.") || lower.Contains("sr "))
            return SeniorityLevel.Senior;
        if (lower.Contains("mid-level") || lower.Contains("mid level") || lower.Contains("middle"))
            return SeniorityLevel.Middle;
        if (lower.Contains("junior") || lower.Contains("jr.") || lower.Contains("jr ") ||
            lower.Contains("entry level") || lower.Contains("entry-level"))
            return SeniorityLevel.Junior;
        if (lower.Contains("intern"))
            return SeniorityLevel.Intern;

        return SeniorityLevel.Unknown;
    }

    private static List<string> ExtractSkills(string text)
    {
        var skills = new List<string>();

        foreach (var keyword in TechKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                skills.Add(keyword);
        }

        return skills.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Extracts a city name from a location string (e.g., "San Francisco, CA" -> "San Francisco").
    /// </summary>
    private static string ExtractCity(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return string.Empty;

        // Take everything before the first comma as the city
        var commaIndex = location.IndexOf(',');
        return commaIndex > 0
            ? location[..commaIndex].Trim()
            : location.Trim();
    }

    /// <summary>
    /// Attempts to extract a country or region from a location string.
    /// </summary>
    private static string ExtractCountry(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return string.Empty;

        var lower = location.ToLowerInvariant();

        // Common country patterns in HN posts
        if (lower.Contains("usa") || lower.Contains("united states") ||
            lower.Contains(", us") || lower.EndsWith(" us") ||
            lower.Contains(", ca") || lower.Contains(", ny") ||
            lower.Contains(", wa") || lower.Contains(", tx"))
            return "US";

        if (lower.Contains("uk") || lower.Contains("united kingdom") || lower.Contains("london"))
            return "UK";

        if (lower.Contains("germany") || lower.Contains("berlin") || lower.Contains("munich"))
            return "DE";

        if (lower.Contains("canada") || lower.Contains("toronto") || lower.Contains("vancouver"))
            return "CA";

        if (lower.Contains("netherlands") || lower.Contains("amsterdam"))
            return "NL";

        if (lower.Contains("remote"))
            return "Remote";

        // Return the part after the last comma as a rough country guess
        var commaIndex = location.LastIndexOf(',');
        return commaIndex > 0 && commaIndex < location.Length - 1
            ? location[(commaIndex + 1)..].Trim()
            : string.Empty;
    }

    /// <summary>
    /// Strips HTML tags from text. HN API returns comment text as HTML.
    /// </summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Replace <p> and <br> tags with newlines for readability
        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<p\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Remove all remaining HTML tags
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);

        // Collapse multiple newlines
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    // ── JSON deserialization models for HN Firebase API ──────────────────

    private sealed class HnUser
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("submitted")]
        public List<long>? Submitted { get; set; }
    }

    private sealed class HnItem
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("by")]
        public string? By { get; set; }

        [JsonPropertyName("time")]
        public long? Time { get; set; }

        [JsonPropertyName("kids")]
        public List<long>? Kids { get; set; }

        [JsonPropertyName("dead")]
        public bool? Dead { get; set; }

        [JsonPropertyName("deleted")]
        public bool? Deleted { get; set; }
    }
}
