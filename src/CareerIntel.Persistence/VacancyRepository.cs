using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CareerIntel.Persistence;

/// <summary>
/// Repository for persisting and querying job vacancies with support for
/// trend analysis, salary tracking, and market volume metrics.
/// </summary>
public sealed class VacancyRepository(CareerIntelDbContext db)
{
    /// <summary>
    /// Saves vacancies using upsert semantics — existing records (by Id) are updated,
    /// new records are inserted.
    /// </summary>
    public async Task SaveVacanciesAsync(IReadOnlyList<JobVacancy> vacancies, CancellationToken ct = default)
    {
        if (vacancies.Count == 0)
            return;

        var existingIds = await db.Vacancies
            .Where(v => vacancies.Select(x => x.Id).Contains(v.Id))
            .Select(v => v.Id)
            .ToHashSetAsync(ct);

        foreach (var vacancy in vacancies)
        {
            if (existingIds.Contains(vacancy.Id))
            {
                db.Vacancies.Update(vacancy);
            }
            else
            {
                db.Vacancies.Add(vacancy);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retrieves vacancies with optional filters on platform, minimum seniority, and date range.
    /// </summary>
    public async Task<List<JobVacancy>> GetVacanciesAsync(
        string? platform = null,
        SeniorityLevel? minSeniority = null,
        DateTimeOffset? since = null,
        CancellationToken ct = default)
    {
        var query = db.Vacancies.AsQueryable();

        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(v => v.SourcePlatform == platform);

        if (minSeniority.HasValue)
            query = query.Where(v => v.SeniorityLevel >= minSeniority.Value);

        if (since.HasValue)
            query = query.Where(v => v.ScrapedDate >= since.Value);

        // SQLite cannot ORDER BY DateTimeOffset — materialize then sort client-side
        var results = await query.ToListAsync(ct);
        return results.OrderByDescending(v => v.ScrapedDate).ToList();
    }

    /// <summary>
    /// Computes skill frequency trends over time, grouped by month.
    /// Returns a dictionary mapping skill names to their monthly occurrence counts.
    /// </summary>
    public async Task<Dictionary<string, List<(DateTimeOffset Date, int Count)>>> GetSkillTrendsAsync(
        int monthsBack = 6, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-monthsBack);

        var vacancies = await db.Vacancies
            .Where(v => v.ScrapedDate >= cutoff)
            .Select(v => new { v.ScrapedDate, v.RequiredSkills, v.PreferredSkills })
            .ToListAsync(ct);

        var result = new Dictionary<string, List<(DateTimeOffset Date, int Count)>>();

        var grouped = vacancies
            .GroupBy(v => new DateTimeOffset(v.ScrapedDate.Year, v.ScrapedDate.Month, 1, 0, 0, 0, TimeSpan.Zero));

        foreach (var monthGroup in grouped.OrderBy(g => g.Key))
        {
            var allSkills = monthGroup
                .SelectMany(v => v.RequiredSkills.Concat(v.PreferredSkills))
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Skill = g.Key, Count = g.Count() });

            foreach (var skill in allSkills)
            {
                if (!result.ContainsKey(skill.Skill))
                    result[skill.Skill] = [];

                result[skill.Skill].Add((monthGroup.Key, skill.Count));
            }
        }

        return result;
    }

    /// <summary>
    /// Computes average salary trends by seniority level over time, grouped by month.
    /// Only includes vacancies with salary data.
    /// </summary>
    public async Task<Dictionary<SeniorityLevel, List<(DateTimeOffset Date, decimal AvgSalary)>>> GetSalaryTrendsAsync(
        int monthsBack = 6, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-monthsBack);

        var vacancies = await db.Vacancies
            .Where(v => v.ScrapedDate >= cutoff && v.SalaryMin.HasValue)
            .Select(v => new { v.ScrapedDate, v.SeniorityLevel, v.SalaryMin, v.SalaryMax })
            .ToListAsync(ct);

        var result = new Dictionary<SeniorityLevel, List<(DateTimeOffset Date, decimal AvgSalary)>>();

        var grouped = vacancies
            .GroupBy(v => new
            {
                Level = v.SeniorityLevel,
                Month = new DateTimeOffset(v.ScrapedDate.Year, v.ScrapedDate.Month, 1, 0, 0, 0, TimeSpan.Zero)
            })
            .OrderBy(g => g.Key.Month);

        foreach (var group in grouped)
        {
            var avgSalary = group.Average(v =>
                v.SalaryMax.HasValue
                    ? (v.SalaryMin!.Value + v.SalaryMax.Value) / 2
                    : v.SalaryMin!.Value);

            if (!result.ContainsKey(group.Key.Level))
                result[group.Key.Level] = [];

            result[group.Key.Level].Add((group.Key.Month, avgSalary));
        }

        return result;
    }

    public async Task SaveChangesAsync(IReadOnlyList<VacancyChange> changes, CancellationToken ct = default)
    {
        if (changes.Count == 0)
            return;

        db.VacancyChanges.AddRange(changes);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<VacancyChange>> GetRecentChangesAsync(int daysBack = 30, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysBack);

        // SQLite cannot ORDER BY DateTimeOffset — materialize then sort client-side
        var results = await db.VacancyChanges
            .Where(c => c.DetectedDate >= cutoff)
            .ToListAsync(ct);
        return results.OrderByDescending(c => c.DetectedDate).ToList();
    }

    /// <summary>
    /// Counts vacancy volume per week over the specified period for market temperature analysis.
    /// </summary>
    public async Task<List<(DateTimeOffset Date, int Count)>> GetVacancyVolumeAsync(
        int monthsBack = 6, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMonths(-monthsBack);

        var vacancies = await db.Vacancies
            .Where(v => v.ScrapedDate >= cutoff)
            .Select(v => v.ScrapedDate)
            .ToListAsync(ct);

        return vacancies
            .GroupBy(d =>
            {
                // Group by ISO week start (Monday)
                var dayOfWeek = ((int)d.DayOfWeek + 6) % 7; // Monday = 0
                return d.AddDays(-dayOfWeek).Date;
            })
            .OrderBy(g => g.Key)
            .Select(g => ((DateTimeOffset)new DateTimeOffset(g.Key, TimeSpan.Zero), g.Count()))
            .ToList();
    }
}
