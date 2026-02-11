using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using CareerIntel.Scrapers.ATS;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Universal scraper that can extract jobs from any company's career page.
/// Auto-detects ATS (Greenhouse, Lever, Workable, etc.) and uses appropriate scraper.
/// </summary>
public sealed class UniversalCompanyScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UniversalCompanyScraper> _logger;
    private readonly GreenhouseScraper _greenhouseScraper;
    private readonly LeverScraper _leverScraper;

    public UniversalCompanyScraper(HttpClient httpClient, ILogger<UniversalCompanyScraper> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _greenhouseScraper = new GreenhouseScraper(httpClient, logger as ILogger<GreenhouseScraper>
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GreenhouseScraper>.Instance);
        _leverScraper = new LeverScraper(httpClient, logger as ILogger<LeverScraper>
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LeverScraper>.Instance);
    }

    /// <summary>
    /// Scrapes all jobs from a company's career page.
    /// Automatically detects the ATS system and uses the appropriate scraper.
    /// </summary>
    public async Task<CompanyJobsResult> ScrapeCompanyAsync(
        string companyName,
        string careersUrl,
        CancellationToken cancellationToken = default)
    {
        var result = new CompanyJobsResult
        {
            CompanyName = companyName,
            CareersUrl = careersUrl,
            ScrapedAt = DateTimeOffset.UtcNow
        };

        try
        {
            _logger.LogInformation("Scraping jobs for {Company} at {Url}", companyName, careersUrl);

            // Step 1: Detect ATS system
            var atsInfo = await DetectATSAsync(careersUrl, cancellationToken);
            result.ATSType = atsInfo.Type;
            result.ATSIdentifier = atsInfo.Identifier;

            _logger.LogInformation("{Company} uses {ATS}", companyName, atsInfo.Type);

            // Step 2: Use appropriate scraper based on ATS
            result.Jobs = atsInfo.Type switch
            {
                "Greenhouse" => await _greenhouseScraper.ScrapeCompanyJobsAsync(
                    companyName, atsInfo.Identifier!, cancellationToken),
                "Lever" => await _leverScraper.ScrapeCompanyJobsAsync(
                    companyName, atsInfo.Identifier!, cancellationToken),
                "Workable" => await ScrapeWorkableJobsAsync(companyName, atsInfo.Identifier!, cancellationToken),
                "Ashby" => await ScrapeAshbyJobsAsync(companyName, atsInfo.Identifier!, cancellationToken),
                "Generic" => await ScrapeGenericCareersPageAsync(companyName, careersUrl, cancellationToken),
                _ => []
            };

            result.Success = result.Jobs.Count > 0;
            _logger.LogInformation("Found {Count} jobs for {Company}", result.Jobs.Count, companyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scraping {Company}", companyName);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Detects which ATS (Applicant Tracking System) a company uses.
    /// </summary>
    private async Task<ATSInfo> DetectATSAsync(string careersUrl, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(careersUrl, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Check for Greenhouse
            if (html.Contains("greenhouse.io") || html.Contains("boards.greenhouse") ||
                careersUrl.Contains("greenhouse"))
            {
                var id = ExtractGreenhouseId(careersUrl, html);
                if (!string.IsNullOrEmpty(id))
                    return new ATSInfo { Type = "Greenhouse", Identifier = id };
            }

            // Check for Lever
            if (html.Contains("lever.co") || html.Contains("jobs.lever") || careersUrl.Contains("lever"))
            {
                var id = ExtractLeverId(careersUrl, html);
                if (!string.IsNullOrEmpty(id))
                    return new ATSInfo { Type = "Lever", Identifier = id };
            }

            // Check for Workable
            if (html.Contains("workable.com") || html.Contains("apply.workable") ||
                careersUrl.Contains("workable"))
            {
                var id = ExtractWorkableId(careersUrl, html);
                if (!string.IsNullOrEmpty(id))
                    return new ATSInfo { Type = "Workable", Identifier = id };
            }

            // Check for Ashby
            if (html.Contains("ashbyhq.com") || html.Contains("jobs.ashbyhq") ||
                careersUrl.Contains("ashbyhq"))
            {
                var id = ExtractAshbyId(careersUrl, html);
                if (!string.IsNullOrEmpty(id))
                    return new ATSInfo { Type = "Ashby", Identifier = id };
            }

            // Fallback to generic scraping
            return new ATSInfo { Type = "Generic", Identifier = careersUrl };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect ATS for {Url}", careersUrl);
            return new ATSInfo { Type = "Unknown", Identifier = null };
        }
    }

    private static string? ExtractGreenhouseId(string url, string html)
    {
        // Try URL patterns: boards.greenhouse.io/company-name
        var match = Regex.Match(url, @"greenhouse\.io/([^/\?]+)");
        if (match.Success)
            return match.Groups[1].Value;

        // Try HTML patterns
        match = Regex.Match(html, @"boards\.greenhouse\.io/([^/\""']+)");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    private static string? ExtractLeverId(string url, string html)
    {
        // Try URL patterns: jobs.lever.co/company-name
        var match = Regex.Match(url, @"lever\.co/([^/\?]+)");
        if (match.Success)
            return match.Groups[1].Value;

        // Try HTML patterns
        match = Regex.Match(html, @"lever\.co/([^/\""']+)");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    private static string? ExtractWorkableId(string url, string html)
    {
        var match = Regex.Match(url, @"workable\.com/j/([^/\?]+)");
        if (match.Success)
            return match.Groups[1].Value;

        match = Regex.Match(html, @"apply\.workable\.com/([^/\""']+)");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    private static string? ExtractAshbyId(string url, string html)
    {
        var match = Regex.Match(url, @"ashbyhq\.com/([^/\?]+)");
        if (match.Success)
            return match.Groups[1].Value;

        return null;
    }

    private async Task<List<JobVacancy>> ScrapeWorkableJobsAsync(
        string companyName, string workableId, CancellationToken cancellationToken)
    {
        // TODO: Implement Workable API scraper
        _logger.LogWarning("Workable scraper not yet implemented for {Company}", companyName);
        return [];
    }

    private async Task<List<JobVacancy>> ScrapeAshbyJobsAsync(
        string companyName, string ashbyId, CancellationToken cancellationToken)
    {
        // TODO: Implement Ashby API scraper
        _logger.LogWarning("Ashby scraper not yet implemented for {Company}", companyName);
        return [];
    }

    private async Task<List<JobVacancy>> ScrapeGenericCareersPageAsync(
        string companyName, string careersUrl, CancellationToken cancellationToken)
    {
        // TODO: Implement generic HTML parser for custom career pages
        _logger.LogWarning("Generic scraper not yet implemented for {Company}", companyName);
        return [];
    }
}

/// <summary>
/// Information about detected ATS system.
/// </summary>
public class ATSInfo
{
    public string Type { get; set; } = "Unknown";
    public string? Identifier { get; set; }
}

/// <summary>
/// Result of scraping a company's jobs.
/// </summary>
public class CompanyJobsResult
{
    public string CompanyName { get; set; } = string.Empty;
    public string CareersUrl { get; set; } = string.Empty;
    public string ATSType { get; set; } = "Unknown";
    public string? ATSIdentifier { get; set; }
    public List<JobVacancy> Jobs { get; set; } = [];
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ScrapedAt { get; set; }
}
