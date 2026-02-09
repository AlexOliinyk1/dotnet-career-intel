using CareerIntel.Core.Enums;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Xunit;

namespace CareerIntel.Tests;

/// <summary>
/// Unit tests for <see cref="EligibilityGate"/> covering engagement type,
/// remote policy, geographic restrictions, filtering, and assessment rules.
/// </summary>
public sealed class EligibilityGateTests
{
    [Fact]
    public void IsEligible_RemoteB2B_ReturnsTrue()
    {
        // Arrange — FullyRemote + ContractB2B is the ideal eligible vacancy
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.FullyRemote,
            engagement: EngagementType.ContractB2B);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsEligible_Employment_ReturnsFalse()
    {
        // Arrange — Employment engagement is not eligible
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.FullyRemote,
            engagement: EngagementType.Employment);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEligible_InsideIR35_ReturnsFalse()
    {
        // Arrange — InsideIR35 is excluded
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.FullyRemote,
            engagement: EngagementType.InsideIR35);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEligible_OnSite_ReturnsFalse()
    {
        // Arrange — OnSite is not eligible
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.OnSite,
            engagement: EngagementType.ContractB2B);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEligible_Hybrid_ReturnsFalse()
    {
        // Arrange — Hybrid is not eligible
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.Hybrid,
            engagement: EngagementType.ContractB2B);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEligible_RemoteFriendly_Freelance_ReturnsTrue()
    {
        // Arrange — RemoteFriendly + Freelance is eligible
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.RemoteFriendly,
            engagement: EngagementType.Freelance);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsEligible_UnknownEngagement_RemoteUnknown_ReturnsTrue()
    {
        // Arrange — Unknown everything gives benefit of the doubt
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.Unknown,
            engagement: EngagementType.Unknown);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert — benefit of the doubt: unknown is not explicitly excluded
        Assert.True(result);
    }

    [Fact]
    public void IsEligible_GeoRestricted_UKOnly_ReturnsFalse()
    {
        // Arrange — geo restriction "UK-only" excludes Ukraine-based workers
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.FullyRemote,
            engagement: EngagementType.ContractB2B,
            geoRestrictions: ["UK-only"]);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEligible_GeoRestricted_WorkAuthRequired_ReturnsFalse()
    {
        // Arrange — "Work-Auth-Required" excludes Ukraine-based workers
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.FullyRemote,
            engagement: EngagementType.ContractB2B,
            geoRestrictions: ["Work-Auth-Required"]);

        // Act
        bool result = EligibilityGate.IsEligible(vacancy);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Filter_MixedList_ReturnsOnlyEligible()
    {
        // Arrange — 5 vacancies: 3 eligible, 2 ineligible
        var vacancies = new List<JobVacancy>
        {
            TestHelpers.CreateVacancy(remote: RemotePolicy.FullyRemote, engagement: EngagementType.ContractB2B),
            TestHelpers.CreateVacancy(remote: RemotePolicy.OnSite, engagement: EngagementType.ContractB2B),
            TestHelpers.CreateVacancy(remote: RemotePolicy.RemoteFriendly, engagement: EngagementType.Freelance),
            TestHelpers.CreateVacancy(remote: RemotePolicy.FullyRemote, engagement: EngagementType.Employment),
            TestHelpers.CreateVacancy(remote: RemotePolicy.FullyRemote, engagement: EngagementType.ContractB2B)
        };

        // Act
        var result = EligibilityGate.Filter(vacancies);

        // Assert — only the 3 eligible vacancies should pass
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Assess_Eligible_AllRulesPass()
    {
        // Arrange — fully eligible vacancy
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.FullyRemote,
            engagement: EngagementType.ContractB2B);

        // Act
        var assessment = EligibilityGate.Assess(vacancy);

        // Assert — all rules should pass
        Assert.True(assessment.IsEligible);
        Assert.All(assessment.Rules, rule => Assert.True(rule.Passed));
        Assert.Equal(3, assessment.Rules.Count);
    }

    [Fact]
    public void Assess_Ineligible_ShowsFailedRules()
    {
        // Arrange — OnSite + Employment = 2 rules fail
        var vacancy = TestHelpers.CreateVacancy(
            remote: RemotePolicy.OnSite,
            engagement: EngagementType.Employment);

        // Act
        var assessment = EligibilityGate.Assess(vacancy);

        // Assert — should be ineligible with 2 failed rules
        Assert.False(assessment.IsEligible);

        var failedRules = assessment.Rules.Where(r => !r.Passed).ToList();
        Assert.Equal(2, failedRules.Count);

        // Verify the two specific rules that failed
        Assert.Contains(failedRules, r => r.RuleName == "Engagement Type");
        Assert.Contains(failedRules, r => r.RuleName == "Remote Policy");

        // Verify each failed rule has a non-empty reason
        Assert.All(failedRules, r => Assert.False(string.IsNullOrWhiteSpace(r.Reason)));
    }
}
