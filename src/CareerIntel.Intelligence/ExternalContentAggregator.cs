using System.Net.Http;
using System.Net.Http.Headers;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace CareerIntel.Intelligence;

/// <summary>
/// Fetches live learning resources from multiple free external APIs
/// (Microsoft Learn, GitHub, RSS blogs, Stack Overflow) and returns
/// a unified <see cref="ExternalContentResult"/>.
/// </summary>
public sealed class ExternalContentAggregator(
    HttpClient httpClient,
    ILogger<ExternalContentAggregator> logger)
{
    private const int MaxItemsPerSource = 15;
    private const string GitHubUserAgent = "CareerIntel/1.0";
    private const string GitHubAccept = "application/vnd.github+json";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly (string Name, string Url)[] RssFeeds =
    [
        ("Microsoft .NET Blog", "https://devblogs.microsoft.com/dotnet/feed/"),
        ("Andrew Lock", "https://andrewlock.net/rss/"),
        ("JetBrains .NET", "https://blog.jetbrains.com/dotnet/feed/"),
        ("Ardalis (Steve Smith)", "https://ardalis.com/rss.xml"),
        ("Scott Hanselman", "https://feeds.hanselman.com/ScottHanselman"),
        ("Nick Chapsas", "https://nickchapsas.com/rss/"),
    ];

    private static readonly HashSet<string> DotNetProducts = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", ".net", "aspnet-core", "azure", "ef-core",
        "csharp", "c#", "blazor", "maui", "visual-studio",
    };

    // Set a default User-Agent if one is not already configured.
    private readonly HttpClient _httpClient = ConfigureClient(httpClient);

    private readonly ILogger<ExternalContentAggregator> _logger = logger;

    // ----------------------------------------------------------------
    // Public API
    // ----------------------------------------------------------------

    /// <summary>
    /// Calls all four external sources in parallel, catches per-source
    /// failures, and returns the combined result.
    /// </summary>
    public async Task<ExternalContentResult> AggregateAsync(
        List<string>? skillFilter = null,
        CancellationToken ct = default)
    {
        var msLearnTask = SafeFetchAsync(
            "Microsoft Learn",
            () => FetchMsLearnModulesAsync(ct));

        var gitHubTask = SafeFetchAsync(
            "GitHub Trending",
            () => FetchGitHubTrendingAsync(
                language: skillFilter?.Count > 0 ? skillFilter[0] : "csharp",
                ct: ct));

        var blogTask = SafeFetchAsync(
            "Blog Posts",
            () => FetchBlogPostsAsync(ct));

        var soTask = SafeFetchAsync(
            "Stack Overflow",
            () => FetchStackOverflowTrendingAsync(ct));

        await Task.WhenAll(msLearnTask, gitHubTask, blogTask, soTask).ConfigureAwait(false);

        return new ExternalContentResult(
            MsLearnModules: msLearnTask.Result ?? [],
            GitHubTrending: gitHubTask.Result ?? [],
            BlogPosts: blogTask.Result ?? [],
            StackOverflowTrending: soTask.Result ?? [],
            FetchedAt: DateTimeOffset.UtcNow);
    }

    // ----------------------------------------------------------------
    // Microsoft Learn Modules
    // ----------------------------------------------------------------

    /// <summary>
    /// Fetches .NET-related learning modules from the Microsoft Learn catalog API.
    /// </summary>
    public async Task<List<MsLearnModule>> FetchMsLearnModulesAsync(
        CancellationToken ct = default)
    {
        const string url =
            "https://learn.microsoft.com/api/catalog/?type=modules&products=dotnet&locale=en-us";

        var json = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        if (json is null)
        {
            return [];
        }

        var modules = new List<MsLearnModule>();

        if (!json.Value.TryGetProperty("modules", out var modulesArray))
        {
            _logger.LogWarning("Microsoft Learn response did not contain a 'modules' array.");
            return [];
        }

        foreach (var item in modulesArray.EnumerateArray())
        {
            try
            {
                var title = item.GetProperty("title").GetString() ?? string.Empty;
                var moduleUrl = item.GetProperty("url").GetString() ?? string.Empty;
                var summary = item.GetProperty("summary").GetString() ?? string.Empty;

                var products = new List<string>();
                if (item.TryGetProperty("products", out var productsElement))
                {
                    foreach (var p in productsElement.EnumerateArray())
                    {
                        var val = p.GetString();
                        if (val is not null)
                        {
                            products.Add(val);
                        }
                    }
                }

                var duration = 0;
                if (item.TryGetProperty("duration_in_minutes", out var dur))
                {
                    duration = dur.GetInt32();
                }

                // Filter for .NET / C# / Azure relevance.
                var isRelevant = products.Exists(p =>
                    DotNetProducts.Contains(p));

                if (!isRelevant)
                {
                    continue;
                }

                modules.Add(new MsLearnModule(title, moduleUrl, summary, products, duration));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse a Microsoft Learn module entry.");
            }
        }

        return modules
            .Take(MaxItemsPerSource)
            .ToList();
    }

    // ----------------------------------------------------------------
    // GitHub Trending Repositories
    // ----------------------------------------------------------------

    /// <summary>
    /// Fetches recently-pushed, top-starred repositories for the given language
    /// from the GitHub Search API.
    /// </summary>
    public async Task<List<TrendingRepo>> FetchGitHubTrendingAsync(
        string language = "csharp",
        CancellationToken ct = default)
    {
        var oneWeekAgo = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
        var url =
            $"https://api.github.com/search/repositories?q=language:{language}+pushed:>{oneWeekAgo}" +
            $"&sort=stars&order=desc&per_page={MaxItemsPerSource}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(GitHubUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubAccept));

        JsonElement? json;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub API returned {StatusCode} for trending repos.",
                    response.StatusCode);
                return [];
            }

            var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token)
                .ConfigureAwait(false);
            json = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub trending repositories.");
            return [];
        }

        if (json is null || !json.Value.TryGetProperty("items", out var items))
        {
            return [];
        }

        var repos = new List<TrendingRepo>();

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var name = item.GetProperty("full_name").GetString() ?? string.Empty;
                var repoUrl = item.GetProperty("html_url").GetString() ?? string.Empty;
                var description = item.TryGetProperty("description", out var desc)
                    ? desc.GetString() ?? string.Empty
                    : string.Empty;
                var lang = item.TryGetProperty("language", out var langEl)
                    ? langEl.GetString() ?? string.Empty
                    : string.Empty;
                var stars = item.GetProperty("stargazers_count").GetInt32();

                repos.Add(new TrendingRepo(name, repoUrl, description, lang, stars));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse a GitHub repository entry.");
            }
        }

        return repos;
    }

    // ----------------------------------------------------------------
    // Blog Posts via RSS
    // ----------------------------------------------------------------

    /// <summary>
    /// Fetches recent blog posts from a curated list of .NET RSS feeds.
    /// </summary>
    public async Task<List<BlogPost>> FetchBlogPostsAsync(
        CancellationToken ct = default)
    {
        var allPosts = new List<BlogPost>();
        var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30);

        // Fetch all feeds in parallel.
        var feedTasks = RssFeeds.Select(feed =>
            SafeFetchFeedAsync(feed.Name, feed.Url, thirtyDaysAgo, ct));

        var results = await Task.WhenAll(feedTasks).ConfigureAwait(false);

        foreach (var posts in results)
        {
            allPosts.AddRange(posts);
        }

        return allPosts
            .OrderByDescending(p => p.Published)
            .Take(MaxItemsPerSource)
            .ToList();
    }

    // ----------------------------------------------------------------
    // Stack Overflow Trending Questions
    // ----------------------------------------------------------------

    /// <summary>
    /// Fetches hot .NET / C# questions from the Stack Exchange API.
    /// </summary>
    public async Task<List<StackOverflowQuestion>> FetchStackOverflowTrendingAsync(
        CancellationToken ct = default)
    {
        var url =
            "https://api.stackexchange.com/2.3/questions" +
            "?tagged=.net;c%23&sort=hot&order=desc&site=stackoverflow" +
            $"&pagesize={MaxItemsPerSource}";

        var json = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        if (json is null)
        {
            return [];
        }

        if (!json.Value.TryGetProperty("items", out var items))
        {
            _logger.LogWarning("Stack Overflow response did not contain an 'items' array.");
            return [];
        }

        var questions = new List<StackOverflowQuestion>();

        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var rawTitle = item.GetProperty("title").GetString() ?? string.Empty;
                var title = System.Net.WebUtility.HtmlDecode(rawTitle);

                var link = item.GetProperty("link").GetString() ?? string.Empty;
                var score = item.GetProperty("score").GetInt32();
                var answerCount = item.GetProperty("answer_count").GetInt32();

                var tags = new List<string>();
                if (item.TryGetProperty("tags", out var tagsElement))
                {
                    foreach (var t in tagsElement.EnumerateArray())
                    {
                        var val = t.GetString();
                        if (val is not null)
                        {
                            tags.Add(val);
                        }
                    }
                }

                questions.Add(new StackOverflowQuestion(title, link, score, answerCount, tags));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse a Stack Overflow question entry.");
            }
        }

        return questions;
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Sends a GET request, validates success, and returns the parsed
    /// <see cref="JsonElement"/> root. Returns <c>null</c> on failure.
    /// </summary>
    private async Task<JsonElement?> FetchJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Don't set Accept-Encoding for gzip — let the API return uncompressed
            // so we avoid decompression issues with DI-provided HttpClient.

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GET {Url} returned HTTP {StatusCode}.",
                    url,
                    (int)response.StatusCode);
                return null;
            }

            var stream = await response.Content
                .ReadAsStreamAsync(cts.Token)
                .ConfigureAwait(false);

            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token)
                .ConfigureAwait(false);

            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to fetch JSON from {Url}.", url);
            return null;
        }
    }

    /// <summary>
    /// Wraps an async fetch delegate so that any exception is caught, logged,
    /// and a <c>null</c> result is returned instead of propagating the failure.
    /// </summary>
    private async Task<List<T>?> SafeFetchAsync<T>(
        string sourceName,
        Func<Task<List<T>>> fetch)
    {
        try
        {
            return await fetch().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "External content source '{Source}' failed. Skipping.",
                sourceName);
            return null;
        }
    }

    /// <summary>
    /// Fetches and parses a single RSS/Atom feed, returning posts
    /// published after <paramref name="since"/>.
    /// </summary>
    private async Task<List<BlogPost>> SafeFetchFeedAsync(
        string feedName,
        string feedUrl,
        DateTimeOffset since,
        CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            var xml = await _httpClient
                .GetStringAsync(feedUrl, cts.Token)
                .ConfigureAwait(false);

            using var stringReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader);
            var feed = SyndicationFeed.Load(xmlReader);

            if (feed is null)
            {
                return [];
            }

            var posts = new List<BlogPost>();

            foreach (var item in feed.Items)
            {
                var published = item.PublishDate != default
                    ? item.PublishDate
                    : item.LastUpdatedTime;

                if (published < since)
                {
                    continue;
                }

                var link = item.Links.FirstOrDefault()?.Uri?.AbsoluteUri ?? string.Empty;
                var title = item.Title?.Text ?? string.Empty;

                posts.Add(new BlogPost(title, link, feedName, published));
            }

            return posts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch RSS feed '{FeedName}' ({FeedUrl}). Skipping.",
                feedName,
                feedUrl);
            return [];
        }
    }

    /// <summary>
    /// Applies default headers to the DI-provided <see cref="HttpClient"/>
    /// if they have not already been configured.
    /// </summary>
    private static HttpClient ConfigureClient(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubUserAgent);
        }

        return client;
    }
}

// ----------------------------------------------------------------
// Result records
// ----------------------------------------------------------------

public sealed record ExternalContentResult(
    IReadOnlyList<MsLearnModule> MsLearnModules,
    IReadOnlyList<TrendingRepo> GitHubTrending,
    IReadOnlyList<BlogPost> BlogPosts,
    IReadOnlyList<StackOverflowQuestion> StackOverflowTrending,
    DateTimeOffset FetchedAt);

public sealed record MsLearnModule(
    string Title,
    string Url,
    string Summary,
    IReadOnlyList<string> Products,
    int DurationMinutes);

public sealed record TrendingRepo(
    string Name,
    string Url,
    string Description,
    string Language,
    int Stars);

public sealed record BlogPost(
    string Title,
    string Url,
    string Source,
    DateTimeOffset Published);

public sealed record StackOverflowQuestion(
    string Title,
    string Url,
    int Score,
    int AnswerCount,
    IReadOnlyList<string> Tags);
