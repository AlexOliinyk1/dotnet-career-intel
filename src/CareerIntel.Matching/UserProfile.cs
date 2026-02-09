using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;

namespace CareerIntel.Matching;

public sealed class UserProfile
{
    public PersonalInfo Personal { get; set; } = new();
    public List<SkillProfile> Skills { get; set; } = [];
    public List<Experience> Experiences { get; set; } = [];
    public Preferences Preferences { get; set; } = new();
}

public sealed class PersonalInfo
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> TargetRoles { get; set; } = [];
    public string LinkedInUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class Experience
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public List<string> TechStack { get; set; } = [];
    public List<string> Achievements { get; set; } = [];
    public string Description { get; set; } = string.Empty;
}

public sealed class Preferences
{
    public decimal MinSalaryUsd { get; set; }
    public decimal TargetSalaryUsd { get; set; }
    public bool RemoteOnly { get; set; } = true;
    public List<string> TargetRegions { get; set; } = ["Ukraine", "EU", "US"];
    public SeniorityLevel MinSeniority { get; set; } = SeniorityLevel.Senior;
    public List<string> ExcludeCompanies { get; set; } = [];
}
