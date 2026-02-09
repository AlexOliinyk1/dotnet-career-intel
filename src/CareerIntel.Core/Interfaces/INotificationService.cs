using CareerIntel.Core.Models;

namespace CareerIntel.Core.Interfaces;

/// <summary>
/// Contract for sending notifications about job matches and market updates.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Human-readable name of the notification channel (e.g., "Telegram", "Email").
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Sends a notification about new high-score vacancy matches.
    /// </summary>
    /// <param name="matches">Matched vacancies with scores.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyMatchesAsync(
        IReadOnlyList<JobVacancy> matches,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a market snapshot summary.
    /// </summary>
    /// <param name="snapshot">Market snapshot to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifySnapshotAsync(
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to the notification channel.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the channel is reachable.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
