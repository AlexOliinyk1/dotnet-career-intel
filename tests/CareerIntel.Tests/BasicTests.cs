using CareerIntel.Core.Models;
using CareerIntel.Core.Enums;
using Xunit;

namespace CareerIntel.Tests;

public class BasicTests
{
    [Fact]
    public void JobVacancy_Should_Initialize_With_Defaults()
    {
        // Arrange & Act
        var vacancy = new JobVacancy();

        // Assert
        Assert.NotNull(vacancy);
        Assert.Equal(string.Empty, vacancy.Title);
        Assert.Equal(string.Empty, vacancy.Company);
        Assert.Equal(RemotePolicy.Unknown, vacancy.RemotePolicy);
    }

    [Fact]
    public void JobVacancy_Should_Store_Complete_Information()
    {
        // Arrange & Act
        var vacancy = new JobVacancy
        {
            Id = "linkedin-12345",
            Title = "Senior .NET Developer",
            Company = "TechCorp",
            Description = "We need a senior developer with C# and Azure experience",
            Country = "USA",
            City = "Remote",
            RemotePolicy = RemotePolicy.FullyRemote,
            SalaryMin = 120000,
            SalaryMax = 160000,
            SalaryCurrency = "USD",
            SeniorityLevel = SeniorityLevel.Senior,
            RequiredSkills = new List<string> { "C#", ".NET", "Azure" },
            Url = "https://example.com/job/12345",
            SourcePlatform = "LinkedIn",
            PostedDate = DateTimeOffset.UtcNow.AddDays(-2),
            ScrapedDate = DateTimeOffset.UtcNow
        };

        // Assert
        Assert.Equal("Senior .NET Developer", vacancy.Title);
        Assert.Equal("TechCorp", vacancy.Company);
        Assert.Equal(RemotePolicy.FullyRemote, vacancy.RemotePolicy);
        Assert.Equal(120000m, vacancy.SalaryMin);
        Assert.Equal(160000m, vacancy.SalaryMax);
        Assert.Contains("C#", vacancy.RequiredSkills);
        Assert.Contains(".NET", vacancy.RequiredSkills);
    }

    [Fact]
    public void LinkedInProposal_Should_Initialize_With_Defaults()
    {
        // Arrange & Act
        var proposal = new LinkedInProposal();

        // Assert
        Assert.NotNull(proposal);
        Assert.Equal(string.Empty, proposal.RecruiterName);
        Assert.Equal(string.Empty, proposal.Company);
        Assert.Equal(ProposalStatus.New, proposal.Status);
    }

    [Fact]
    public void LinkedInProposal_Should_Store_Recruiter_Information()
    {
        // Arrange & Act
        var proposal = new LinkedInProposal
        {
            RecruiterName = "John Recruiter",
            Company = "TechCorp",
            JobTitle = "Senior C# Developer",
            TechStack = "C#, .NET, Azure, React",
            RemotePolicy = "Remote",
            Location = "USA",
            SalaryHint = "$120k-$160k",
            MessageSummary = "Great opportunity for senior developer",
            ProposalDate = DateTimeOffset.UtcNow.AddDays(-5),
            Status = ProposalStatus.New
        };

        // Assert
        Assert.Equal("John Recruiter", proposal.RecruiterName);
        Assert.Equal("TechCorp", proposal.Company);
        Assert.Equal("Remote", proposal.RemotePolicy);
        Assert.Contains("C#", proposal.TechStack);
    }

    [Theory]
    [InlineData("Senior .NET Developer", ".NET", true)]
    [InlineData("Python Developer", ".NET", false)]
    [InlineData("Full Stack C# Engineer", "C#", true)]
    [InlineData("Java Backend Developer", "C#", false)]
    public void JobVacancy_Title_Should_Match_Keywords(string title, string keyword, bool shouldMatch)
    {
        // Arrange
        var vacancy = new JobVacancy
        {
            Title = title,
            Description = $"We are looking for a {title}"
        };

        // Act
        bool matches = vacancy.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                      vacancy.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        // Assert
        Assert.Equal(shouldMatch, matches);
    }

    [Fact]
    public void RemotePolicy_Enum_Should_Have_All_Values()
    {
        // Arrange & Act
        var values = Enum.GetValues<RemotePolicy>();

        // Assert
        Assert.Contains(RemotePolicy.FullyRemote, values);
        Assert.Contains(RemotePolicy.Hybrid, values);
        Assert.Contains(RemotePolicy.OnSite, values);
        Assert.Contains(RemotePolicy.Unknown, values);
    }

    [Fact]
    public void SeniorityLevel_Enum_Should_Have_All_Values()
    {
        // Arrange & Act
        var values = Enum.GetValues<SeniorityLevel>();

        // Assert
        Assert.Contains(SeniorityLevel.Junior, values);
        Assert.Contains(SeniorityLevel.Middle, values);
        Assert.Contains(SeniorityLevel.Senior, values);
        Assert.Contains(SeniorityLevel.Lead, values);
        Assert.Contains(SeniorityLevel.Unknown, values);
    }
}
