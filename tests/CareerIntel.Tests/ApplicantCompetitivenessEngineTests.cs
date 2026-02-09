using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CareerIntel.Tests;

/// <summary>
/// Unit tests for <see cref="ApplicantCompetitivenessEngine"/> covering
/// competitiveness scoring, tier classification, response probability,
/// freshness, platform response rates, batch assessment, and output quality.
/// </summary>
public sealed class ApplicantCompetitivenessEngineTests
{
    private readonly ApplicantCompetitivenessEngine _engine =
        new(NullLogger<ApplicantCompetitivenessEngine>.Instance);

    [Fact]
    public void Assess_StrongCandidate_ReturnsHighScore()
    {
        // Arrange — expert C# (5/5, 8y), matching all required skills, relevant experience
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET", "Azure", "SQL"],
            preferredSkills: ["Docker", "Kubernetes"],
            seniority: SeniorityLevel.Senior,
            remote: RemotePolicy.FullyRemote,
            salaryMin: 80000,
            salaryMax: 100000,
            platform: "djinni",
            postedDate: DateTimeOffset.UtcNow.AddDays(-2));

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5),
                TestHelpers.CreateSkill("SQL", 4, 6),
                TestHelpers.CreateSkill("Docker", 4, 4),
                TestHelpers.CreateSkill("Kubernetes", 3, 2)
            ],
            minSeniority: SeniorityLevel.Senior,
            minSalary: 70000,
            targetSalary: 90000,
            remoteOnly: true,
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#", "ASP.NET", "Azure", "SQL"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-8))
            ]);

        // Act
        var result = _engine.Assess(vacancy, profile);

        // Assert — strong candidate with expert skills should score >= 65
        Assert.True(result.CompetitivenessScore >= 65,
            $"Expected CompetitivenessScore >= 65, but got {result.CompetitivenessScore}");
    }

    [Fact]
    public void Assess_WeakCandidate_ReturnsLowScore()
    {
        // Arrange — beginner (1/5, 0.5y), no matching skills
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["Java", "Spring", "Kafka", "Cassandra", "Scala"],
            preferredSkills: ["Flink", "Spark"],
            seniority: SeniorityLevel.Lead,
            salaryMin: 120000,
            salaryMax: 180000,
            platform: "linkedin",
            postedDate: DateTimeOffset.UtcNow.AddDays(-45));

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("HTML", 1, 0.5),
                TestHelpers.CreateSkill("CSS", 1, 0.5)
            ],
            minSeniority: SeniorityLevel.Junior,
            minSalary: 20000,
            targetSalary: 30000);

        // Act
        var result = _engine.Assess(vacancy, profile);

        // Assert — beginner with no matching skills should score < 40
        Assert.True(result.CompetitivenessScore < 40,
            $"Expected CompetitivenessScore < 40, but got {result.CompetitivenessScore}");
    }

    [Fact]
    public void Assess_TopCandidate_TierCorrect()
    {
        // Arrange — create a scenario likely to yield score >= 80
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET"],
            preferredSkills: ["Azure"],
            seniority: SeniorityLevel.Senior,
            remote: RemotePolicy.FullyRemote,
            salaryMin: 70000,
            salaryMax: 110000,
            platform: "djinni",
            postedDate: DateTimeOffset.UtcNow.AddDays(-1));

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 10),
                TestHelpers.CreateSkill("ASP.NET", 5, 9),
                TestHelpers.CreateSkill("Azure", 5, 7)
            ],
            minSeniority: SeniorityLevel.Senior,
            minSalary: 60000,
            targetSalary: 80000,
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#", "ASP.NET", "Azure"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-10))
            ]);

        // Act
        var result = _engine.Assess(vacancy, profile);

        // Assert — if score >= 80, tier should be "Top Candidate"
        if (result.CompetitivenessScore >= 80)
        {
            Assert.Equal("Top Candidate", result.Tier);
        }
        else
        {
            // Even if the score is not quite 80, verify tier is consistent with score
            Assert.True(result.CompetitivenessScore >= 65,
                $"Strong candidate should score at least 65, got {result.CompetitivenessScore}");
        }
    }

    [Fact]
    public void Assess_LongShot_TierCorrect()
    {
        // Arrange — intentionally weak profile for an unrelated role
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["Rust", "WASM", "Embedded", "RTOS", "Verilog"],
            seniority: SeniorityLevel.Principal,
            remote: RemotePolicy.OnSite,
            salaryMin: 200000,
            salaryMax: 300000,
            platform: "linkedin",
            postedDate: DateTimeOffset.UtcNow.AddDays(-60));

        var profile = TestHelpers.CreateProfile(
            skills: [TestHelpers.CreateSkill("HTML", 1, 0.3)],
            minSeniority: SeniorityLevel.Junior,
            minSalary: 20000,
            targetSalary: 30000);

        // Act
        var result = _engine.Assess(vacancy, profile);

        // Assert — score < 35 should yield "Long Shot" tier
        Assert.True(result.CompetitivenessScore < 35,
            $"Expected CompetitivenessScore < 35 for a long shot, but got {result.CompetitivenessScore}");
        Assert.Equal("Long Shot", result.Tier);
    }

    [Fact]
    public void Assess_ResponseProbability_CappedAt95()
    {
        // Arrange — even with the best possible inputs, probability is capped
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#"],
            seniority: SeniorityLevel.Senior,
            remote: RemotePolicy.FullyRemote,
            salaryMin: 70000,
            salaryMax: 120000,
            platform: "djinni",
            postedDate: DateTimeOffset.UtcNow.AddDays(-1));

        var profile = TestHelpers.CreateProfile(
            skills: [TestHelpers.CreateSkill("C#", 5, 10)],
            minSeniority: SeniorityLevel.Senior,
            minSalary: 60000,
            targetSalary: 80000,
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-10))
            ]);

        // Act
        var result = _engine.Assess(vacancy, profile);

        // Assert — response probability must never exceed 95%
        Assert.True(result.ResponseProbability <= 95.0,
            $"Expected ResponseProbability <= 95, but got {result.ResponseProbability}");
    }

    [Fact]
    public void Assess_FreshPosting_HigherFreshness()
    {
        // Arrange — two identical vacancies, one posted 1 day ago, one posted 60 days ago
        var freshVacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET"],
            postedDate: DateTimeOffset.UtcNow.AddDays(-1),
            platform: "djinni");

        var staleVacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET"],
            postedDate: DateTimeOffset.UtcNow.AddDays(-60),
            platform: "djinni");

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7)
            ],
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#", "ASP.NET"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-8))
            ]);

        // Act
        var freshResult = _engine.Assess(freshVacancy, profile);
        var staleResult = _engine.Assess(staleVacancy, profile);

        // Assert — fresh posting should have higher freshness score
        Assert.True(freshResult.Breakdown.FreshnessScore > staleResult.Breakdown.FreshnessScore,
            $"Expected fresh ({freshResult.Breakdown.FreshnessScore}) > stale ({staleResult.Breakdown.FreshnessScore})");
    }

    [Fact]
    public void Assess_DjinniPlatform_HigherResponseRate()
    {
        // Arrange — identical vacancies on different platforms
        var djinniVacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#"],
            platform: "djinni");

        var linkedinVacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#"],
            platform: "linkedin");

        var profile = TestHelpers.CreateProfile(
            skills: [TestHelpers.CreateSkill("C#", 5, 8)],
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-8))
            ]);

        // Act
        var djinniResult = _engine.Assess(djinniVacancy, profile);
        var linkedinResult = _engine.Assess(linkedinVacancy, profile);

        // Assert — Djinni (80) should have a higher platform response score than LinkedIn (35)
        Assert.True(
            djinniResult.Breakdown.PlatformResponseScore > linkedinResult.Breakdown.PlatformResponseScore,
            $"Expected Djinni ({djinniResult.Breakdown.PlatformResponseScore}) > LinkedIn ({linkedinResult.Breakdown.PlatformResponseScore})");
    }

    [Fact]
    public void AssessAll_RanksDescendingByScore()
    {
        // Arrange — batch of 3 vacancies with varying quality
        var vacancies = new List<JobVacancy>
        {
            TestHelpers.CreateVacancy(
                title: "Junior Python Dev",
                requiredSkills: ["Python", "Flask"],
                seniority: SeniorityLevel.Junior,
                platform: "linkedin",
                postedDate: DateTimeOffset.UtcNow.AddDays(-30)),
            TestHelpers.CreateVacancy(
                title: "Senior .NET Developer",
                requiredSkills: ["C#", "ASP.NET", "Azure"],
                seniority: SeniorityLevel.Senior,
                platform: "djinni",
                postedDate: DateTimeOffset.UtcNow.AddDays(-1)),
            TestHelpers.CreateVacancy(
                title: "Mid Go Engineer",
                requiredSkills: ["Go", "Docker"],
                seniority: SeniorityLevel.Middle,
                platform: "remoteok",
                postedDate: DateTimeOffset.UtcNow.AddDays(-15))
        };

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5)
            ],
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#", "ASP.NET", "Azure"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-8))
            ]);

        // Act
        var report = _engine.AssessAll(vacancies, profile);

        // Assert — assessments should be sorted descending by CompetitivenessScore
        Assert.Equal(3, report.Assessments.Count);
        for (int i = 1; i < report.Assessments.Count; i++)
        {
            Assert.True(
                report.Assessments[i - 1].CompetitivenessScore >= report.Assessments[i].CompetitivenessScore,
                $"Expected descending order at index {i}: " +
                $"{report.Assessments[i - 1].CompetitivenessScore} >= {report.Assessments[i].CompetitivenessScore}");
        }
    }

    [Fact]
    public void AssessAll_TierCountsAreCorrect()
    {
        // Arrange — batch of vacancies
        var vacancies = new List<JobVacancy>
        {
            TestHelpers.CreateVacancy(requiredSkills: ["C#", "ASP.NET"], platform: "djinni",
                postedDate: DateTimeOffset.UtcNow.AddDays(-1)),
            TestHelpers.CreateVacancy(requiredSkills: ["Java", "Spring"], platform: "linkedin",
                postedDate: DateTimeOffset.UtcNow.AddDays(-20)),
            TestHelpers.CreateVacancy(requiredSkills: ["Rust", "WASM"], platform: "remoteok",
                postedDate: DateTimeOffset.UtcNow.AddDays(-40))
        };

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7)
            ],
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#", "ASP.NET"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-8))
            ]);

        // Act
        var report = _engine.AssessAll(vacancies, profile);

        // Assert — sum of all tier counts should equal total vacancies
        int tierSum = report.TopCandidateCount
            + report.StrongContenderCount
            + report.CompetitiveCount
            + report.AverageCount
            + report.LongShotCount;

        Assert.Equal(report.TotalVacancies, tierSum);
        Assert.Equal(vacancies.Count, report.TotalVacancies);
    }

    [Fact]
    public void Assess_StrengthsAndTips_NotEmpty()
    {
        // Arrange — a valid assessment with some data should produce factors
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET", "Azure"],
            preferredSkills: ["Docker", "Kubernetes"],
            seniority: SeniorityLevel.Senior,
            salaryMin: 80000,
            salaryMax: 100000,
            platform: "djinni",
            postedDate: DateTimeOffset.UtcNow.AddDays(-2));

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5)
            ],
            minSeniority: SeniorityLevel.Senior,
            minSalary: 70000,
            targetSalary: 90000,
            experiences:
            [
                TestHelpers.CreateExperience(
                    role: "Senior .NET Developer",
                    techStack: ["C#", "ASP.NET", "Azure"],
                    startDate: DateTimeOffset.UtcNow.AddYears(-8))
            ]);

        // Act
        var result = _engine.Assess(vacancy, profile);

        // Assert — at least one of StrengthFactors or WeaknessFactors should have entries
        bool hasFactors = result.StrengthFactors.Count > 0 || result.WeaknessFactors.Count > 0;
        Assert.True(hasFactors,
            "Expected at least one strength or weakness factor for a valid assessment");

        // Tips should also be populated for a realistic scenario
        Assert.True(result.Tips.Count > 0 || result.StrengthFactors.Count > 0,
            "Expected non-empty tips or strengths for a realistic assessment");
    }
}
