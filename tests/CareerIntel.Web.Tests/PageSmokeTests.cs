using System.Net;
using Xunit;

namespace CareerIntel.Web.Tests;

/// <summary>
/// Smoke tests that verify every page in the application returns HTTP 200
/// and renders without server-side errors. Uses WebApplicationFactory with
/// auth disabled and an isolated temp database.
/// </summary>
public sealed class PageSmokeTests : IClassFixture<CareerIntelWebFactory>
{
    private readonly HttpClient _client;

    public PageSmokeTests(CareerIntelWebFactory factory)
    {
        _client = factory.CreateClient(new()
        {
            AllowAutoRedirect = true,
            HandleCookies = true,
        });
    }

    // ─── Overview ───
    [Fact] public Task Dashboard() => AssertPageLoads("/");

    // ─── Jobs ───
    [Fact] public Task Jobs() => AssertPageLoads("/jobs");
    [Fact] public Task RemoteJobs() => AssertPageLoads("/jobs/remote");
    [Fact] public Task TechDemand() => AssertPageLoads("/jobs/tech-demand");
    [Fact] public Task ScanImage() => AssertPageLoads("/jobs/scan-image");
    [Fact] public Task StackAnalysis() => AssertPageLoads("/jobs/stack-analysis");

    // ─── Applications ───
    [Fact] public Task Pipeline() => AssertPageLoads("/applications");
    [Fact] public Task Decisions() => AssertPageLoads("/applications/decisions");
    [Fact] public Task Strategy() => AssertPageLoads("/applications/strategy");

    // ─── LinkedIn ───
    [Fact] public Task Proposals() => AssertPageLoads("/linkedin");
    [Fact] public Task ProfileReview() => AssertPageLoads("/linkedin/profile");
    [Fact] public Task Connections() => AssertPageLoads("/linkedin/connections");
    [Fact] public Task LinkedInStats() => AssertPageLoads("/linkedin/stats");

    // ─── Companies ───
    [Fact] public Task Companies() => AssertPageLoads("/companies");
    [Fact] public Task ScrapeCompany() => AssertPageLoads("/companies/scrape");
    [Fact] public Task BridgeCheck() => AssertPageLoads("/companies/bridge-check");

    // ─── Interview ───
    [Fact] public Task InterviewPrep() => AssertPageLoads("/interview/prep");
    [Fact] public Task MockInterview() => AssertPageLoads("/interview/mock");
    [Fact] public Task Feedback() => AssertPageLoads("/interview/feedback");
    [Fact] public Task Questions() => AssertPageLoads("/interview/questions");
    [Fact] public Task Insights() => AssertPageLoads("/interview/insights");

    // ─── Career ───
    [Fact] public Task MarketPulse() => AssertPageLoads("/career/pulse");
    [Fact] public Task Roadmap() => AssertPageLoads("/career/roadmap");
    [Fact] public Task SkillGaps() => AssertPageLoads("/career/gaps");
    [Fact] public Task AiImpact() => AssertPageLoads("/career/ai-impact");
    [Fact] public Task Outreach() => AssertPageLoads("/career/outreach");

    // ─── Learning ───
    [Fact] public Task LearnPlan() => AssertPageLoads("/learning");
    [Fact] public Task AdaptiveLearning() => AssertPageLoads("/learning/adaptive");
    [Fact] public Task Documentation() => AssertPageLoads("/learning/docs");
    [Fact] public Task MicroLearn() => AssertPageLoads("/learning/micro");
    [Fact] public Task LearningProgress() => AssertPageLoads("/learning/progress");
    [Fact] public Task LearningResources() => AssertPageLoads("/learning/resources");

    // ─── Resume & Salary ───
    [Fact] public Task ResumeBuilder() => AssertPageLoads("/resume");
    [Fact] public Task ResumeReview() => AssertPageLoads("/resume/review");
    [Fact] public Task AtsSimulator() => AssertPageLoads("/resume/simulator");
    [Fact] public Task SalaryIntel() => AssertPageLoads("/salary");
    [Fact] public Task Negotiate() => AssertPageLoads("/salary/negotiate");
    [Fact] public Task CompareOffers() => AssertPageLoads("/salary/compare");

    // ─── Monitor ───
    [Fact] public Task WatchPanel() => AssertPageLoads("/monitor");
    [Fact] public Task Schedule() => AssertPageLoads("/monitor/schedule");

    // ─── Settings ───
    [Fact] public Task Profile() => AssertPageLoads("/settings/profile");
    [Fact] public Task Config() => AssertPageLoads("/settings");
    [Fact] public Task VerifySetup() => AssertPageLoads("/settings/verify");
    [Fact] public Task ChangePassword() => AssertPageLoads("/settings/change-password");

    // ─── System ───
    [Fact] public Task HealthCheck() => AssertPageLoads("/health", expectHtml: false);

    /// <summary>
    /// Core assertion: page returns 200 and contains expected content markers.
    /// </summary>
    private async Task AssertPageLoads(string path, bool expectHtml = true)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content.Length > 0, $"Page {path} returned empty content");

        if (expectHtml)
        {
            // Should contain HTML structure
            Assert.Contains("<!DOCTYPE html>", content, StringComparison.OrdinalIgnoreCase);

            // Should NOT contain unhandled exception markers
            Assert.DoesNotContain("An unhandled exception occurred", content);
            Assert.DoesNotContain("stack trace", content, StringComparison.OrdinalIgnoreCase);
        }
    }
}
