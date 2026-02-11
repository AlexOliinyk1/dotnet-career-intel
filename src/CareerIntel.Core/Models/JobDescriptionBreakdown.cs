namespace CareerIntel.Core.Models;

/// <summary>
/// Structured breakdown of a job description into standard sections.
/// Parsed from raw description text so you can quickly compare positions.
/// </summary>
public sealed class JobDescriptionBreakdown
{
    /// <summary>Short company/role intro paragraph.</summary>
    public string AboutCompany { get; set; } = string.Empty;

    /// <summary>What you'll actually be doing day-to-day.</summary>
    public List<string> Responsibilities { get; set; } = [];

    /// <summary>Must-have requirements (hard skills, experience, education).</summary>
    public List<string> Requirements { get; set; } = [];

    /// <summary>Nice-to-have / bonus qualifications.</summary>
    public List<string> NiceToHave { get; set; } = [];

    /// <summary>Compensation, perks, culture — what they offer you.</summary>
    public List<string> Benefits { get; set; } = [];

    /// <summary>Tech stack / tools mentioned in the description.</summary>
    public List<string> TechStack { get; set; } = [];

    /// <summary>Interview process if mentioned.</summary>
    public string InterviewProcess { get; set; } = string.Empty;

    /// <summary>Anything that didn't fit into the above sections.</summary>
    public string Other { get; set; } = string.Empty;

    /// <summary>True if the parser successfully extracted at least one section.</summary>
    public bool HasStructuredData =>
        Responsibilities.Count > 0 || Requirements.Count > 0 ||
        Benefits.Count > 0 || NiceToHave.Count > 0;
}
