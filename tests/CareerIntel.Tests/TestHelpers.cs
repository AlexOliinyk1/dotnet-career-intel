using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;

namespace CareerIntel.Tests;

/// <summary>
/// Factory methods for creating test instances of domain models with sensible defaults.
/// </summary>
internal static class TestHelpers
{
    internal static JobVacancy CreateVacancy(
        string title = "Senior .NET Developer",
        string company = "TestCorp",
        List<string>? requiredSkills = null,
        List<string>? preferredSkills = null,
        SeniorityLevel seniority = SeniorityLevel.Senior,
        RemotePolicy remote = RemotePolicy.FullyRemote,
        EngagementType engagement = EngagementType.ContractB2B,
        decimal? salaryMin = null,
        decimal? salaryMax = null,
        string platform = "djinni",
        DateTimeOffset? postedDate = null,
        List<string>? geoRestrictions = null,
        string city = "",
        string country = "")
    {
        return new JobVacancy
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Company = company,
            RequiredSkills = requiredSkills ?? [],
            PreferredSkills = preferredSkills ?? [],
            SeniorityLevel = seniority,
            RemotePolicy = remote,
            EngagementType = engagement,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            SourcePlatform = platform,
            PostedDate = postedDate ?? DateTimeOffset.UtcNow.AddDays(-1),
            GeoRestrictions = geoRestrictions ?? [],
            City = city,
            Country = country
        };
    }

    internal static UserProfile CreateProfile(
        List<SkillProfile>? skills = null,
        SeniorityLevel minSeniority = SeniorityLevel.Senior,
        decimal minSalary = 60000,
        decimal targetSalary = 90000,
        bool remoteOnly = true,
        List<Experience>? experiences = null)
    {
        return new UserProfile
        {
            Personal = new PersonalInfo
            {
                Name = "Test User",
                Location = "Kyiv, Ukraine",
                Title = "Senior .NET Developer",
                Summary = "Experienced .NET developer"
            },
            Skills = skills ?? [],
            Experiences = experiences ?? [],
            Preferences = new Preferences
            {
                MinSalaryUsd = minSalary,
                TargetSalaryUsd = targetSalary,
                RemoteOnly = remoteOnly,
                MinSeniority = minSeniority
            }
        };
    }

    internal static SkillProfile CreateSkill(
        string name,
        int proficiency = 4,
        double years = 5,
        SkillCategory category = SkillCategory.CoreDotNet)
    {
        return new SkillProfile
        {
            SkillName = name,
            ProficiencyLevel = proficiency,
            YearsOfExperience = years,
            Category = category,
            LastUsedDate = DateTimeOffset.UtcNow
        };
    }

    internal static Experience CreateExperience(
        string company = "PreviousCorp",
        string role = "Senior .NET Developer",
        List<string>? techStack = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null)
    {
        return new Experience
        {
            Company = company,
            Role = role,
            TechStack = techStack ?? [],
            StartDate = startDate ?? DateTimeOffset.UtcNow.AddYears(-5),
            EndDate = endDate,
            Duration = "5 years"
        };
    }
}
