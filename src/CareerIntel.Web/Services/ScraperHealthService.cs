using System.Collections.Concurrent;
using CareerIntel.Core.Interfaces;

namespace CareerIntel.Web.Services;

/// <summary>
/// Tracks scraper health status: success/failure rates, last run times, and staleness.
/// Singleton service — lives for the application lifetime.
/// </summary>
public sealed class ScraperHealthService
{
    private readonly ConcurrentDictionary<string, ScraperHealthRecord> _records = new();

    public void RecordSuccess(string platform, int vacanciesFound, TimeSpan duration)
    {
        _records.AddOrUpdate(platform,
            _ => new ScraperHealthRecord
            {
                Platform = platform,
                LastRunUtc = DateTimeOffset.UtcNow,
                LastSuccess = DateTimeOffset.UtcNow,
                TotalRuns = 1,
                SuccessCount = 1,
                LastVacancyCount = vacanciesFound,
                LastDuration = duration,
                Status = HealthStatus.Healthy
            },
            (_, existing) =>
            {
                existing.LastRunUtc = DateTimeOffset.UtcNow;
                existing.LastSuccess = DateTimeOffset.UtcNow;
                existing.TotalRuns++;
                existing.SuccessCount++;
                existing.LastVacancyCount = vacanciesFound;
                existing.LastDuration = duration;
                existing.LastError = null;
                existing.Status = HealthStatus.Healthy;
                return existing;
            });
    }

    public void RecordFailure(string platform, string errorMessage, TimeSpan duration)
    {
        _records.AddOrUpdate(platform,
            _ => new ScraperHealthRecord
            {
                Platform = platform,
                LastRunUtc = DateTimeOffset.UtcNow,
                TotalRuns = 1,
                FailureCount = 1,
                LastError = errorMessage,
                LastDuration = duration,
                Status = HealthStatus.Down
            },
            (_, existing) =>
            {
                existing.LastRunUtc = DateTimeOffset.UtcNow;
                existing.TotalRuns++;
                existing.FailureCount++;
                existing.LastError = errorMessage;
                existing.LastDuration = duration;
                // Mark as degraded if some successes, down if mostly failures
                var failRate = (double)existing.FailureCount / existing.TotalRuns;
                existing.Status = failRate > 0.5 ? HealthStatus.Down : HealthStatus.Degraded;
                return existing;
            });
    }

    public void RecordTimeout(string platform)
    {
        RecordFailure(platform, "Timed out (30s)", TimeSpan.FromSeconds(30));
    }

    public IReadOnlyList<ScraperHealthRecord> GetAllHealth()
    {
        PruneStale();
        return _records.Values
            .OrderBy(r => r.Platform)
            .ToList();
    }

    /// <summary>
    /// Removes records for scrapers that haven't run in over 7 days to prevent memory leak.
    /// </summary>
    private void PruneStale()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        foreach (var kvp in _records)
        {
            if (kvp.Value.LastRunUtc < cutoff)
                _records.TryRemove(kvp.Key, out _);
        }
    }

    public ScraperHealthRecord? GetHealth(string platform)
    {
        _records.TryGetValue(platform, out var record);
        return record;
    }

    public ScraperHealthSummary GetSummary()
    {
        var records = _records.Values.ToList();
        return new ScraperHealthSummary
        {
            TotalScrapers = records.Count,
            Healthy = records.Count(r => r.Status == HealthStatus.Healthy),
            Degraded = records.Count(r => r.Status == HealthStatus.Degraded),
            Down = records.Count(r => r.Status == HealthStatus.Down),
            Unknown = records.Count(r => r.Status == HealthStatus.Unknown),
            TotalSuccesses = records.Sum(r => r.SuccessCount),
            TotalFailures = records.Sum(r => r.FailureCount),
            LastScanUtc = records.Any() ? records.Max(r => r.LastRunUtc) : null
        };
    }
}

public sealed class ScraperHealthRecord
{
    public string Platform { get; set; } = "";
    public DateTimeOffset LastRunUtc { get; set; }
    public DateTimeOffset? LastSuccess { get; set; }
    public int TotalRuns { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int LastVacancyCount { get; set; }
    public TimeSpan LastDuration { get; set; }
    public string? LastError { get; set; }
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;

    public double SuccessRate => TotalRuns > 0 ? (double)SuccessCount / TotalRuns * 100 : 0;
    public bool IsStale => LastRunUtc < DateTimeOffset.UtcNow.AddHours(-24);
}

public sealed class ScraperHealthSummary
{
    public int TotalScrapers { get; set; }
    public int Healthy { get; set; }
    public int Degraded { get; set; }
    public int Down { get; set; }
    public int Unknown { get; set; }
    public int TotalSuccesses { get; set; }
    public int TotalFailures { get; set; }
    public DateTimeOffset? LastScanUtc { get; set; }
}

public enum HealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Down
}
