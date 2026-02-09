using CareerIntel.Core.Enums;

namespace CareerIntel.Core.Models;

/// <summary>
/// A point-in-time snapshot of the job market for a given platform or aggregate.
/// </summary>
public sealed class MarketSnapshot
{
    /// <summary>
    /// Date this snapshot was taken.
    /// </summary>
    public DateTimeOffset Date { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Total number of vacancies included in this snapshot.
    /// </summary>
    public int TotalVacancies { get; set; }

    /// <summary>
    /// Platform this snapshot is for, or "aggregate" for all platforms.
    /// </summary>
    public string Platform { get; set; } = "aggregate";

    /// <summary>
    /// How often each skill appears across all vacancies.
    /// </summary>
    public Dictionary<string, int> SkillFrequency { get; set; } = new();

    /// <summary>
    /// Average salary by seniority level (in USD equivalent).
    /// </summary>
    public Dictionary<SeniorityLevel, decimal> AverageSalaryByLevel { get; set; } = new();

    /// <summary>
    /// Most common skill combinations found in vacancies.
    /// </summary>
    public List<SkillCombination> TopSkillCombinations { get; set; } = [];

    /// <summary>
    /// Distribution of remote policies across vacancies.
    /// </summary>
    public Dictionary<RemotePolicy, int> RemotePolicyDistribution { get; set; } = new();
}

/// <summary>
/// Represents a combination of skills that frequently appear together.
/// </summary>
public sealed class SkillCombination
{
    public List<string> Skills { get; set; } = [];

    /// <summary>
    /// Number of vacancies containing this exact combination.
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// Average salary for vacancies requiring this combination.
    /// </summary>
    public decimal AverageSalary { get; set; }

    public override string ToString() =>
        $"[{string.Join(" + ", Skills)}] x{Frequency} (avg ${AverageSalary:N0})";
}
