using System.Text.Json.Serialization;
using CareerIntel.Core.Enums;

namespace CareerIntel.Core.Models;

/// <summary>
/// Represents a scraped job vacancy from any supported platform.
/// </summary>
public sealed class JobVacancy
{
    /// <summary>
    /// Unique identifier composed of source platform + original ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public RemotePolicy RemotePolicy { get; set; } = RemotePolicy.Unknown;

    public EngagementType EngagementType { get; set; } = EngagementType.Unknown;

    /// <summary>
    /// Detected geographic restrictions (e.g. "UK-only", "EU-only", "US-only").
    /// Empty list means no restrictions detected.
    /// </summary>
    public List<string> GeoRestrictions { get; set; } = [];

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    public string SalaryCurrency { get; set; } = "USD";

    public SeniorityLevel SeniorityLevel { get; set; } = SeniorityLevel.Unknown;

    /// <summary>
    /// Skills explicitly listed as required in the vacancy.
    /// </summary>
    public List<string> RequiredSkills { get; set; } = [];

    /// <summary>
    /// Skills listed as nice-to-have or preferred.
    /// </summary>
    public List<string> PreferredSkills { get; set; } = [];

    /// <summary>
    /// Full description text of the vacancy.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Original URL of the vacancy posting.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Platform the vacancy was scraped from (e.g., "djinni", "dou", "linkedin").
    /// </summary>
    public string SourcePlatform { get; set; } = string.Empty;

    public DateTimeOffset PostedDate { get; set; }

    /// <summary>
    /// Date when the vacancy expires or closes for applications.
    /// Null if expiration date is not specified.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset ScrapedDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Structured breakdown of the description into responsibilities, requirements, benefits, etc.
    /// Null until parsing is performed via JobDescriptionParser.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JobDescriptionBreakdown? Breakdown { get; set; }

    /// <summary>
    /// Computed match score against the user's profile. Null until matching is performed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MatchScore? MatchScore { get; set; }

    public override string ToString() =>
        $"[{SourcePlatform}] {Title} at {Company} ({SeniorityLevel}, {RemotePolicy})";
}
