using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Enforces ethical scraping practices including per-domain rate limiting,
/// robots.txt compliance, and full audit logging of all scraping activity.
/// </summary>
public sealed class ScrapingCompliance(ILogger<ScrapingCompliance> logger)
{
    private readonly Dictionary<string, RateLimitPolicy> _policies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["djinni.co"] = new(MaxRequestsPerMinute: 20, MinDelayMs: 3000, RespectRobotsTxt: true),
        ["jobs.dou.ua"] = new(MaxRequestsPerMinute: 15, MinDelayMs: 3000, RespectRobotsTxt: true),
        ["www.linkedin.com"] = new(MaxRequestsPerMinute: 5, MinDelayMs: 5000, RespectRobotsTxt: true),
        ["justjoin.it"] = new(MaxRequestsPerMinute: 30, MinDelayMs: 2000, RespectRobotsTxt: true),
        ["remoteok.com"] = new(MaxRequestsPerMinute: 10, MinDelayMs: 3000, RespectRobotsTxt: true),
        ["weworkremotely.com"] = new(MaxRequestsPerMinute: 10, MinDelayMs: 3000, RespectRobotsTxt: true),
        ["hacker-news.firebaseio.com"] = new(MaxRequestsPerMinute: 60, MinDelayMs: 500, RespectRobotsTxt: false),
        ["himalayas.app"] = new(MaxRequestsPerMinute: 15, MinDelayMs: 3000, RespectRobotsTxt: true),
        ["jobicy.com"] = new(MaxRequestsPerMinute: 15, MinDelayMs: 3000, RespectRobotsTxt: true),
        ["nofluffjobs.com"] = new(MaxRequestsPerMinute: 10, MinDelayMs: 4000, RespectRobotsTxt: true),
        ["www.toptal.com"] = new(MaxRequestsPerMinute: 6, MinDelayMs: 5000, RespectRobotsTxt: true),
        ["dou.ua"] = new(MaxRequestsPerMinute: 10, MinDelayMs: 4000, RespectRobotsTxt: true),
        ["www.reddit.com"] = new(MaxRequestsPerMinute: 20, MinDelayMs: 3000, RespectRobotsTxt: true),
    };

    private readonly Dictionary<string, RequestLog> _requestLogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AuditEntry> _auditLog = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Cache of parsed robots.txt rules per domain, with expiry tracking.
    /// </summary>
    private readonly ConcurrentDictionary<string, RobotsCacheEntry> _robotsCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan RobotsCacheDuration = TimeSpan.FromHours(1);

    // ═══════════════════════════════════════════════════════════════════════
    //  RATE LIMITING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether a request to the specified domain is allowed under
    /// the current rate limit policy. Returns <c>false</c> if the domain has
    /// exceeded its maximum requests per minute.
    /// </summary>
    public bool CanRequest(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        lock (_lock)
        {
            var policy = GetPolicyForDomain(domain);
            var log = GetOrCreateLog(domain);

            PruneExpiredEntries(log);

            bool allowed = log.RequestTimes.Count < policy.MaxRequestsPerMinute;

            if (!allowed)
            {
                log.BlockedRequests++;

                AddAuditEntryLocked(domain, url: string.Empty, "RateLimited",
                    $"Blocked: {log.RequestTimes.Count}/{policy.MaxRequestsPerMinute} requests in last minute.");

                logger.LogWarning(
                    "Rate limit reached for {Domain}: {Current}/{Max} requests/min",
                    domain, log.RequestTimes.Count, policy.MaxRequestsPerMinute);
            }

            return allowed;
        }
    }

    /// <summary>
    /// Records that a request was made to the given domain. Updates both
    /// the request log and the audit trail.
    /// </summary>
    public void RecordRequest(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        lock (_lock)
        {
            var log = GetOrCreateLog(domain);

            PruneExpiredEntries(log);

            log.RequestTimes.Enqueue(DateTimeOffset.UtcNow);
            log.TotalRequests++;

            AddAuditEntryLocked(domain, url: string.Empty, "Allowed",
                $"Request recorded. Total: {log.TotalRequests}, in window: {log.RequestTimes.Count}.");

            logger.LogDebug(
                "Recorded request for {Domain}. Window: {Count}, Total: {Total}",
                domain, log.RequestTimes.Count, log.TotalRequests);
        }
    }

    /// <summary>
    /// Records a request to a specific URL, combining rate tracking with audit logging.
    /// </summary>
    public void RecordRequest(string domain, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        lock (_lock)
        {
            var log = GetOrCreateLog(domain);

            PruneExpiredEntries(log);

            log.RequestTimes.Enqueue(DateTimeOffset.UtcNow);
            log.TotalRequests++;

            AddAuditEntryLocked(domain, url, "Allowed",
                $"Request recorded. Total: {log.TotalRequests}, in window: {log.RequestTimes.Count}.");

            logger.LogDebug(
                "Recorded request for {Domain} ({Url}). Window: {Count}, Total: {Total}",
                domain, url, log.RequestTimes.Count, log.TotalRequests);
        }
    }

    /// <summary>
    /// Returns the current number of requests in the sliding window and the
    /// maximum allowed for the given domain.
    /// </summary>
    public (int RequestsInLastMinute, int MaxAllowed) GetRateStatus(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        lock (_lock)
        {
            var policy = GetPolicyForDomain(domain);
            var log = GetOrCreateLog(domain);

            PruneExpiredEntries(log);

            return (log.RequestTimes.Count, policy.MaxRequestsPerMinute);
        }
    }

    /// <summary>
    /// Returns the minimum delay in milliseconds that must elapse between requests
    /// to the given domain.
    /// </summary>
    public int GetMinDelayMs(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return GetPolicyForDomain(domain).MinDelayMs;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ROBOTS.TXT COMPLIANCE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether the given URL path is allowed by the domain's robots.txt.
    /// Fetches and caches robots.txt rules for up to 1 hour per domain.
    /// Returns <c>true</c> if the path is allowed or if robots.txt could not be fetched.
    /// </summary>
    public async Task<bool> IsPathAllowedAsync(HttpClient client, string url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Invalid URL provided to robots.txt check: {Url}", url);
            return true; // Allow if URL is malformed (fail-open for non-critical check)
        }

        string domain = uri.Host;
        var policy = GetPolicyForDomain(domain);

        if (!policy.RespectRobotsTxt)
        {
            logger.LogDebug("Robots.txt checking disabled for {Domain}", domain);
            return true;
        }

        var rules = await GetOrFetchRobotsRulesAsync(client, domain, uri.Scheme, ct);

        if (rules is null || rules.Count == 0)
        {
            logger.LogDebug("No robots.txt rules found for {Domain}. Allowing request.", domain);
            return true;
        }

        string path = uri.AbsolutePath;

        // Check Disallow rules - most specific match wins
        bool isDisallowed = false;
        int longestMatchLength = 0;
        string matchedRule = string.Empty;

        foreach (var rule in rules)
        {
            if (path.StartsWith(rule.Path, StringComparison.OrdinalIgnoreCase) &&
                rule.Path.Length > longestMatchLength)
            {
                longestMatchLength = rule.Path.Length;
                isDisallowed = rule.IsDisallow;
                matchedRule = rule.Path;
            }
        }

        if (isDisallowed)
        {
            logger.LogInformation(
                "Path blocked by robots.txt for {Domain}: {Path} (matched rule: {Rule})",
                domain, path, matchedRule);

            lock (_lock)
            {
                AddAuditEntryLocked(domain, url, "Blocked",
                    $"Disallowed by robots.txt rule: {matchedRule}");
            }
        }
        else
        {
            logger.LogDebug("Path allowed by robots.txt for {Domain}: {Path}", domain, path);
        }

        return !isDisallowed;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AUDIT LOG
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns audit entries, optionally filtered by domain, up to the specified
    /// maximum number of entries, most recent first.
    /// </summary>
    public IReadOnlyList<AuditEntry> GetAuditLog(string? domain = null, int maxEntries = 100)
    {
        lock (_lock)
        {
            IEnumerable<AuditEntry> query = _auditLog;

            if (!string.IsNullOrWhiteSpace(domain))
                query = query.Where(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

            return query
                .OrderByDescending(e => e.Timestamp)
                .Take(maxEntries)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Returns summary statistics for all tracked domains.
    /// </summary>
    public IReadOnlyDictionary<string, (int Total, int Blocked)> GetDomainStatistics()
    {
        lock (_lock)
        {
            var stats = new Dictionary<string, (int Total, int Blocked)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (domain, log) in _requestLogs)
            {
                stats[domain] = (log.TotalRequests, log.BlockedRequests);
            }

            return stats.AsReadOnly();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  POLICY MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Registers or updates a rate limit policy for the specified domain.
    /// </summary>
    public void SetPolicy(string domain, RateLimitPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(policy);

        lock (_lock)
        {
            _policies[domain] = policy;
        }

        logger.LogInformation(
            "Policy set for {Domain}: {Max} req/min, {Delay}ms delay, robots.txt={Robots}",
            domain, policy.MaxRequestsPerMinute, policy.MinDelayMs, policy.RespectRobotsTxt);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private RateLimitPolicy GetPolicyForDomain(string domain)
    {
        // Return specific policy if it exists, otherwise a conservative default
        if (_policies.TryGetValue(domain, out var policy))
            return policy;

        return new RateLimitPolicy(MaxRequestsPerMinute: 10, MinDelayMs: 3000, RespectRobotsTxt: true);
    }

    private RequestLog GetOrCreateLog(string domain)
    {
        if (!_requestLogs.TryGetValue(domain, out var log))
        {
            log = new RequestLog();
            _requestLogs[domain] = log;
        }

        return log;
    }

    /// <summary>
    /// Removes request timestamps older than 1 minute from the sliding window.
    /// </summary>
    private static void PruneExpiredEntries(RequestLog log)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);

        while (log.RequestTimes.Count > 0 && log.RequestTimes.Peek() < cutoff)
        {
            log.RequestTimes.Dequeue();
        }
    }

    private void AddAuditEntryLocked(string domain, string url, string action, string reason)
    {
        _auditLog.Add(new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Domain = domain,
            Url = url,
            Action = action,
            Reason = reason
        });

        // Keep audit log bounded to prevent unbounded memory growth
        const int maxAuditSize = 10_000;
        if (_auditLog.Count > maxAuditSize)
        {
            _auditLog.RemoveRange(0, _auditLog.Count - maxAuditSize);
        }
    }

    /// <summary>
    /// Fetches robots.txt from the domain and parses Disallow/Allow rules for the
    /// wildcard (*) and our specific user-agent. Results are cached for 1 hour.
    /// </summary>
    private async Task<List<RobotsRule>?> GetOrFetchRobotsRulesAsync(
        HttpClient client, string domain, string scheme, CancellationToken ct)
    {
        // Check cache first
        if (_robotsCache.TryGetValue(domain, out var cached) &&
            DateTimeOffset.UtcNow - cached.FetchedAt < RobotsCacheDuration)
        {
            logger.LogDebug("Using cached robots.txt for {Domain} (age: {Age})",
                domain, DateTimeOffset.UtcNow - cached.FetchedAt);
            return cached.Rules;
        }

        // Fetch fresh robots.txt
        string robotsUrl = $"{scheme}://{domain}/robots.txt";
        logger.LogDebug("Fetching robots.txt from {Url}", robotsUrl);

        try
        {
            var response = await client.GetAsync(robotsUrl, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("robots.txt not found for {Domain} (HTTP {Status})",
                    domain, response.StatusCode);

                // Cache the "no rules" result to avoid repeated fetches
                _robotsCache[domain] = new RobotsCacheEntry([], DateTimeOffset.UtcNow);
                return null;
            }

            string content = await response.Content.ReadAsStringAsync(ct);
            var rules = ParseRobotsTxt(content);

            _robotsCache[domain] = new RobotsCacheEntry(rules, DateTimeOffset.UtcNow);

            logger.LogInformation(
                "Parsed {Count} robots.txt rules for {Domain}",
                rules.Count, domain);

            return rules;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch robots.txt for {Domain}. Allowing requests.", domain);

            // Cache empty rules so we don't retry immediately
            _robotsCache[domain] = new RobotsCacheEntry([], DateTimeOffset.UtcNow);
            return null;
        }
    }

    /// <summary>
    /// Parses a robots.txt file and extracts Disallow and Allow rules that apply
    /// to our scraper (matching User-agent: * or specific bot names).
    /// </summary>
    private static List<RobotsRule> ParseRobotsTxt(string content)
    {
        var rules = new List<RobotsRule>();
        bool inRelevantBlock = false;
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            // Strip comments
            string line = rawLine.Contains('#')
                ? rawLine[..rawLine.IndexOf('#')].Trim()
                : rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                string agent = line["User-agent:".Length..].Trim();

                // We respect rules for the wildcard agent
                inRelevantBlock = agent == "*"
                    || agent.Equals("CareerIntelBot", StringComparison.OrdinalIgnoreCase);
            }
            else if (inRelevantBlock)
            {
                if (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["Disallow:".Length..].Trim();
                    if (!string.IsNullOrEmpty(path))
                        rules.Add(new RobotsRule(path, IsDisallow: true));
                }
                else if (line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase))
                {
                    string path = line["Allow:".Length..].Trim();
                    if (!string.IsNullOrEmpty(path))
                        rules.Add(new RobotsRule(path, IsDisallow: false));
                }
            }
        }

        return rules;
    }

    // ── Internal types ──────────────────────────────────────────────────

    private sealed record RobotsCacheEntry(List<RobotsRule> Rules, DateTimeOffset FetchedAt);

    private sealed record RobotsRule(string Path, bool IsDisallow);
}

// ═══════════════════════════════════════════════════════════════════════════
//  PUBLIC SUPPORTING TYPES
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Defines the rate limiting policy for a specific domain.
/// </summary>
/// <param name="MaxRequestsPerMinute">Maximum number of requests allowed in a 1-minute sliding window.</param>
/// <param name="MinDelayMs">Minimum delay in milliseconds between consecutive requests.</param>
/// <param name="RespectRobotsTxt">Whether to fetch and respect robots.txt Disallow rules.</param>
public record RateLimitPolicy(int MaxRequestsPerMinute, int MinDelayMs, bool RespectRobotsTxt);

/// <summary>
/// Tracks request timestamps and counters for a single domain within the sliding window.
/// </summary>
public sealed class RequestLog
{
    /// <summary>
    /// Timestamps of requests within the current 1-minute sliding window.
    /// </summary>
    public Queue<DateTimeOffset> RequestTimes { get; } = new();

    /// <summary>
    /// Total number of requests ever made to this domain.
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// Number of requests that were blocked due to rate limiting.
    /// </summary>
    public int BlockedRequests { get; set; }
}

/// <summary>
/// Represents a single entry in the scraping compliance audit trail.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>
    /// When the action occurred (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// The domain the action was associated with.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// The full URL that was requested (if available).
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The compliance decision: "Allowed", "Blocked", or "RateLimited".
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable explanation for the decision.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {Action} {Domain}{(string.IsNullOrEmpty(Url) ? "" : $" ({Url})")}: {Reason}";
}
