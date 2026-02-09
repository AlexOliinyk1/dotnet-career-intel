namespace CareerIntel.Intelligence;

using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using CareerIntel.Matching;
using Microsoft.Extensions.Logging;

#region Result Records

public sealed record InferredTopic(
    string TopicId,
    string TopicName,
    int Confidence,
    List<string> DetectedKeywords);

public sealed record InferredTopicProfile(
    string VacancyId,
    List<InferredTopic> InferredTopics,
    List<string> SenioritySignals,
    string EstimatedInterviewDifficulty,
    string SalaryContext);

public sealed record TopicDemandEntry(
    string TopicId,
    string TopicName,
    int VacancyCount,
    double PercentageOfVacancies,
    decimal AvgSalaryForTopic,
    List<string> TopCompanies);

public sealed record MarketTopicDemand(
    List<TopicDemandEntry> TopicRankings,
    int TotalVacanciesAnalyzed,
    List<string> HottestSkills);

public sealed record TopicGapAnalysis(
    List<InferredTopic> MatchedTopics,
    List<InferredTopic> GapTopics,
    List<InferredTopic> BonusTopics,
    double ReadinessScore,
    List<string> RecommendedStudyOrder);

#endregion

public sealed class TopicInferenceEngine(ILogger<TopicInferenceEngine> logger)
{
    // ─────────────────────────────────────────────────────────────
    //  Comprehensive keyword database — topic ID -> keyword patterns
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, TopicKeywordEntry> KeywordDatabase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["algorithms"] = new("Algorithms & Data Structures",
        [
            "big-o", "big o", "time complexity", "space complexity", "leetcode", "hackerrank", "codility",
            "data structure", "data structures", "binary search", "linear search", "sorting", "merge sort",
            "quick sort", "bubble sort", "insertion sort", "heap sort", "radix sort", "counting sort",
            "hash", "hash map", "hash table", "hash set", "dictionary", "hashing",
            "tree", "binary tree", "BST", "binary search tree", "AVL", "red-black tree", "B-tree",
            "graph", "graph traversal", "BFS", "DFS", "breadth-first", "depth-first", "dijkstra",
            "bellman-ford", "topological sort", "adjacency list", "adjacency matrix",
            "dynamic programming", "memoization", "tabulation", "knapsack", "longest common subsequence",
            "sliding window", "two pointer", "two pointers", "fast and slow pointer",
            "linked list", "singly linked", "doubly linked", "circular linked list",
            "stack", "queue", "deque", "priority queue", "monotonic stack", "monotonic queue",
            "heap", "min heap", "max heap", "binary heap",
            "trie", "prefix tree", "suffix tree", "suffix array",
            "recursion", "recursive", "backtracking", "branch and bound",
            "greedy", "greedy algorithm", "divide and conquer",
            "union find", "disjoint set", "segment tree", "fenwick tree", "bit manipulation",
            "algorithm", "algorithmic"
        ]),

        ["system-design"] = new("System Design",
        [
            "system design", "systems design", "architecture design", "high-level design", "low-level design",
            "distributed", "distributed system", "distributed computing",
            "microservice", "microservices", "micro-service", "micro-services",
            "monolith", "monolithic", "modular monolith",
            "CQRS", "command query responsibility segregation",
            "event sourcing", "event store", "event-driven", "event driven",
            "saga", "saga pattern", "choreography", "orchestration",
            "message queue", "message broker", "messaging",
            "kafka", "rabbitmq", "rabbit mq", "service bus", "azure service bus",
            "load balancer", "load balancing", "nginx", "HAProxy", "reverse proxy",
            "caching", "cache", "cache invalidation", "write-through", "write-behind", "cache-aside",
            "CDN", "content delivery network",
            "sharding", "horizontal scaling", "vertical scaling", "partitioning",
            "replication", "leader-follower", "master-slave", "consensus",
            "CAP theorem", "CAP", "eventual consistency", "strong consistency",
            "domain-driven", "domain driven design", "DDD",
            "bounded context", "aggregate", "aggregate root", "domain event", "ubiquitous language",
            "API design", "idempotency", "rate limiting", "circuit breaker", "bulkhead",
            "leader election", "service discovery", "service mesh", "sidecar pattern",
            "back pressure", "backpressure", "fan-out", "fan-in", "pub/sub", "publish subscribe",
            "scalability", "scalable", "high availability", "fault tolerance", "resilience"
        ]),

        ["dotnet-internals"] = new(".NET Internals",
        [
            "CLR", "common language runtime", "CoreCLR",
            "GC", "garbage collection", "garbage collector", "gen 0", "gen 1", "gen 2",
            "large object heap", "LOH", "pinned object heap", "POH", "workstation GC", "server GC",
            "JIT", "just-in-time", "tiered compilation", "ready to run", "R2R",
            "AOT", "ahead-of-time", "NativeAOT", "native AOT", "trimming", "self-contained",
            "IL", "MSIL", "CIL", "intermediate language", "bytecode",
            "Span<", "Span<T>", "ReadOnlySpan", "Memory<", "Memory<T>", "ReadOnlyMemory",
            "ref struct", "ref return", "ref local", "ref readonly",
            "stackalloc", "stack allocation",
            "value type", "reference type", "struct", "value tuple",
            "boxing", "unboxing", "box", "unbox",
            "finalizer", "destructor", "weak reference", "weak event",
            "IDisposable", "Dispose", "using statement", "dispose pattern", "SafeHandle",
            "AppDomain", "AssemblyLoadContext", "assembly loading",
            "assembly", "module", "metadata",
            "reflection", "System.Reflection", "MethodInfo", "PropertyInfo", "dynamic",
            "expression tree", "Expression<", "ExpressionVisitor",
            "roslyn", "Roslyn", "compiler platform",
            "source generator", "source generators", "incremental generator", "analyzer",
            "unsafe", "pointer", "fixed", "Marshal", "P/Invoke", "interop",
            "generic", "generics", "covariance", "contravariance", "type constraint",
            "delegate", "event", "multicast delegate", "Func<", "Action<", "Predicate<"
        ]),

        ["backend-architecture"] = new("Backend Architecture",
        [
            "ASP.NET", "ASP.NET Core", "aspnet", "asp.net",
            "middleware", "request pipeline", "HTTP pipeline",
            "minimal API", "minimal APIs", "map endpoints",
            "controller", "controllers", "API controller", "MVC controller",
            "MVC", "model-view-controller",
            "REST", "RESTful", "REST API", "Web API", "HTTP API",
            "gRPC", "protobuf", "protocol buffer",
            "GraphQL", "HotChocolate", "StrawberryShake",
            "API gateway", "Ocelot", "YARP",
            "reverse proxy",
            "health check", "health checks", "liveness", "readiness",
            "rate limit", "rate limiting", "throttling",
            "filter", "action filter", "result filter", "exception filter", "authorization filter",
            "model binding", "model validation", "data annotation",
            "validation", "FluentValidation", "fluent validation",
            "MediatR", "Mediator", "mediator pattern", "CQRS handler",
            "pipeline behavior", "request handler",
            "clean architecture", "clean arch",
            "onion architecture",
            "hexagonal", "hexagonal architecture", "ports and adapters",
            "n-layer", "n-tier", "layered architecture",
            "vertical slice", "vertical slices", "vertical slice architecture",
            "modular monolith",
            "dependency injection", "DI", "IoC", "inversion of control", "service collection",
            "Swagger", "OpenAPI", "Swashbuckle", "NSwag",
            "versioning", "API versioning",
            "background service", "hosted service", "IHostedService", "BackgroundService",
            "SignalR", "real-time", "WebSocket", "Server-Sent Events",
            "output caching", "response caching", "ETag",
            "Polly", "retry", "circuit breaker", "resilience"
        ]),

        ["databases"] = new("Databases",
        [
            "SQL", "T-SQL", "PL/SQL",
            "PostgreSQL", "Postgres", "Npgsql",
            "MSSQL", "SQL Server", "Microsoft SQL",
            "MySQL", "MariaDB",
            "NoSQL", "document database",
            "MongoDB", "Mongo",
            "CosmosDB", "Cosmos DB", "Azure Cosmos",
            "Redis", "cache", "key-value store",
            "Elasticsearch", "Elastic Search", "Lucene", "OpenSearch",
            "index", "indexing", "covering index",
            "query optimization", "query plan", "execution plan", "query performance",
            "stored procedure", "stored proc", "function", "trigger",
            "transaction", "transactions", "begin transaction",
            "ACID", "atomicity", "consistency", "isolation", "durability",
            "isolation level", "read committed", "serializable", "snapshot isolation", "repeatable read",
            "deadlock", "lock", "row lock", "table lock", "optimistic locking", "pessimistic locking",
            "migration", "database migration", "schema migration",
            "partition", "table partition", "range partition",
            "replication", "read replica", "geo-replication",
            "sharding", "horizontal partition",
            "connection pool", "connection pooling", "connection string",
            "B-tree", "B+ tree",
            "clustered index", "nonclustered index", "non-clustered index", "composite index",
            "foreign key", "primary key", "unique constraint", "check constraint",
            "normalization", "1NF", "2NF", "3NF", "BCNF",
            "denormalization", "materialized view",
            "CTE", "common table expression", "window function", "OVER", "ROW_NUMBER", "RANK",
            "temporal table", "audit trail",
            "DynamoDB", "Cassandra", "Neo4j", "graph database",
            "SQLite", "LiteDB", "RavenDB"
        ]),

        ["orm-data-access"] = new("ORM & Data Access",
        [
            "Entity Framework", "EF", "EF Core", "Entity Framework Core",
            "Dapper", "micro-ORM", "micro ORM",
            "LINQ", "Language Integrated Query", "LINQ to SQL", "LINQ to Entities",
            "IQueryable", "IEnumerable vs IQueryable", "expression tree",
            "DbContext", "DbSet", "database context",
            "migration", "EF migration", "add-migration", "update-database",
            "code-first", "code first", "model-first",
            "database-first", "database first", "scaffold",
            "lazy loading", "lazy load",
            "eager loading", "Include", ".Include(", ".ThenInclude(",
            "explicit loading", "Entry", "Collection", "Reference",
            "change tracking", "change tracker", "ChangeTracker",
            "AsNoTracking", "no tracking", "tracking behavior",
            "compiled query", "compiled queries", "EF.CompileQuery",
            "raw SQL", "FromSqlRaw", "ExecuteSqlRaw", "SqlQuery",
            "repository pattern", "repository", "generic repository",
            "unit of work", "UoW",
            "N+1", "N+1 problem", "select N+1",
            "query splitting", "split query", "AsSplitQuery",
            "value converter", "ValueConverter", "HasConversion",
            "owned entity", "owned type", "OwnsOne", "OwnsMany",
            "TPH", "table per hierarchy",
            "TPT", "table per type",
            "TPC", "table per concrete type",
            "global query filter", "soft delete",
            "concurrency token", "row version", "optimistic concurrency",
            "shadow property", "backing field",
            "interceptor", "SaveChanges interceptor", "DbCommandInterceptor",
            "ADO.NET", "SqlConnection", "SqlCommand", "DataReader"
        ]),

        ["performance"] = new("Performance & Optimization",
        [
            "performance", "performant", "high performance", "high-performance",
            "profiling", "profiler", "profile",
            "benchmark", "benchmarking", "BenchmarkDotNet",
            "memory leak", "memory leaks", "memory pressure",
            "allocation", "allocations", "zero allocation", "allocation-free",
            "ArrayPool", "MemoryPool", "object pool", "ObjectPool",
            "Span", "Span<T>", "ReadOnlySpan", "Memory<T>",
            "pipeline", "pipelining", "System.IO.Pipelines",
            "caching", "cache", "in-memory cache", "IMemoryCache", "MemoryCache",
            "Redis", "distributed cache", "IDistributedCache",
            "output caching", "response caching",
            "response compression", "gzip", "brotli",
            "hot path", "critical path",
            "PerfView", "dotMemory", "dotTrace", "Visual Studio Profiler",
            "memory-mapped", "memory mapped file",
            "throughput", "latency", "p99", "p95", "percentile",
            "thread pool starvation", "thread pool",
            "async optimization", "ValueTask",
            "string interning", "StringBuilder", "string concatenation",
            "SIMD", "vectorization", "Vector<T>", "Vector128", "Vector256",
            "inlining", "AggressiveInlining", "MethodImpl",
            "struct", "readonly struct", "record struct",
            "frozen collection", "FrozenDictionary", "FrozenSet",
            "lazy initialization", "Lazy<T>",
            "connection pooling", "HTTP client pool", "IHttpClientFactory",
            "load testing", "stress testing", "capacity planning",
            "optimize", "optimization", "tuning", "fine-tuning"
        ]),

        ["concurrency"] = new("Concurrency & Async Programming",
        [
            "async", "asynchronous", "async/await",
            "await", "awaitable", "awaiter",
            "Task", "Task<T>", "ValueTask", "ValueTask<T>",
            "parallel", "parallelism", "parallel processing",
            "thread", "threading", "multi-thread", "multithreaded", "multithreading",
            "concurrent", "concurrency", "concurrent programming",
            "lock", "lock statement", "Monitor", "SpinLock", "ReaderWriterLock",
            "semaphore", "SemaphoreSlim", "Semaphore",
            "mutex", "Mutex", "named mutex",
            "channel", "Channel<T>", "System.Threading.Channels", "bounded channel", "unbounded channel",
            "producer-consumer", "producer consumer",
            "CancellationToken", "CancellationTokenSource", "cancellation",
            "ConfigureAwait", "ConfigureAwait(false)",
            "SynchronizationContext", "synchronization context",
            "TaskScheduler", "task scheduler", "custom scheduler",
            "Parallel.ForEach", "Parallel.ForEachAsync", "Parallel.For", "Parallel.Invoke",
            "PLINQ", "AsParallel", "parallel LINQ",
            "Interlocked", "Interlocked.Increment", "Interlocked.CompareExchange",
            "volatile", "Volatile.Read", "Volatile.Write",
            "async stream", "IAsyncEnumerable", "IAsyncEnumerator", "await foreach",
            "TaskCompletionSource", "TaskFactory",
            "WhenAll", "WhenAny", "Task.WhenAll", "Task.WhenAny",
            "async disposal", "IAsyncDisposable", "DisposeAsync",
            "thread safety", "thread-safe", "race condition", "deadlock",
            "actor model", "Dataflow", "TPL Dataflow", "ActionBlock", "TransformBlock",
            "barrier", "CountdownEvent", "ManualResetEvent", "AutoResetEvent"
        ]),

        ["cloud"] = new("Cloud & Infrastructure",
        [
            "Azure", "Microsoft Azure",
            "AWS", "Amazon Web Services",
            "cloud", "cloud-native", "cloud native",
            "App Service", "Azure App Service", "Web App",
            "Container Apps", "Azure Container Apps",
            "AKS", "Azure Kubernetes Service",
            "Kubernetes", "k8s", "kubectl", "pod", "deployment", "service mesh",
            "Lambda", "AWS Lambda",
            "EC2", "Elastic Compute",
            "S3", "Simple Storage Service", "blob storage", "Azure Blob",
            "Azure Functions", "functions app", "durable functions",
            "serverless", "FaaS", "function as a service",
            "Service Bus", "Azure Service Bus", "queue", "topic", "subscription",
            "Event Hub", "Event Hubs", "event streaming",
            "Event Grid", "Azure Event Grid",
            "Key Vault", "Azure Key Vault", "secrets management",
            "managed identity", "service principal", "workload identity",
            "Terraform", "HCL", "terraform plan", "terraform apply",
            "Bicep", "ARM template", "Azure Resource Manager",
            "Pulumi", "infrastructure as code", "IaC",
            "blob storage", "table storage", "queue storage",
            "CDN", "Azure CDN", "CloudFront",
            "API Management", "APIM", "Azure API Management",
            "Application Insights", "App Insights",
            "Monitor", "Azure Monitor", "Log Analytics",
            "CloudWatch", "AWS CloudWatch",
            "SQS", "Simple Queue Service",
            "SNS", "Simple Notification Service",
            "DynamoDB",
            "GCP", "Google Cloud",
            "ECS", "Fargate", "EKS",
            "Azure DevOps", "Azure Pipelines",
            "Azure SQL", "Azure Database",
            "Elastic Beanstalk",
            "VPC", "virtual network", "VNet", "subnet", "NSG",
            "Azure Front Door", "Traffic Manager", "Application Gateway"
        ]),

        ["security"] = new("Security",
        [
            "OWASP", "OWASP Top 10",
            "security", "secure coding", "security best practices",
            "authentication", "auth", "authn",
            "authorization", "authz", "authorize",
            "OAuth", "OAuth 2.0", "OAuth2",
            "OpenID", "OpenID Connect", "OIDC",
            "JWT", "JSON Web Token", "access token", "refresh token", "bearer token",
            "CORS", "Cross-Origin Resource Sharing",
            "CSRF", "Cross-Site Request Forgery", "antiforgery",
            "XSS", "Cross-Site Scripting", "script injection",
            "SQL injection", "injection attack", "command injection",
            "Content Security Policy", "CSP",
            "HTTPS", "TLS", "SSL", "certificate", "X.509",
            "encryption", "AES", "RSA", "symmetric", "asymmetric", "encrypt", "decrypt",
            "hashing", "SHA", "SHA-256", "SHA-512", "MD5",
            "bcrypt", "PBKDF2", "Argon2", "password hashing",
            "Data Protection", "ASP.NET Data Protection", "DPAPI",
            "Identity", "ASP.NET Identity", "IdentityServer", "Duende",
            "claims", "claim", "ClaimsPrincipal", "ClaimsIdentity",
            "policy", "authorization policy", "requirement", "handler",
            "role", "role-based", "RBAC",
            "ABAC", "attribute-based access control",
            "MFA", "multi-factor", "2FA", "two-factor",
            "rate limiting", "brute force", "account lockout",
            "WAF", "Web Application Firewall",
            "penetration testing", "pen test", "security audit", "vulnerability",
            "secret", "secrets", "secret management",
            "SAML", "SSO", "single sign-on", "federation",
            "API key", "API keys", "token validation",
            "input validation", "sanitization", "output encoding"
        ]),

        ["testing"] = new("Testing",
        [
            "test", "tests", "testing",
            "unit test", "unit tests", "unit testing",
            "integration test", "integration tests", "integration testing",
            "xUnit", "xunit", "Fact", "Theory", "InlineData",
            "NUnit", "nunit", "TestFixture", "TestCase",
            "MSTest", "mstest", "TestMethod", "TestClass",
            "Moq", "moq", "Mock<", "mock", "mocking", "stub", "fake",
            "NSubstitute", "Substitute.For",
            "WebApplicationFactory", "TestServer", "in-memory server",
            "Testcontainers", "test containers", "docker test",
            "TDD", "test-driven", "test driven development",
            "BDD", "behavior-driven", "behaviour-driven",
            "SpecFlow", "Gherkin", "Cucumber",
            "code coverage", "coverage", "line coverage", "branch coverage",
            "Coverlet", "ReportGenerator",
            "Pact", "contract test", "contract testing", "consumer-driven",
            "WireMock", "HTTP mock", "mock server",
            "Playwright", "browser testing", "browser automation",
            "Selenium", "WebDriver", "ChromeDriver",
            "E2E", "end-to-end", "end to end",
            "smoke test", "smoke testing", "sanity test",
            "load test", "load testing", "performance test",
            "k6", "NBomber", "JMeter", "Gatling",
            "test pyramid", "testing pyramid",
            "AAA", "Arrange Act Assert", "Given When Then",
            "snapshot testing", "approval testing", "Verify",
            "mutation testing", "Stryker",
            "Bogus", "AutoFixture", "test data", "fixture",
            "FluentAssertions", "Shouldly", "assertion"
        ]),

        ["frontend"] = new("Frontend & UI",
        [
            "Blazor", "Blazor Server", "Blazor WebAssembly", "Blazor WASM", "Blazor Hybrid",
            "React", "ReactJS", "React.js",
            "Angular", "AngularJS",
            "Vue", "Vue.js", "VueJS", "Nuxt",
            "TypeScript", "TS",
            "JavaScript", "JS", "ECMAScript", "ES6",
            "HTML", "HTML5",
            "CSS", "CSS3", "SCSS", "SASS", "Less",
            "SignalR", "real-time", "real time",
            "WebSocket", "WebSockets", "web socket",
            "SPA", "single page application", "single-page",
            "SSR", "server-side rendering", "server side rendering",
            "Razor", "Razor Pages", "Razor Components", ".cshtml",
            "Web Components", "custom elements",
            "Material UI", "MUI", "Material Design",
            "Tailwind", "Tailwind CSS",
            "Bootstrap",
            "webpack", "Webpack",
            "Vite", "esbuild", "Rollup",
            "npm", "yarn", "pnpm", "node_modules",
            "REST client", "HTTP client", "fetch", "axios",
            "state management", "Redux", "MobX", "Zustand", "Pinia",
            "Next.js", "NextJS",
            "responsive", "mobile-first", "responsive design",
            "accessibility", "a11y", "WCAG", "ARIA",
            "PWA", "progressive web app",
            "MAUI", ".NET MAUI", "Xamarin", "mobile development",
            "WPF", "Windows Forms", "WinForms", "WinUI", "UWP",
            "Electron", "Tauri"
        ]),

        ["devops"] = new("DevOps & Infrastructure",
        [
            "Docker", "docker", "dockerfile", "Dockerfile", "container", "containerization",
            "docker-compose", "docker compose", "compose file",
            "Kubernetes", "k8s", "kubectl", "Helm", "Helm chart",
            "CI/CD", "CI", "CD", "continuous integration", "continuous delivery", "continuous deployment",
            "GitHub Actions", "GitHub Action", "workflow", ".github/workflows",
            "Azure DevOps", "Azure Pipelines", "YAML pipeline",
            "GitLab CI", "GitLab", ".gitlab-ci",
            "Jenkins", "Jenkinsfile",
            "Terraform", "terraform", "HCL",
            "ArgoCD", "Argo CD", "GitOps",
            "Prometheus", "PromQL", "metrics",
            "Grafana", "dashboard", "alerting",
            "ELK", "Elastic Stack",
            "Elastic", "Elasticsearch",
            "Kibana", "log visualization",
            "Logstash", "log pipeline",
            "Serilog", "structured logging", "NLog", "log4net",
            "OpenTelemetry", "OTel", "OTLP",
            "Jaeger", "distributed tracing",
            "Zipkin", "trace",
            "health check", "health checks",
            "feature flag", "feature flags", "feature toggle", "LaunchDarkly",
            "canary", "canary deployment", "canary release",
            "blue-green", "blue/green", "blue green deployment",
            "rolling update", "rolling deployment", "zero downtime",
            "infrastructure as code", "IaC",
            "monitoring", "monitor",
            "alerting", "alert", "PagerDuty", "OpsGenie",
            "SRE", "site reliability", "reliability engineering",
            "incident response", "incident management", "postmortem",
            "runbook", "playbook",
            "observability", "three pillars",
            "logging", "log", "logs",
            "tracing", "trace", "traces",
            "metrics", "metric",
            "Ansible", "Chef", "Puppet",
            "Octopus Deploy", "Octopus",
            "Nginx", "reverse proxy", "Traefik", "Envoy",
            "registry", "container registry", "ACR", "ECR", "Docker Hub"
        ]),

        ["behavioral"] = new("Behavioral & Leadership",
        [
            "leadership", "leader", "lead a team",
            "mentoring", "mentor", "coaching", "coach",
            "agile", "Agile methodology",
            "scrum", "Scrum Master", "scrum ceremonies",
            "kanban", "Kanban board",
            "sprint", "sprint planning", "sprint review",
            "retrospective", "retro",
            "stand-up", "standup", "daily stand-up", "daily scrum",
            "code review", "code reviews", "PR review", "peer review",
            "pull request", "merge request",
            "stakeholder", "stakeholders", "stakeholder management",
            "estimation", "story points", "planning poker", "t-shirt sizing",
            "planning", "roadmap", "backlog", "backlog grooming", "refinement",
            "communication", "communicate",
            "conflict", "conflict resolution",
            "team lead", "tech lead", "engineering lead", "engineering manager",
            "architect", "solution architect", "software architect",
            "collaboration", "cross-functional", "cross-team",
            "documentation", "technical writing", "ADR", "architecture decision record",
            "ownership", "accountability", "responsibility",
            "problem solving", "problem-solving", "critical thinking",
            "time management", "prioritization", "priority",
            "pair programming", "mob programming",
            "knowledge sharing", "tech talk", "brown bag",
            "onboarding", "ramping up",
            "feedback", "constructive feedback", "1-on-1", "one-on-one",
            "growth mindset", "continuous improvement", "kaizen",
            "remote work", "distributed team", "async communication"
        ])
    };

    // ─────────────────────────────────────────────────────────────
    //  Seniority signal keywords
    // ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, List<string>> SeniorityKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Junior"] =
        [
            "junior", "entry level", "entry-level", "trainee", "intern", "internship",
            "0-2 years", "0-1 years", "1-2 years", "graduate", "fresh graduate",
            "no experience required", "beginner", "early career"
        ],
        ["Mid"] =
        [
            "middle", "mid-level", "mid level", "midlevel", "intermediate",
            "2-5 years", "2-4 years", "3-5 years", "3+ years", "2+ years",
            "some experience", "moderate experience"
        ],
        ["Senior"] =
        [
            "senior", "sr.", "sr ", "5+ years", "5-10 years", "7+ years", "8+ years",
            "lead", "principal", "staff", "architect",
            "expert", "advanced", "extensive experience",
            "10+ years", "deep knowledge", "strong background"
        ],
        ["Staff"] =
        [
            "staff engineer", "staff software", "principal engineer", "principal software",
            "distinguished", "fellow", "director of engineering",
            "VP of engineering", "head of engineering", "CTO",
            "10+ years", "15+ years", "20+ years"
        ]
    };

    // ─────────────────────────────────────────────────────────────
    //  Main method: infer topics from a single vacancy
    // ─────────────────────────────────────────────────────────────

    public InferredTopicProfile InferTopics(JobVacancy vacancy)
    {
        ArgumentNullException.ThrowIfNull(vacancy);

        logger.LogDebug("Inferring topics for vacancy {VacancyId}: {Title}", vacancy.Id, vacancy.Title);

        var corpus = BuildSearchCorpus(vacancy);
        var inferredTopics = DetectTopics(corpus, vacancy);
        var senioritySignals = DetectSenioritySignals(corpus);
        var difficulty = EstimateDifficulty(senioritySignals, vacancy);
        var salaryContext = FormatSalaryContext(vacancy);

        logger.LogInformation(
            "Vacancy {VacancyId}: inferred {TopicCount} topics, difficulty={Difficulty}",
            vacancy.Id, inferredTopics.Count, difficulty);

        return new InferredTopicProfile(
            VacancyId: vacancy.Id,
            InferredTopics: inferredTopics,
            SenioritySignals: senioritySignals,
            EstimatedInterviewDifficulty: difficulty,
            SalaryContext: salaryContext);
    }

    // ─────────────────────────────────────────────────────────────
    //  Batch analysis: market-wide topic demand
    // ─────────────────────────────────────────────────────────────

    public MarketTopicDemand AnalyzeMarketDemand(IReadOnlyList<JobVacancy> vacancies)
    {
        ArgumentNullException.ThrowIfNull(vacancies);

        logger.LogInformation("Analyzing market demand across {Count} vacancies", vacancies.Count);

        var profiles = vacancies
            .Select(InferTopics)
            .ToList();

        // Aggregate topic frequency, salary, and companies per topic.
        var topicAggregation = new Dictionary<string, TopicAggregator>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            var vacancy = vacancies.First(v => v.Id == profile.VacancyId);

            foreach (var topic in profile.InferredTopics)
            {
                if (!topicAggregation.TryGetValue(topic.TopicId, out var aggregator))
                {
                    aggregator = new TopicAggregator(topic.TopicId, topic.TopicName);
                    topicAggregation[topic.TopicId] = aggregator;
                }

                aggregator.VacancyCount++;

                if (vacancy.SalaryMax.HasValue)
                    aggregator.Salaries.Add(vacancy.SalaryMax.Value);

                if (!string.IsNullOrWhiteSpace(vacancy.Company))
                    aggregator.Companies.Add(vacancy.Company);

                foreach (var keyword in topic.DetectedKeywords)
                    aggregator.AllKeywords.Add(keyword);
            }
        }

        var totalVacancies = vacancies.Count;

        var topicRankings = topicAggregation.Values
            .OrderByDescending(a => a.VacancyCount)
            .Select(a => new TopicDemandEntry(
                TopicId: a.TopicId,
                TopicName: a.TopicName,
                VacancyCount: a.VacancyCount,
                PercentageOfVacancies: totalVacancies > 0
                    ? Math.Round(a.VacancyCount * 100.0 / totalVacancies, 1)
                    : 0,
                AvgSalaryForTopic: a.Salaries.Count > 0
                    ? Math.Round(a.Salaries.Average(), 0)
                    : 0m,
                TopCompanies: a.Companies
                    .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Take(5)
                    .Select(g => g.Key)
                    .ToList()))
            .ToList();

        // Find the top 20 individual skills across all vacancies by frequency.
        var skillFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var vacancy in vacancies)
        {
            foreach (var skill in vacancy.RequiredSkills.Concat(vacancy.PreferredSkills))
            {
                var normalized = skill.Trim().ToLowerInvariant();
                if (normalized.Length > 0)
                    skillFrequency[normalized] = skillFrequency.GetValueOrDefault(normalized) + 1;
            }
        }

        var hottestSkills = skillFrequency
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => kv.Key)
            .ToList();

        logger.LogInformation(
            "Market demand analysis complete: {TopicCount} topics across {VacancyCount} vacancies",
            topicRankings.Count, totalVacancies);

        return new MarketTopicDemand(
            TopicRankings: topicRankings,
            TotalVacanciesAnalyzed: totalVacancies,
            HottestSkills: hottestSkills);
    }

    // ─────────────────────────────────────────────────────────────
    //  Gap analysis: compare user's skills vs vacancy requirements
    // ─────────────────────────────────────────────────────────────

    public TopicGapAnalysis AnalyzeGaps(JobVacancy vacancy, UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(vacancy);
        ArgumentNullException.ThrowIfNull(profile);

        logger.LogDebug(
            "Analyzing skill gaps for {User} against vacancy {VacancyId}",
            profile.Personal.Name, vacancy.Id);

        var inferred = InferTopics(vacancy);

        // Build a set of all skill names and tech stacks the user knows.
        var userSkillNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var skill in profile.Skills)
        {
            if (!string.IsNullOrWhiteSpace(skill.SkillName))
                userSkillNames.Add(skill.SkillName.Trim());
        }

        foreach (var experience in profile.Experiences)
        {
            foreach (var tech in experience.TechStack)
            {
                if (!string.IsNullOrWhiteSpace(tech))
                    userSkillNames.Add(tech.Trim());
            }
        }

        // Also build a corpus from the user's profile for topic-level matching.
        var userCorpus = string.Join(" ",
            profile.Skills.Select(s => s.SkillName),
            profile.Personal.Title,
            profile.Personal.Summary,
            string.Join(" ", profile.Experiences.SelectMany(e => e.TechStack)),
            string.Join(" ", profile.Experiences.Select(e => e.Description)));

        var userTopicIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (topicId, entry) in KeywordDatabase)
        {
            foreach (var keyword in entry.Keywords)
            {
                if (ContainsKeyword(userCorpus, keyword))
                {
                    userTopicIds.Add(topicId);
                    break;
                }
            }
        }

        // Also check individual user skills against topic keywords.
        foreach (var skillName in userSkillNames)
        {
            foreach (var (topicId, entry) in KeywordDatabase)
            {
                foreach (var keyword in entry.Keywords)
                {
                    if (skillName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        keyword.Contains(skillName, StringComparison.OrdinalIgnoreCase))
                    {
                        userTopicIds.Add(topicId);
                        break;
                    }
                }
            }
        }

        var matched = new List<InferredTopic>();
        var gaps = new List<InferredTopic>();

        foreach (var topic in inferred.InferredTopics)
        {
            if (userTopicIds.Contains(topic.TopicId))
                matched.Add(topic);
            else
                gaps.Add(topic);
        }

        // Bonus: topics the user knows that weren't detected in the vacancy.
        var vacancyTopicIds = new HashSet<string>(
            inferred.InferredTopics.Select(t => t.TopicId),
            StringComparer.OrdinalIgnoreCase);

        var bonus = userTopicIds
            .Where(id => !vacancyTopicIds.Contains(id) && KeywordDatabase.ContainsKey(id))
            .Select(id => new InferredTopic(
                TopicId: id,
                TopicName: KeywordDatabase[id].DisplayName,
                Confidence: 50,
                DetectedKeywords: ["from user profile"]))
            .ToList();

        // Readiness score: weighted by confidence.
        var totalConfidence = inferred.InferredTopics.Sum(t => t.Confidence);
        var matchedConfidence = matched.Sum(t => t.Confidence);
        var readinessScore = totalConfidence > 0
            ? Math.Round(matchedConfidence * 100.0 / totalConfidence, 1)
            : 0;

        // Recommended study order: gaps sorted by confidence descending (highest demand + biggest gap first).
        var recommendedStudyOrder = gaps
            .OrderByDescending(t => t.Confidence)
            .Select(t => t.TopicName)
            .ToList();

        logger.LogInformation(
            "Gap analysis for {VacancyId}: readiness={Readiness}%, matched={Matched}, gaps={Gaps}, bonus={Bonus}",
            vacancy.Id, readinessScore, matched.Count, gaps.Count, bonus.Count);

        return new TopicGapAnalysis(
            MatchedTopics: matched,
            GapTopics: gaps,
            BonusTopics: bonus,
            ReadinessScore: readinessScore,
            RecommendedStudyOrder: recommendedStudyOrder);
    }

    // ─────────────────────────────────────────────────────────────
    //  Private helpers
    // ─────────────────────────────────────────────────────────────

    private static string BuildSearchCorpus(JobVacancy vacancy)
    {
        var parts = new List<string>(6)
        {
            vacancy.Title,
            vacancy.Description,
            string.Join(" ", vacancy.RequiredSkills),
            string.Join(" ", vacancy.PreferredSkills),
            vacancy.Company,
            vacancy.SeniorityLevel.ToString()
        };

        return string.Join(" ", parts);
    }

    private static List<InferredTopic> DetectTopics(string corpus, JobVacancy vacancy)
    {
        var results = new List<InferredTopic>();

        foreach (var (topicId, entry) in KeywordDatabase)
        {
            var detectedKeywords = new List<string>();

            foreach (var keyword in entry.Keywords)
            {
                if (ContainsKeyword(corpus, keyword))
                    detectedKeywords.Add(keyword);
            }

            if (detectedKeywords.Count == 0)
                continue;

            // Calculate confidence based on how many keywords matched
            // relative to a reasonable threshold for the topic.
            var confidence = CalculateConfidence(detectedKeywords.Count, entry.Keywords.Count, vacancy, topicId);

            results.Add(new InferredTopic(
                TopicId: topicId,
                TopicName: entry.DisplayName,
                Confidence: confidence,
                DetectedKeywords: detectedKeywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return results
            .OrderByDescending(t => t.Confidence)
            .ToList();
    }

    private static int CalculateConfidence(int matchedCount, int totalKeywords, JobVacancy vacancy, string topicId)
    {
        // Base confidence from keyword density.
        var ratio = (double)matchedCount / Math.Min(totalKeywords, 20);
        var baseConfidence = Math.Min(ratio * 100, 80);

        // Boost if keywords appear in required skills (strong signal).
        var requiredBoost = vacancy.RequiredSkills
            .Count(skill => KeywordDatabase.TryGetValue(topicId, out var entry) &&
                            entry.Keywords.Any(kw => skill.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        // Smaller boost for preferred skills.
        var preferredBoost = vacancy.PreferredSkills
            .Count(skill => KeywordDatabase.TryGetValue(topicId, out var entry) &&
                            entry.Keywords.Any(kw => skill.Contains(kw, StringComparison.OrdinalIgnoreCase)));

        var totalConfidence = baseConfidence + (requiredBoost * 8) + (preferredBoost * 4);

        return (int)Math.Clamp(totalConfidence, 1, 100);
    }

    private static bool ContainsKeyword(string corpus, string keyword)
    {
        if (string.IsNullOrWhiteSpace(corpus) || string.IsNullOrWhiteSpace(keyword))
            return false;

        // For short keywords (3 chars or less like "GC", "IL", "DI"),
        // use word-boundary matching to avoid false positives.
        if (keyword.Length <= 3)
        {
            var pattern = $@"\b{Regex.Escape(keyword)}\b";
            return Regex.IsMatch(corpus, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        }

        return corpus.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> DetectSenioritySignals(string corpus)
    {
        var signals = new List<string>();

        foreach (var (level, keywords) in SeniorityKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (ContainsKeyword(corpus, keyword))
                    signals.Add($"{level}: \"{keyword}\"");
            }
        }

        return signals;
    }

    private static string EstimateDifficulty(List<string> senioritySignals, JobVacancy vacancy)
    {
        // Score each difficulty level by signal count.
        var levelScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Junior"] = 0,
            ["Mid"] = 0,
            ["Senior"] = 0,
            ["Staff"] = 0
        };

        foreach (var signal in senioritySignals)
        {
            var level = signal.Split(':')[0].Trim();
            if (levelScores.ContainsKey(level))
                levelScores[level]++;
        }

        // Also consider the vacancy's own seniority level as a strong signal.
        switch (vacancy.SeniorityLevel)
        {
            case Core.Enums.SeniorityLevel.Intern:
            case Core.Enums.SeniorityLevel.Junior:
                levelScores["Junior"] += 3;
                break;
            case Core.Enums.SeniorityLevel.Middle:
                levelScores["Mid"] += 3;
                break;
            case Core.Enums.SeniorityLevel.Senior:
                levelScores["Senior"] += 3;
                break;
            case Core.Enums.SeniorityLevel.Lead:
            case Core.Enums.SeniorityLevel.Architect:
            case Core.Enums.SeniorityLevel.Principal:
                levelScores["Staff"] += 3;
                break;
        }

        // Return the level with the highest score.
        var best = levelScores
            .OrderByDescending(kv => kv.Value)
            .First();

        // Default to "Mid" if no signals at all.
        return best.Value > 0 ? best.Key : "Mid";
    }

    private static string FormatSalaryContext(JobVacancy vacancy)
    {
        if (!vacancy.SalaryMin.HasValue && !vacancy.SalaryMax.HasValue)
            return "Salary not specified";

        var currency = string.IsNullOrWhiteSpace(vacancy.SalaryCurrency) ? "USD" : vacancy.SalaryCurrency;

        if (vacancy.SalaryMin.HasValue && vacancy.SalaryMax.HasValue)
            return $"{currency} {vacancy.SalaryMin.Value:N0} - {vacancy.SalaryMax.Value:N0}";

        if (vacancy.SalaryMin.HasValue)
            return $"{currency} {vacancy.SalaryMin.Value:N0}+";

        return $"Up to {currency} {vacancy.SalaryMax!.Value:N0}";
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal aggregation helper
    // ─────────────────────────────────────────────────────────────

    private sealed class TopicAggregator(string topicId, string topicName)
    {
        public string TopicId { get; } = topicId;
        public string TopicName { get; } = topicName;
        public int VacancyCount { get; set; }
        public List<decimal> Salaries { get; } = [];
        public HashSet<string> Companies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllKeywords { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record TopicKeywordEntry(string DisplayName, List<string> Keywords);
}
