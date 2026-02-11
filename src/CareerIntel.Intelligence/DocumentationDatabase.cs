using System.Text.Json;
using CareerIntel.Core.Models;

namespace CareerIntel.Intelligence;

/// <summary>
/// Database for storing and querying scraped documentation articles.
/// Enables topic-based learning resource discovery.
/// </summary>
public sealed class DocumentationDatabase
{
    private readonly string _databasePath;
    private readonly Dictionary<string, DocumentationSet> _documentationSets = new();

    public DocumentationDatabase(string databasePath)
    {
        _databasePath = databasePath;
        LoadDatabase();
    }

    /// <summary>
    /// Add articles to the database.
    /// </summary>
    public void AddArticles(List<DocumentationArticle> articles)
    {
        foreach (var article in articles)
        {
            var key = GetKey(article.Technology, article.Topics.FirstOrDefault() ?? "General");

            if (!_documentationSets.TryGetValue(key, out var docSet))
            {
                docSet = new DocumentationSet
                {
                    Technology = article.Technology,
                    Topic = article.Topics.FirstOrDefault() ?? "General"
                };
                _documentationSets[key] = docSet;
            }

            // Deduplicate by URL
            if (!docSet.Articles.Any(a => a.Url == article.Url))
            {
                docSet.Articles.Add(article);
            }

            docSet.LastUpdated = DateTimeOffset.UtcNow;
        }

        UpdateStatistics();
        SaveDatabase();
    }

    /// <summary>
    /// Search articles by topic.
    /// </summary>
    public List<DocumentationArticle> SearchByTopic(string topic)
    {
        return _documentationSets.Values
            .SelectMany(ds => ds.Articles)
            .Where(a => a.Topics.Any(t => t.Contains(topic, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => a.Relevance)
            .ToList();
    }

    /// <summary>
    /// Get articles for a specific technology.
    /// </summary>
    public List<DocumentationArticle> GetTechnologyArticles(string technology)
    {
        return _documentationSets.Values
            .Where(ds => ds.Technology.Equals(technology, StringComparison.OrdinalIgnoreCase))
            .SelectMany(ds => ds.Articles)
            .OrderByDescending(a => a.Relevance)
            .ToList();
    }

    /// <summary>
    /// Search articles by any query (title, summary, content, topics).
    /// </summary>
    public List<DocumentationArticle> Search(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        return _documentationSets.Values
            .SelectMany(ds => ds.Articles)
            .Where(a =>
                a.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.Topics.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                a.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(a => CalculateRelevance(a, query))
            .ToList();
    }

    /// <summary>
    /// Get articles by difficulty level.
    /// </summary>
    public List<DocumentationArticle> GetArticlesByDifficulty(DifficultyLevel difficulty)
    {
        return _documentationSets.Values
            .SelectMany(ds => ds.Articles)
            .Where(a => a.Difficulty == difficulty)
            .OrderByDescending(a => a.Relevance)
            .ToList();
    }

    /// <summary>
    /// Get learning path for a topic (progressive difficulty).
    /// </summary>
    public List<DocumentationArticle> GetLearningPath(string topic, int count = 10)
    {
        var allArticles = SearchByTopic(topic);

        // 30% Easy, 50% Medium, 20% Hard
        var easy = allArticles.Where(a => a.Difficulty == DifficultyLevel.Easy)
            .OrderByDescending(a => a.Relevance)
            .Take((int)(count * 0.3));

        var medium = allArticles.Where(a => a.Difficulty == DifficultyLevel.Medium)
            .OrderByDescending(a => a.Relevance)
            .Take((int)(count * 0.5));

        var hard = allArticles.Where(a => a.Difficulty == DifficultyLevel.Hard)
            .OrderByDescending(a => a.Relevance)
            .Take((int)(count * 0.2));

        return [.. easy, .. medium, .. hard];
    }

    /// <summary>
    /// Get recommended articles for skill gaps.
    /// </summary>
    public List<DocumentationArticle> GetRecommendedForSkills(List<string> missingSkills, int count = 5)
    {
        var articles = new List<DocumentationArticle>();

        foreach (var skill in missingSkills)
        {
            var skillArticles = Search(skill)
                .OrderBy(a => a.Difficulty) // Start with easier articles
                .Take(count / missingSkills.Count)
                .ToList();

            articles.AddRange(skillArticles);
        }

        return articles
            .DistinctBy(a => a.Url)
            .OrderBy(a => a.Difficulty)
            .ThenByDescending(a => a.Relevance)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Get statistics about the documentation database.
    /// </summary>
    public DocumentationStats GetStatistics()
    {
        var allArticles = _documentationSets.Values.SelectMany(ds => ds.Articles).ToList();

        return new DocumentationStats
        {
            TotalArticles = allArticles.Count,
            TotalSources = allArticles.Select(a => a.Source).Distinct().Count(),
            TotalTechnologies = _documentationSets.Values.Select(ds => ds.Technology).Distinct().Count(),
            ArticlesBySource = allArticles
                .GroupBy(a => a.Source)
                .ToDictionary(g => g.Key, g => g.Count()),
            ArticlesByTechnology = allArticles
                .GroupBy(a => a.Technology)
                .ToDictionary(g => g.Key, g => g.Count()),
            ArticlesByDifficulty = allArticles
                .GroupBy(a => a.Difficulty)
                .ToDictionary(g => g.Key, g => g.Count()),
            LastUpdated = _documentationSets.Values.Any()
                ? _documentationSets.Values.Max(ds => ds.LastUpdated)
                : DateTimeOffset.MinValue
        };
    }

    private static string GetKey(string technology, string topic)
    {
        return $"{technology}|{topic}".ToLowerInvariant();
    }

    private static double CalculateRelevance(DocumentationArticle article, string query)
    {
        var score = article.Relevance;

        // Boost if query is in title
        if (article.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 20;

        // Boost if query matches topic exactly
        if (article.Topics.Any(t => t.Equals(query, StringComparison.OrdinalIgnoreCase)))
            score += 15;

        // Boost if query is in summary
        if (article.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 10;

        return score;
    }

    private void UpdateStatistics()
    {
        foreach (var docSet in _documentationSets.Values)
        {
            docSet.TopicFrequency = docSet.Articles
                .SelectMany(a => a.Topics)
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }

    private void LoadDatabase()
    {
        if (!File.Exists(_databasePath))
            return;

        try
        {
            var json = File.ReadAllText(_databasePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DocumentationSet>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _documentationSets.Clear();
            foreach (var kvp in loaded)
            {
                _documentationSets[kvp.Key] = kvp.Value;
            }
        }
        catch
        {
            // Ignore load errors, start fresh
        }
    }

    private void SaveDatabase()
    {
        try
        {
            var dir = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_documentationSets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_databasePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
}
