using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerIntel.Persistence;

/// <summary>
/// Repository for persisting and querying LinkedIn recruiter proposals.
/// Replaces JSON file storage (linkedin-proposals.json) with SQLite via EF Core.
/// </summary>
public sealed class ProposalRepository(CareerIntelDbContext db)
{
    /// <summary>
    /// Saves a batch of proposals using upsert semantics — existing records (by Id) are updated,
    /// new records (Id == 0) are inserted.
    /// </summary>
    public async Task SaveAllAsync(IReadOnlyList<LinkedInProposal> proposals, CancellationToken ct = default)
    {
        if (proposals.Count == 0)
            return;

        foreach (var proposal in proposals)
        {
            if (proposal.Id == 0)
            {
                // Check for duplicate by ConversationId to avoid re-importing
                var existing = await db.Proposals
                    .FirstOrDefaultAsync(p => p.ConversationId == proposal.ConversationId, ct);

                if (existing is not null)
                {
                    // Update existing record with latest data
                    db.Entry(existing).CurrentValues.SetValues(proposal);
                    // Preserve the original Id
                    existing.Id = existing.Id;
                }
                else
                {
                    db.Proposals.Add(proposal);
                }
            }
            else
            {
                var existing = await db.Proposals.FindAsync([proposal.Id], ct);
                if (existing is null)
                {
                    db.Proposals.Add(proposal);
                }
                else
                {
                    db.Entry(existing).CurrentValues.SetValues(proposal);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves all LinkedIn proposals, ordered by proposal date descending.
    /// </summary>
    public async Task<List<LinkedInProposal>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Proposals
            .OrderByDescending(p => p.ProposalDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retrieves LinkedIn proposals filtered by status.
    /// </summary>
    public async Task<List<LinkedInProposal>> GetByStatusAsync(ProposalStatus status, CancellationToken ct = default)
    {
        return await db.Proposals
            .Where(p => p.Status == status)
            .OrderByDescending(p => p.ProposalDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Updates the status of an existing LinkedIn proposal.
    /// </summary>
    public async Task UpdateStatusAsync(int id, ProposalStatus status, CancellationToken ct = default)
    {
        var proposal = await db.Proposals.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Proposal with ID {id} not found.");

        proposal.Status = status;
        await db.SaveChangesAsync(ct);
    }
}
