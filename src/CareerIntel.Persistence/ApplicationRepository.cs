using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerIntel.Persistence;

/// <summary>
/// Repository for persisting and querying job applications.
/// Replaces JSON file storage (applications.json) with SQLite via EF Core.
/// </summary>
public sealed class ApplicationRepository(CareerIntelDbContext db)
{
    /// <summary>
    /// Saves a new job application or updates an existing one (matched by Id).
    /// New applications (Id == 0) are inserted; existing applications are updated.
    /// </summary>
    public async Task SaveAsync(JobApplication application, CancellationToken ct = default)
    {
        if (application.Id == 0)
        {
            db.Applications.Add(application);
        }
        else
        {
            var existing = await db.Applications.FindAsync([application.Id], ct);
            if (existing is null)
            {
                db.Applications.Add(application);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(application);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves all job applications, ordered by creation date descending.
    /// </summary>
    public async Task<List<JobApplication>> GetAllAsync(CancellationToken ct = default)
    {
        return await db.Applications
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retrieves job applications filtered by status.
    /// </summary>
    public async Task<List<JobApplication>> GetByStatusAsync(ApplicationStatus status, CancellationToken ct = default)
    {
        return await db.Applications
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Retrieves job applications for a specific company, case-insensitive.
    /// </summary>
    public async Task<List<JobApplication>> GetByCompanyAsync(string company, CancellationToken ct = default)
    {
        return await db.Applications
            .Where(a => EF.Functions.Like(a.Company, company))
            .OrderByDescending(a => a.CreatedDate)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Updates the status of an existing job application.
    /// Automatically sets ResponseDate when transitioning to a response-bearing status.
    /// </summary>
    public async Task UpdateStatusAsync(int id, ApplicationStatus status, CancellationToken ct = default)
    {
        var application = await db.Applications.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"Application with ID {id} not found.");

        application.Status = status;

        // Auto-set response date on first response-bearing status transition
        if (application.ResponseDate is null && status is ApplicationStatus.Screening
            or ApplicationStatus.Interview or ApplicationStatus.Offer
            or ApplicationStatus.Rejected)
        {
            application.ResponseDate = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
