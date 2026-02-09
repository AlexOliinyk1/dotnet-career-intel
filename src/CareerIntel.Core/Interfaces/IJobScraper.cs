using CareerIntel.Core.Models;

namespace CareerIntel.Core.Interfaces;

/// <summary>
/// Contract for job board scrapers. Each platform implements this interface.
/// </summary>
public interface IJobScraper
{
    /// <summary>
    /// Human-readable name of the platform (e.g., "Djinni", "DOU", "LinkedIn").
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Scrapes job vacancies from the platform with optional filters.
    /// </summary>
    /// <param name="keywords">Search keywords (e.g., ".NET", "C#").</param>
    /// <param name="maxPages">Maximum number of pages to scrape.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of scraped job vacancies.</returns>
    Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrapes a single vacancy detail page for full information.
    /// </summary>
    /// <param name="url">URL of the vacancy detail page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Fully populated job vacancy.</returns>
    Task<JobVacancy?> ScrapeDetailAsync(
        string url,
        CancellationToken cancellationToken = default);
}
