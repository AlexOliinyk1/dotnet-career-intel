using System.Text.Json;
using CareerIntel.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CareerIntel.Persistence;

/// <summary>
/// EF Core DbContext for the CareerIntel SQLite database.
/// Manages persistence of vacancies, interview feedback, company profiles,
/// negotiation states, and portfolio projects.
/// </summary>
public sealed class CareerIntelDbContext(DbContextOptions<CareerIntelDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DbSet<JobVacancy> Vacancies => Set<JobVacancy>();
    public DbSet<InterviewFeedback> InterviewFeedbacks => Set<InterviewFeedback>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<NegotiationState> NegotiationStates => Set<NegotiationState>();
    public DbSet<PortfolioProject> PortfolioProjects => Set<PortfolioProject>();
    public DbSet<VacancyChange> VacancyChanges => Set<VacancyChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureJobVacancy(modelBuilder);
        ConfigureInterviewFeedback(modelBuilder);
        ConfigureCompanyProfile(modelBuilder);
        ConfigureNegotiationState(modelBuilder);
        ConfigurePortfolioProject(modelBuilder);
        ConfigureVacancyChange(modelBuilder);
    }

    private static void ConfigureJobVacancy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<JobVacancy>();

        entity.HasKey(v => v.Id);
        entity.HasIndex(v => v.SourcePlatform);
        entity.HasIndex(v => v.ScrapedDate);
        entity.HasIndex(v => v.SeniorityLevel);

        entity.Property(v => v.SeniorityLevel)
            .HasConversion<string>();

        entity.Property(v => v.RemotePolicy)
            .HasConversion<string>();

        entity.Property(v => v.EngagementType)
            .HasConversion<string>();

        entity.Property(v => v.GeoRestrictions)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(v => v.RequiredSkills)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(v => v.PreferredSkills)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        // Ignore computed / non-persisted properties
        entity.Ignore(v => v.MatchScore);
        entity.Ignore(v => v.Breakdown);
    }

    private static void ConfigureInterviewFeedback(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<InterviewFeedback>();

        entity.HasKey(f => f.Id);
        entity.Property(f => f.Id).ValueGeneratedOnAdd();
        entity.HasIndex(f => f.Company);
        entity.HasIndex(f => f.InterviewDate);

        entity.Property(f => f.WeakAreas)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(f => f.StrongAreas)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());
    }

    private static void ConfigureCompanyProfile(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanyProfile>();

        entity.HasKey(c => c.Id);
        entity.Property(c => c.Id).ValueGeneratedOnAdd();
        entity.HasIndex(c => c.Name).IsUnique();

        entity.Property(c => c.RealTechStack)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(c => c.InterviewRounds)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(c => c.CommonRejectionReasons)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(c => c.RedFlags)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(c => c.Pros)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        // OfferRate is computed, not stored
        entity.Ignore(c => c.OfferRate);
    }

    private static void ConfigureNegotiationState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<NegotiationState>();

        entity.HasKey(n => n.Id);
        entity.Property(n => n.Id).ValueGeneratedOnAdd();
        entity.HasIndex(n => n.Status);

        entity.Property(n => n.Leverage)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());
    }

    private static void ConfigurePortfolioProject(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PortfolioProject>();

        entity.HasKey(p => p.Id);
        entity.Property(p => p.Id).ValueGeneratedOnAdd();

        entity.Property(p => p.TechStack)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(p => p.Backlog)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());

        entity.Property(p => p.TargetSkillGaps)
            .HasConversion(CreateStringListConverter())
            .Metadata.SetValueComparer(CreateStringListComparer());
    }

    private static void ConfigureVacancyChange(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<VacancyChange>();

        entity.HasKey(c => c.Id);
        entity.Property(c => c.Id).ValueGeneratedOnAdd();
        entity.HasIndex(c => c.VacancyId);
        entity.HasIndex(c => c.DetectedDate);
    }

    /// <summary>
    /// Creates a ValueConverter that serializes List&lt;string&gt; to/from JSON for SQLite storage.
    /// </summary>
    private static ValueConverter<List<string>, string> CreateStringListConverter() =>
        new(
            list => JsonSerializer.Serialize(list, JsonOptions),
            json => JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>()
        );

    /// <summary>
    /// Creates a ValueComparer for List&lt;string&gt; so EF Core can detect changes correctly.
    /// </summary>
    private static ValueComparer<List<string>> CreateStringListComparer() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            list => list.ToList()
        );
}
