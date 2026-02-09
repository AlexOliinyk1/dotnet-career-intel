namespace CareerIntel.Core.Models;

/// <summary>
/// Tracks changes detected in a vacancy between successive scrapes.
/// </summary>
public sealed class VacancyChange
{
    public int Id { get; set; }
    public string VacancyId { get; set; } = string.Empty;
    public DateTimeOffset DetectedDate { get; set; } = DateTimeOffset.UtcNow;
    public string ChangeType { get; set; } = string.Empty; // "SkillsAdded", "SkillsRemoved", "SeniorityChanged", "SalaryChanged", "Reposted"
    public string FieldName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    /// <summary>Content hash of the vacancy at detection time.</summary>
    public string ContentHash { get; set; } = string.Empty;
}
