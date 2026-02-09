using System.Security.Cryptography;
using System.Text;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Analysis;

public sealed class DriftDetector(ILogger<DriftDetector> logger)
{
    public List<VacancyChange> DetectChanges(
        IReadOnlyList<JobVacancy> currentVacancies,
        IReadOnlyList<JobVacancy> previousVacancies)
    {
        logger.LogInformation(
            "Detecting drift between {CurrentCount} current and {PreviousCount} previous vacancies",
            currentVacancies.Count, previousVacancies.Count);

        var previousMap = previousVacancies.ToDictionary(v => v.Id);
        var changes = new List<VacancyChange>();

        foreach (var current in currentVacancies)
        {
            if (!previousMap.TryGetValue(current.Id, out var previous))
                continue;

            var currentHash = ComputeContentHash(current);
            var previousHash = ComputeContentHash(previous);

            if (currentHash == previousHash)
                continue;

            var now = DateTimeOffset.UtcNow;

            // Skills added
            var addedSkills = current.RequiredSkills
                .Except(previous.RequiredSkills, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (addedSkills.Count > 0)
            {
                changes.Add(new VacancyChange
                {
                    VacancyId = current.Id,
                    DetectedDate = now,
                    ChangeType = "SkillsAdded",
                    FieldName = "RequiredSkills",
                    OldValue = string.Join(", ", previous.RequiredSkills.Order()),
                    NewValue = string.Join(", ", addedSkills.Order()),
                    ContentHash = currentHash
                });
            }

            // Skills removed
            var removedSkills = previous.RequiredSkills
                .Except(current.RequiredSkills, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (removedSkills.Count > 0)
            {
                changes.Add(new VacancyChange
                {
                    VacancyId = current.Id,
                    DetectedDate = now,
                    ChangeType = "SkillsRemoved",
                    FieldName = "RequiredSkills",
                    OldValue = string.Join(", ", removedSkills.Order()),
                    NewValue = string.Join(", ", current.RequiredSkills.Order()),
                    ContentHash = currentHash
                });
            }

            // Seniority changed
            if (current.SeniorityLevel != previous.SeniorityLevel)
            {
                changes.Add(new VacancyChange
                {
                    VacancyId = current.Id,
                    DetectedDate = now,
                    ChangeType = "SeniorityChanged",
                    FieldName = "SeniorityLevel",
                    OldValue = previous.SeniorityLevel.ToString(),
                    NewValue = current.SeniorityLevel.ToString(),
                    ContentHash = currentHash
                });
            }

            // Salary changed
            if (current.SalaryMin != previous.SalaryMin || current.SalaryMax != previous.SalaryMax)
            {
                changes.Add(new VacancyChange
                {
                    VacancyId = current.Id,
                    DetectedDate = now,
                    ChangeType = "SalaryChanged",
                    FieldName = "Salary",
                    OldValue = FormatSalaryRange(previous.SalaryMin, previous.SalaryMax),
                    NewValue = FormatSalaryRange(current.SalaryMin, current.SalaryMax),
                    ContentHash = currentHash
                });
            }

            // Reposted — catch-all for any remaining hash difference
            var hasSpecificChange = addedSkills.Count > 0
                || removedSkills.Count > 0
                || current.SeniorityLevel != previous.SeniorityLevel
                || current.SalaryMin != previous.SalaryMin
                || current.SalaryMax != previous.SalaryMax;

            if (!hasSpecificChange)
            {
                changes.Add(new VacancyChange
                {
                    VacancyId = current.Id,
                    DetectedDate = now,
                    ChangeType = "Reposted",
                    FieldName = "Content",
                    OldValue = previousHash,
                    NewValue = currentHash,
                    ContentHash = currentHash
                });
            }
        }

        logger.LogInformation("Detected {ChangeCount} changes across vacancies", changes.Count);
        return changes;
    }

    public DriftSummary AnalyzeDrift(IReadOnlyList<VacancyChange> changes)
    {
        logger.LogInformation("Analyzing drift from {Count} changes", changes.Count);

        var summary = new DriftSummary
        {
            TotalChanges = changes.Count,
            VacanciesChanged = changes.Select(c => c.VacancyId).Distinct().Count(),
            AnalyzedDate = DateTimeOffset.UtcNow
        };

        // Rising skills: skills being added across multiple vacancies
        var skillsAdded = changes
            .Where(c => c.ChangeType == "SkillsAdded")
            .SelectMany(c => c.NewValue.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Skill: g.Key, AddedCount: g.Count()))
            .OrderByDescending(x => x.AddedCount)
            .ToList();

        summary.RisingSkills = skillsAdded;

        // Fading skills: skills being removed across multiple vacancies
        var skillsRemoved = changes
            .Where(c => c.ChangeType == "SkillsRemoved")
            .SelectMany(c => c.OldValue.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Skill: g.Key, RemovedCount: g.Count()))
            .OrderByDescending(x => x.RemovedCount)
            .ToList();

        summary.FadingSkills = skillsRemoved;

        // Seniority trend
        var seniorityChanges = changes
            .Where(c => c.ChangeType == "SeniorityChanged")
            .ToList();

        if (seniorityChanges.Count > 0)
        {
            var upCount = seniorityChanges.Count(c =>
                Enum.TryParse<CareerIntel.Core.Enums.SeniorityLevel>(c.NewValue, out var newLevel) &&
                Enum.TryParse<CareerIntel.Core.Enums.SeniorityLevel>(c.OldValue, out var oldLevel) &&
                newLevel > oldLevel);

            var downCount = seniorityChanges.Count - upCount;

            summary.SeniorityTrend = upCount > downCount ? "Rising"
                : downCount > upCount ? "Falling"
                : "Stable";
        }

        // Salary trend
        var salaryChanges = changes
            .Where(c => c.ChangeType == "SalaryChanged")
            .ToList();

        if (salaryChanges.Count > 0)
        {
            var upCount = 0;
            var downCount = 0;

            foreach (var change in salaryChanges)
            {
                var oldMid = ParseSalaryMidpoint(change.OldValue);
                var newMid = ParseSalaryMidpoint(change.NewValue);

                if (newMid > oldMid) upCount++;
                else if (newMid < oldMid) downCount++;
            }

            summary.SalaryTrend = upCount > downCount ? "Rising"
                : downCount > upCount ? "Falling"
                : "Stable";
        }

        return summary;
    }

    private static string ComputeContentHash(JobVacancy vacancy)
    {
        var builder = new StringBuilder();
        builder.Append(vacancy.Id);
        builder.Append('|');
        builder.Append(vacancy.Title);
        builder.Append('|');
        builder.Append(string.Join(",", vacancy.RequiredSkills.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        builder.Append('|');
        builder.Append(string.Join(",", vacancy.PreferredSkills.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        builder.Append('|');
        builder.Append(vacancy.SeniorityLevel);
        builder.Append('|');
        builder.Append(vacancy.SalaryMin);
        builder.Append('|');
        builder.Append(vacancy.SalaryMax);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FormatSalaryRange(decimal? min, decimal? max) =>
        (min, max) switch
        {
            (null, null) => "N/A",
            (not null, null) => $"{min}",
            (null, not null) => $"{max}",
            _ => $"{min}-{max}"
        };

    private static decimal ParseSalaryMidpoint(string salaryRange)
    {
        if (salaryRange == "N/A")
            return 0m;

        var parts = salaryRange.Split('-');
        if (parts.Length == 2
            && decimal.TryParse(parts[0], out var lo)
            && decimal.TryParse(parts[1], out var hi))
        {
            return (lo + hi) / 2m;
        }

        return decimal.TryParse(salaryRange, out var single) ? single : 0m;
    }
}
