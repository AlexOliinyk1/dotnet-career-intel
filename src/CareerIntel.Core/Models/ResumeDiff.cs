namespace CareerIntel.Core.Models;

/// <summary>
/// Tracks all changes made during resume tailoring for a specific vacancy.
/// Enables human review and trust building.
/// </summary>
public sealed class ResumeDiff
{
    public string VacancyId { get; set; } = string.Empty;
    public string VacancyTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public DateTimeOffset GeneratedDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Individual changes made to the resume.</summary>
    public List<ResumeChange> Changes { get; set; } = [];

    /// <summary>Sections that were reordered for relevance.</summary>
    public List<string> ReorderedSections { get; set; } = [];

    /// <summary>Keywords injected for ATS optimization.</summary>
    public List<string> AtsKeywordsInjected { get; set; } = [];

    /// <summary>Keywords already present in the base resume.</summary>
    public List<string> AtsKeywordsExisting { get; set; } = [];

    /// <summary>Final keyword density percentage.</summary>
    public double KeywordDensityPercent { get; set; }

    /// <summary>Skills from the vacancy that the resume highlights.</summary>
    public List<string> HighlightedSkills { get; set; } = [];

    /// <summary>Skills from the vacancy NOT covered in the resume.</summary>
    public List<string> UncoveredSkills { get; set; } = [];
}

/// <summary>
/// A single tracked change in the resume tailoring process.
/// </summary>
public sealed class ResumeChange
{
    /// <summary>Section of the resume that was modified.</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Type of change made.</summary>
    public string ChangeType { get; set; } = string.Empty; // "KeywordInjection", "SectionReorder", "ExperienceEmphasis", "SkillHighlight", "TitleAlignment"

    /// <summary>Why this change was made.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>What was changed (brief description).</summary>
    public string Description { get; set; } = string.Empty;
}
