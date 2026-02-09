using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Xunit;

namespace CareerIntel.Tests;

/// <summary>
/// Unit tests for <see cref="ScoringEngine"/> covering weighted scoring,
/// skill matching, salary alignment, remote policy, and confidence calculation.
/// </summary>
public sealed class ScoringEngineTests
{
    private readonly ScoringEngine _engine = new();

    [Fact]
    public void Score_PerfectMatch_ReturnsHighScore()
    {
        // Arrange — all required skills match, seniority exact, salary in range, fully remote
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET", "Azure", "SQL"],
            preferredSkills: ["Docker", "Kubernetes"],
            seniority: SeniorityLevel.Senior,
            remote: RemotePolicy.FullyRemote,
            salaryMin: 80000,
            salaryMax: 100000);

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
            remoteOnly: true);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — perfect match across all dimensions should yield >= 80
        Assert.True(result.OverallScore >= 80,
            $"Expected OverallScore >= 80 for a perfect match, but got {result.OverallScore}");
    }

    [Fact]
    public void Score_NoSkillMatch_ReturnsLowScore()
    {
        // Arrange — zero skill overlap, salary below target, seniority mismatch, on-site
        // to ensure all dimensions score poorly, not just skills
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["Java", "Spring", "Kafka", "Cassandra"],
            preferredSkills: ["Scala", "Flink"],
            seniority: SeniorityLevel.Intern,
            remote: RemotePolicy.OnSite,
            salaryMin: 20000,
            salaryMax: 30000);

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
            remoteOnly: true);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — no skill overlap combined with mismatches on every dimension
        Assert.Equal(0, result.MatchingSkills.Count);
        Assert.True(result.OverallScore < 40,
            $"Expected OverallScore < 40 with no skill match and poor alignment, but got {result.OverallScore}");
    }

    [Fact]
    public void Score_MissingSkills_IdentifiedCorrectly()
    {
        // Arrange — user has C# and SQL but missing Azure and Docker
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "Azure", "SQL", "Docker"]);

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("SQL", 4, 6)
            ]);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — Azure and Docker should be in MissingSkills
        Assert.Contains("Azure", result.MissingSkills);
        Assert.Contains("Docker", result.MissingSkills);
        Assert.Equal(2, result.MissingSkills.Count);
    }

    [Fact]
    public void Score_BonusSkills_IdentifiedCorrectly()
    {
        // Arrange — user has preferred skills listed
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#"],
            preferredSkills: ["Docker", "Kubernetes", "Terraform"]);

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("Docker", 4, 4),
                TestHelpers.CreateSkill("Kubernetes", 3, 2)
            ]);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — matched preferred skills appear in BonusSkills
        Assert.Contains("Docker", result.BonusSkills);
        Assert.Contains("Kubernetes", result.BonusSkills);
        Assert.DoesNotContain("Terraform", result.BonusSkills);
    }

    [Fact]
    public void Score_Apply_WhenHighScore()
    {
        // Arrange — strong match to trigger Apply recommendation (overall >= 75)
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#", "ASP.NET", "Azure"],
            preferredSkills: ["Docker"],
            seniority: SeniorityLevel.Senior,
            remote: RemotePolicy.FullyRemote,
            salaryMin: 90000,
            salaryMax: 120000);

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5),
                TestHelpers.CreateSkill("Docker", 4, 4)
            ],
            minSeniority: SeniorityLevel.Senior,
            minSalary: 70000,
            targetSalary: 90000,
            remoteOnly: true);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert
        Assert.True(result.OverallScore >= 75,
            $"Expected OverallScore >= 75, but got {result.OverallScore}");
        Assert.Equal(RecommendedAction.Apply, result.RecommendedAction);
    }

    [Fact]
    public void Score_Skip_WhenLowScore()
    {
        // Arrange — very poor match: no skill overlap, wrong seniority, bad salary, on-site
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["Java", "Spring", "Kafka", "Cassandra", "Scala"],
            preferredSkills: ["Flink", "Spark"],
            seniority: SeniorityLevel.Lead,
            remote: RemotePolicy.OnSite,
            salaryMin: 30000,
            salaryMax: 40000);

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
            remoteOnly: true);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert
        Assert.True(result.OverallScore < 35,
            $"Expected OverallScore < 35 for a terrible match, but got {result.OverallScore}");
        Assert.Equal(RecommendedAction.Skip, result.RecommendedAction);
    }

    [Fact]
    public void Score_EmptyVacancy_ReturnsNeutral()
    {
        // Arrange — vacancy with no skills, no salary, unknown seniority
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: [],
            preferredSkills: [],
            seniority: SeniorityLevel.Unknown,
            remote: RemotePolicy.Unknown,
            salaryMin: null,
            salaryMax: null);

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5)
            ]);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — all dimensions return neutral ~50, overall should be around 50
        Assert.InRange(result.OverallScore, 40, 60);
    }

    [Fact]
    public void Score_SalaryAboveTarget_Returns100()
    {
        // Arrange — vacancy salary exceeds user target
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#"],
            salaryMin: 100000,
            salaryMax: 150000);

        var profile = TestHelpers.CreateProfile(
            skills: [TestHelpers.CreateSkill("C#", 5, 8)],
            minSalary: 60000,
            targetSalary: 90000);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — salary above target yields 100 for salary dimension
        Assert.Equal(100.0, result.SalaryMatchScore);
    }

    [Fact]
    public void Score_RemoteOnly_OnSite_Returns0()
    {
        // Arrange — user wants remote only, vacancy is on-site
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: ["C#"],
            remote: RemotePolicy.OnSite);

        var profile = TestHelpers.CreateProfile(
            skills: [TestHelpers.CreateSkill("C#", 5, 8)],
            remoteOnly: true);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — remote-only user + on-site vacancy = 0 for remote dimension
        Assert.Equal(0.0, result.RemoteMatchScore);
    }

    [Fact]
    public void Score_Confidence_LowerForIncompleteData()
    {
        // Arrange — vacancy with missing data: no skills, no salary, unknown seniority, unknown remote
        var incompleteVacancy = TestHelpers.CreateVacancy(
            requiredSkills: [],
            preferredSkills: [],
            seniority: SeniorityLevel.Unknown,
            remote: RemotePolicy.Unknown,
            salaryMin: null,
            salaryMax: null);

        // Profile with few skills (< 3)
        var sparseProfile = TestHelpers.CreateProfile(
            skills: [TestHelpers.CreateSkill("C#", 5, 8)]);

        // Act
        var result = _engine.Score(incompleteVacancy, sparseProfile);

        // Assert — incomplete data should reduce confidence below 1.0
        Assert.True(result.Confidence < 1.0,
            $"Expected Confidence < 1.0 for incomplete data, but got {result.Confidence}");
        Assert.True(result.Confidence >= 0.1,
            $"Expected Confidence >= 0.1, but got {result.Confidence}");
    }

    [Theory]
    [InlineData(SeniorityLevel.Senior, SeniorityLevel.Senior, 100.0)]
    [InlineData(SeniorityLevel.Senior, SeniorityLevel.Lead, 75.0)]
    [InlineData(SeniorityLevel.Senior, SeniorityLevel.Architect, 40.0)]
    [InlineData(SeniorityLevel.Senior, SeniorityLevel.Junior, 40.0)]
    [InlineData(SeniorityLevel.Unknown, SeniorityLevel.Senior, 50.0)]
    public void Score_SeniorityAlignment_MatchesExpected(
        SeniorityLevel vacancyLevel,
        SeniorityLevel userLevel,
        double expectedScore)
    {
        // Arrange — isolate seniority dimension by using a vacancy with no skills/salary
        // so other dimensions return neutral, and we can inspect SeniorityMatchScore directly.
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: [],
            preferredSkills: [],
            seniority: vacancyLevel,
            salaryMin: null,
            salaryMax: null);

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5)
            ],
            minSeniority: userLevel);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — check the SeniorityMatchScore dimension directly
        Assert.Equal(expectedScore, result.SeniorityMatchScore);
    }

    [Theory]
    [InlineData(RemotePolicy.FullyRemote, true, 100.0)]
    [InlineData(RemotePolicy.RemoteFriendly, true, 80.0)]
    [InlineData(RemotePolicy.Hybrid, true, 40.0)]
    [InlineData(RemotePolicy.OnSite, true, 0.0)]
    [InlineData(RemotePolicy.OnSite, false, 80.0)]
    public void Score_RemoteAlignment_MatchesExpected(
        RemotePolicy vacancyPolicy,
        bool userRemoteOnly,
        double expectedScore)
    {
        // Arrange — isolate remote dimension
        var vacancy = TestHelpers.CreateVacancy(
            requiredSkills: [],
            preferredSkills: [],
            remote: vacancyPolicy,
            salaryMin: null,
            salaryMax: null);

        var profile = TestHelpers.CreateProfile(
            skills:
            [
                TestHelpers.CreateSkill("C#", 5, 8),
                TestHelpers.CreateSkill("ASP.NET", 5, 7),
                TestHelpers.CreateSkill("Azure", 4, 5)
            ],
            remoteOnly: userRemoteOnly);

        // Act
        var result = _engine.Score(vacancy, profile);

        // Assert — check the RemoteMatchScore dimension directly
        Assert.Equal(expectedScore, result.RemoteMatchScore);
    }
}
