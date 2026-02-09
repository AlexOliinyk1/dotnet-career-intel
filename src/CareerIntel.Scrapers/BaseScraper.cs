using System.Net;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;

namespace CareerIntel.Scrapers;

/// <summary>
/// Base class for all job board scrapers. Provides shared HTTP client handling,
/// rate limiting, retry logic, and HTML parsing utilities.
/// </summary>
public abstract class BaseScraper : IJobScraper
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly ScrapingCompliance? _compliance;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);

    /// <summary>
    /// Minimum delay between requests to avoid detection and rate limiting.
    /// </summary>
    protected virtual TimeSpan RequestDelay => TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum number of retry attempts for failed requests.
    /// </summary>
    protected virtual int MaxRetries => 3;

    public abstract string PlatformName { get; }

    protected BaseScraper(HttpClient httpClient, ILogger logger, ScrapingCompliance? compliance = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _compliance = compliance;

        ConfigureDefaultHeaders();
    }

    public abstract Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default);

    public abstract Task<JobVacancy?> ScrapeDetailAsync(
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a page with rate limiting and returns the parsed HTML document.
    /// </summary>
    protected async Task<HtmlDocument?> FetchPageAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            _logger.LogDebug("[{Platform}] Fetching: {Url}", PlatformName, url);

            // Enforce scraping compliance (robots.txt + rate limits)
            if (_compliance is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var domain = uri.Host;

                if (!_compliance.CanRequest(domain))
                {
                    _logger.LogWarning("[{Platform}] Rate limited for {Domain}, skipping {Url}",
                        PlatformName, domain, url);
                    return null;
                }

                if (!await _compliance.IsPathAllowedAsync(_httpClient, url, cancellationToken))
                {
                    _logger.LogWarning("[{Platform}] Blocked by robots.txt: {Url}", PlatformName, url);
                    return null;
                }

                // Use the policy-defined delay if it's longer than the scraper default
                var policyDelayMs = _compliance.GetMinDelayMs(domain);
                var effectiveDelay = TimeSpan.FromMilliseconds(Math.Max(policyDelayMs, RequestDelay.TotalMilliseconds));
                await Task.Delay(effectiveDelay, cancellationToken);

                _compliance.RecordRequest(domain, url);
            }
            else
            {
                await Task.Delay(RequestDelay, cancellationToken);
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[{Platform}] HTTP {StatusCode} for {Url}",
                    PlatformName, response.StatusCode, url);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);

            return document;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "[{Platform}] Request failed for {Url}", PlatformName, url);
            return null;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    /// <summary>
    /// Extracts clean text content from an HTML node, trimming whitespace.
    /// </summary>
    protected static string ExtractText(HtmlNode? node)
    {
        if (node is null) return string.Empty;

        var text = WebUtility.HtmlDecode(node.InnerText);
        return text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Extracts an attribute value from an HTML node.
    /// </summary>
    protected static string ExtractAttribute(HtmlNode? node, string attribute)
    {
        if (node is null) return string.Empty;
        return node.GetAttributeValue(attribute, string.Empty);
    }

    /// <summary>
    /// Selects nodes using XPath from the document.
    /// </summary>
    protected static HtmlNodeCollection? SelectNodes(HtmlDocument doc, string xpath)
    {
        return doc.DocumentNode.SelectNodes(xpath);
    }

    /// <summary>
    /// Selects a single node using XPath.
    /// </summary>
    protected static HtmlNode? SelectSingleNode(HtmlDocument doc, string xpath)
    {
        return doc.DocumentNode.SelectSingleNode(xpath);
    }

    /// <summary>
    /// Attempts to parse a salary range string into min and max values.
    /// Handles formats like "$3000-5000", "3000 - 5000 USD", "up to $5000", etc.
    /// </summary>
    protected static (decimal? Min, decimal? Max, string Currency) ParseSalaryRange(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null, "USD");

        var currency = "USD";
        if (text.Contains("EUR", StringComparison.OrdinalIgnoreCase)) currency = "EUR";
        else if (text.Contains("UAH", StringComparison.OrdinalIgnoreCase)) currency = "UAH";
        else if (text.Contains("GBP", StringComparison.OrdinalIgnoreCase)) currency = "GBP";

        var numbers = System.Text.RegularExpressions.Regex
            .Matches(text, @"\d[\d,]*")
            .Select(m => decimal.TryParse(m.Value.Replace(",", ""), out var v) ? v : 0)
            .Where(v => v > 0)
            .ToList();

        return numbers.Count switch
        {
            0 => (null, null, currency),
            1 => text.Contains("up to", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("to", StringComparison.OrdinalIgnoreCase)
                ? (null, numbers[0], currency)
                : (numbers[0], null, currency),
            _ => (numbers[0], numbers[1], currency)
        };
    }

    /// <summary>
    /// Generates a unique vacancy ID based on platform and source identifier.
    /// </summary>
    protected string GenerateId(string sourceId) =>
        $"{PlatformName.ToLowerInvariant()}:{sourceId}";

    /// <summary>
    /// Detects the engagement type from vacancy text (title, description, employment info).
    /// </summary>
    protected static Core.Enums.EngagementType DetectEngagementType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Core.Enums.EngagementType.Unknown;

        var lower = text.ToLowerInvariant();

        // Inside IR35 (must check before generic "contract" patterns)
        if (lower.Contains("inside ir35") || lower.Contains("ir35 inside") ||
            lower.Contains("paye only") || lower.Contains("deemed employment"))
            return Core.Enums.EngagementType.InsideIR35;

        // Employment / payroll-only signals
        if (lower.Contains("payroll only") || lower.Contains("payroll-only") ||
            lower.Contains("full-time employee") || lower.Contains("fte only") ||
            lower.Contains("permanent employment") || lower.Contains("staff position"))
            return Core.Enums.EngagementType.Employment;

        // B2B / Contract signals
        if (lower.Contains("b2b") || lower.Contains("contractor") ||
            lower.Contains("outside ir35") || lower.Contains("c2c") ||
            lower.Contains("1099") || lower.Contains("фоп") ||
            lower.Contains("contract-based") || lower.Contains("b2b contract") ||
            lower.Contains("corp-to-corp") || lower.Contains("independent contractor"))
            return Core.Enums.EngagementType.ContractB2B;

        // Freelance signals
        if (lower.Contains("freelance") || lower.Contains("project-based contract"))
            return Core.Enums.EngagementType.Freelance;

        return Core.Enums.EngagementType.Unknown;
    }

    /// <summary>
    /// Detects remote work policy from vacancy text.
    /// </summary>
    protected static Core.Enums.RemotePolicy DetectRemotePolicy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Core.Enums.RemotePolicy.Unknown;

        var lower = text.ToLowerInvariant();

        if (lower.Contains("fully remote") || lower.Contains("full remote") ||
            lower.Contains("100% remote") || lower.Contains("remote only"))
            return Core.Enums.RemotePolicy.FullyRemote;

        if (lower.Contains("hybrid"))
            return Core.Enums.RemotePolicy.Hybrid;

        if (lower.Contains("remote"))
            return Core.Enums.RemotePolicy.RemoteFriendly;

        if (lower.Contains("office") || lower.Contains("on-site") ||
            lower.Contains("onsite") || lower.Contains("on site"))
            return Core.Enums.RemotePolicy.OnSite;

        return Core.Enums.RemotePolicy.Unknown;
    }

    /// <summary>
    /// Detects geographic restrictions from vacancy text that would prevent
    /// remote work from Ukraine.
    /// </summary>
    protected static List<string> DetectGeoRestrictions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lower = text.ToLowerInvariant();
        var restrictions = new List<string>();

        if (lower.Contains("uk only") || lower.Contains("uk-only") ||
            lower.Contains("uk-based only") || lower.Contains("must be based in the uk") ||
            lower.Contains("must reside in the uk") || lower.Contains("uk residents only"))
            restrictions.Add("UK-only");

        if (lower.Contains("eu only") || lower.Contains("eu-only") ||
            lower.Contains("eu-based only") || lower.Contains("eu residents only") ||
            lower.Contains("must be based in the eu") || lower.Contains("european union only"))
            restrictions.Add("EU-only");

        if (lower.Contains("us only") || lower.Contains("us-only") ||
            lower.Contains("us-based only") || lower.Contains("must be based in the us") ||
            lower.Contains("must reside in the us") || lower.Contains("us residents only") ||
            lower.Contains("united states only"))
            restrictions.Add("US-only");

        if (lower.Contains("au only") || lower.Contains("au-only") ||
            lower.Contains("australia only") || lower.Contains("australian residents only"))
            restrictions.Add("AU-only");

        // Work authorization requirements
        if (lower.Contains("must be authorized to work in") ||
            lower.Contains("work authorization required") ||
            lower.Contains("right to work in the us") ||
            lower.Contains("right to work in the uk") ||
            lower.Contains("must have the right to work") ||
            lower.Contains("eligible to work in the u"))
            restrictions.Add("Work-Auth-Required");

        // Visa sponsorship denial
        if (lower.Contains("visa sponsorship not available") ||
            lower.Contains("visa sponsorship is not available") ||
            lower.Contains("no visa sponsorship") ||
            lower.Contains("unable to sponsor") ||
            lower.Contains("cannot sponsor") ||
            lower.Contains("will not sponsor"))
            restrictions.Add("No-Visa-Sponsorship");

        // Security clearance (implies US/UK residency)
        if (lower.Contains("security clearance required") ||
            lower.Contains("security clearance") ||
            lower.Contains("itar restricted") ||
            lower.Contains("us persons only"))
            restrictions.Add("Security-Clearance-Required");

        return restrictions;
    }

    private void ConfigureDefaultHeaders()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
        {
            _httpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Accept-Language"))
        {
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,uk;q=0.8");
        }
    }
}
