using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CareerIntel.Analysis;
using CareerIntel.Core.Interfaces;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using CareerIntel.Notifications;
using CareerIntel.Persistence;
using CareerIntel.Resume;
using CareerIntel.Scrapers;
using CareerIntel.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Data directory — same as CLI
var dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));
if (!Directory.Exists(dataDir))
    Directory.CreateDirectory(dataDir);

builder.Services.AddSingleton(new DataDirectoryConfig { Path = dataDir });

// HttpClient factory
builder.Services.AddHttpClient("default");
builder.Services.AddHttpClient("telegram");

// Scraping compliance
builder.Services.AddSingleton<ScrapingCompliance>();

// Job scrapers — typed HttpClients
builder.Services.AddHttpClient<DjinniScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<DjinniScraper>());
builder.Services.AddHttpClient<DouScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<DouScraper>());
builder.Services.AddHttpClient<LinkedInScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<LinkedInScraper>());
builder.Services.AddHttpClient<JustJoinItScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<JustJoinItScraper>());
builder.Services.AddHttpClient<RemoteOkScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<RemoteOkScraper>());
builder.Services.AddHttpClient<WeWorkRemotelyScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<WeWorkRemotelyScraper>());
builder.Services.AddHttpClient<HackerNewsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<HackerNewsScraper>());
builder.Services.AddHttpClient<HimalayasScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<HimalayasScraper>());
builder.Services.AddHttpClient<JobicyScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<JobicyScraper>());
builder.Services.AddHttpClient<NoFluffJobsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<NoFluffJobsScraper>());
builder.Services.AddHttpClient<ToptalScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<ToptalScraper>());
builder.Services.AddHttpClient<ArcDevScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<ArcDevScraper>());
builder.Services.AddHttpClient<WorkUaScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<WorkUaScraper>());
builder.Services.AddHttpClient<WellfoundScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<WellfoundScraper>());
builder.Services.AddHttpClient<BuiltInScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<BuiltInScraper>());
builder.Services.AddHttpClient<DiceScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<DiceScraper>());
builder.Services.AddHttpClient<RemotiveScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<RemotiveScraper>());
builder.Services.AddHttpClient<JustRemoteScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<JustRemoteScraper>());
builder.Services.AddHttpClient<WorkingNomadsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<WorkingNomadsScraper>());
builder.Services.AddHttpClient<DynamiteJobsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<DynamiteJobsScraper>());
builder.Services.AddHttpClient<EuRemoteJobsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<EuRemoteJobsScraper>());

// Forum scrapers
builder.Services.AddHttpClient<DouForumScraper>();
builder.Services.AddHttpClient<RedditScraper>();

// Analysis
builder.Services.AddTransient<ISkillAnalyzer, SkillAnalysisService>();

// Matching
builder.Services.AddSingleton<ScoringEngine>();
builder.Services.AddSingleton<RelevanceFilter>(sp =>
    new RelevanceFilter(sp.GetRequiredService<ILogger<RelevanceFilter>>()));
builder.Services.AddSingleton<IMatchEngine>(sp =>
{
    var profilePath = Path.Combine(dataDir, "my-profile.json");
    if (!File.Exists(profilePath))
    {
        File.WriteAllText(profilePath, """
            {
              "personal": { "name": "", "title": "" },
              "skills": [],
              "experiences": [],
              "preferences": { "minSalaryUsd": 0, "targetSalaryUsd": 0 }
            }
            """);
    }
    return new ProfileMatcher(
        profilePath,
        sp.GetRequiredService<ScoringEngine>(),
        sp.GetRequiredService<RelevanceFilter>(),
        sp.GetRequiredService<ILogger<ProfileMatcher>>());
});

// Resume
builder.Services.AddTransient<ResumeBuilder>();
builder.Services.AddTransient<AtsTailorer>();
builder.Services.AddTransient<CoverLetterGenerator>();
builder.Services.AddTransient<ResumeSimulator>();
builder.Services.AddTransient<ResumeDiffTracker>();

// Intelligence engines
builder.Services.AddTransient<OfferReadinessEngine>();
builder.Services.AddTransient<InterviewFeedbackEngine>();
builder.Services.AddTransient<LearningROIEngine>();
builder.Services.AddTransient<NegotiationEngine>();
builder.Services.AddTransient<PortfolioGenerator>();
builder.Services.AddTransient<AdaptivePriorityEngine>();
builder.Services.AddTransient<EnergyModelEngine>();
builder.Services.AddTransient<CompanyIntelligenceEngine>();
builder.Services.AddTransient<NegotiationIntelligenceEngine>();
builder.Services.AddTransient<ProjectEvidenceEngine>();
builder.Services.AddTransient<InterviewPrepEngine>();
builder.Services.AddTransient<InterviewLearningPlanner>();
builder.Services.AddTransient<CompanyDiscoveryEngine>();
builder.Services.AddTransient<AutoApplyEngine>();
builder.Services.AddTransient<TopicInferenceEngine>();
builder.Services.AddTransient<ResourceRecommendationEngine>();
builder.Services.AddTransient<SalaryIntelligenceEngine>();
builder.Services.AddTransient<QuestionClassifier>();
builder.Services.AddTransient<KnowledgeBaseManager>();
builder.Services.AddTransient<LearningProgressTracker>();
builder.Services.AddTransient<DynamicContentPipeline>();
builder.Services.AddTransient<VacancyImageScanner>();
builder.Services.AddTransient<ApplicantCompetitivenessEngine>();
builder.Services.AddHttpClient<ExternalContentAggregator>();

// Drift detection
builder.Services.AddTransient<DriftDetector>();

// Persistence
var dbPath = Path.Combine(dataDir, "career-intel.db");
builder.Services.AddDbContext<CareerIntelDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"),
    ServiceLifetime.Transient);
builder.Services.AddTransient<VacancyRepository>();
builder.Services.AddTransient<InterviewRepository>();
builder.Services.AddTransient<CompanyRepository>();
builder.Services.AddTransient<NegotiationRepository>();

// Notifications
builder.Services.AddTransient<NotificationConfig>(sp =>
{
    var configPath = Path.Combine(dataDir, "notification-config.json");
    if (File.Exists(configPath))
    {
        var json = File.ReadAllText(configPath);
        return System.Text.Json.JsonSerializer.Deserialize<NotificationConfig>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new NotificationConfig();
    }
    return new NotificationConfig();
});

var app = builder.Build();

// Ensure DB exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CareerIntelDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Holds the data directory path for DI injection.
/// </summary>
public class DataDirectoryConfig
{
    public string Path { get; set; } = string.Empty;
}
