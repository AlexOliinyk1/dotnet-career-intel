using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CareerIntel.Analysis;
using CareerIntel.Core.Interfaces;
using CareerIntel.Intelligence;
using CareerIntel.Matching;
using CareerIntel.Notifications;
using CareerIntel.Persistence;
using CareerIntel.Resume;
using CareerIntel.Scrapers;
using CareerIntel.Cli.Commands;

namespace CareerIntel.Cli;

/// <summary>
/// Entry point for the CareerIntel CLI application.
/// Configures dependency injection, logging, and System.CommandLine commands.
/// </summary>
public static class Program
{
    /// <summary>
    /// Default data directory relative to the executable location.
    /// </summary>
    public static string DataDirectory { get; set; } = GetDefaultDataDirectory();

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("CareerIntel - .NET Career Intelligence CLI")
        {
            ScanCommand.Create(),
            AnalyzeCommand.Create(),
            MatchCommand.Create(),
            ResumeCommand.Create(),
            FeedbackCommand.Create(),
            ReadinessCommand.Create(),
            InsightsCommand.Create(),
            LearnCommand.Create(),
            NegotiateCommand.Create(),
            PortfolioCommand.Create(),
            SimulateCommand.Create(),
            PipelineCommand.Create(),
            InterviewPrepCommand.Create(),
            CompaniesCommand.Create(),
            ApplyCommand.Create(),
            SalaryCommand.Create(),
            TopicsCommand.Create(),
            ResourcesCommand.Create(),
            LearnDynamicCommand.Create(),
            ScanImageCommand.Create(),
            ProfileCommand.Create(),
            DashboardCommand.Create(),
            AssessCommand.Create(),
            WatchCommand.Create()
        };

        var dataOption = new Option<string>(
            "--data-dir",
            getDefaultValue: GetDefaultDataDirectory,
            description: "Path to the data directory");

        rootCommand.AddGlobalOption(dataOption);

        // Use middleware to intercept --data-dir before any command handler runs (P3 fix)
        var parser = new CommandLineBuilder(rootCommand)
            .AddMiddleware(async (context, next) =>
            {
                var dir = context.ParseResult.GetValueForOption(dataOption);
                if (!string.IsNullOrEmpty(dir))
                {
                    DataDirectory = dir;
                }
                await next(context);
            })
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }

    /// <summary>
    /// Builds the DI service provider with all application services registered.
    /// </summary>
    public static ServiceProvider BuildServiceProvider(string? dataDir = null)
    {
        if (dataDir is not null)
            DataDirectory = dataDir;

        EnsureDataDirectoryExists();

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // HttpClient factory
        services.AddHttpClient("default");
        services.AddHttpClient("telegram");

        // Scraping compliance (P1: shared singleton across all scrapers)
        services.AddSingleton<ScrapingCompliance>();

        // Scrapers — register typed HttpClients, then wire IJobScraper to use
        // the typed registrations so each scraper receives its own HttpClient instance.
        services.AddHttpClient<DjinniScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<DjinniScraper>());

        services.AddHttpClient<DouScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<DouScraper>());

        services.AddHttpClient<LinkedInScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<LinkedInScraper>());

        services.AddHttpClient<JustJoinItScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<JustJoinItScraper>());

        services.AddHttpClient<RemoteOkScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<RemoteOkScraper>());

        services.AddHttpClient<WeWorkRemotelyScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<WeWorkRemotelyScraper>());

        services.AddHttpClient<HackerNewsScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<HackerNewsScraper>());

        services.AddHttpClient<HimalayasScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<HimalayasScraper>());

        services.AddHttpClient<JobicyScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<JobicyScraper>());

        services.AddHttpClient<NoFluffJobsScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<NoFluffJobsScraper>());

        services.AddHttpClient<ToptalScraper>();
        services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<ToptalScraper>());

        // Forum scrapers (interview questions — not registered as IJobScraper)
        services.AddHttpClient<DouForumScraper>();
        services.AddHttpClient<RedditScraper>();

        // Analysis
        services.AddTransient<ISkillAnalyzer, SkillAnalysisService>();

        // Matching
        services.AddSingleton<ScoringEngine>();
        services.AddSingleton<RelevanceFilter>(sp =>
            new RelevanceFilter(sp.GetRequiredService<ILogger<RelevanceFilter>>()));
        services.AddSingleton<IMatchEngine>(sp =>
        {
            var profilePath = Path.Combine(DataDirectory, "my-profile.json");

            // Ensure the profile file exists before constructing ProfileMatcher,
            // which loads the profile eagerly in its constructor.
            if (!File.Exists(profilePath))
            {
                var logger = sp.GetRequiredService<ILogger<ProfileMatcher>>();
                logger.LogWarning(
                    "Profile file not found at {Path}. " +
                    "Match and resume commands will fail until a profile is created.",
                    profilePath);

                // Create a minimal placeholder so ProfileMatcher can be constructed
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
        services.AddTransient<ResumeBuilder>();
        services.AddTransient<AtsTailorer>();
        services.AddTransient<CoverLetterGenerator>();
        services.AddTransient<ResumeSimulator>();
        services.AddTransient<ResumeDiffTracker>();

        // Intelligence engines
        services.AddTransient<OfferReadinessEngine>();
        services.AddTransient<InterviewFeedbackEngine>();
        services.AddTransient<LearningROIEngine>();
        services.AddTransient<NegotiationEngine>();
        services.AddTransient<PortfolioGenerator>();
        services.AddTransient<AdaptivePriorityEngine>();
        services.AddTransient<EnergyModelEngine>();
        services.AddTransient<CompanyIntelligenceEngine>();
        services.AddTransient<NegotiationIntelligenceEngine>();
        services.AddTransient<ProjectEvidenceEngine>();
        services.AddTransient<InterviewPrepEngine>();
        services.AddTransient<InterviewLearningPlanner>();
        services.AddTransient<CompanyDiscoveryEngine>();
        services.AddTransient<AutoApplyEngine>();
        services.AddTransient<TopicInferenceEngine>();
        services.AddTransient<ResourceRecommendationEngine>();
        services.AddTransient<SalaryIntelligenceEngine>();
        services.AddTransient<QuestionClassifier>();
        services.AddTransient<KnowledgeBaseManager>();
        services.AddTransient<LearningProgressTracker>();
        services.AddTransient<DynamicContentPipeline>();
        services.AddTransient<VacancyImageScanner>();
        services.AddTransient<ApplicantCompetitivenessEngine>();
        services.AddHttpClient<ExternalContentAggregator>();

        // Analysis engines (P6: drift detection)
        services.AddTransient<DriftDetector>();

        // Persistence (P0: SQLite + repositories)
        var dbPath = Path.Combine(DataDirectory, "career-intel.db");
        services.AddDbContext<CareerIntelDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Transient);

        services.AddTransient<VacancyRepository>();
        services.AddTransient<InterviewRepository>();
        services.AddTransient<CompanyRepository>();
        services.AddTransient<NegotiationRepository>();

        // Notifications
        services.AddTransient<NotificationConfig>(sp =>
        {
            var configPath = Path.Combine(DataDirectory, "notification-config.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                return System.Text.Json.JsonSerializer.Deserialize<NotificationConfig>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new NotificationConfig();
            }
            return new NotificationConfig();
        });

        // Register INotificationService implementations from config
        services.AddTransient<INotificationService>(sp =>
        {
            var config = sp.GetRequiredService<NotificationConfig>();
            if (config.Telegram?.Enabled == true)
            {
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpFactory.CreateClient("telegram");
                var tgLogger = sp.GetRequiredService<ILogger<TelegramNotifier>>();
                return new TelegramNotifier(httpClient, config.Telegram.BotToken, config.Telegram.ChatId, tgLogger);
            }
            if (config.Email?.Enabled == true)
            {
                var emailLogger = sp.GetRequiredService<ILogger<EmailNotifier>>();
                return new EmailNotifier(
                    config.Email.SmtpHost, config.Email.SmtpPort,
                    config.Email.Username, config.Email.Password,
                    config.Email.FromAddress, config.Email.ToAddress, emailLogger);
            }
            // Fallback: return a no-op notifier when nothing is configured
            return new NoOpNotifier();
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Initializes the SQLite database, ensuring schema is created.
    /// Called once at startup by commands that need persistence.
    /// </summary>
    public static async Task EnsureDatabaseAsync(ServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<CareerIntelDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    private static string GetDefaultDataDirectory()
    {
        var exeDir = AppContext.BaseDirectory;
        var dataDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "data"));

        // Fallback: if the resolved path doesn't exist, try relative to working directory
        if (!Directory.Exists(dataDir))
        {
            dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));
        }

        return dataDir;
    }

    private static void EnsureDataDirectoryExists()
    {
        if (!Directory.Exists(DataDirectory))
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }
}
