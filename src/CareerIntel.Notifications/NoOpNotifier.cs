using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;

namespace CareerIntel.Notifications;

/// <summary>
/// No-op notification service used when no notification channels are configured.
/// </summary>
public sealed class NoOpNotifier : INotificationService
{
    public string ChannelName => "None";

    public Task NotifyMatchesAsync(IReadOnlyList<JobVacancy> matches, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task NotifySnapshotAsync(MarketSnapshot snapshot, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
