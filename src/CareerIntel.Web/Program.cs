using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Serilog;
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

// Serilog structured logging
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine("data", "logs", "careerintel-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.Cookie.Name = "CareerIntel.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Data directory — same as CLI
var dataDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));
if (!Directory.Exists(dataDir))
    Directory.CreateDirectory(dataDir);

builder.Services.AddSingleton(new DataDirectoryConfig { Path = dataDir });

// Auth config — load or create default
var authConfigPath = Path.Combine(dataDir, "auth-config.json");
if (!File.Exists(authConfigPath))
{
    // Default password: "admin" — user must change on first login
    var defaultHash = AuthService.HashPassword("admin");
    var defaultConfig = new AuthConfig { PasswordHash = defaultHash, Enabled = true, MustChangePassword = true };
    File.WriteAllText(authConfigPath, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
}
var authConfig = JsonSerializer.Deserialize<AuthConfig>(File.ReadAllText(authConfigPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AuthConfig();
builder.Services.AddSingleton(authConfig);
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<LoginRateLimiter>();

// Memory cache for dashboard stats
builder.Services.AddMemoryCache();

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
builder.Services.AddHttpClient<RemoteOkApiScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<RemoteOkApiScraper>());
builder.Services.AddHttpClient<AdzunaApiScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<AdzunaApiScraper>());
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
builder.Services.AddHttpClient<ZipRecruiterScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<ZipRecruiterScraper>());
builder.Services.AddHttpClient<HiredScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<HiredScraper>());
builder.Services.AddHttpClient<TorreAiScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<TorreAiScraper>());
builder.Services.AddHttpClient<GunIoScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<GunIoScraper>());
builder.Services.AddHttpClient<BraintrustScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<BraintrustScraper>());
builder.Services.AddHttpClient<LemonIoScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<LemonIoScraper>());
builder.Services.AddHttpClient<SwissDevJobsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<SwissDevJobsScraper>());
builder.Services.AddHttpClient<LandingJobsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<LandingJobsScraper>());
builder.Services.AddHttpClient<GermanTechJobsScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<GermanTechJobsScraper>());
builder.Services.AddHttpClient<RelocateMeScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<RelocateMeScraper>());
builder.Services.AddHttpClient<WeAreDevelopersScraper>();
builder.Services.AddTransient<IJobScraper>(sp => sp.GetRequiredService<WeAreDevelopersScraper>());

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
builder.Services.AddTransient<ApplicationRepository>();
builder.Services.AddTransient<ProposalRepository>();

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

// Toast notifications — scoped per circuit
builder.Services.AddScoped<CareerIntel.Web.Services.ToastService>();

// Scraper health monitoring
builder.Services.AddSingleton<CareerIntel.Web.Services.ScraperHealthService>();

// Credential protection
var credentialProtector = new CareerIntel.Web.Services.CredentialProtector(dataDir);
builder.Services.AddSingleton(credentialProtector);

// Encrypt any plaintext credentials in notification config
var notifConfigPath = Path.Combine(dataDir, "notification-config.json");
await credentialProtector.EncryptConfigFileAsync(notifConfigPath);

// Health checks
builder.Services.AddHealthChecks()
    .AddSqlite($"Data Source={dbPath}", name: "sqlite");

var app = builder.Build();

// Ensure DB exists.
// NOTE: EnsureCreatedAsync() does NOT support schema migrations. If the domain model changes
// (e.g. new columns, renamed properties), the existing database will NOT be updated automatically.
// For production deployments or team collaboration, migrate to EF Core Migrations:
//   1. dotnet ef migrations add InitialCreate -p src/CareerIntel.Persistence -s src/CareerIntel.Web
//   2. Replace EnsureCreatedAsync() with: await db.Database.MigrateAsync();
// This will enable safe schema evolution with rollback support.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CareerIntelDbContext>();
    await db.Database.EnsureCreatedAsync();

    // EnsureCreatedAsync won't alter existing tables or add new tables to an existing DB.
    // Apply schema patches so existing databases stay in sync with model changes.
    string[] schemaPatchSql =
    [
        // Missing columns on Vacancies (added after initial schema)
        "ALTER TABLE \"Vacancies\" ADD COLUMN \"IsHourlyRate\" INTEGER NOT NULL DEFAULT 0",
        "ALTER TABLE \"Vacancies\" ADD COLUMN \"GeoRestrictions\" TEXT NOT NULL DEFAULT '[]'",
        "ALTER TABLE \"Vacancies\" ADD COLUMN \"ExpiresAt\" INTEGER NULL",

        // Applications table (added after initial schema)
        """
        CREATE TABLE IF NOT EXISTS "Applications" (
            "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            "VacancyId" TEXT NOT NULL DEFAULT '',
            "Company" TEXT NOT NULL DEFAULT '',
            "VacancyTitle" TEXT NOT NULL DEFAULT '',
            "VacancyUrl" TEXT NOT NULL DEFAULT '',
            "Status" TEXT NOT NULL DEFAULT 'Pending',
            "ResumeVersion" TEXT NOT NULL DEFAULT '',
            "CoverLetterPath" TEXT NOT NULL DEFAULT '',
            "MatchScore" REAL NOT NULL DEFAULT 0,
            "ApplyMethod" TEXT NOT NULL DEFAULT '',
            "ApplyUrl" TEXT NOT NULL DEFAULT '',
            "Notes" TEXT NOT NULL DEFAULT '',
            "CreatedDate" INTEGER NOT NULL DEFAULT 0,
            "AppliedDate" INTEGER NULL,
            "ResponseDate" INTEGER NULL,
            "ResponseNotes" TEXT NOT NULL DEFAULT ''
        )
        """,
        "CREATE INDEX IF NOT EXISTS \"IX_Applications_Status\" ON \"Applications\" (\"Status\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Applications_Company\" ON \"Applications\" (\"Company\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Applications_CreatedDate\" ON \"Applications\" (\"CreatedDate\")",

        // Proposals table (added after initial schema)
        """
        CREATE TABLE IF NOT EXISTS "Proposals" (
            "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            "ConversationId" TEXT NOT NULL DEFAULT '',
            "RecruiterName" TEXT NOT NULL DEFAULT '',
            "RecruiterProfileUrl" TEXT NOT NULL DEFAULT '',
            "Company" TEXT NOT NULL DEFAULT '',
            "JobTitle" TEXT NOT NULL DEFAULT '',
            "TechStack" TEXT NOT NULL DEFAULT '',
            "RemotePolicy" TEXT NOT NULL DEFAULT '',
            "Location" TEXT NOT NULL DEFAULT '',
            "RelocationOffered" INTEGER NOT NULL DEFAULT 0,
            "SalaryHint" TEXT NOT NULL DEFAULT '',
            "MessageSummary" TEXT NOT NULL DEFAULT '',
            "FullContent" TEXT NOT NULL DEFAULT '',
            "ProposalDate" INTEGER NOT NULL DEFAULT 0,
            "LastMessageDate" INTEGER NULL,
            "MessageCount" INTEGER NOT NULL DEFAULT 0,
            "Status" TEXT NOT NULL DEFAULT 'New',
            "Notes" TEXT NOT NULL DEFAULT '',
            "SourceFile" TEXT NOT NULL DEFAULT ''
        )
        """,
        "CREATE INDEX IF NOT EXISTS \"IX_Proposals_Status\" ON \"Proposals\" (\"Status\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Proposals_Company\" ON \"Proposals\" (\"Company\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Proposals_ProposalDate\" ON \"Proposals\" (\"ProposalDate\")",
        "CREATE INDEX IF NOT EXISTS \"IX_Proposals_ConversationId\" ON \"Proposals\" (\"ConversationId\")",
    ];
    foreach (var sql in schemaPatchSql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Microsoft.Data.Sqlite.SqliteException) { /* already exists — safe to skip */ }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws: wss:;";
    await next();
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Login endpoint — validates password, sets auth cookie
app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var authCfg = context.RequestServices.GetRequiredService<AuthConfig>();
    var authSvc = context.RequestServices.GetRequiredService<AuthService>();
    var rateLimiter = context.RequestServices.GetRequiredService<LoginRateLimiter>();

    if (!authCfg.Enabled)
    {
        await SignInUser(context);
        return Results.Redirect("/");
    }

    // Rate limiting — 5 attempts per IP per 5 minutes
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!rateLimiter.IsAllowed(clientIp))
    {
        return Results.Redirect("/login?error=rate");
    }

    var form = await context.Request.ReadFormAsync();
    var password = form["password"].ToString();

    var verifyResult = string.IsNullOrEmpty(password)
        ? new PasswordVerifyResult(false)
        : authSvc.VerifyPassword(password, authCfg.PasswordHash);

    if (!verifyResult.Success)
    {
        rateLimiter.RecordFailure(clientIp);
        return Results.Redirect("/login?error=1");
    }

    // Auto-rehash legacy SHA256 passwords to PBKDF2
    if (verifyResult.NeedsRehash)
    {
        authCfg.PasswordHash = AuthService.HashPassword(password);
        var rehashPath = Path.Combine(
            context.RequestServices.GetRequiredService<DataDirectoryConfig>().Path,
            "auth-config.json");
        await File.WriteAllTextAsync(rehashPath, JsonSerializer.Serialize(authCfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    rateLimiter.RecordSuccess(clientIp);
    await SignInUser(context);

    // Redirect to password change if required
    if (authCfg.MustChangePassword)
        return Results.Redirect("/settings/change-password");

    return Results.Redirect("/");
});

// Password change endpoint
app.MapPost("/api/auth/change-password", async (HttpContext context) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
        return Results.Redirect("/login");

    var authCfg = context.RequestServices.GetRequiredService<AuthConfig>();
    var authSvc = context.RequestServices.GetRequiredService<AuthService>();

    var form = await context.Request.ReadFormAsync();
    var currentPassword = form["currentPassword"].ToString();
    var newPassword = form["newPassword"].ToString();
    var confirmPassword = form["confirmPassword"].ToString();

    if (string.IsNullOrEmpty(newPassword) || !AuthService.MeetsComplexity(newPassword))
        return Results.Redirect("/settings/change-password?error=complexity");

    if (newPassword != confirmPassword)
        return Results.Redirect("/settings/change-password?error=mismatch");

    // Verify current password (skip if forced change from default)
    if (!authCfg.MustChangePassword &&
        (string.IsNullOrEmpty(currentPassword) || !authSvc.VerifyPassword(currentPassword, authCfg.PasswordHash).Success))
        return Results.Redirect("/settings/change-password?error=wrong");

    // Update password
    authCfg.PasswordHash = AuthService.HashPassword(newPassword);
    authCfg.MustChangePassword = false;

    var configPath = Path.Combine(
        context.RequestServices.GetRequiredService<DataDirectoryConfig>().Path,
        "auth-config.json");
    await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(authCfg, new JsonSerializerOptions { WriteIndented = true }));

    return Results.Redirect("/?changed=1");
});

// Logout endpoint — clears auth cookie
app.MapGet("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Protect all pages except login and static files
app.Use(async (context, next) =>
{
    var authCfg = context.RequestServices.GetRequiredService<AuthConfig>();
    var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

    // Skip auth check if auth is disabled, or for allowed paths
    if (!authCfg.Enabled
        || path.StartsWith("/login")
        || path.StartsWith("/api/auth/")
        || path.StartsWith("/settings/change-password")
        || path.StartsWith("/_framework")
        || path.StartsWith("/_blazor")
        || path.StartsWith("/css")
        || path.StartsWith("/favicon")
        || path == "/health")
    {
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login");
        return;
    }

    await next();
});

app.MapHealthChecks("/health");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task SignInUser(HttpContext context)
{
    var claims = new List<Claim> { new(ClaimTypes.Name, "admin") };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
        new AuthenticationProperties { IsPersistent = true });
}

/// <summary>
/// Holds the data directory path for DI injection.
/// </summary>
public class DataDirectoryConfig
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Authentication configuration loaded from data/auth-config.json.
/// </summary>
public class AuthConfig
{
    public string PasswordHash { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool MustChangePassword { get; set; }
}

/// <summary>
/// Password authentication using ASP.NET Identity's PBKDF2-based PasswordHasher.
/// Backwards-compatible: detects legacy SHA256 hex hashes and auto-upgrades on verify.
/// </summary>
public class AuthService
{
    private static readonly PasswordHasher<object> Hasher = new();
    private readonly AuthConfig _config;

    public AuthService(AuthConfig config) => _config = config;

    public static string HashPassword(string password) =>
        Hasher.HashPassword(null!, password);

    public PasswordVerifyResult VerifyPassword(string password, string storedHash)
    {
        // Legacy SHA256 hash detection — hex string, 64 chars
        // Auto-upgrade to PBKDF2 on successful verify
        if (storedHash.Length == 64 && storedHash.All(c => char.IsAsciiHexDigit(c)))
        {
            var sha256 = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(password));
            var legacyHash = Convert.ToHexStringLower(sha256);
            if (string.Equals(legacyHash, storedHash, StringComparison.OrdinalIgnoreCase))
                return new PasswordVerifyResult(true, NeedsRehash: true);
            return new PasswordVerifyResult(false);
        }

        var result = Hasher.VerifyHashedPassword(null!, storedHash, password);
        return new PasswordVerifyResult(
            result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded,
            NeedsRehash: result == PasswordVerificationResult.SuccessRehashNeeded);
    }

    public static bool MeetsComplexity(string password) =>
        password.Length >= 8 &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsLower) &&
        password.Any(char.IsDigit);

    public bool IsEnabled => _config.Enabled;
}

/// <summary>
/// Result of password verification, indicating whether a rehash is needed.
/// </summary>
public record PasswordVerifyResult(bool Success, bool NeedsRehash = false);

/// <summary>
/// In-memory rate limiter for login attempts. 5 failures per IP within a sliding 5-minute window.
/// </summary>
public class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, LoginAttemptTracker> _attempts = new();
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public bool IsAllowed(string clientIp)
    {
        if (!_attempts.TryGetValue(clientIp, out var tracker))
            return true;

        tracker.PruneOld();
        return tracker.FailureCount < MaxAttempts;
    }

    public void RecordFailure(string clientIp)
    {
        var tracker = _attempts.GetOrAdd(clientIp, _ => new LoginAttemptTracker());
        tracker.PruneOld();
        tracker.Failures.Add(DateTimeOffset.UtcNow);
    }

    public void RecordSuccess(string clientIp)
    {
        _attempts.TryRemove(clientIp, out _);
    }

    private sealed class LoginAttemptTracker
    {
        public List<DateTimeOffset> Failures { get; } = [];
        public int FailureCount => Failures.Count;

        public void PruneOld()
        {
            var cutoff = DateTimeOffset.UtcNow - Window;
            Failures.RemoveAll(f => f < cutoff);
        }
    }
}

// Expose Program for WebApplicationFactory in integration tests
public partial class Program;
