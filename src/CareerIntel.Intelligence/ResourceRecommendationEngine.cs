namespace CareerIntel.Intelligence;

using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

#region Records

public sealed record ResourcePlan(
    IReadOnlyList<ResourceRecommendation> Recommendations,
    int TotalEstimatedHours,
    string Priority,
    IReadOnlyList<string> QuickWins);

public sealed record ResourceRecommendation(
    string Skill,
    string TopicArea,
    IReadOnlyList<LearningLink> Links,
    int EstimatedHours,
    string Priority);

public sealed record LearningLink(
    string Title,
    string Url,
    string Type,
    string Platform,
    bool IsFree,
    string Language);

public sealed record TopicLearningPath(
    string TopicId,
    string TopicName,
    IReadOnlyList<LearningPhase> Phases);

public sealed record LearningPhase(
    string Name,
    IReadOnlyList<LearningLink> Resources,
    int EstimatedHours,
    IReadOnlyList<string> Prerequisites);

public sealed record CertificationRecommendation(
    string Name,
    string Provider,
    string Url,
    string Level,
    double SalaryImpactPercent,
    IReadOnlyList<string> RelevantSkills);

public sealed record PracticeResource(
    string Platform,
    string Url,
    string Description,
    IReadOnlyList<string> Skills,
    bool IsFree);

#endregion

/// <summary>
/// Recommends learning resources based on skill gaps, detected topics, and learning
/// priorities.  Contains a comprehensive built-in database of real, curated resources
/// covering documentation, courses, videos, books, practice platforms, certifications,
/// and Ukrainian-language materials for every .NET interview topic area.
/// </summary>
public sealed class ResourceRecommendationEngine(ILogger<ResourceRecommendationEngine> logger)
{
    // ──────────────────────────────────────────────────────────────────
    //  Topic-area identifiers (match InterviewTopicBank topic IDs)
    // ──────────────────────────────────────────────────────────────────
    private const string Algorithms = "algorithms-data-structures";
    private const string SystemDesign = "system-design";
    private const string DotNetInternals = "dotnet-internals";
    private const string BackendArch = "backend-architecture";
    private const string Databases = "databases";
    private const string OrmDataAccess = "orm-data-access";
    private const string Performance = "performance";
    private const string Concurrency = "concurrency";
    private const string Cloud = "cloud";
    private const string Security = "security";
    private const string Testing = "testing";
    private const string Frontend = "frontend";
    private const string DevOps = "devops";
    private const string Behavioral = "behavioral";

    // ──────────────────────────────────────────────────────────────────
    //  Quick-win threshold (hours)
    // ──────────────────────────────────────────────────────────────────
    private const int QuickWinHoursThreshold = 8;

    // ──────────────────────────────────────────────────────────────────
    //  Skill-to-topic-area mapping
    // ──────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, string> SkillToTopicMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Algorithms & Data Structures
        ["Algorithms"] = Algorithms,
        ["Data Structures"] = Algorithms,
        ["LeetCode"] = Algorithms,
        ["Problem Solving"] = Algorithms,
        ["Sorting"] = Algorithms,
        ["Graphs"] = Algorithms,
        ["Dynamic Programming"] = Algorithms,

        // System Design
        ["System Design"] = SystemDesign,
        ["Distributed Systems"] = SystemDesign,
        ["Microservices"] = SystemDesign,
        ["Event-Driven Architecture"] = SystemDesign,
        ["CQRS"] = SystemDesign,
        ["Event Sourcing"] = SystemDesign,
        ["DDD"] = SystemDesign,
        ["Domain-Driven Design"] = SystemDesign,
        ["Architecture"] = SystemDesign,

        // .NET Internals
        [".NET Internals"] = DotNetInternals,
        ["CLR"] = DotNetInternals,
        ["GC"] = DotNetInternals,
        ["Garbage Collection"] = DotNetInternals,
        ["JIT"] = DotNetInternals,
        ["C#"] = DotNetInternals,
        [".NET"] = DotNetInternals,
        ["LINQ"] = DotNetInternals,
        ["Reflection"] = DotNetInternals,
        ["Source Generators"] = DotNetInternals,
        ["Memory Management"] = DotNetInternals,

        // Backend / ASP.NET Core
        ["ASP.NET Core"] = BackendArch,
        ["ASP.NET"] = BackendArch,
        ["Web API"] = BackendArch,
        ["REST"] = BackendArch,
        ["gRPC"] = BackendArch,
        ["GraphQL"] = BackendArch,
        ["SignalR"] = BackendArch,
        ["Middleware"] = BackendArch,
        ["Minimal APIs"] = BackendArch,
        ["Blazor"] = BackendArch,

        // Databases
        ["SQL"] = Databases,
        ["SQL Server"] = Databases,
        ["PostgreSQL"] = Databases,
        ["MySQL"] = Databases,
        ["MongoDB"] = Databases,
        ["Redis"] = Databases,
        ["CosmosDB"] = Databases,
        ["Database Design"] = Databases,
        ["NoSQL"] = Databases,
        ["Elasticsearch"] = Databases,

        // ORM / Data Access
        ["Entity Framework"] = OrmDataAccess,
        ["Entity Framework Core"] = OrmDataAccess,
        ["EF Core"] = OrmDataAccess,
        ["Dapper"] = OrmDataAccess,
        ["ORM"] = OrmDataAccess,
        ["Migrations"] = OrmDataAccess,
        ["LINQ to Entities"] = OrmDataAccess,

        // Performance
        ["Performance"] = Performance,
        ["Benchmarking"] = Performance,
        ["BenchmarkDotNet"] = Performance,
        ["Profiling"] = Performance,
        ["Caching"] = Performance,
        ["Span<T>"] = Performance,
        ["Memory<T>"] = Performance,
        ["High Performance"] = Performance,
        ["ArrayPool"] = Performance,

        // Concurrency
        ["Concurrency"] = Concurrency,
        ["Async"] = Concurrency,
        ["async/await"] = Concurrency,
        ["Multithreading"] = Concurrency,
        ["Parallel Programming"] = Concurrency,
        ["TPL"] = Concurrency,
        ["Channels"] = Concurrency,
        ["Synchronization"] = Concurrency,
        ["Task"] = Concurrency,
        ["ValueTask"] = Concurrency,

        // Cloud
        ["Azure"] = Cloud,
        ["AWS"] = Cloud,
        ["Cloud"] = Cloud,
        ["Azure Functions"] = Cloud,
        ["Azure Service Bus"] = Cloud,
        ["Azure DevOps"] = Cloud,
        ["S3"] = Cloud,
        ["Lambda"] = Cloud,
        ["Serverless"] = Cloud,

        // Security
        ["Security"] = Security,
        ["OAuth"] = Security,
        ["OAuth2"] = Security,
        ["OpenID Connect"] = Security,
        ["JWT"] = Security,
        ["Authentication"] = Security,
        ["Authorization"] = Security,
        ["Identity"] = Security,
        ["OWASP"] = Security,
        ["IdentityServer"] = Security,
        ["Duende"] = Security,

        // Testing
        ["Testing"] = Testing,
        ["Unit Testing"] = Testing,
        ["Integration Testing"] = Testing,
        ["xUnit"] = Testing,
        ["NUnit"] = Testing,
        ["Moq"] = Testing,
        ["TDD"] = Testing,
        ["BDD"] = Testing,
        ["Test Automation"] = Testing,
        ["SpecFlow"] = Testing,
        ["NSubstitute"] = Testing,
        ["FluentAssertions"] = Testing,
        ["Testcontainers"] = Testing,

        // Frontend
        ["React"] = Frontend,
        ["Angular"] = Frontend,
        ["TypeScript"] = Frontend,
        ["JavaScript"] = Frontend,
        ["Blazor WebAssembly"] = Frontend,
        ["CSS"] = Frontend,
        ["HTML"] = Frontend,
        ["Vue"] = Frontend,
        ["HTMX"] = Frontend,

        // DevOps
        ["Docker"] = DevOps,
        ["Kubernetes"] = DevOps,
        ["CI/CD"] = DevOps,
        ["GitHub Actions"] = DevOps,
        ["Terraform"] = DevOps,
        ["Helm"] = DevOps,
        ["Jenkins"] = DevOps,
        ["Azure Pipelines"] = DevOps,
        ["GitOps"] = DevOps,
        ["IaC"] = DevOps,
        ["Containers"] = DevOps,
        ["Observability"] = DevOps,
        ["Prometheus"] = DevOps,
        ["Grafana"] = DevOps,
        ["OpenTelemetry"] = DevOps,

        // Behavioral / Soft Skills
        ["Leadership"] = Behavioral,
        ["Communication"] = Behavioral,
        ["Team Management"] = Behavioral,
        ["Agile"] = Behavioral,
        ["Scrum"] = Behavioral,
        ["Kanban"] = Behavioral,
        ["Mentoring"] = Behavioral,
        ["Soft Skills"] = Behavioral,
    };

    // ──────────────────────────────────────────────────────────────────
    //  Estimated hours per topic area (for a mid-level engineer)
    // ──────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, int> BaseHoursPerTopic = new()
    {
        [Algorithms] = 40,
        [SystemDesign] = 35,
        [DotNetInternals] = 30,
        [BackendArch] = 25,
        [Databases] = 25,
        [OrmDataAccess] = 15,
        [Performance] = 20,
        [Concurrency] = 25,
        [Cloud] = 30,
        [Security] = 20,
        [Testing] = 20,
        [Frontend] = 25,
        [DevOps] = 30,
        [Behavioral] = 10,
    };

    // ──────────────────────────────────────────────────────────────────
    //  Level multipliers (scale estimated hours)
    // ──────────────────────────────────────────────────────────────────
    private static double LevelMultiplier(string level) => level switch
    {
        "Junior" => 1.5,
        "Mid" => 1.0,
        "Senior" => 0.7,
        "Lead" => 0.5,
        "Architect" => 0.4,
        _ => 1.0,
    };

    // ──────────────────────────────────────────────────────────────────
    //  Priority scoring (lower = higher priority)
    // ──────────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, int> TopicPriorityOrder = new()
    {
        [Algorithms] = 1,
        [SystemDesign] = 2,
        [DotNetInternals] = 3,
        [BackendArch] = 4,
        [Databases] = 5,
        [Concurrency] = 6,
        [Performance] = 7,
        [Cloud] = 8,
        [Testing] = 9,
        [OrmDataAccess] = 10,
        [Security] = 11,
        [DevOps] = 12,
        [Frontend] = 13,
        [Behavioral] = 14,
    };

    // ══════════════════════════════════════════════════════════════════
    //  RESOURCE DATABASE
    // ══════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, IReadOnlyList<LearningLink>> TopicResources = new()
    {
        // ─────────────────── Algorithms & Data Structures ───────────────────
        [Algorithms] =
        [
            new("Grokking Algorithms by Aditya Bhargava",
                "https://www.manning.com/books/grokking-algorithms",
                "Book", "Manning", false, "en"),
            new("NeetCode - Practice Roadmap",
                "https://neetcode.io/roadmap",
                "Practice", "NeetCode", true, "en"),
            new("VisuAlgo - Visualising Data Structures & Algorithms",
                "https://visualgo.net/",
                "Tool", "VisuAlgo", true, "en"),
            new("LeetCode Problem Set",
                "https://leetcode.com/problemset/",
                "Practice", "LeetCode", true, "en"),
            new("HackerRank Algorithms Domain",
                "https://www.hackerrank.com/domains/algorithms",
                "Practice", "HackerRank", true, "en"),
            new("Codewars C# Kata",
                "https://www.codewars.com/kata/search/csharp",
                "Practice", "Codewars", true, "en"),
            new("Exercism C# Track",
                "https://exercism.org/tracks/csharp",
                "Practice", "Exercism", true, "en"),
            new("Data Structures & Algorithms in C# - Pluralsight",
                "https://www.pluralsight.com/paths/algorithms-and-data-structures-in-csharp",
                "Course", "Pluralsight", false, "en"),
            new("Algorithms Specialization - Coursera (Stanford)",
                "https://www.coursera.org/specializations/algorithms",
                "Course", "Coursera", false, "en"),
            new("r/csharp Interview Preparation Thread",
                "https://www.reddit.com/r/csharp/comments/hs1u1e/",
                "Article", "Reddit", true, "en"),
            new("roadmap.sh - Computer Science Roadmap",
                "https://roadmap.sh/computer-science",
                "Tool", "roadmap.sh", true, "en"),
            new("DOU.ua - Algorithms & Interview Articles",
                "https://dou.ua/lenta/tags/algorithms/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── System Design ───────────────────
        [SystemDesign] =
        [
            new("Designing Data-Intensive Applications by Martin Kleppmann",
                "https://dataintensive.net/",
                "Book", "O'Reilly", false, "en"),
            new("System Design Primer (GitHub)",
                "https://github.com/donnemartin/system-design-primer",
                "Article", "GitHub", true, "en"),
            new("Microsoft - .NET Microservices Architecture Guide",
                "https://learn.microsoft.com/en-us/dotnet/architecture/microservices/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("ByteByteGo - System Design Fundamentals (YouTube)",
                "https://www.youtube.com/@ByteByteGo",
                "Video", "YouTube", true, "en"),
            new("Nick Chapsas - Microservices & Architecture in .NET",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("Domain-Driven Design Quickly (InfoQ)",
                "https://www.infoq.com/minibooks/domain-driven-design-quickly/",
                "Book", "InfoQ", true, "en"),
            new("eShopOnContainers Reference Application",
                "https://github.com/dotnet/eShop",
                "Practice", "GitHub", true, "en"),
            new("Clean Architecture with ASP.NET Core (Pluralsight)",
                "https://www.pluralsight.com/courses/clean-architecture-asp-dot-net-core",
                "Course", "Pluralsight", false, "en"),
            new("CQRS & Event Sourcing in .NET - Amichai Mantinband",
                "https://www.youtube.com/@amaborhossain",
                "Video", "YouTube", true, "en"),
            new("roadmap.sh - System Design Roadmap",
                "https://roadmap.sh/system-design",
                "Tool", "roadmap.sh", true, "en"),
            new("DOU.ua - Architecture & System Design",
                "https://dou.ua/lenta/tags/architecture/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── .NET Internals ───────────────────
        [DotNetInternals] =
        [
            new("CLR via C# by Jeffrey Richter",
                "https://www.microsoftpressstore.com/store/clr-via-c-sharp-9780735667457",
                "Book", "Microsoft Press", false, "en"),
            new("Writing High-Performance .NET Code by Ben Watson",
                "https://www.writinghighperf.net/",
                "Book", "Ben Watson", false, "en"),
            new("Pro .NET Memory Management by Konrad Kokosa",
                "https://prodotnetmemory.com/",
                "Book", "Apress", false, "en"),
            new("Microsoft Learn - .NET Fundamentals",
                "https://learn.microsoft.com/en-us/dotnet/fundamentals/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Microsoft Learn - C# Language Reference",
                "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Nick Chapsas - .NET Internals & Performance",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("Adam Sitnik - High Performance .NET (Blog)",
                "https://adamsitnik.com/",
                "Article", "Personal Blog", true, "en"),
            new("C# in Depth by Jon Skeet (4th Edition)",
                "https://csharpindepth.com/",
                "Book", "Manning", false, "en"),
            new("Pluralsight - C# Path with Skill IQ Assessment",
                "https://www.pluralsight.com/paths/csharp",
                "Course", "Pluralsight", false, "en"),
            new("Stephen Toub - .NET Blog Posts (Performance)",
                "https://devblogs.microsoft.com/dotnet/author/toub/",
                "Article", "Microsoft DevBlogs", true, "en"),
            new("AspNetCore-Developer-Roadmap",
                "https://github.com/MoienTajik/AspNetCore-Developer-Roadmap",
                "Tool", "GitHub", true, "en"),
            new("DOU.ua - .NET Developer Roadmap & Articles",
                "https://dou.ua/lenta/tags/.net/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Backend Architecture / ASP.NET Core ───────────────────
        [BackendArch] =
        [
            new("Microsoft Learn - ASP.NET Core Fundamentals",
                "https://learn.microsoft.com/en-us/aspnet/core/fundamentals/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Andrew Lock - .NET Escapades Blog",
                "https://andrewlock.net/",
                "Article", "Personal Blog", true, "en"),
            new("ASP.NET Core in Action by Andrew Lock",
                "https://www.manning.com/books/asp-net-core-in-action-third-edition",
                "Book", "Manning", false, "en"),
            new("Tim Corey - ASP.NET Core Full Courses",
                "https://www.youtube.com/@IAmTimCorey",
                "Video", "YouTube", true, "en"),
            new("Raw Coding - ASP.NET Core Deep Dives",
                "https://www.youtube.com/@RawCoding",
                "Video", "YouTube", true, "en"),
            new("Minimal APIs in .NET - Official Tutorial",
                "https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Amichai Mantinband - Clean Architecture in .NET",
                "https://www.youtube.com/@amaborhossain",
                "Video", "YouTube", true, "en"),
            new("Microsoft Learn - gRPC Services in ASP.NET Core",
                "https://learn.microsoft.com/en-us/aspnet/core/grpc/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("ASP.NET Core REST API Course - Udemy",
                "https://www.udemy.com/topic/asp-net-core/",
                "Course", "Udemy", false, "en"),
            new("roadmap.sh - ASP.NET Core Developer Roadmap",
                "https://roadmap.sh/aspnet-core",
                "Tool", "roadmap.sh", true, "en"),
            new("AspNetCore-Developer-Roadmap",
                "https://github.com/MoienTajik/AspNetCore-Developer-Roadmap",
                "Tool", "GitHub", true, "en"),
            new("DOU.ua - ASP.NET / Backend Articles",
                "https://dou.ua/lenta/tags/asp.net/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Databases ───────────────────
        [Databases] =
        [
            new("Microsoft Learn - SQL Server Documentation",
                "https://learn.microsoft.com/en-us/sql/sql-server/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("PostgreSQL Official Documentation",
                "https://www.postgresql.org/docs/current/",
                "Documentation", "Official Docs", true, "en"),
            new("Use The Index, Luke - SQL Indexing & Tuning",
                "https://use-the-index-luke.com/",
                "Article", "Official Docs", true, "en"),
            new("Redis University - Free Courses",
                "https://university.redis.io/",
                "Course", "Redis", true, "en"),
            new("MongoDB University - Free Courses",
                "https://learn.mongodb.com/",
                "Course", "MongoDB", true, "en"),
            new("SQLBolt - Interactive SQL Lessons",
                "https://sqlbolt.com/",
                "Practice", "SQLBolt", true, "en"),
            new("HackerRank SQL Domain",
                "https://www.hackerrank.com/domains/sql",
                "Practice", "HackerRank", true, "en"),
            new("Designing Data-Intensive Applications (database chapters)",
                "https://dataintensive.net/",
                "Book", "O'Reilly", false, "en"),
            new("Tim Corey - SQL Fundamentals for C# Developers",
                "https://www.youtube.com/@IAmTimCorey",
                "Video", "YouTube", true, "en"),
            new("Microsoft Learn - Azure Cosmos DB",
                "https://learn.microsoft.com/en-us/azure/cosmos-db/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("DOU.ua - Database Articles & Discussions",
                "https://dou.ua/lenta/tags/database/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── ORM / Data Access ───────────────────
        [OrmDataAccess] =
        [
            new("Microsoft Learn - Entity Framework Core Documentation",
                "https://learn.microsoft.com/en-us/ef/core/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Julie Lerman - EF Core Courses (Pluralsight)",
                "https://www.pluralsight.com/authors/julie-lerman",
                "Course", "Pluralsight", false, "en"),
            new("Entity Framework Core in Action by Jon P Smith",
                "https://www.manning.com/books/entity-framework-core-in-action-second-edition",
                "Book", "Manning", false, "en"),
            new("Nick Chapsas - EF Core Tips & Tricks",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("Dapper Documentation & Tutorial",
                "https://github.com/DapperLib/Dapper",
                "Documentation", "GitHub", true, "en"),
            new("Microsoft Learn - EF Core Migrations",
                "https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Raw Coding - EF Core & Data Access Patterns",
                "https://www.youtube.com/@RawCoding",
                "Video", "YouTube", true, "en"),
            new("DOU.ua - ORM & Data Access Discussions",
                "https://dou.ua/forums/topic/41292/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Performance ───────────────────
        [Performance] =
        [
            new("Writing High-Performance .NET Code by Ben Watson",
                "https://www.writinghighperf.net/",
                "Book", "Ben Watson", false, "en"),
            new("Pro .NET Benchmarking by Andrey Akinshin",
                "https://aakinshin.net/prodotnetbenchmarking/",
                "Book", "Apress", false, "en"),
            new("BenchmarkDotNet Official Documentation",
                "https://benchmarkdotnet.org/",
                "Documentation", "Official Docs", true, "en"),
            new("Microsoft Learn - .NET Performance Tips",
                "https://learn.microsoft.com/en-us/dotnet/framework/performance/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Adam Sitnik - Performance Blog",
                "https://adamsitnik.com/",
                "Article", "Personal Blog", true, "en"),
            new("Nick Chapsas - .NET Performance Deep Dives",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("Stephen Toub - Performance Improvements in .NET",
                "https://devblogs.microsoft.com/dotnet/author/toub/",
                "Article", "Microsoft DevBlogs", true, "en"),
            new("Microsoft Learn - Memory & Span Usage Guidelines",
                "https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("dotnet-counters & dotnet-trace Diagnostic Tools",
                "https://learn.microsoft.com/en-us/dotnet/core/diagnostics/",
                "Tool", "Microsoft Learn", true, "en"),
            new("DOU.ua - .NET Performance Articles",
                "https://dou.ua/lenta/tags/performance/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Concurrency ───────────────────
        [Concurrency] =
        [
            new("Concurrency in C# Cookbook by Stephen Cleary",
                "https://www.oreilly.com/library/view/concurrency-in-c/9781492054498/",
                "Book", "O'Reilly", false, "en"),
            new("Stephen Cleary - Async/Await Best Practices Blog",
                "https://blog.stephencleary.com/",
                "Article", "Personal Blog", true, "en"),
            new("Microsoft Learn - Asynchronous Programming",
                "https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Microsoft Learn - Task Parallel Library",
                "https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Microsoft Learn - System.Threading.Channels",
                "https://learn.microsoft.com/en-us/dotnet/core/extensions/channels",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Nick Chapsas - Channels & Async Patterns in .NET",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("Pluralsight - Concurrent & Parallel Programming in .NET",
                "https://www.pluralsight.com/courses/c-sharp-concurrent-collections",
                "Course", "Pluralsight", false, "en"),
            new("Threading in C# by Joseph Albahari (Online Book)",
                "https://www.albahari.com/threading/",
                "Book", "Personal Blog", true, "en"),
            new("DOU.ua - Concurrency & Multithreading Articles",
                "https://dou.ua/lenta/tags/multithreading/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Cloud ───────────────────
        [Cloud] =
        [
            new("Microsoft Learn - Azure for .NET Developers",
                "https://learn.microsoft.com/en-us/dotnet/azure/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Microsoft Learn - Azure Developer (AZ-204) Learning Path",
                "https://learn.microsoft.com/en-us/credentials/certifications/azure-developer/",
                "Certification", "Microsoft Learn", true, "en"),
            new("Microsoft Learn - Azure Solutions Architect (AZ-305)",
                "https://learn.microsoft.com/en-us/credentials/certifications/azure-solutions-architect/",
                "Certification", "Microsoft Learn", true, "en"),
            new("AWS Certified Developer - Associate",
                "https://aws.amazon.com/certification/certified-developer-associate/",
                "Certification", "AWS", false, "en"),
            new("Microsoft Learn - Azure Functions",
                "https://learn.microsoft.com/en-us/azure/azure-functions/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Microsoft Learn - Azure Service Bus",
                "https://learn.microsoft.com/en-us/azure/service-bus-messaging/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Tim Corey - Azure for .NET Developers",
                "https://www.youtube.com/@IAmTimCorey",
                "Video", "YouTube", true, "en"),
            new("Azure Cloud Adoption Framework",
                "https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Pluralsight - Azure Developer Path",
                "https://www.pluralsight.com/paths/developing-solutions-for-microsoft-azure-az-204",
                "Course", "Pluralsight", false, "en"),
            new("DOU.ua - Cloud & Azure Articles",
                "https://dou.ua/lenta/tags/cloud/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Security ───────────────────
        [Security] =
        [
            new("Microsoft Learn - ASP.NET Core Security",
                "https://learn.microsoft.com/en-us/aspnet/core/security/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("OWASP Top 10",
                "https://owasp.org/www-project-top-ten/",
                "Documentation", "Official Docs", true, "en"),
            new("Microsoft Learn - OAuth 2.0 & OpenID Connect",
                "https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols",
                "Documentation", "Microsoft Learn", true, "en"),
            new("Duende IdentityServer Documentation",
                "https://docs.duendesoftware.com/identityserver/",
                "Documentation", "Official Docs", true, "en"),
            new("Scott Brady - Identity & Security Blog",
                "https://www.scottbrady91.com/",
                "Article", "Personal Blog", true, "en"),
            new("Nick Chapsas - Security in .NET",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("OAuth 2.0 in Action by Justin Richer & Antonio Sanso",
                "https://www.manning.com/books/oauth-2-in-action",
                "Book", "Manning", false, "en"),
            new("OWASP .NET Security Cheat Sheet",
                "https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html",
                "Documentation", "Official Docs", true, "en"),
            new("DOU.ua - Security Articles",
                "https://dou.ua/lenta/tags/security/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Testing ───────────────────
        [Testing] =
        [
            new("Unit Testing Principles, Practices, and Patterns by Vladimir Khorikov",
                "https://www.manning.com/books/unit-testing",
                "Book", "Manning", false, "en"),
            new("Microsoft Learn - Unit Testing in .NET",
                "https://learn.microsoft.com/en-us/dotnet/core/testing/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("xUnit.net Official Documentation",
                "https://xunit.net/",
                "Documentation", "Official Docs", true, "en"),
            new("FluentAssertions Documentation",
                "https://fluentassertions.com/",
                "Documentation", "Official Docs", true, "en"),
            new("Testcontainers for .NET",
                "https://dotnet.testcontainers.org/",
                "Documentation", "Official Docs", true, "en"),
            new("Nick Chapsas - Testing in .NET",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("Tim Corey - Unit Testing in C# with xUnit",
                "https://www.youtube.com/@IAmTimCorey",
                "Video", "YouTube", true, "en"),
            new("DevSkiller - Automated Coding Tests & Assessment",
                "https://devskiller.com/",
                "Practice", "DevSkiller", false, "en"),
            new("Andrew Lock - Integration Testing in ASP.NET Core",
                "https://andrewlock.net/tag/integration-testing/",
                "Article", "Personal Blog", true, "en"),
            new("NSubstitute Documentation",
                "https://nsubstitute.github.io/help.html",
                "Documentation", "Official Docs", true, "en"),
            new("DOU.ua - Testing & QA Articles",
                "https://dou.ua/lenta/tags/testing/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Frontend ───────────────────
        [Frontend] =
        [
            new("Microsoft Learn - Blazor Documentation",
                "https://learn.microsoft.com/en-us/aspnet/core/blazor/",
                "Documentation", "Microsoft Learn", true, "en"),
            new("React Official Documentation",
                "https://react.dev/",
                "Documentation", "Official Docs", true, "en"),
            new("TypeScript Official Handbook",
                "https://www.typescriptlang.org/docs/handbook/",
                "Documentation", "Official Docs", true, "en"),
            new("Angular Official Documentation",
                "https://angular.dev/",
                "Documentation", "Official Docs", true, "en"),
            new("Frontend Masters Courses",
                "https://frontendmasters.com/",
                "Course", "Frontend Masters", false, "en"),
            new("Nick Chapsas - Blazor in .NET",
                "https://www.youtube.com/@nickchapsas",
                "Video", "YouTube", true, "en"),
            new("HTMX - High-Power Tools for HTML",
                "https://htmx.org/docs/",
                "Documentation", "Official Docs", true, "en"),
            new("roadmap.sh - Frontend Developer Roadmap",
                "https://roadmap.sh/frontend",
                "Tool", "roadmap.sh", true, "en"),
            new("DOU.ua - Frontend Articles",
                "https://dou.ua/lenta/tags/frontend/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── DevOps ───────────────────
        [DevOps] =
        [
            new("Docker Official Getting Started",
                "https://docs.docker.com/get-started/",
                "Documentation", "Official Docs", true, "en"),
            new("Docker Deep Dive by Nigel Poulton",
                "https://nigelpoulton.com/books/",
                "Book", "Nigel Poulton", false, "en"),
            new("Kubernetes Official Documentation",
                "https://kubernetes.io/docs/home/",
                "Documentation", "Official Docs", true, "en"),
            new("Kubernetes CKAD Certification",
                "https://training.linuxfoundation.org/certification/certified-kubernetes-application-developer-ckad/",
                "Certification", "Linux Foundation", false, "en"),
            new("Kubernetes CKA Certification",
                "https://training.linuxfoundation.org/certification/certified-kubernetes-administrator-cka/",
                "Certification", "Linux Foundation", false, "en"),
            new("GitHub Actions Official Documentation",
                "https://docs.github.com/en/actions",
                "Documentation", "Official Docs", true, "en"),
            new("Terraform Documentation & Tutorials",
                "https://developer.hashicorp.com/terraform/tutorials",
                "Documentation", "Official Docs", true, "en"),
            new("Microsoft Learn - Azure DevOps Engineer (AZ-400)",
                "https://learn.microsoft.com/en-us/credentials/certifications/devops-engineer/",
                "Certification", "Microsoft Learn", true, "en"),
            new("OpenTelemetry .NET Documentation",
                "https://opentelemetry.io/docs/languages/net/",
                "Documentation", "Official Docs", true, "en"),
            new("Pluralsight - Docker & Kubernetes Path",
                "https://www.pluralsight.com/paths/docker-and-kubernetes",
                "Course", "Pluralsight", false, "en"),
            new("roadmap.sh - DevOps Roadmap",
                "https://roadmap.sh/devops",
                "Tool", "roadmap.sh", true, "en"),
            new("DOU.ua - DevOps Articles",
                "https://dou.ua/lenta/tags/devops/",
                "Article", "DOU.ua", true, "uk"),
        ],

        // ─────────────────── Behavioral / Soft Skills ───────────────────
        [Behavioral] =
        [
            new("The Manager's Path by Camille Fournier",
                "https://www.oreilly.com/library/view/the-managers-path/9781491973882/",
                "Book", "O'Reilly", false, "en"),
            new("Cracking the Coding Interview (Behavioral Section)",
                "https://www.crackingthecodinginterview.com/",
                "Book", "CareerCup", false, "en"),
            new("DOU.ua - Career & Soft Skills Articles",
                "https://dou.ua/lenta/tags/career/",
                "Article", "DOU.ua", true, "uk"),
            new("r/dotnet Interview Experience Threads",
                "https://www.reddit.com/r/dotnet/",
                "Article", "Reddit", true, "en"),
            new("r/csharp Interview Questions & Answers",
                "https://www.reddit.com/r/csharp/comments/hs1u1e/",
                "Article", "Reddit", true, "en"),
            new("Pluralsight - Communication Skills for Developers",
                "https://www.pluralsight.com/courses/communication-skills-developers",
                "Course", "Pluralsight", false, "en"),
            new("roadmap.sh - Career Path Guidance",
                "https://roadmap.sh/",
                "Tool", "roadmap.sh", true, "en"),
        ],
    };

    // ══════════════════════════════════════════════════════════════════
    //  CERTIFICATIONS DATABASE
    // ══════════════════════════════════════════════════════════════════

    private static readonly IReadOnlyList<CertificationRecommendation> AllCertifications =
    [
        new("Microsoft Certified: Azure Developer Associate (AZ-204)",
            "Microsoft",
            "https://learn.microsoft.com/en-us/credentials/certifications/azure-developer/",
            "Associate",
            12.0,
            ["Azure", "Azure Functions", "Azure Service Bus", "CosmosDB", "ASP.NET Core", "Cloud"]),

        new("Microsoft Certified: Azure Solutions Architect Expert (AZ-305)",
            "Microsoft",
            "https://learn.microsoft.com/en-us/credentials/certifications/azure-solutions-architect/",
            "Expert",
            18.0,
            ["Azure", "System Design", "Architecture", "Cloud", "Distributed Systems", "Microservices"]),

        new("Microsoft Certified: DevOps Engineer Expert (AZ-400)",
            "Microsoft",
            "https://learn.microsoft.com/en-us/credentials/certifications/devops-engineer/",
            "Expert",
            15.0,
            ["CI/CD", "Azure DevOps", "Docker", "Kubernetes", "Terraform", "GitHub Actions", "DevOps"]),

        new("AWS Certified Developer - Associate",
            "AWS",
            "https://aws.amazon.com/certification/certified-developer-associate/",
            "Associate",
            10.0,
            ["AWS", "Lambda", "S3", "Cloud", "Serverless"]),

        new("Certified Kubernetes Application Developer (CKAD)",
            "Linux Foundation",
            "https://training.linuxfoundation.org/certification/certified-kubernetes-application-developer-ckad/",
            "Associate",
            14.0,
            ["Kubernetes", "Docker", "Containers", "DevOps", "Cloud"]),

        new("Certified Kubernetes Administrator (CKA)",
            "Linux Foundation",
            "https://training.linuxfoundation.org/certification/certified-kubernetes-administrator-cka/",
            "Professional",
            16.0,
            ["Kubernetes", "Docker", "Containers", "DevOps", "Cloud", "System Design"]),
    ];

    // ══════════════════════════════════════════════════════════════════
    //  PRACTICE PLATFORMS DATABASE
    // ══════════════════════════════════════════════════════════════════

    private static readonly IReadOnlyList<PracticeResource> AllPracticeResources =
    [
        new("LeetCode",
            "https://leetcode.com/problemset/",
            "Industry-standard algorithm practice with thousands of problems. Filter by difficulty and topic. Many .NET interview questions sourced from here.",
            ["Algorithms", "Data Structures", "Problem Solving", "Dynamic Programming"],
            true),

        new("HackerRank",
            "https://www.hackerrank.com/domains/algorithms",
            "Structured problem sets for algorithms, data structures, SQL, and C#. Includes certification challenges. Used by companies for screening.",
            ["Algorithms", "Data Structures", "SQL", "C#", "Problem Solving"],
            true),

        new("NeetCode",
            "https://neetcode.io/roadmap",
            "Curated problem roadmap with video explanations. Covers the most common 150 problems organized by pattern. Excellent for interview prep.",
            ["Algorithms", "Data Structures", "System Design", "Problem Solving"],
            true),

        new("Codewars",
            "https://www.codewars.com/kata/search/csharp",
            "Community-driven kata (code challenges) with C# support. Focuses on code quality and elegant solutions. Ranked difficulty system.",
            ["C#", "Algorithms", "LINQ", "Problem Solving"],
            true),

        new("Exercism",
            "https://exercism.org/tracks/csharp",
            "Free mentored C# track with 100+ exercises. Excellent for learning idiomatic C# patterns. Community mentors review your solutions.",
            ["C#", ".NET", "LINQ", "Algorithms", "Data Structures"],
            true),

        new("DevSkiller",
            "https://devskiller.com/",
            "Automated technical assessment platform used by employers. Practice real-world .NET coding tasks similar to those in actual hiring pipelines.",
            ["C#", ".NET", "ASP.NET Core", "Entity Framework", "Testing", "SQL"],
            false),

        new("SQLBolt",
            "https://sqlbolt.com/",
            "Interactive SQL tutorials and exercises. Learn SQL step by step from basics to advanced queries.",
            ["SQL", "Databases"],
            true),

        new("Pluralsight Skill IQ",
            "https://www.pluralsight.com/product/skill-iq",
            "Free skill assessments that measure your proficiency level across hundreds of tech skills. Use to identify knowledge gaps.",
            ["C#", ".NET", "ASP.NET Core", "Azure", "SQL", "Docker", "Kubernetes"],
            true),

        new("Microsoft Learn Assessments",
            "https://learn.microsoft.com/en-us/credentials/",
            "Free assessments and learning paths aligned with Microsoft certifications. Track progress and earn credentials.",
            ["Azure", ".NET", "C#", "ASP.NET Core", "SQL Server"],
            true),

        new("VisuAlgo",
            "https://visualgo.net/",
            "Visualize algorithms and data structures through animation. Excellent for understanding how algorithms work step by step.",
            ["Algorithms", "Data Structures", "Sorting", "Graphs"],
            true),

        new("GitHub Copilot",
            "https://github.com/features/copilot",
            "AI pair programmer for practice. Use it to generate tests, explore APIs, and learn by studying its suggestions. Also useful for practicing code review.",
            ["C#", ".NET", "ASP.NET Core", "Testing"],
            false),
    ];

    // ══════════════════════════════════════════════════════════════════
    //  LEARNING PATH DEFINITIONS (per topic area)
    // ══════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, TopicLearningPath> LearningPaths = BuildAllLearningPaths();

    // ══════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recommends resources for specific skill gaps, sorted by priority.
    /// </summary>
    public ResourcePlan RecommendForGaps(IReadOnlyList<string> gapSkills, string currentLevel = "Mid")
    {
        logger.LogInformation("Generating resource plan for {Count} skill gaps at {Level} level",
            gapSkills.Count, currentLevel);

        var multiplier = LevelMultiplier(currentLevel);
        var recommendations = new List<ResourceRecommendation>();

        foreach (var skill in gapSkills)
        {
            var topicArea = ResolveTopicArea(skill);
            var links = GetLinksForSkill(skill, topicArea);
            var baseHours = BaseHoursPerTopic.GetValueOrDefault(topicArea, 20);
            var hours = (int)Math.Ceiling(baseHours * multiplier);
            var priority = DeterminePriority(topicArea);

            recommendations.Add(new ResourceRecommendation(skill, topicArea, links, hours, priority));
        }

        // Sort by priority: Critical > High > Medium > Low
        recommendations.Sort((a, b) => PriorityOrdinal(a.Priority).CompareTo(PriorityOrdinal(b.Priority)));

        var totalHours = recommendations.Sum(r => r.EstimatedHours);

        var quickWins = recommendations
            .Where(r => r.EstimatedHours <= QuickWinHoursThreshold)
            .Select(r => r.Skill)
            .ToList();

        var overallPriority = recommendations.Count switch
        {
            0 => "Low",
            _ => recommendations[0].Priority,
        };

        logger.LogInformation(
            "Resource plan generated: {RecCount} recommendations, {Hours}h total, {QW} quick wins, priority={Priority}",
            recommendations.Count, totalHours, quickWins.Count, overallPriority);

        return new ResourcePlan(recommendations, totalHours, overallPriority, quickWins);
    }

    /// <summary>
    /// Returns a full multi-phase learning path for a given topic area.
    /// </summary>
    public TopicLearningPath GetLearningPath(string topicId)
    {
        logger.LogInformation("Retrieving learning path for topic '{TopicId}'", topicId);

        if (LearningPaths.TryGetValue(topicId, out var path))
        {
            return path;
        }

        logger.LogWarning("No learning path found for topic '{TopicId}', returning generic path", topicId);
        return BuildGenericLearningPath(topicId);
    }

    /// <summary>
    /// Recommends certifications based on the user's existing skills and target roles.
    /// </summary>
    public List<CertificationRecommendation> RecommendCertifications(UserProfile profile)
    {
        logger.LogInformation("Evaluating certifications for user '{Name}'", profile.Personal.Name);

        var userSkills = profile.Skills
            .Select(s => s.SkillName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetRoles = profile.Personal.TargetRoles;

        var scored = new List<(CertificationRecommendation Cert, double Score)>();

        foreach (var cert in AllCertifications)
        {
            // Score based on overlap between user's skills and cert's relevant skills
            var overlap = cert.RelevantSkills.Count(s => userSkills.Contains(s));
            var relevanceRatio = cert.RelevantSkills.Count > 0
                ? (double)overlap / cert.RelevantSkills.Count
                : 0.0;

            // Boost score for certs that match target roles
            var roleBoost = 0.0;
            if (targetRoles.Any(r => r.Contains("Architect", StringComparison.OrdinalIgnoreCase))
                && cert.Level is "Expert" or "Professional")
            {
                roleBoost = 0.3;
            }
            else if (targetRoles.Any(r => r.Contains("Senior", StringComparison.OrdinalIgnoreCase))
                && cert.Level is "Associate" or "Expert")
            {
                roleBoost = 0.2;
            }
            else if (targetRoles.Any(r => r.Contains("DevOps", StringComparison.OrdinalIgnoreCase))
                && cert.Name.Contains("DevOps", StringComparison.OrdinalIgnoreCase))
            {
                roleBoost = 0.4;
            }

            // Consider salary impact
            var salaryBoost = cert.SalaryImpactPercent / 100.0;

            var totalScore = relevanceRatio * 0.4 + roleBoost + salaryBoost * 0.3;

            // Only recommend if there is some skill overlap (at least 1 matching skill)
            if (overlap > 0)
            {
                scored.Add((cert, totalScore));
            }
        }

        var result = scored
            .OrderByDescending(x => x.Score)
            .Select(x => x.Cert)
            .ToList();

        logger.LogInformation("Recommended {Count} certifications for user", result.Count);
        return result;
    }

    /// <summary>
    /// Returns practice platforms relevant to the provided skills.
    /// </summary>
    public List<PracticeResource> RecommendPractice(IReadOnlyList<string> skills)
    {
        logger.LogInformation("Finding practice platforms for {Count} skills", skills.Count);

        var normalizedSkills = skills
            .Select(s => s.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scored = new List<(PracticeResource Resource, int MatchCount)>();

        foreach (var resource in AllPracticeResources)
        {
            var matchCount = resource.Skills.Count(s => normalizedSkills.Contains(s));
            if (matchCount > 0)
            {
                scored.Add((resource, matchCount));
            }
        }

        // Also add universally relevant platforms when there are algorithm-related skills
        var hasAlgoSkills = normalizedSkills.Overlaps(
            ["Algorithms", "Data Structures", "Problem Solving", "LeetCode"]);

        if (hasAlgoSkills)
        {
            foreach (var resource in AllPracticeResources)
            {
                if (resource.Platform is "LeetCode" or "HackerRank" or "NeetCode" or "Codewars"
                    && scored.All(s => s.Resource.Platform != resource.Platform))
                {
                    scored.Add((resource, 1));
                }
            }
        }

        var result = scored
            .OrderByDescending(x => x.MatchCount)
            .ThenBy(x => x.Resource.IsFree ? 0 : 1) // Prefer free first
            .Select(x => x.Resource)
            .ToList();

        logger.LogInformation("Recommended {Count} practice platforms", result.Count);
        return result;
    }

    // ══════════════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════════════

    private static string ResolveTopicArea(string skill)
    {
        if (SkillToTopicMap.TryGetValue(skill, out var topic))
            return topic;

        // Fuzzy fallback: check if any key is contained in the skill name
        foreach (var (key, value) in SkillToTopicMap)
        {
            if (skill.Contains(key, StringComparison.OrdinalIgnoreCase)
                || key.Contains(skill, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return BackendArch; // sensible default for .NET developers
    }

    private static IReadOnlyList<LearningLink> GetLinksForSkill(string skill, string topicArea)
    {
        if (!TopicResources.TryGetValue(topicArea, out var topicLinks))
            return [];

        // Return all resources for the topic, supplemented with skill-specific filtering
        // where possible. We always return the full set because all resources in a topic
        // are relevant to every skill within that topic.
        return topicLinks;
    }

    private static string DeterminePriority(string topicArea)
    {
        var order = TopicPriorityOrder.GetValueOrDefault(topicArea, 10);
        return order switch
        {
            <= 3 => "Critical",
            <= 6 => "High",
            <= 10 => "Medium",
            _ => "Low",
        };
    }

    private static int PriorityOrdinal(string priority) => priority switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        "Low" => 3,
        _ => 4,
    };

    private static TopicLearningPath BuildGenericLearningPath(string topicId) =>
        new(topicId, topicId,
        [
            new("Fundamentals",
            [
                new("Microsoft Learn - .NET Documentation",
                    "https://learn.microsoft.com/en-us/dotnet/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("roadmap.sh - Developer Roadmaps",
                    "https://roadmap.sh/",
                    "Tool", "roadmap.sh", true, "en"),
                new("AspNetCore-Developer-Roadmap",
                    "https://github.com/MoienTajik/AspNetCore-Developer-Roadmap",
                    "Tool", "GitHub", true, "en"),
            ],
            10, []),
            new("Intermediate",
            [
                new("Pluralsight - C# & .NET Paths",
                    "https://www.pluralsight.com/paths/csharp",
                    "Course", "Pluralsight", false, "en"),
                new("Nick Chapsas YouTube Channel",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ],
            15, ["Fundamentals"]),
            new("Advanced",
            [
                new("DOU.ua - Community Articles",
                    "https://dou.ua/",
                    "Article", "DOU.ua", true, "uk"),
            ],
            20, ["Intermediate"]),
        ]);

    // ──────────────────────────────────────────────────────────────────
    //  Build all topic learning paths
    // ──────────────────────────────────────────────────────────────────

    private static Dictionary<string, TopicLearningPath> BuildAllLearningPaths() => new()
    {
        [Algorithms] = new(Algorithms, "Algorithms & Data Structures",
        [
            new("Fundamentals", [
                new("Grokking Algorithms by Aditya Bhargava",
                    "https://www.manning.com/books/grokking-algorithms",
                    "Book", "Manning", false, "en"),
                new("VisuAlgo - Visualising Data Structures & Algorithms",
                    "https://visualgo.net/",
                    "Tool", "VisuAlgo", true, "en"),
                new("Exercism C# Track",
                    "https://exercism.org/tracks/csharp",
                    "Practice", "Exercism", true, "en"),
                new("roadmap.sh - Computer Science Roadmap",
                    "https://roadmap.sh/computer-science",
                    "Tool", "roadmap.sh", true, "en"),
            ], 15, []),

            new("Intermediate", [
                new("NeetCode 150 Roadmap",
                    "https://neetcode.io/roadmap",
                    "Practice", "NeetCode", true, "en"),
                new("LeetCode - Medium Problems",
                    "https://leetcode.com/problemset/?difficulty=MEDIUM",
                    "Practice", "LeetCode", true, "en"),
                new("HackerRank Algorithms Domain",
                    "https://www.hackerrank.com/domains/algorithms",
                    "Practice", "HackerRank", true, "en"),
                new("Algorithms Specialization - Coursera (Stanford)",
                    "https://www.coursera.org/specializations/algorithms",
                    "Course", "Coursera", false, "en"),
            ], 25, ["Fundamentals"]),

            new("Advanced", [
                new("LeetCode - Hard Problems",
                    "https://leetcode.com/problemset/?difficulty=HARD",
                    "Practice", "LeetCode", true, "en"),
                new("Codewars - Advanced C# Kata",
                    "https://www.codewars.com/kata/search/csharp?q=&r%5B%5D=-6&r%5B%5D=-7&r%5B%5D=-8",
                    "Practice", "Codewars", true, "en"),
                new("Introduction to Algorithms (CLRS)",
                    "https://mitpress.mit.edu/9780262046305/introduction-to-algorithms/",
                    "Book", "MIT Press", false, "en"),
            ], 30, ["Intermediate"]),

            new("Expert", [
                new("Competitive Programming Contests",
                    "https://codeforces.com/",
                    "Practice", "Codeforces", true, "en"),
                new("Pluralsight - Advanced Algorithms & Data Structures",
                    "https://www.pluralsight.com/paths/algorithms-and-data-structures-in-csharp",
                    "Course", "Pluralsight", false, "en"),
            ], 40, ["Advanced"]),
        ]),

        [SystemDesign] = new(SystemDesign, "System Design",
        [
            new("Fundamentals", [
                new("System Design Primer (GitHub)",
                    "https://github.com/donnemartin/system-design-primer",
                    "Article", "GitHub", true, "en"),
                new("ByteByteGo - System Design Fundamentals",
                    "https://www.youtube.com/@ByteByteGo",
                    "Video", "YouTube", true, "en"),
                new("Microsoft - .NET Architecture Guides",
                    "https://learn.microsoft.com/en-us/dotnet/architecture/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("roadmap.sh - System Design Roadmap",
                    "https://roadmap.sh/system-design",
                    "Tool", "roadmap.sh", true, "en"),
            ], 12, []),

            new("Intermediate", [
                new("Designing Data-Intensive Applications by Martin Kleppmann",
                    "https://dataintensive.net/",
                    "Book", "O'Reilly", false, "en"),
                new("Microsoft - Microservices Architecture Guide",
                    "https://learn.microsoft.com/en-us/dotnet/architecture/microservices/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("eShop Reference Application",
                    "https://github.com/dotnet/eShop",
                    "Practice", "GitHub", true, "en"),
                new("Nick Chapsas - Architecture in .NET",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ], 20, ["Fundamentals"]),

            new("Advanced", [
                new("Domain-Driven Design by Eric Evans",
                    "https://www.domainlanguage.com/ddd/",
                    "Book", "Addison-Wesley", false, "en"),
                new("Clean Architecture with ASP.NET Core (Pluralsight)",
                    "https://www.pluralsight.com/courses/clean-architecture-asp-dot-net-core",
                    "Course", "Pluralsight", false, "en"),
                new("Amichai Mantinband - CQRS & DDD in .NET",
                    "https://www.youtube.com/@amaborhossain",
                    "Video", "YouTube", true, "en"),
            ], 25, ["Intermediate"]),

            new("Expert", [
                new("Building Microservices by Sam Newman (2nd Edition)",
                    "https://www.oreilly.com/library/view/building-microservices-2nd/9781492034018/",
                    "Book", "O'Reilly", false, "en"),
                new("Patterns of Enterprise Application Architecture by Martin Fowler",
                    "https://martinfowler.com/books/eaa.html",
                    "Book", "Addison-Wesley", false, "en"),
                new("DOU.ua - Architecture Discussions",
                    "https://dou.ua/lenta/tags/architecture/",
                    "Article", "DOU.ua", true, "uk"),
            ], 30, ["Advanced"]),
        ]),

        [DotNetInternals] = new(DotNetInternals, ".NET Internals",
        [
            new("Fundamentals", [
                new("Microsoft Learn - .NET Fundamentals",
                    "https://learn.microsoft.com/en-us/dotnet/fundamentals/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Microsoft Learn - C# Language Reference",
                    "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Tim Corey - C# Fundamentals",
                    "https://www.youtube.com/@IAmTimCorey",
                    "Video", "YouTube", true, "en"),
                new("Pluralsight - C# Path with Skill IQ",
                    "https://www.pluralsight.com/paths/csharp",
                    "Course", "Pluralsight", false, "en"),
            ], 10, []),

            new("Intermediate", [
                new("C# in Depth by Jon Skeet (4th Edition)",
                    "https://csharpindepth.com/",
                    "Book", "Manning", false, "en"),
                new("Nick Chapsas - Modern C# & .NET",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
                new("Stephen Toub - .NET Blog Posts",
                    "https://devblogs.microsoft.com/dotnet/author/toub/",
                    "Article", "Microsoft DevBlogs", true, "en"),
                new("AspNetCore-Developer-Roadmap",
                    "https://github.com/MoienTajik/AspNetCore-Developer-Roadmap",
                    "Tool", "GitHub", true, "en"),
            ], 20, ["Fundamentals"]),

            new("Advanced", [
                new("CLR via C# by Jeffrey Richter",
                    "https://www.microsoftpressstore.com/store/clr-via-c-sharp-9780735667457",
                    "Book", "Microsoft Press", false, "en"),
                new("Pro .NET Memory Management by Konrad Kokosa",
                    "https://prodotnetmemory.com/",
                    "Book", "Apress", false, "en"),
                new("Adam Sitnik - High-Performance .NET Blog",
                    "https://adamsitnik.com/",
                    "Article", "Personal Blog", true, "en"),
            ], 25, ["Intermediate"]),

            new("Expert", [
                new("Writing High-Performance .NET Code by Ben Watson",
                    "https://www.writinghighperf.net/",
                    "Book", "Ben Watson", false, "en"),
                new(".NET Runtime Source Code (GitHub)",
                    "https://github.com/dotnet/runtime",
                    "Practice", "GitHub", true, "en"),
                new("DOU.ua - .NET Deep Dive Articles",
                    "https://dou.ua/lenta/tags/.net/",
                    "Article", "DOU.ua", true, "uk"),
            ], 30, ["Advanced"]),
        ]),

        [BackendArch] = new(BackendArch, "Backend Architecture / ASP.NET Core",
        [
            new("Fundamentals", [
                new("Microsoft Learn - ASP.NET Core Fundamentals",
                    "https://learn.microsoft.com/en-us/aspnet/core/fundamentals/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Minimal APIs Tutorial",
                    "https://learn.microsoft.com/en-us/aspnet/core/tutorials/min-web-api",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Tim Corey - ASP.NET Core Basics",
                    "https://www.youtube.com/@IAmTimCorey",
                    "Video", "YouTube", true, "en"),
                new("roadmap.sh - ASP.NET Core Developer Roadmap",
                    "https://roadmap.sh/aspnet-core",
                    "Tool", "roadmap.sh", true, "en"),
            ], 10, []),

            new("Intermediate", [
                new("ASP.NET Core in Action by Andrew Lock",
                    "https://www.manning.com/books/asp-net-core-in-action-third-edition",
                    "Book", "Manning", false, "en"),
                new("Andrew Lock - .NET Escapades Blog",
                    "https://andrewlock.net/",
                    "Article", "Personal Blog", true, "en"),
                new("Raw Coding - ASP.NET Core Deep Dives",
                    "https://www.youtube.com/@RawCoding",
                    "Video", "YouTube", true, "en"),
                new("Microsoft Learn - gRPC in ASP.NET Core",
                    "https://learn.microsoft.com/en-us/aspnet/core/grpc/",
                    "Documentation", "Microsoft Learn", true, "en"),
            ], 15, ["Fundamentals"]),

            new("Advanced", [
                new("Amichai Mantinband - Clean Architecture in .NET",
                    "https://www.youtube.com/@amaborhossain",
                    "Video", "YouTube", true, "en"),
                new("ASP.NET Core REST API - Udemy Courses",
                    "https://www.udemy.com/topic/asp-net-core/",
                    "Course", "Udemy", false, "en"),
                new("AspNetCore-Developer-Roadmap",
                    "https://github.com/MoienTajik/AspNetCore-Developer-Roadmap",
                    "Tool", "GitHub", true, "en"),
                new("Nick Chapsas - Advanced ASP.NET Core Patterns",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ], 20, ["Intermediate"]),

            new("Expert", [
                new("Microsoft - Cloud Design Patterns",
                    "https://learn.microsoft.com/en-us/azure/architecture/patterns/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("DOU.ua - ASP.NET Core Discussions",
                    "https://dou.ua/lenta/tags/asp.net/",
                    "Article", "DOU.ua", true, "uk"),
            ], 15, ["Advanced"]),
        ]),

        [Databases] = new(Databases, "Databases",
        [
            new("Fundamentals", [
                new("SQLBolt - Interactive SQL Lessons",
                    "https://sqlbolt.com/",
                    "Practice", "SQLBolt", true, "en"),
                new("Microsoft Learn - SQL Server Documentation",
                    "https://learn.microsoft.com/en-us/sql/sql-server/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("HackerRank SQL Domain",
                    "https://www.hackerrank.com/domains/sql",
                    "Practice", "HackerRank", true, "en"),
            ], 10, []),

            new("Intermediate", [
                new("Use The Index, Luke - SQL Indexing & Tuning",
                    "https://use-the-index-luke.com/",
                    "Article", "Official Docs", true, "en"),
                new("PostgreSQL Official Documentation",
                    "https://www.postgresql.org/docs/current/",
                    "Documentation", "Official Docs", true, "en"),
                new("Redis University - Free Courses",
                    "https://university.redis.io/",
                    "Course", "Redis", true, "en"),
                new("MongoDB University - Free Courses",
                    "https://learn.mongodb.com/",
                    "Course", "MongoDB", true, "en"),
            ], 15, ["Fundamentals"]),

            new("Advanced", [
                new("Designing Data-Intensive Applications (database chapters)",
                    "https://dataintensive.net/",
                    "Book", "O'Reilly", false, "en"),
                new("Microsoft Learn - Azure Cosmos DB",
                    "https://learn.microsoft.com/en-us/azure/cosmos-db/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Tim Corey - Advanced SQL for Developers",
                    "https://www.youtube.com/@IAmTimCorey",
                    "Video", "YouTube", true, "en"),
            ], 20, ["Intermediate"]),

            new("Expert", [
                new("DOU.ua - Database Architecture Discussions",
                    "https://dou.ua/lenta/tags/database/",
                    "Article", "DOU.ua", true, "uk"),
            ], 15, ["Advanced"]),
        ]),

        [OrmDataAccess] = new(OrmDataAccess, "ORM & Data Access",
        [
            new("Fundamentals", [
                new("Microsoft Learn - EF Core Documentation",
                    "https://learn.microsoft.com/en-us/ef/core/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Microsoft Learn - EF Core Migrations",
                    "https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Tim Corey - EF Core Basics",
                    "https://www.youtube.com/@IAmTimCorey",
                    "Video", "YouTube", true, "en"),
            ], 6, []),

            new("Intermediate", [
                new("Julie Lerman - EF Core Courses (Pluralsight)",
                    "https://www.pluralsight.com/authors/julie-lerman",
                    "Course", "Pluralsight", false, "en"),
                new("Entity Framework Core in Action by Jon P Smith",
                    "https://www.manning.com/books/entity-framework-core-in-action-second-edition",
                    "Book", "Manning", false, "en"),
                new("Dapper Documentation & Tutorial",
                    "https://github.com/DapperLib/Dapper",
                    "Documentation", "GitHub", true, "en"),
            ], 10, ["Fundamentals"]),

            new("Advanced", [
                new("Nick Chapsas - EF Core Performance & Best Practices",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
                new("Raw Coding - Data Access Patterns in .NET",
                    "https://www.youtube.com/@RawCoding",
                    "Video", "YouTube", true, "en"),
                new("DOU.ua - ORM Discussions",
                    "https://dou.ua/forums/topic/41292/",
                    "Article", "DOU.ua", true, "uk"),
            ], 10, ["Intermediate"]),
        ]),

        [Performance] = new(Performance, "Performance",
        [
            new("Fundamentals", [
                new("Microsoft Learn - .NET Performance Tips",
                    "https://learn.microsoft.com/en-us/dotnet/framework/performance/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("BenchmarkDotNet Documentation",
                    "https://benchmarkdotnet.org/",
                    "Documentation", "Official Docs", true, "en"),
                new("Microsoft Learn - Diagnostic Tools",
                    "https://learn.microsoft.com/en-us/dotnet/core/diagnostics/",
                    "Tool", "Microsoft Learn", true, "en"),
            ], 8, []),

            new("Intermediate", [
                new("Pro .NET Benchmarking by Andrey Akinshin",
                    "https://aakinshin.net/prodotnetbenchmarking/",
                    "Book", "Apress", false, "en"),
                new("Microsoft Learn - Memory & Span Usage",
                    "https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Nick Chapsas - .NET Performance Videos",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ], 12, ["Fundamentals"]),

            new("Advanced", [
                new("Writing High-Performance .NET Code by Ben Watson",
                    "https://www.writinghighperf.net/",
                    "Book", "Ben Watson", false, "en"),
                new("Adam Sitnik - Performance Blog",
                    "https://adamsitnik.com/",
                    "Article", "Personal Blog", true, "en"),
                new("Stephen Toub - Annual .NET Performance Posts",
                    "https://devblogs.microsoft.com/dotnet/author/toub/",
                    "Article", "Microsoft DevBlogs", true, "en"),
            ], 15, ["Intermediate"]),

            new("Expert", [
                new("Pro .NET Memory Management by Konrad Kokosa",
                    "https://prodotnetmemory.com/",
                    "Book", "Apress", false, "en"),
                new("DOU.ua - Performance Articles",
                    "https://dou.ua/lenta/tags/performance/",
                    "Article", "DOU.ua", true, "uk"),
            ], 20, ["Advanced"]),
        ]),

        [Concurrency] = new(Concurrency, "Concurrency & Async Programming",
        [
            new("Fundamentals", [
                new("Microsoft Learn - Asynchronous Programming",
                    "https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Stephen Cleary - Async/Await Best Practices",
                    "https://blog.stephencleary.com/",
                    "Article", "Personal Blog", true, "en"),
                new("Threading in C# by Joseph Albahari (Free Online)",
                    "https://www.albahari.com/threading/",
                    "Book", "Personal Blog", true, "en"),
            ], 10, []),

            new("Intermediate", [
                new("Concurrency in C# Cookbook by Stephen Cleary",
                    "https://www.oreilly.com/library/view/concurrency-in-c/9781492054498/",
                    "Book", "O'Reilly", false, "en"),
                new("Microsoft Learn - Task Parallel Library",
                    "https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Microsoft Learn - Channels",
                    "https://learn.microsoft.com/en-us/dotnet/core/extensions/channels",
                    "Documentation", "Microsoft Learn", true, "en"),
            ], 15, ["Fundamentals"]),

            new("Advanced", [
                new("Nick Chapsas - Channels & Advanced Async",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
                new("Pluralsight - Concurrent Programming in .NET",
                    "https://www.pluralsight.com/courses/c-sharp-concurrent-collections",
                    "Course", "Pluralsight", false, "en"),
                new("DOU.ua - Concurrency Articles",
                    "https://dou.ua/lenta/tags/multithreading/",
                    "Article", "DOU.ua", true, "uk"),
            ], 20, ["Intermediate"]),
        ]),

        [Cloud] = new(Cloud, "Cloud (Azure & AWS)",
        [
            new("Fundamentals", [
                new("Microsoft Learn - Azure for .NET Developers",
                    "https://learn.microsoft.com/en-us/dotnet/azure/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Microsoft Learn - Azure Fundamentals (AZ-900) Path",
                    "https://learn.microsoft.com/en-us/credentials/certifications/azure-fundamentals/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Tim Corey - Azure for .NET Developers",
                    "https://www.youtube.com/@IAmTimCorey",
                    "Video", "YouTube", true, "en"),
            ], 12, []),

            new("Intermediate", [
                new("Microsoft Learn - AZ-204 Learning Path",
                    "https://learn.microsoft.com/en-us/credentials/certifications/azure-developer/",
                    "Certification", "Microsoft Learn", true, "en"),
                new("Microsoft Learn - Azure Functions",
                    "https://learn.microsoft.com/en-us/azure/azure-functions/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Pluralsight - Azure Developer Path (AZ-204)",
                    "https://www.pluralsight.com/paths/developing-solutions-for-microsoft-azure-az-204",
                    "Course", "Pluralsight", false, "en"),
                new("Azure Cloud Adoption Framework",
                    "https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/",
                    "Documentation", "Microsoft Learn", true, "en"),
            ], 20, ["Fundamentals"]),

            new("Advanced", [
                new("Microsoft Learn - AZ-305 (Solutions Architect)",
                    "https://learn.microsoft.com/en-us/credentials/certifications/azure-solutions-architect/",
                    "Certification", "Microsoft Learn", true, "en"),
                new("AWS Certified Developer - Associate",
                    "https://aws.amazon.com/certification/certified-developer-associate/",
                    "Certification", "AWS", false, "en"),
                new("Microsoft Learn - Azure Service Bus",
                    "https://learn.microsoft.com/en-us/azure/service-bus-messaging/",
                    "Documentation", "Microsoft Learn", true, "en"),
            ], 25, ["Intermediate"]),

            new("Expert", [
                new("DOU.ua - Cloud & Azure Articles",
                    "https://dou.ua/lenta/tags/cloud/",
                    "Article", "DOU.ua", true, "uk"),
            ], 15, ["Advanced"]),
        ]),

        [Security] = new(Security, "Security",
        [
            new("Fundamentals", [
                new("Microsoft Learn - ASP.NET Core Security",
                    "https://learn.microsoft.com/en-us/aspnet/core/security/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("OWASP Top 10",
                    "https://owasp.org/www-project-top-ten/",
                    "Documentation", "Official Docs", true, "en"),
                new("OWASP .NET Security Cheat Sheet",
                    "https://cheatsheetseries.owasp.org/cheatsheets/DotNet_Security_Cheat_Sheet.html",
                    "Documentation", "Official Docs", true, "en"),
            ], 8, []),

            new("Intermediate", [
                new("Microsoft Learn - OAuth 2.0 & OpenID Connect",
                    "https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("Duende IdentityServer Documentation",
                    "https://docs.duendesoftware.com/identityserver/",
                    "Documentation", "Official Docs", true, "en"),
                new("Scott Brady - Identity & Security Blog",
                    "https://www.scottbrady91.com/",
                    "Article", "Personal Blog", true, "en"),
                new("Nick Chapsas - Security in .NET",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ], 12, ["Fundamentals"]),

            new("Advanced", [
                new("OAuth 2.0 in Action by Justin Richer & Antonio Sanso",
                    "https://www.manning.com/books/oauth-2-in-action",
                    "Book", "Manning", false, "en"),
                new("DOU.ua - Security Articles",
                    "https://dou.ua/lenta/tags/security/",
                    "Article", "DOU.ua", true, "uk"),
            ], 15, ["Intermediate"]),
        ]),

        [Testing] = new(Testing, "Testing",
        [
            new("Fundamentals", [
                new("Microsoft Learn - Unit Testing in .NET",
                    "https://learn.microsoft.com/en-us/dotnet/core/testing/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("xUnit.net Official Documentation",
                    "https://xunit.net/",
                    "Documentation", "Official Docs", true, "en"),
                new("Tim Corey - Unit Testing in C#",
                    "https://www.youtube.com/@IAmTimCorey",
                    "Video", "YouTube", true, "en"),
            ], 8, []),

            new("Intermediate", [
                new("Unit Testing Principles by Vladimir Khorikov",
                    "https://www.manning.com/books/unit-testing",
                    "Book", "Manning", false, "en"),
                new("FluentAssertions Documentation",
                    "https://fluentassertions.com/",
                    "Documentation", "Official Docs", true, "en"),
                new("NSubstitute Documentation",
                    "https://nsubstitute.github.io/help.html",
                    "Documentation", "Official Docs", true, "en"),
                new("Nick Chapsas - Testing in .NET",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ], 12, ["Fundamentals"]),

            new("Advanced", [
                new("Testcontainers for .NET",
                    "https://dotnet.testcontainers.org/",
                    "Documentation", "Official Docs", true, "en"),
                new("Andrew Lock - Integration Testing in ASP.NET Core",
                    "https://andrewlock.net/tag/integration-testing/",
                    "Article", "Personal Blog", true, "en"),
                new("DevSkiller - Automated Coding Assessments",
                    "https://devskiller.com/",
                    "Practice", "DevSkiller", false, "en"),
                new("DOU.ua - Testing Articles",
                    "https://dou.ua/lenta/tags/testing/",
                    "Article", "DOU.ua", true, "uk"),
            ], 15, ["Intermediate"]),
        ]),

        [Frontend] = new(Frontend, "Frontend",
        [
            new("Fundamentals", [
                new("Microsoft Learn - Blazor Documentation",
                    "https://learn.microsoft.com/en-us/aspnet/core/blazor/",
                    "Documentation", "Microsoft Learn", true, "en"),
                new("React Official Documentation",
                    "https://react.dev/",
                    "Documentation", "Official Docs", true, "en"),
                new("TypeScript Official Handbook",
                    "https://www.typescriptlang.org/docs/handbook/",
                    "Documentation", "Official Docs", true, "en"),
                new("roadmap.sh - Frontend Developer Roadmap",
                    "https://roadmap.sh/frontend",
                    "Tool", "roadmap.sh", true, "en"),
            ], 10, []),

            new("Intermediate", [
                new("Angular Official Documentation",
                    "https://angular.dev/",
                    "Documentation", "Official Docs", true, "en"),
                new("Frontend Masters Courses",
                    "https://frontendmasters.com/",
                    "Course", "Frontend Masters", false, "en"),
                new("HTMX Documentation",
                    "https://htmx.org/docs/",
                    "Documentation", "Official Docs", true, "en"),
                new("Nick Chapsas - Blazor in .NET",
                    "https://www.youtube.com/@nickchapsas",
                    "Video", "YouTube", true, "en"),
            ], 15, ["Fundamentals"]),

            new("Advanced", [
                new("DOU.ua - Frontend Articles",
                    "https://dou.ua/lenta/tags/frontend/",
                    "Article", "DOU.ua", true, "uk"),
            ], 20, ["Intermediate"]),
        ]),

        [DevOps] = new(DevOps, "DevOps & Containers",
        [
            new("Fundamentals", [
                new("Docker Official Getting Started",
                    "https://docs.docker.com/get-started/",
                    "Documentation", "Official Docs", true, "en"),
                new("GitHub Actions Official Documentation",
                    "https://docs.github.com/en/actions",
                    "Documentation", "Official Docs", true, "en"),
                new("roadmap.sh - DevOps Roadmap",
                    "https://roadmap.sh/devops",
                    "Tool", "roadmap.sh", true, "en"),
            ], 10, []),

            new("Intermediate", [
                new("Docker Deep Dive by Nigel Poulton",
                    "https://nigelpoulton.com/books/",
                    "Book", "Nigel Poulton", false, "en"),
                new("Kubernetes Official Documentation",
                    "https://kubernetes.io/docs/home/",
                    "Documentation", "Official Docs", true, "en"),
                new("Terraform Tutorials",
                    "https://developer.hashicorp.com/terraform/tutorials",
                    "Documentation", "Official Docs", true, "en"),
                new("Pluralsight - Docker & Kubernetes Path",
                    "https://www.pluralsight.com/paths/docker-and-kubernetes",
                    "Course", "Pluralsight", false, "en"),
            ], 15, ["Fundamentals"]),

            new("Advanced", [
                new("CKAD Certification",
                    "https://training.linuxfoundation.org/certification/certified-kubernetes-application-developer-ckad/",
                    "Certification", "Linux Foundation", false, "en"),
                new("Microsoft Learn - AZ-400 (DevOps Engineer)",
                    "https://learn.microsoft.com/en-us/credentials/certifications/devops-engineer/",
                    "Certification", "Microsoft Learn", true, "en"),
                new("OpenTelemetry .NET Documentation",
                    "https://opentelemetry.io/docs/languages/net/",
                    "Documentation", "Official Docs", true, "en"),
            ], 20, ["Intermediate"]),

            new("Expert", [
                new("CKA Certification",
                    "https://training.linuxfoundation.org/certification/certified-kubernetes-administrator-cka/",
                    "Certification", "Linux Foundation", false, "en"),
                new("DOU.ua - DevOps Articles",
                    "https://dou.ua/lenta/tags/devops/",
                    "Article", "DOU.ua", true, "uk"),
            ], 25, ["Advanced"]),
        ]),

        [Behavioral] = new(Behavioral, "Behavioral & Soft Skills",
        [
            new("Fundamentals", [
                new("Cracking the Coding Interview (Behavioral Section)",
                    "https://www.crackingthecodinginterview.com/",
                    "Book", "CareerCup", false, "en"),
                new("r/csharp Interview Preparation Thread",
                    "https://www.reddit.com/r/csharp/comments/hs1u1e/",
                    "Article", "Reddit", true, "en"),
                new("DOU.ua - Career & Soft Skills Articles",
                    "https://dou.ua/lenta/tags/career/",
                    "Article", "DOU.ua", true, "uk"),
            ], 4, []),

            new("Intermediate", [
                new("The Manager's Path by Camille Fournier",
                    "https://www.oreilly.com/library/view/the-managers-path/9781491973882/",
                    "Book", "O'Reilly", false, "en"),
                new("Pluralsight - Communication Skills for Developers",
                    "https://www.pluralsight.com/courses/communication-skills-developers",
                    "Course", "Pluralsight", false, "en"),
                new("r/dotnet Interview Experience Threads",
                    "https://www.reddit.com/r/dotnet/",
                    "Article", "Reddit", true, "en"),
            ], 6, ["Fundamentals"]),

            new("Advanced", [
                new("roadmap.sh - Career Path Guidance",
                    "https://roadmap.sh/",
                    "Tool", "roadmap.sh", true, "en"),
            ], 5, ["Intermediate"]),
        ]),
    };
}
