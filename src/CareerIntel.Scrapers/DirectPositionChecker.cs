using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Given a detected end client name, discovers their careers page,
/// scrapes direct job postings, and matches them against the intermediary posting.
/// </summary>
public sealed class DirectPositionChecker
{
    private readonly HttpClient _httpClient;
    private readonly UniversalCompanyScraper _companyScraper;
    private readonly ILogger _logger;

    private static readonly string[] CareersPathPatterns =
    [
        "/careers", "/jobs", "/company/careers", "/en/careers",
        "/about/careers", "/join-us", "/work-with-us"
    ];

    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "the", "and", "or", "at", "in", "for", "of", "to", "with",
        "-", "/", "|", "&", "+"
    };

    public DirectPositionChecker(HttpClient httpClient, UniversalCompanyScraper companyScraper, ILogger logger)
    {
        _httpClient = httpClient;
        _companyScraper = companyScraper;
        _logger = logger;
    }

    /// <summary>
    /// Full check: discover careers URL, scrape direct postings, and match against intermediary vacancy.
    /// </summary>
    public async Task<DirectCheckResult> CheckForDirectPostingsAsync(
        EndClientDetection detection,
        string? overrideCareersUrl = null,
        CancellationToken cancellationToken = default)
    {
        var result = new DirectCheckResult
        {
            ClientName = detection.DetectedClientName
        };

        if (string.IsNullOrWhiteSpace(detection.DetectedClientName))
        {
            result.Error = "No client name detected";
            return result;
        }

        try
        {
            // Step 1: Discover careers URL
            var careersUrl = overrideCareersUrl
                ?? await DiscoverCareersUrlAsync(detection.DetectedClientName, cancellationToken);

            if (string.IsNullOrEmpty(careersUrl))
            {
                result.Error = $"Could not find careers page for '{detection.DetectedClientName}'. Use --client-url to provide it manually.";
                return result;
            }

            result.CareersUrl = careersUrl;

            // Step 2: Detect ATS and scrape
            var scrapeResult = await _companyScraper.ScrapeCompanyAsync(
                detection.DetectedClientName, careersUrl, cancellationToken);

            result.ATSType = scrapeResult.ATSType;
            result.AllDirectPostings = scrapeResult.Jobs;

            if (!scrapeResult.Success || scrapeResult.Jobs.Count == 0)
            {
                result.Error = scrapeResult.Error ?? "No direct postings found";
                return result;
            }

            // Step 3: Match positions
            result.Matches = FindMatchingPositions(detection.OriginalVacancy, scrapeResult.Jobs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking direct postings for {Client}", detection.DetectedClientName);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Discovers a company's careers page URL by trying common patterns.
    /// </summary>
    public async Task<string?> DiscoverCareersUrlAsync(
        string companyName,
        CancellationToken cancellationToken = default)
    {
        var domain = GuessDomain(companyName);
        if (string.IsNullOrEmpty(domain))
            return null;

        _logger.LogInformation("Trying to discover careers page for {Company} (domain: {Domain})",
            companyName, domain);

        // Try main domain with careers paths
        foreach (var path in CareersPathPatterns)
        {
            var url = $"https://www.{domain}{path}";
            if (await IsUrlReachableAsync(url, cancellationToken))
            {
                _logger.LogInformation("Found careers page: {Url}", url);
                return url;
            }

            url = $"https://{domain}{path}";
            if (await IsUrlReachableAsync(url, cancellationToken))
            {
                _logger.LogInformation("Found careers page: {Url}", url);
                return url;
            }
        }

        // Try subdomain patterns
        var subdomainPatterns = new[] { $"https://careers.{domain}", $"https://jobs.{domain}" };
        foreach (var url in subdomainPatterns)
        {
            if (await IsUrlReachableAsync(url, cancellationToken))
            {
                _logger.LogInformation("Found careers page: {Url}", url);
                return url;
            }
        }

        // Try well-known ATS direct URLs
        var atsUrls = new[]
        {
            $"https://boards.greenhouse.io/{NormalizeForAts(companyName)}",
            $"https://jobs.lever.co/{NormalizeForAts(companyName)}",
            $"https://apply.workable.com/{NormalizeForAts(companyName)}/",
            $"https://jobs.ashbyhq.com/{NormalizeForAts(companyName)}"
        };

        foreach (var url in atsUrls)
        {
            if (await IsUrlReachableAsync(url, cancellationToken))
            {
                _logger.LogInformation("Found ATS page: {Url}", url);
                return url;
            }
        }

        // Fallback: try Google search for careers page
        var googleUrl = await TryGoogleSearchAsync(companyName, cancellationToken);
        if (googleUrl != null)
        {
            _logger.LogInformation("Found careers page via search: {Url}", googleUrl);
            return googleUrl;
        }

        _logger.LogWarning("Could not discover careers page for {Company}", companyName);
        return null;
    }

    private async Task<string?> TryGoogleSearchAsync(string companyName, CancellationToken cancellationToken)
    {
        try
        {
            // Use a simple search scrape to find careers page
            var query = Uri.EscapeDataString($"{companyName} careers jobs");
            var searchUrl = $"https://www.google.com/search?q={query}&num=5";

            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Extract URLs from search results that look like careers pages
            var urlMatches = Regex.Matches(html, @"https?://[^""'\s<>]+(?:careers|jobs)[^""'\s<>]*",
                RegexOptions.IgnoreCase);

            foreach (Match match in urlMatches)
            {
                var url = match.Value;
                // Filter out Google's own URLs and common noise
                if (!url.Contains("google.com") && !url.Contains("youtube.com") &&
                    !url.Contains("glassdoor") && !url.Contains("linkedin.com") &&
                    !url.Contains("indeed.com"))
                {
                    return url;
                }
            }
        }
        catch
        {
            // Google search is best-effort fallback
        }

        return null;
    }

    /// <summary>
    /// Compares the intermediary vacancy against direct vacancies to find matches.
    /// </summary>
    public List<PositionMatch> FindMatchingPositions(
        JobVacancy intermediaryVacancy,
        List<JobVacancy> directVacancies)
    {
        var matches = new List<PositionMatch>();

        foreach (var direct in directVacancies)
        {
            var titleSim = ComputeTitleSimilarity(intermediaryVacancy.Title, direct.Title);
            var skillOverlap = ComputeSkillOverlap(
                intermediaryVacancy.RequiredSkills ?? [],
                direct.RequiredSkills ?? []);

            var locationMatch = IsLocationCompatible(intermediaryVacancy, direct);

            // Weighted overall confidence
            var overall = (titleSim * 0.45) + (skillOverlap * 0.40) + (locationMatch ? 0.15 : 0.0);

            // Only include if there's meaningful similarity
            if (overall >= 0.25)
            {
                matches.Add(new PositionMatch
                {
                    IntermediaryPosting = intermediaryVacancy,
                    DirectPosting = direct,
                    TitleSimilarity = titleSim,
                    SkillOverlap = skillOverlap,
                    LocationMatch = locationMatch,
                    OverallConfidence = overall
                });
            }
        }

        return matches.OrderByDescending(m => m.OverallConfidence).ToList();
    }

    private double ComputeTitleSimilarity(string? title1, string? title2)
    {
        if (string.IsNullOrWhiteSpace(title1) || string.IsNullOrWhiteSpace(title2))
            return 0;

        var tokens1 = Tokenize(title1).Except(NoiseWords, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokens2 = Tokenize(title2).Except(NoiseWords, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var intersection = tokens1.Intersect(tokens2, StringComparer.OrdinalIgnoreCase).Count();
        var union = tokens1.Union(tokens2, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private static double ComputeSkillOverlap(List<string> skills1, List<string> skills2)
    {
        if (skills1.Count == 0 || skills2.Count == 0)
            return 0;

        var set1 = skills1.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var set2 = skills2.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var intersection = set1.Intersect(set2, StringComparer.OrdinalIgnoreCase).Count();
        var union = set1.Union(set2, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0.0;
    }

    private static bool IsLocationCompatible(JobVacancy v1, JobVacancy v2)
    {
        // Both remote = compatible
        if (v1.RemotePolicy == Core.Enums.RemotePolicy.FullyRemote &&
            v2.RemotePolicy == Core.Enums.RemotePolicy.FullyRemote)
            return true;

        // Same country = compatible
        if (!string.IsNullOrEmpty(v1.Country) && !string.IsNullOrEmpty(v2.Country) &&
            v1.Country.Equals(v2.Country, StringComparison.OrdinalIgnoreCase))
            return true;

        // Same city = compatible
        if (!string.IsNullOrEmpty(v1.City) && !string.IsNullOrEmpty(v2.City) &&
            v1.City.Equals(v2.City, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return Regex.Split(text.ToLowerInvariant(), @"[\s\-/|,()]+")
            .Where(t => t.Length > 0);
    }

    private static string GuessDomain(string companyName)
    {
        // Normalize company name to likely domain
        var normalized = companyName.Trim().ToLowerInvariant();

        // Remove common suffixes
        var suffixes = new[] { " inc", " inc.", " ltd", " ltd.", " gmbh", " ag", " s.a.", " corp", " corporation", " group", " technologies", " technology", " software" };
        foreach (var suffix in suffixes)
        {
            if (normalized.EndsWith(suffix))
                normalized = normalized[..^suffix.Length].Trim();
        }

        // Replace spaces/special chars with nothing (most domains are concatenated)
        var domain = Regex.Replace(normalized, @"[^a-z0-9]", "") + ".com";

        return domain;
    }

    private static string NormalizeForAts(string companyName)
    {
        return Regex.Replace(companyName.Trim().ToLowerInvariant(), @"[^a-z0-9]", "-")
            .Trim('-');
    }

    private async Task<bool> IsUrlReachableAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; CareerIntel/1.0)");

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
