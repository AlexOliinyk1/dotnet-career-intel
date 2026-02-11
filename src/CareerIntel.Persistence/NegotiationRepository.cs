using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerIntel.Persistence;

/// <summary>
/// Repository for managing offer negotiation states with status tracking.
/// </summary>
public sealed class NegotiationRepository(CareerIntelDbContext db)
{
    /// <summary>
    /// Saves a new negotiation state to the database.
    /// </summary>
    public async Task SaveAsync(NegotiationState state, CancellationToken ct = default)
    {
        db.NegotiationStates.Add(state);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves all active negotiations (status is "Pending" or "Negotiating").
    /// </summary>
    public async Task<List<NegotiationState>> GetActiveAsync(CancellationToken ct = default)
    {
        // SQLite cannot ORDER BY DateTimeOffset — sort client-side
        var active = await db.NegotiationStates
            .Where(n => n.Status == "Pending" || n.Status == "Negotiating")
            .ToListAsync(ct);
        return active.OrderByDescending(n => n.ReceivedDate).ToList();
    }

    /// <summary>
    /// Retrieves a single negotiation state by its ID.
    /// </summary>
    public async Task<NegotiationState?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await db.NegotiationStates.FindAsync([id], ct);
    }

    /// <summary>
    /// Updates the status of an existing negotiation.
    /// Valid statuses: "Pending", "Negotiating", "Accepted", "Rejected", "Expired".
    /// </summary>
    public async Task UpdateStatusAsync(int id, string status, CancellationToken ct = default)
    {
        var negotiation = await db.NegotiationStates.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Negotiation with ID {id} not found.");

        negotiation.Status = status;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves all negotiations regardless of status for historical analysis.
    /// </summary>
    public async Task<List<NegotiationState>> GetAllAsync(CancellationToken ct = default)
    {
        // SQLite cannot ORDER BY DateTimeOffset — sort client-side
        var results = await db.NegotiationStates.ToListAsync(ct);
        return results.OrderByDescending(n => n.ReceivedDate).ToList();
    }

    /// <summary>
    /// Retrieves negotiations filtered by outcome status.
    /// </summary>
    public async Task<List<NegotiationState>> GetByStatusAsync(string status, CancellationToken ct = default)
    {
        // SQLite cannot ORDER BY DateTimeOffset — sort client-side
        var results = await db.NegotiationStates
            .Where(n => n.Status == status)
            .ToListAsync(ct);
        return results.OrderByDescending(n => n.ReceivedDate).ToList();
    }
}
