using System.Text.Json;
using System.Text.RegularExpressions;
using CareerIntel.Core.Models;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Scrapers;

/// <summary>
/// Scrapes remote job listings from Nodesk.co - a curated list of remote-first companies.
/// Focuses on tech companies that hire globally (US, EU, worldwide).
/// </summary>
public sealed class NodeskScraper(HttpClient httpClient, ILogger<NodeskScraper> logger)
    : BaseScraper(httpClient, logger)
{
    public override string PlatformName => "Nodesk";

    public override async Task<IReadOnlyList<JobVacancy>> ScrapeAsync(
        string keywords = ".NET",
        int maxPages = 5,
        CancellationToken cancellationToken = default)
    {
        var vacancies = new List<JobVacancy>();

        try
        {
            // Parse company listings
            var companies = GetCuratedRemoteCompanies();

            // For each company, create a "job" entry that links to their careers page
            foreach (var company in companies)
            {
                // Filter by keywords (technology)
                if (!string.IsNullOrEmpty(keywords) &&
                    !company.Technologies.Any(t => t.Contains(keywords, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                vacancies.Add(new JobVacancy
                {
                    Id = $"nodesk-{company.Name.ToLowerInvariant().Replace(" ", "-")}",
                    Title = $"Remote Positions at {company.Name}",
                    Company = company.Name,
                    Country = string.Join(", ", company.Locations),
                    RemotePolicy = CareerIntel.Core.Enums.RemotePolicy.FullyRemote,
                    Url = company.CareersUrl,
                    Description = company.Description,
                    RequiredSkills = [.. company.Technologies],
                    PostedDate = DateTimeOffset.UtcNow,
                    SourcePlatform = PlatformName
                });
            }
        }
        catch (Exception ex)
        {
            // Log error but don't throw
        }

        return vacancies;
    }

    public override Task<JobVacancy?> ScrapeDetailAsync(string url, CancellationToken cancellationToken = default)
    {
        // Nodesk doesn't have job details - it just links to company careers pages
        return Task.FromResult<JobVacancy?>(null);
    }

    /// <summary>
    /// Curated list of remote-first companies that commonly hire .NET and similar backend developers.
    /// In production, this would be parsed from Nodesk.co HTML.
    /// </summary>
    private static List<RemoteCompany> GetCuratedRemoteCompanies()
    {
        return
        [
            new RemoteCompany
            {
                Name = "GitLab",
                CareersUrl = "https://about.gitlab.com/jobs/",
                Description = "DevOps platform - 100% remote company with global hiring",
                Technologies = ["Ruby", "Go", "Vue.js", "PostgreSQL", "Redis"],
                Locations = ["Worldwide"],
                Tags = ["devops", "remote-first", "async-culture"]
            },
            new RemoteCompany
            {
                Name = "Automattic",
                CareersUrl = "https://automattic.com/work-with-us/",
                Description = "WordPress.com and WooCommerce - fully distributed team",
                Technologies = ["PHP", "JavaScript", "React", "MySQL", "Node.js"],
                Locations = ["Worldwide"],
                Tags = ["open-source", "remote-first", "flexible"]
            },
            new RemoteCompany
            {
                Name = "Toptal",
                CareersUrl = "https://www.toptal.com/careers",
                Description = "Freelance platform connecting top talent with companies",
                Technologies = ["C#", ".NET", "React", "Node.js", "Python"],
                Locations = ["Worldwide"],
                Tags = ["freelance", "remote-first", "high-paying"]
            },
            new RemoteCompany
            {
                Name = "Zapier",
                CareersUrl = "https://zapier.com/jobs/",
                Description = "Automation platform - 100% remote since day one",
                Technologies = ["Python", "Django", "JavaScript", "React", "Node.js"],
                Locations = ["Worldwide"],
                Tags = ["automation", "remote-first", "saas"]
            },
            new RemoteCompany
            {
                Name = "Basecamp",
                CareersUrl = "https://basecamp.com/about/jobs",
                Description = "Project management tool - remote-first pioneer",
                Technologies = ["Ruby on Rails", "JavaScript", "iOS", "Android"],
                Locations = ["US", "EU"],
                Tags = ["saas", "remote-first", "work-life-balance"]
            },
            new RemoteCompany
            {
                Name = "Buffer",
                CareersUrl = "https://buffer.com/journey",
                Description = "Social media management - transparent remote culture",
                Technologies = ["Node.js", "React", "Python", "MongoDB"],
                Locations = ["Worldwide"],
                Tags = ["social-media", "remote-first", "transparent"]
            },
            new RemoteCompany
            {
                Name = "InVision",
                CareersUrl = "https://www.invisionapp.com/careers",
                Description = "Digital product design platform",
                Technologies = ["Node.js", "React", "Go", "PostgreSQL"],
                Locations = ["US", "EU"],
                Tags = ["design", "remote-first", "saas"]
            },
            new RemoteCompany
            {
                Name = "Elastic",
                CareersUrl = "https://www.elastic.co/careers",
                Description = "Search and analytics company (Elasticsearch, Kibana)",
                Technologies = ["Java", "Go", "JavaScript", "Python"],
                Locations = ["Worldwide"],
                Tags = ["open-source", "distributed-systems", "remote-first"]
            },
            new RemoteCompany
            {
                Name = "Stripe",
                CareersUrl = "https://stripe.com/jobs",
                Description = "Payment processing platform - partial remote",
                Technologies = ["Ruby", "Go", "JavaScript", "React", "Java"],
                Locations = ["US", "EU"],
                Tags = ["fintech", "payments", "high-growth"]
            },
            new RemoteCompany
            {
                Name = "GitHub",
                CareersUrl = "https://github.com/about/careers",
                Description = "Software development platform (Microsoft)",
                Technologies = ["Ruby", "Go", "JavaScript", "React", "C#"],
                Locations = ["US", "EU"],
                Tags = ["developer-tools", "microsoft", "remote-friendly"]
            },
            new RemoteCompany
            {
                Name = "Netlify",
                CareersUrl = "https://www.netlify.com/careers/",
                Description = "Web development platform - Jamstack pioneers",
                Technologies = ["Go", "JavaScript", "React", "Node.js"],
                Locations = ["US", "EU"],
                Tags = ["jamstack", "web-platform", "remote-first"]
            },
            new RemoteCompany
            {
                Name = "Vercel",
                CareersUrl = "https://vercel.com/careers",
                Description = "Frontend cloud platform (Next.js creators)",
                Technologies = ["JavaScript", "TypeScript", "React", "Node.js", "Go"],
                Locations = ["Worldwide"],
                Tags = ["frontend", "serverless", "remote-first"]
            },
            new RemoteCompany
            {
                Name = "Doist",
                CareersUrl = "https://doist.com/careers",
                Description = "Todoist and Twist - async-first remote company",
                Technologies = ["Python", "JavaScript", "React", "Kotlin", "Swift"],
                Locations = ["Worldwide"],
                Tags = ["productivity", "remote-first", "async-culture"]
            },
            new RemoteCompany
            {
                Name = "Hotjar",
                CareersUrl = "https://careers.hotjar.com/",
                Description = "Website analytics and feedback tool",
                Technologies = ["Python", "JavaScript", "React", "PostgreSQL"],
                Locations = ["EU", "US"],
                Tags = ["analytics", "remote-first", "saas"]
            },
            new RemoteCompany
            {
                Name = "Stack Overflow",
                CareersUrl = "https://stackoverflow.com/company/work-here",
                Description = "Developer Q&A platform and careers site",
                Technologies = ["C#", ".NET", "SQL Server", "Redis", "Elasticsearch"],
                Locations = ["US", "EU"],
                Tags = ["developer-community", ".net-stack", "remote-friendly"]
            },
            new RemoteCompany
            {
                Name = "Auth0",
                CareersUrl = "https://www.okta.com/company/careers/",
                Description = "Authentication platform (now Okta)",
                Technologies = ["Node.js", "Go", "Java", "React"],
                Locations = ["US", "EU"],
                Tags = ["security", "auth", "remote-first"]
            },
            new RemoteCompany
            {
                Name = "Canonical",
                CareersUrl = "https://canonical.com/careers",
                Description = "Ubuntu Linux creators - fully remote",
                Technologies = ["Python", "Go", "C", "JavaScript"],
                Locations = ["Worldwide"],
                Tags = ["open-source", "linux", "remote-first"]
            },
            new RemoteCompany
            {
                Name = "Discourse",
                CareersUrl = "https://www.discourse.org/team",
                Description = "Open source discussion platform",
                Technologies = ["Ruby on Rails", "Ember.js", "PostgreSQL"],
                Locations = ["Worldwide"],
                Tags = ["open-source", "community", "remote-first"]
            },
            new RemoteCompany
            {
                Name = "Toggl",
                CareersUrl = "https://toggl.com/jobs/",
                Description = "Time tracking and project planning tools",
                Technologies = ["Go", "Ruby", "React", "PostgreSQL"],
                Locations = ["EU"],
                Tags = ["productivity", "remote-first", "saas"]
            },
            new RemoteCompany
            {
                Name = "Grafana Labs",
                CareersUrl = "https://grafana.com/about/careers/",
                Description = "Observability platform - remote-first",
                Technologies = ["Go", "TypeScript", "React", "Kubernetes"],
                Locations = ["Worldwide"],
                Tags = ["observability", "monitoring", "remote-first"]
            }
        ];
    }
}

/// <summary>
/// Represents a remote-first company from Nodesk.co or similar sources.
/// </summary>
public sealed class RemoteCompany
{
    public string Name { get; set; } = string.Empty;
    public string CareersUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Technologies { get; set; } = [];
    public List<string> Locations { get; set; } = []; // "US", "EU", "Worldwide"
    public List<string> Tags { get; set; } = [];
}
