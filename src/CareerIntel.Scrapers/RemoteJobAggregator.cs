using CareerIntel.Core.Interfaces;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Aggregates remote job listings from multiple specialized remote job boards.
/// Prioritizes EU/US time zone friendly positions.
/// </summary>
public sealed class RemoteJobAggregator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RemoteJobAggregator> _logger;
    private readonly List<IJobScraper> _scrapers;

    public RemoteJobAggregator(HttpClient httpClient, ILogger<RemoteJobAggregator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _scrapers = InitializeScrapers();
    }

    /// <summary>
    /// Remote job boards ranked by quality and .NET friendliness.
    /// </summary>
    public static readonly RemoteJobBoard[] TopRemoteBoards =
    [
        new RemoteJobBoard
        {
            Name = "We Work Remotely",
            Url = "https://weworkremotely.com",
            Priority = 10,
            Tags = ["premium", "high-quality", "dev-heavy"],
            DotNetFriendly = 8,
            EuFriendly = 9,
            Description = "Premier remote job board - high-quality dev positions"
        },
        new RemoteJobBoard
        {
            Name = "RemoteOK",
            Url = "https://remoteok.com",
            Priority = 9,
            Tags = ["large-volume", "tech-focused", "worldwide"],
            DotNetFriendly = 7,
            EuFriendly = 8,
            Description = "Largest remote job board - great for backend devs"
        },
        new RemoteJobBoard
        {
            Name = "Remote.co",
            Url = "https://remote.co",
            Priority = 9,
            Tags = ["curated", "reputable", "established-companies"],
            DotNetFriendly = 8,
            EuFriendly = 9,
            Description = "Curated remote jobs from established companies"
        },
        new RemoteJobBoard
        {
            Name = "Arc.dev",
            Url = "https://arc.dev/companies",
            Priority = 9,
            Tags = ["tech-only", "vetted", "high-paying"],
            DotNetFriendly = 9,
            EuFriendly = 7,
            Description = "Tech talent marketplace - vetted positions, high pay"
        },
        new RemoteJobBoard
        {
            Name = "Dynamite Jobs",
            Url = "https://dynamitejobs.com",
            Priority = 8,
            Tags = ["quality", "async-friendly", "time-zone-flexible"],
            DotNetFriendly = 7,
            EuFriendly = 10,
            Description = "Time-zone flexible remote jobs - great for EU"
        },
        new RemoteJobBoard
        {
            Name = "EU Remote Jobs",
            Url = "https://euremotejobs.com",
            Priority = 8,
            Tags = ["eu-only", "local-companies", "time-zone-friendly"],
            DotNetFriendly = 8,
            EuFriendly = 10,
            Description = "EU-specific remote positions - perfect for European devs"
        },
        new RemoteJobBoard
        {
            Name = "Remote100k",
            Url = "https://remote100k.com",
            Priority = 8,
            Tags = ["high-paying", "senior", "100k+"],
            DotNetFriendly = 8,
            EuFriendly = 7,
            Description = "Remote jobs paying $100k+ - senior positions"
        },
        new RemoteJobBoard
        {
            Name = "JustRemote",
            Url = "https://justremote.co",
            Priority = 7,
            Tags = ["global", "diverse", "all-levels"],
            DotNetFriendly = 7,
            EuFriendly = 8,
            Description = "Global remote job board - all experience levels"
        },
        new RemoteJobBoard
        {
            Name = "Remotive",
            Url = "https://remotive.com",
            Priority = 7,
            Tags = ["tech-focused", "community", "newsletter"],
            DotNetFriendly = 7,
            EuFriendly = 8,
            Description = "Tech remote jobs with active community"
        },
        new RemoteJobBoard
        {
            Name = "FlexJobs",
            Url = "https://flexjobs.com",
            Priority = 7,
            Tags = ["vetted", "scam-free", "subscription"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Hand-screened remote jobs (requires subscription)"
        },
        new RemoteJobBoard
        {
            Name = "Working Nomads",
            Url = "https://workingnomads.com",
            Priority = 6,
            Tags = ["digital-nomad", "time-zone-flexible", "global"],
            DotNetFriendly = 6,
            EuFriendly = 8,
            Description = "Remote jobs for digital nomads"
        },
        new RemoteJobBoard
        {
            Name = "Jobgether",
            Url = "https://jobgether.com",
            Priority = 6,
            Tags = ["eu-focused", "tech", "startup-friendly"],
            DotNetFriendly = 7,
            EuFriendly = 9,
            Description = "EU-friendly remote tech positions"
        },
        new RemoteJobBoard
        {
            Name = "Jobspresso",
            Url = "https://jobspresso.co",
            Priority = 6,
            Tags = ["curated", "tech", "marketing"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Curated remote jobs in tech"
        },
        new RemoteJobBoard
        {
            Name = "Startup Jobs (Remote)",
            Url = "https://startup.jobs/remote-jobs",
            Priority = 7,
            Tags = ["startups", "equity", "early-stage"],
            DotNetFriendly = 7,
            EuFriendly = 7,
            Description = "Remote jobs at startups - equity opportunities"
        },
        new RemoteJobBoard
        {
            Name = "Wellfound (AngelList)",
            Url = "https://wellfound.com",
            Priority = 8,
            Tags = ["startups", "tech", "equity", "high-growth"],
            DotNetFriendly = 7,
            EuFriendly = 6,
            Description = "Startup jobs with equity - tech-heavy"
        },
        new RemoteJobBoard
        {
            Name = "Nodesk",
            Url = "https://nodesk.co",
            Priority = 7,
            Tags = ["curated", "remote-first", "companies"],
            DotNetFriendly = 6,
            EuFriendly = 8,
            Description = "Curated list of remote-first companies"
        },
        new RemoteJobBoard
        {
            Name = "Workster",
            Url = "https://workster.co",
            Priority = 6,
            Tags = ["global", "remote", "diverse"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Global remote job opportunities"
        },
        new RemoteJobBoard
        {
            Name = "Workew",
            Url = "https://workew.com",
            Priority = 6,
            Tags = ["remote", "tech", "worldwide"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Remote tech jobs worldwide"
        },
        new RemoteJobBoard
        {
            Name = "Remoters",
            Url = "https://remoters.net",
            Priority = 6,
            Tags = ["remote", "tech", "community"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Remote tech jobs and community"
        },
        new RemoteJobBoard
        {
            Name = "Pangian",
            Url = "https://pangian.com",
            Priority = 7,
            Tags = ["global", "remote", "diverse"],
            DotNetFriendly = 6,
            EuFriendly = 8,
            Description = "Global remote job board with diverse opportunities"
        },
        new RemoteJobBoard
        {
            Name = "PowerToFly",
            Url = "https://powertofly.com",
            Priority = 7,
            Tags = ["diversity", "tech", "remote"],
            DotNetFriendly = 7,
            EuFriendly = 7,
            Description = "Diversity-focused remote tech jobs"
        },
        new RemoteJobBoard
        {
            Name = "Skip The Drive",
            Url = "https://skipthedrive.com",
            Priority = 6,
            Tags = ["remote", "telecommute", "us-focused"],
            DotNetFriendly = 6,
            EuFriendly = 5,
            Description = "Remote and telecommute jobs"
        },
        new RemoteJobBoard
        {
            Name = "Citizen Remote",
            Url = "https://citizenremote.com",
            Priority = 6,
            Tags = ["remote", "digital-nomad", "global"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Remote jobs for digital nomads"
        },
        new RemoteJobBoard
        {
            Name = "Virtual Vocations",
            Url = "https://virtualvocations.com",
            Priority = 6,
            Tags = ["remote", "telecommute", "verified"],
            DotNetFriendly = 6,
            EuFriendly = 6,
            Description = "Hand-screened remote and telecommute jobs"
        },
        new RemoteJobBoard
        {
            Name = "Inclusively Remote",
            Url = "https://inclusivelyremote.com",
            Priority = 6,
            Tags = ["diversity", "inclusion", "remote"],
            DotNetFriendly = 6,
            EuFriendly = 7,
            Description = "Inclusive remote job opportunities"
        },
        new RemoteJobBoard
        {
            Name = "Remote Nomad Jobs",
            Url = "https://remotenomadjobs.com",
            Priority = 5,
            Tags = ["digital-nomad", "remote", "travel"],
            DotNetFriendly = 5,
            EuFriendly = 7,
            Description = "Remote jobs for digital nomads"
        },
        new RemoteJobBoard
        {
            Name = "Open To Work Remote",
            Url = "https://opentoworkremote.com",
            Priority = 5,
            Tags = ["remote", "open", "global"],
            DotNetFriendly = 5,
            EuFriendly = 6,
            Description = "Open remote job opportunities"
        }
    ];

    /// <summary>
    /// Scrape remote jobs from multiple boards in parallel.
    /// </summary>
    public async Task<AggregatedRemoteJobs> ScrapeAllAsync(
        string[] preferredStacks,
        string[] preferredLocations,
        int minSalary = 0,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting aggregated remote job scrape for stacks: {Stacks}", string.Join(", ", preferredStacks));

        var result = new AggregatedRemoteJobs
        {
            RequestedStacks = preferredStacks.ToList(),
            RequestedLocations = preferredLocations.ToList(),
            MinSalary = minSalary
        };

        // Get boards to scrape (prioritize EU-friendly for EU locations)
        var boardsToScrape = GetRecommendedBoards(preferredLocations, preferredStacks);
        result.BoardsScraped = boardsToScrape.Select(b => b.Name).ToList();

        // Scrape all boards in parallel
        var scrapeTasks = _scrapers.Select(async scraper =>
        {
            try
            {
                var jobs = await ScrapeWithTimeoutAsync(scraper, preferredStacks.FirstOrDefault() ?? ".NET", cancellationToken);

                _logger.LogInformation("Scraped {Count} jobs from {Source}", jobs.Count, scraper.PlatformName);

                return (scraper.PlatformName, jobs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scrape {Source}", scraper.PlatformName);
                return (scraper.PlatformName, new List<JobVacancy>());
            }
        }).ToList();

        var results = await Task.WhenAll(scrapeTasks);

        // Aggregate all jobs
        foreach (var (source, jobs) in results)
        {
            result.JobsBySource[source] = jobs.Count;
            result.AllJobs.AddRange(jobs);
        }

        // Filter and rank jobs
        result.FilteredJobs = FilterAndRankJobs(result.AllJobs, preferredStacks, preferredLocations, minSalary);

        _logger.LogInformation("Aggregated {Total} total jobs, {Filtered} after filtering",
            result.AllJobs.Count, result.FilteredJobs.Count);

        return result;
    }

    /// <summary>
    /// Get recommended boards based on user's location and stack preferences.
    /// </summary>
    public static List<RemoteJobBoard> GetRecommendedBoards(string[] locations, string[] stacks)
    {
        var isEuFocused = locations.Any(l =>
            l.Contains("EU", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("Europe", StringComparison.OrdinalIgnoreCase));

        var isDotNet = stacks.Any(s =>
            s.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("C#", StringComparison.OrdinalIgnoreCase));

        return TopRemoteBoards
            .OrderByDescending(b =>
            {
                var score = b.Priority * 10;

                // Boost EU-friendly boards for EU users
                if (isEuFocused)
                    score += b.EuFriendly * 2;

                // Boost .NET-friendly boards for .NET devs
                if (isDotNet)
                    score += b.DotNetFriendly * 2;

                return score;
            })
            .Take(10)
            .ToList();
    }

    private List<IJobScraper> InitializeScrapers()
    {
        // Initialize all available scrapers
        var scrapers = new List<IJobScraper>
        {
            new RemoteOkScraper(_httpClient, _logger as ILogger<RemoteOkScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteOkScraper>.Instance),
            new NodeskScraper(_httpClient, _logger as ILogger<NodeskScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<NodeskScraper>.Instance),
            new WeWorkRemotelyScraper(_httpClient, _logger as ILogger<WeWorkRemotelyScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WeWorkRemotelyScraper>.Instance),
            new RemotiveScraper(_httpClient, _logger as ILogger<RemotiveScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RemotiveScraper>.Instance),
            new JustRemoteScraper(_httpClient, _logger as ILogger<JustRemoteScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JustRemoteScraper>.Instance),
            new DynamiteJobsScraper(_httpClient, _logger as ILogger<DynamiteJobsScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DynamiteJobsScraper>.Instance),
            new EuRemoteJobsScraper(_httpClient, _logger as ILogger<EuRemoteJobsScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<EuRemoteJobsScraper>.Instance),
            new WorkingNomadsScraper(_httpClient, _logger as ILogger<WorkingNomadsScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkingNomadsScraper>.Instance),
            new ArcDevScraper(_httpClient, _logger as ILogger<ArcDevScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ArcDevScraper>.Instance),
            new WellfoundScraper(_httpClient, _logger as ILogger<WellfoundScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WellfoundScraper>.Instance),
            new RemoteCoScraper(_httpClient, _logger as ILogger<RemoteCoScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RemoteCoScraper>.Instance),
            new FlexJobsScraper(_httpClient, _logger as ILogger<FlexJobsScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FlexJobsScraper>.Instance),
            new JobspressoScraper(_httpClient, _logger as ILogger<JobspressoScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<JobspressoScraper>.Instance),
            new PangianScraper(_httpClient, _logger as ILogger<PangianScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PangianScraper>.Instance),
            new StartupJobsScraper(_httpClient, _logger as ILogger<StartupJobsScraper> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupJobsScraper>.Instance)
        };

        return scrapers;
    }

    private async Task<List<JobVacancy>> ScrapeWithTimeoutAsync(
        IJobScraper scraper,
        string keywords,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout per scraper

        try
        {
            var jobs = await scraper.ScrapeAsync(keywords, maxPages: 3, cts.Token);
            return jobs.ToList();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Scraping {Source} timed out after 30 seconds", scraper.PlatformName);
            return [];
        }
    }

    private List<JobVacancy> FilterAndRankJobs(
        List<JobVacancy> allJobs,
        string[] preferredStacks,
        string[] preferredLocations,
        int minSalary)
    {
        var filtered = allJobs.Where(job =>
        {
            // Filter by stack
            if (preferredStacks.Length > 0)
            {
                var hasStack = preferredStacks.Any(stack =>
                    job.RequiredSkills.Any(skill => skill.Contains(stack, StringComparison.OrdinalIgnoreCase)) ||
                    job.Title.Contains(stack, StringComparison.OrdinalIgnoreCase) ||
                    job.Description.Contains(stack, StringComparison.OrdinalIgnoreCase));

                if (!hasStack)
                    return false;
            }

            // Filter by location if not worldwide
            if (preferredLocations.Length > 0 && !preferredLocations.Contains("Worldwide", StringComparer.OrdinalIgnoreCase))
            {
                var matchesLocation = preferredLocations.Any(loc =>
                    job.Country.Contains(loc, StringComparison.OrdinalIgnoreCase) ||
                    job.GeoRestrictions.Any(geo => geo.Contains(loc, StringComparison.OrdinalIgnoreCase)));

                if (!matchesLocation && job.RemotePolicy != CareerIntel.Core.Enums.RemotePolicy.FullyRemote)
                    return false;
            }

            // Filter by salary
            if (minSalary > 0 && job.SalaryMin.HasValue && job.SalaryMin < minSalary)
                return false;

            return true;
        })
        .OrderByDescending(job =>
        {
            var score = 0;

            // Prioritize fully remote
            if (job.RemotePolicy == CareerIntel.Core.Enums.RemotePolicy.FullyRemote)
                score += 50;

            // Prioritize higher salaries
            if (job.SalaryMin.HasValue)
                score += (int)(job.SalaryMin.Value / 1000);

            // Prioritize recent postings
            if (job.PostedDate > DateTimeOffset.UtcNow.AddDays(-7))
                score += 20;

            // Prioritize stack matches
            var stackMatches = preferredStacks.Count(stack =>
                job.RequiredSkills.Any(skill => skill.Contains(stack, StringComparison.OrdinalIgnoreCase)));
            score += stackMatches * 10;

            return score;
        })
        .ToList();

        return filtered;
    }
}

/// <summary>
/// Information about a remote job board.
/// </summary>
public sealed class RemoteJobBoard
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Priority { get; set; } // 1-10, higher is better
    public List<string> Tags { get; set; } = [];
    public int DotNetFriendly { get; set; } // 1-10, how many .NET jobs
    public int EuFriendly { get; set; } // 1-10, how EU-friendly
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Result of aggregated remote job scraping.
/// </summary>
public sealed class AggregatedRemoteJobs
{
    public List<string> RequestedStacks { get; set; } = [];
    public List<string> RequestedLocations { get; set; } = [];
    public int MinSalary { get; set; }
    public List<string> BoardsScraped { get; set; } = [];
    public Dictionary<string, int> JobsBySource { get; set; } = new();
    public List<JobVacancy> AllJobs { get; set; } = [];
    public List<JobVacancy> FilteredJobs { get; set; } = [];
    public DateTimeOffset ScrapedAt { get; set; } = DateTimeOffset.UtcNow;
}
